using UnityEngine;
using TFOU.Ocean;

namespace TFOU.Ship
{
    /// <summary>Telemetry the camera rig reads from the ship (implemented by master_ship).</summary>
    public interface IShipState
    {
        float Speed01 { get; }
        float Rudder01 { get; }
        float HeelDegrees { get; }
        float SpeedKnots { get; }
        float EngineLoad01 { get; }
    }

    /// <summary>
    /// Cinematic ship camera. Lives on the camera GameObject.
    /// Modes:
    ///   Chase     - classic stern chase cam, yaw-follows the ship
    ///   Tactical  - high wide command view
    ///   Orbit     - full manual control (hold RMB)
    ///   Cinematic - slow auto-orbiting "screensaver" shots
    /// Adds spring-arm collision, speed shake, slam impulses, FOV kick and a
    /// subtle banking roll into turns. Cycles with C / gamepad Y.
    /// </summary>
    public class ShipCameraRig : MonoBehaviour
    {
        public enum Mode { Chase, Tactical, Orbit, Cinematic }

        [Header("=== TARGET ===")]
        public Transform ship;
        public IShipState ShipState;
        public Vector3 pivotOffsetLocal;

        [Header("=== DISTANCES ===")]
        public float defaultDistance = 55f;
        public float tacticalDistance = 120f;
        public float minDistance = 18f;
        public float maxZoomOut = 400f;
        public float zoomSpeed = 10f;

        [Header("=== FOLLOW ===")]
        public bool autoFollowShipYaw = true;
        public float yawFollowSpeed = 1.8f;
        public float orbitSpeed = 3f;
        [Range(5f, 80f)] public float minPitch = 8f;
        [Range(5f, 80f)] public float maxPitch = 70f;

        [Header("=== LENS ===")]
        public float baseFOV = 60f;
        public float maxSpeedFOV = 72f;
        public float bankRollDegrees = 3f;

        [Header("=== MOTION ===")]
        public float positionSmoothTime = 0.16f;
        public float speedShakeAmount = 0.14f;
        public float slamShakeMultiplier = 1.6f;

        public Mode CurrentMode { get; private set; } = Mode.Chase;

        private Camera _cam;
        private float _camYaw, _camPitch = 16f;
        private float _currentDistance, _targetDistance;
        private Vector3 _posVel;
        private float _shake;
        private float _fovPunch;
        private float _bankRoll;
        private float _cinematicTimer;
        private float _cinematicPitchBase;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _currentDistance = defaultDistance;
            _targetDistance = defaultDistance;
        }

        private void OnEnable()
        {
            _cinematicPitchBase = Random.Range(14f, 30f);
        }

        public void Initialize(Transform shipTransform, IShipState state, Vector3 pivotLocal)
        {
            ship = shipTransform;
            ShipState = state;
            pivotOffsetLocal = pivotLocal;
            _camYaw = shipTransform != null ? shipTransform.eulerAngles.y : 0f;
        }

        public void SetMode(Mode m)
        {
            CurrentMode = m;
            if (m == Mode.Tactical)
            {
                _targetDistance = tacticalDistance;
                _camPitch = Mathf.Max(_camPitch, 50f);
            }
            else if (m == Mode.Chase)
            {
                _targetDistance = Mathf.Min(_targetDistance, defaultDistance + 20f);
            }
            else if (m == Mode.Cinematic)
            {
                _cinematicTimer = 0f;
            }
        }

        public void CycleMode() => SetMode((Mode)(((int)CurrentMode + 1) % 4));

        /// <summary>Impulse from events like bow slams.</summary>
        public void AddShake(float amount) => _shake = Mathf.Min(_shake + amount, 1.5f);

        public void AddFovPunch(float amount) => _fovPunch = Mathf.Min(_fovPunch + amount, 8f);

        private void LateUpdate()
        {
            if (ship == null || _cam == null) return;

            float dt = Time.deltaTime;
            float speed01 = ShipState != null ? ShipState.Speed01 : 0f;
            float rudder = ShipState != null ? ShipState.Rudder01 : 0f;
            float heel = ShipState != null ? ShipState.HeelDegrees : 0f;

            HandleInput();
            UpdateModeLogic(dt, speed01);

            _camPitch = Mathf.Clamp(_camPitch, minPitch, maxPitch);
            _currentDistance = Mathf.Lerp(_currentDistance, _targetDistance, dt * 4f);

            Vector3 pivot = ship.TransformPoint(pivotOffsetLocal);

            // --- orbit position ---
            Quaternion orbitRot = Quaternion.Euler(_camPitch, _camYaw, 0f);
            Vector3 desired = pivot - orbitRot * Vector3.forward * _currentDistance;

            // Turn lag: camera sweeps wide during hard rudder.
            desired += ship.right * Mathf.Clamp(-rudder * speed01 * 6f, -10f, 10f);

            // --- spring arm collision ---
            Vector3 dir = desired - pivot;
            float dist = dir.magnitude;
            if (dist > 0.01f)
            {
                dir /= dist;
                if (Physics.SphereCast(pivot, 0.9f, dir, out RaycastHit hit, dist,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider != null && hit.collider.transform != ship &&
                        !hit.collider.transform.IsChildOf(ship))
                    {
                        dist = Mathf.Max(2.5f, hit.distance - 1.2f);
                    }
                }
                desired = pivot + dir * dist;
            }

            // --- shake ---
            _shake = Mathf.MoveTowards(_shake, 0f, dt * 0.9f);
            float shakeAmp = (speed01 * speed01) * speedShakeAmount * 0.35f + _shake * slamShakeMultiplier * 0.4f;
            if (shakeAmp > 0.001f)
            {
                float st = Time.time * 14f;
                desired += new Vector3(
                    (Mathf.PerlinNoise(st, 0.3f) - 0.5f),
                    (Mathf.PerlinNoise(0.7f, st) - 0.5f),
                    (Mathf.PerlinNoise(st, st * 0.5f) - 0.5f)) * shakeAmp;
            }

            transform.position = Vector3.SmoothDamp(transform.position, desired, ref _posVel, positionSmoothTime);

            // --- look + bank roll ---
            Quaternion look = Quaternion.LookRotation(pivot - transform.position);
            float targetRoll = Mathf.Clamp(-rudder * speed01, -1f, 1f) * bankRollDegrees + heel * 0.15f;
            _bankRoll = Mathf.Lerp(_bankRoll, targetRoll, dt * 3f);
            transform.rotation = Quaternion.Slerp(transform.rotation, look * Quaternion.Euler(0f, 0f, _bankRoll), dt * 8f);

            // --- FOV: speed + slam punch ---
            _fovPunch = Mathf.MoveTowards(_fovPunch, 0f, dt * 6f);
            float targetFov = Mathf.Lerp(baseFOV, maxSpeedFOV, speed01) + _fovPunch;
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, targetFov, dt * 3f);

            // Never let the horizon clip at extreme zoom.
            float waterY = OceanWaves.Instance != null ? OceanWaves.Instance.waterLevel : SuimonoBridge.GetBaseLevel();
            if (transform.position.y < waterY + 1.5f && CurrentMode != Mode.Orbit)
            {
                Vector3 p = transform.position;
                p.y = Mathf.Lerp(p.y, waterY + 1.5f, dt * 2f);
                transform.position = p;
            }
        }

        private void HandleInput()
        {
            if (ShipInput.KeyDown(KeyCode.C) || ShipInput.KeyDown(KeyCode.Tab) || ShipInput.GamepadButtonDown('y'))
                CycleMode();

            if (ShipInput.OrbitHeld())
            {
                Vector2 d = ShipInput.MouseDelta();
                _camYaw += d.x * orbitSpeed * 0.08f;
                _camPitch -= d.y * orbitSpeed * 0.08f;
                if (CurrentMode == Mode.Chase && Mathf.Abs(d.x) > 1.5f) SetMode(Mode.Orbit);
            }

            float scroll = ShipInput.Scroll();
            if (Mathf.Abs(scroll) > 0.001f)
            {
                _targetDistance = Mathf.Clamp(_targetDistance - scroll * zoomSpeed, minDistance, maxZoomOut);
            }
        }

        private void UpdateModeLogic(float dt, float speed01)
        {
            switch (CurrentMode)
            {
                case Mode.Chase:
                    if (autoFollowShipYaw && ship != null)
                        _camYaw = Mathf.LerpAngle(_camYaw, ship.eulerAngles.y, dt * yawFollowSpeed);
                    break;

                case Mode.Tactical:
                    if (autoFollowShipYaw && ship != null)
                        _camYaw = Mathf.LerpAngle(_camYaw, ship.eulerAngles.y, dt * yawFollowSpeed * 0.4f);
                    break;

                case Mode.Orbit:
                    // fully manual
                    break;

                case Mode.Cinematic:
                    _cinematicTimer += dt;
                    _camYaw += dt * 4.5f; // slow 360 sweep
                    _camPitch = _cinematicPitchBase + Mathf.PerlinNoise(_cinematicTimer * 0.05f, 0.5f) * 22f - 8f;
                    _targetDistance = defaultDistance * (1.15f + Mathf.PerlinNoise(0.5f, _cinematicTimer * 0.03f) * 0.5f);
                    break;
            }
        }
    }
}

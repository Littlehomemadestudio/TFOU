using UnityEngine;
using TFOU.Ocean;

namespace TFOU.Ship
{
    /// <summary>
    /// Multi-point buoyancy sampler. Instead of tilting the ship from a single
    /// wave normal at the center (which makes long hulls look like bath toys),
    /// this samples the ocean at 9 points along the hull, least-squares-fits a
    /// plane through the local surface, and derives real pitch/roll targets.
    /// Also detects bow slams (hull re-entering water) and fires events so
    /// effects/camera can react.
    ///
    /// Sampling is pure math on OceanWaves - deterministic, allocation-free,
    /// and always consistent with what the GPU renders.
    /// </summary>
    public class ShipBuoyancy : MonoBehaviour
    {
        [Header("=== HULL ===")]
        public Bounds localBounds = new Bounds(Vector3.zero, new Vector3(14f, 8f, 60f));

        [Range(0.05f, 0.5f)]
        [Tooltip("Fraction of hull height that sits below the waterline.")]
        public float draftRatio = 0.22f;

        [Header("=== HEAVE (vertical) ===")]
        public float verticalSpring = 2.5f;
        public float bobFollowSpeed = 3f;
        public float maxVerticalSpeed = 8f;

        [Header("=== WAVE TILT ===")]
        [Range(0f, 1f)]
        [Tooltip("How strongly the hull conforms to the fitted wave plane.")]
        public float tiltInfluence = 0.55f;

        public float tiltSmooth = 2.5f;

        [Header("=== SLAM DETECTION ===")]
        [Tooltip("Downward bow impact speed (m/s) that counts as a slam.")]
        public float slamThreshold = 3.5f;

        /// <summary>Fired on bow slam with normalized intensity 0..1.</summary>
        public event System.Action<float> OnBowSlam;

        // ---- smoothed outputs (read by master_ship) ----
        public float HeaveVelocity { get; private set; }
        public float TargetY { get; private set; }
        public Vector3 SmoothedWaveNormal { get; private set; } = Vector3.up;
        public float Draft { get; private set; } = 2f;
        public float BowImmersion { get; private set; }

        private Vector3[] _pointsLocal;
        private float[] _surfaceCache;
        private bool _initialized;
        private float _prevBowSurfaceGap;

        private void Awake()
        {
            BuildPoints();
        }

        public void ConfigureFrom(Bounds bounds, float spring, float bob, float maxV, float influence, float smooth)
        {
            localBounds = bounds;
            verticalSpring = spring;
            bobFollowSpeed = bob;
            maxVerticalSpeed = maxV;
            tiltInfluence = influence;
            tiltSmooth = smooth;
            BuildPoints();
            _initialized = false;
        }

        private void BuildPoints()
        {
            // 9 hull sample points: center, bow, stern, 4 quarters, 2 midship sides.
            float L = localBounds.size.z;
            float B = localBounds.size.x;
            float y = localBounds.center.y;

            _pointsLocal = new[]
            {
                new Vector3(0f,        y, 0f),          // center
                new Vector3(0f,        y, L * 0.42f),   // bow
                new Vector3(0f,        y, -L * 0.42f),  // stern
                new Vector3(B * 0.42f, y, L * 0.28f),   // fwd starboard
                new Vector3(-B * 0.42f, y, L * 0.28f),  // fwd port
                new Vector3(B * 0.42f, y, -L * 0.28f),  // aft starboard
                new Vector3(-B * 0.42f, y, -L * 0.28f), // aft port
                new Vector3(B * 0.45f, y, 0f),          // mid starboard
                new Vector3(-B * 0.45f, y, 0f),         // mid port
            };

            _surfaceCache = new float[_pointsLocal.Length];
            _initialized = false;
        }

        /// <summary>
        /// Advance the buoyancy simulation. Called from master_ship.FixedUpdate
        /// BEFORE velocity composition so ordering is deterministic.
        /// </summary>
        public void Simulate(float dt)
        {
            var waves = OceanWaves.Instance;
            float waterY = waves != null ? waves.waterLevel : 0f;
            float t = Time.time;

            Draft = Mathf.Clamp(localBounds.size.y * draftRatio, 0.5f, 8f);

            // Sample the surface under each hull point.
            Vector3 centerWorld = transform.TransformPoint(_pointsLocal[0]);
            float sxx = 0f, szz = 0f, sxz = 0f, syx = 0f, syz = 0f;

            for (int i = 0; i < _pointsLocal.Length; i++)
            {
                Vector3 wp = i == 0 ? centerWorld : transform.TransformPoint(_pointsLocal[i]);
                float surf = waves != null ? waves.SurfaceY(wp.x, wp.z, t) : waterY;
                _surfaceCache[i] = surf;

                // Plane-fit accumulators in world-horizontal space around the center.
                float rx = wp.x - centerWorld.x;
                float rz = wp.z - centerWorld.z;
                float ry = surf - _surfaceCache[0];
                sxx += rx * rx; szz += rz * rz; sxz += rx * rz;
                syx += rx * ry; syz += rz * ry;
            }

            // Heave: weighted mean surface (center counts double).
            float meanSurface = (_surfaceCache[0] * 2f +
                                 _surfaceCache[1] + _surfaceCache[2] +
                                 (_surfaceCache[3] + _surfaceCache[4] + _surfaceCache[5] + _surfaceCache[6]) * 0.5f +
                                 (_surfaceCache[7] + _surfaceCache[8]) * 0.5f) / 7f;

            float rawTargetY = meanSurface - Draft - localBounds.min.y;
            if (!_initialized)
            {
                TargetY = rawTargetY;
                _initialized = true;
            }
            TargetY = Mathf.MoveTowards(TargetY, rawTargetY, bobFollowSpeed * dt);
            HeaveVelocity = Mathf.Clamp((TargetY - transform.position.y) * verticalSpring, -maxVerticalSpeed, maxVerticalSpeed);

            // Least-squares plane -> wave normal (world space).
            float det = sxx * szz - sxz * sxz;
            Vector3 waveNormal = Vector3.up;
            if (Mathf.Abs(det) > 1e-5f)
            {
                float gx = (szz * syx - sxz * syz) / det;
                float gz = (sxx * syz - sxz * syx) / det;
                waveNormal = new Vector3(-gx, 1f, -gz);
                if (waveNormal.y <= 0.05f) waveNormal = Vector3.up;
                waveNormal.Normalize();
            }

            Vector3 desiredUp = Vector3.Slerp(Vector3.up, waveNormal, tiltInfluence).normalized;
            SmoothedWaveNormal = Vector3.Slerp(SmoothedWaveNormal, desiredUp, tiltSmooth * dt).normalized;

            // Bow slam detection: bow point crossing from dry to deeply immersed.
            float bowSurface = _surfaceCache[1];
            Vector3 bowWorld = transform.TransformPoint(_pointsLocal[1]);
            float gap = bowWorld.y - bowSurface;           // >0 = bow above water
            BowImmersion = Mathf.Max(0f, -gap);

            float impactSpeed = (_prevBowSurfaceGap - gap) / Mathf.Max(dt, 1e-4f); // + = driving down into water
            if (_prevBowSurfaceGap > 0.05f && gap < -0.15f && impactSpeed > slamThreshold)
            {
                float intensity = Mathf.Clamp01((impactSpeed - slamThreshold) / 8f);
                OnBowSlam?.Invoke(intensity);
            }

            _prevBowSurfaceGap = gap;
        }

        private void OnDrawGizmosSelected()
        {
            if (_pointsLocal == null) return;
            Gizmos.color = Color.cyan;
            for (int i = 0; i < _pointsLocal.Length; i++)
            {
                Vector3 wp = transform.TransformPoint(_pointsLocal[i]);
                Gizmos.DrawWireSphere(wp, 0.35f);
                if (i < _surfaceCache.Length && _surfaceCache[i] != 0f)
                    Gizmos.DrawLine(wp, new Vector3(wp.x, _surfaceCache[i], wp.z));
            }
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, transform.position + SmoothedWaveNormal * 12f);
        }
    }
}

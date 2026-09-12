using UnityEngine;

namespace TFOU.Ocean
{
    /// <summary>
    /// The single source of truth for the ocean surface.
    /// Builds an 8-wave Beaufort-scale Gerstner spectrum and shares it between:
    ///   - the GPU  (Shader.SetGlobalVectorArray -> AutoOceanWater vertex shader)
    ///   - the CPU  (buoyancy sampling in ShipBuoyancy)
    /// Because both sides evaluate the identical analytic field, the ship always
    /// sits exactly on the waves you see. No mesh rewriting, no per-frame allocation.
    /// </summary>
    public class OceanWaves : MonoBehaviour
    {
        public const int WAVE_COUNT = 8;

        public static OceanWaves Instance { get; private set; }

        [Header("=== SEA STATE (Beaufort 0-10) ===")]
        [Range(0f, 10f)]
        [Tooltip("Beaufort sea state. 0 = glass, 3 = light breeze, 6 = rough, 9 = severe storm.")]
        public float seaState = 4f;

        [Range(0f, 360f)]
        [Tooltip("Direction the wind (and dominant swell) travels TOWARDS, in degrees.")]
        public float windDirectionDeg = 45f;

        [Header("=== FINE TUNING ===")]
        [Tooltip("Global multiplier on wave amplitude (legacy 'waveAmplitude').")]
        public float amplitudeMultiplier = 1f;

        [Range(0f, 1f)]
        [Tooltip("0 = round swell, 1 = sharp crests that start to curl.")]
        public float choppiness = 0.55f;

        [Tooltip("Multiplier on all wavelengths.")]
        public float lengthScale = 1f;

        [Tooltip("Multiplier on wave travel speed (1 = physical deep-water dispersion).")]
        public float speedMultiplier = 1f;

        [Tooltip("World Y of the calm water plane.")]
        public float waterLevel = 0f;

        // GPU payload: A = (dirX, dirZ, k, amplitude)   B = (omega, steepness, detailClass, 0)
        private readonly Vector4[] _dataA = new Vector4[WAVE_COUNT];
        private readonly Vector4[] _dataB = new Vector4[WAVE_COUNT];

        // CPU mirror of the same data (avoids re-reading GPU arrays).
        private float[] _dirX = new float[WAVE_COUNT];
        private float[] _dirZ = new float[WAVE_COUNT];
        private float[] _k = new float[WAVE_COUNT];
        private float[] _amp = new float[WAVE_COUNT];
        private float[] _omega = new float[WAVE_COUNT];
        private float[] _q = new float[WAVE_COUNT];

        private bool _dirty = true;

        // Snapshot of what generated the current spectrum (for dirty checks).
        private float _genSeaState = -1f, _genWind = -1f, _genAmp = -1f, _genChop = -1f, _genScale = -1f, _genSpeed = -1f;

        public float SignificantWaveHeight { get; private set; }
        public float TotalAmplitude { get; private set; }
        public Vector2 WindDirection { get; private set; } = new Vector2(0.7f, 0.7f);

        private static readonly int GerstAId = Shader.PropertyToID("_GerstA");
        private static readonly int GerstBId = Shader.PropertyToID("_GerstB");
        private static readonly int OceanTimeId = Shader.PropertyToID("_OceanTime");

        // Approximate significant wave height (metres) per Beaufort force.
        private static readonly float[] BeaufortHs = { 0f, 0.1f, 0.25f, 0.5f, 0.9f, 1.6f, 2.5f, 3.6f, 5.0f, 6.8f, 9.0f };

        /// <summary>Finds the shared instance or creates one.</summary>
        public static OceanWaves Ensure()
        {
            if (Instance != null) return Instance;

            Instance = FindFirstObjectByType<OceanWaves>();
            if (Instance == null)
            {
                var go = new GameObject("OceanWaves");
                Instance = go.AddComponent<OceanWaves>();
            }
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Rebuild(force: true);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            if (_dirty) Rebuild(force: false);
            PushTime();
        }

        private void OnValidate()
        {
            _dirty = true;
        }

        public void MarkDirty() => _dirty = true;

        private void PushTime()
        {
            // x = time, yz = wind direction, w = normalized sea state (shader uses it for foam/glitter bias)
            Shader.SetGlobalVector(OceanTimeId, new Vector4(Time.time, WindDirection.x, WindDirection.y, seaState / 10f));
        }

        // ===================== SPECTRUM =====================

        private void Rebuild(bool force)
        {
            if (!force &&
                _genSeaState == seaState && _genWind == windDirectionDeg && _genAmp == amplitudeMultiplier &&
                _genChop == choppiness && _genScale == lengthScale && _genSpeed == speedMultiplier)
            {
                _dirty = false;
                return;
            }

            _genSeaState = seaState; _genWind = windDirectionDeg; _genAmp = amplitudeMultiplier;
            _genChop = choppiness; _genScale = lengthScale; _genSpeed = speedMultiplier;
            _dirty = false;

            int force01 = Mathf.Clamp(Mathf.RoundToInt(seaState), 0, 10);
            float hs = Mathf.Lerp(BeaufortHs[force01], BeaufortHs[Mathf.Min(force01 + 1, 10)], Mathf.Clamp01(seaState - force01));
            hs = Mathf.Max(hs, 0.02f);
            SignificantWaveHeight = hs;

            // Peak wavelength grows with sea state; everything else is banded off it.
            float lp = Mathf.Clamp(hs * 22f, 6f, 190f) * Mathf.Max(0.1f, lengthScale);

            float[] lengthFracs = { 1.60f, 1.15f, 0.85f, 0.62f, 0.44f, 0.30f, 0.20f, 0.13f };
            float[] ampFracs = { 0.36f, 0.24f, 0.15f, 0.10f, 0.065f, 0.045f, 0.025f, 0.015f };
            float[] dirOffsets = { 0f, 13f, -16f, 28f, -31f, 47f, -54f, 71f };

            float windRad = windDirectionDeg * Mathf.Deg2Rad;
            WindDirection = new Vector2(Mathf.Sin(windRad), Mathf.Cos(windRad));

            float ampSum = 0f;
            for (int i = 0; i < WAVE_COUNT; i++)
            {
                float dirRad = windRad + dirOffsets[i] * Mathf.Deg2Rad;
                float dx = Mathf.Sin(dirRad);
                float dz = Mathf.Cos(dirRad);

                // 6 m floor keeps the shortest waves resolvable by the near ring's cells.
                float L = Mathf.Max(6f, lp * lengthFracs[i]);
                float k = 2f * Mathf.PI / L;
                float a = Mathf.Max(0.0005f, hs * 0.5f * ampFracs[i] * amplitudeMultiplier);

                // Deep-water dispersion: c = sqrt(g/k), omega = k*c
                float c = Mathf.Sqrt(9.81f / k) * Mathf.Max(0.05f, speedMultiplier);
                float omega = k * c;

                // Steepness budget shared across waves; clamp per wave so crests never loop over.
                float q = Mathf.Min(choppiness / (k * a * WAVE_COUNT), 0.9f / (k * a));

                // Detail class gates each wave to rings whose cell density can
                // resolve it: long swell everywhere, short chop only up close.
                float detailClass = L >= 60f ? 0f : (L >= 34f ? 0.5f : 1f);

                _dirX[i] = dx; _dirZ[i] = dz; _k[i] = k; _amp[i] = a; _omega[i] = omega; _q[i] = q;

                _dataA[i] = new Vector4(dx, dz, k, a);
                _dataB[i] = new Vector4(omega, q, detailClass, 0f);

                ampSum += a;
            }
            TotalAmplitude = ampSum;

            Shader.SetGlobalVectorArray(GerstAId, _dataA);
            Shader.SetGlobalVectorArray(GerstBId, _dataB);
            PushTime();
        }

        // ===================== CPU SAMPLING (mirrors the vertex shader exactly) =====================

        /// <summary>Calm-water height at world (x,z). Add waterLevel for absolute Y.</summary>
        public float SampleHeight(float x, float z, float t)
        {
            float h = 0f;
            for (int i = 0; i < WAVE_COUNT; i++)
            {
                float phi = _k[i] * (_dirX[i] * x + _dirZ[i] * z) - _omega[i] * t;
                h += _amp[i] * Mathf.Sin(phi);
            }
            return h;
        }

        /// <summary>World Y of the ocean surface (waves + water level).</summary>
        public float SurfaceY(float x, float z, float t) => waterLevel + SampleHeight(x, z, t);

        /// <summary>
        /// Full Gerstner displacement (horizontal choppiness included) and analytic normal.
        /// Identical math to the shader's ApplyGerstner().
        /// </summary>
        public void SampleFull(float x, float z, float t, out Vector3 displacement, out Vector3 normal)
        {
            float hx = 0f, hy = 0f, hz = 0f;
            float nx = 0f, ny = 1f, nz = 0f;

            for (int i = 0; i < WAVE_COUNT; i++)
            {
                float phi = _k[i] * (_dirX[i] * x + _dirZ[i] * z) - _omega[i] * t;
                float s = Mathf.Sin(phi);
                float c = Mathf.Cos(phi);

                float qa = _q[i] * _amp[i];
                hx += _dirX[i] * qa * c;
                hz += _dirZ[i] * qa * c;
                hy += _amp[i] * s;

                nx -= _dirX[i] * _k[i] * _amp[i] * c;
                nz -= _dirZ[i] * _k[i] * _amp[i] * c;
                ny -= _q[i] * _k[i] * _amp[i] * s;
            }

            displacement = new Vector3(hx, hy, hz);
            normal = new Vector3(nx, Mathf.Max(ny, 0.15f), nz).normalized;
        }

        public Vector3 SampleNormal(float x, float z, float t)
        {
            SampleFull(x, z, t, out _, out Vector3 n);
            return n;
        }
    }
}

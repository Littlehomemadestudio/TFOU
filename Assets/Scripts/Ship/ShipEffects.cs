using UnityEngine;
using TFOU.Ocean;

namespace TFOU.Ship
{
    /// <summary>
    /// Procedural ship FX - no prefabs, no textures, no assets required.
    ///   - Bow spray (L/R): continuous mist at speed + heavy bursts on bow slam
    ///   - Bow wave sheet: white water pushed aside at the stem
    ///   - Stern wake: foam churn behind the transom
    ///   - Funnel smoke: dark exhaust that thickens with engine load
    /// All systems use the generated AutoSoftParticleMat (radial soft shader).
    /// </summary>
    public class ShipEffects : MonoBehaviour
    {
        public bool bowSpray = true;
        public bool sternWake = true;
        public bool funnelSmoke = true;

        [Range(0f, 2f)] public float densityScale = 1f;

        private Transform _ship;
        private IShipState _state;
        private ShipBuoyancy _buoyancy;
        private Bounds _bounds;
        private Material _mat;

        private ParticleSystem _sprayL, _sprayR, _bowSheet, _sternWake, _smoke;
        private Transform _sprayLPos, _sprayRPos, _bowSheetPos, _sternPos, _funnelPos;

        public void Initialize(Transform ship, IShipState state, ShipBuoyancy buoyancy, Bounds localBounds, Material softMat)
        {
            _ship = ship;
            _state = state;
            _buoyancy = buoyancy;
            _bounds = localBounds;
            _mat = softMat;

            if (_mat == null)
            {
                Debug.LogWarning("[ShipEffects] soft particle material missing - FX disabled.");
                enabled = false;
                return;
            }

            if (_buoyancy != null) _buoyancy.OnBowSlam += HandleBowSlam;

            BuildSystems();
        }

        private void OnDestroy()
        {
            if (_buoyancy != null) _buoyancy.OnBowSlam -= HandleBowSlam;
        }

        // ===================== CONSTRUCTION =====================

        private static GameObject Anchor(string name, Transform parent, Vector3 localPos, Vector3 localEuler)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localEulerAngles = localEuler;
            return go;
        }

        private ParticleSystem NewPS(GameObject host, int maxParticles)
        {
            var ps = host.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            main.playOnAwake = false;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.material = _mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.None;
            return ps;
        }

        private static Gradient AlphaFade(Color c)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.55f, 0.35f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        private void BuildSystems()
        {
            float L = _bounds.size.z;
            float B = _bounds.size.x;
            float waterlineLocal = _bounds.min.y + _bounds.size.y * 0.28f;

            // --- Bow spray: two cones angled out/up from the stem ---
            if (bowSpray)
            {
                var lp = new Vector3(B * 0.30f, waterlineLocal, L * 0.44f);
                var rp = new Vector3(-B * 0.30f, waterlineLocal, L * 0.44f);

                _sprayLPos = Anchor("FX_SprayL", transform, lp, new Vector3(-35f, 32f, 0f)).transform;
                _sprayRPos = Anchor("FX_SprayR", transform, rp, new Vector3(-35f, -32f, 0f)).transform;
                _sprayL = MakeSpray(_sprayLPos.gameObject, 240);
                _sprayR = MakeSpray(_sprayRPos.gameObject, 240);

                // --- Bow sheet: wide low fan of white water at the stem ---
                _bowSheetPos = Anchor("FX_BowSheet", transform, new Vector3(0f, waterlineLocal - 0.4f, L * 0.48f), new Vector3(-8f, 0f, 0f)).transform;
                _bowSheet = NewPS(_bowSheetPos.gameObject, 200);
                var bsMain = _bowSheet.main;
                bsMain.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
                bsMain.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
                bsMain.startSize = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
                bsMain.startColor = new Color(0.92f, 0.96f, 1f, 0.55f);
                bsMain.gravityModifier = 0.25f;
                var bsShape = _bowSheet.shape;
                bsShape.enabled = true;
                bsShape.shapeType = ParticleSystemShapeType.Cone;
                bsShape.angle = 55f;
                bsShape.radius = Mathf.Max(0.6f, B * 0.18f);
                var bsCol = _bowSheet.colorOverLifetime;
                bsCol.enabled = true;
                bsCol.color = AlphaFade(Color.white);
                var bsSize = _bowSheet.sizeOverLifetime;
                bsSize.enabled = true;
                bsSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.8f)));
            }

            // --- Stern wake foam ---
            if (sternWake)
            {
                _sternPos = Anchor("FX_SternWake", transform, new Vector3(0f, waterlineLocal - 0.5f, -L * 0.5f), new Vector3(8f, 180f, 0f)).transform;
                _sternWake = NewPS(_sternPos.gameObject, 300);
                var swMain = _sternWake.main;
                swMain.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.5f);
                swMain.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
                swMain.startSize = new ParticleSystem.MinMaxCurve(2f, 5f);
                swMain.startColor = new Color(0.88f, 0.94f, 1f, 0.5f);
                swMain.gravityModifier = 0f;
                var swShape = _sternWake.shape;
                swShape.enabled = true;
                swShape.shapeType = ParticleSystemShapeType.Box;
                swShape.scale = new Vector3(Mathf.Max(1f, B * 0.55f), 0.3f, 1f);
                var swCol = _sternWake.colorOverLifetime;
                swCol.enabled = true;
                swCol.color = AlphaFade(Color.white);
                var swSize = _sternWake.sizeOverLifetime;
                swSize.enabled = true;
                swSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 2.4f)));
                var swRot = _sternWake.rotationOverLifetime;
                swRot.enabled = true;
                swRot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            }

            // --- Funnel smoke ---
            if (funnelSmoke)
            {
                _funnelPos = Anchor("FX_FunnelSmoke", transform,
                    new Vector3(0f, _bounds.max.y + 0.5f, -L * 0.06f), Vector3.zero).transform;
                _smoke = NewPS(_funnelPos.gameObject, 160);
                var smMain = _smoke.main;
                smMain.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 6f);
                smMain.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.5f);
                smMain.startSize = new ParticleSystem.MinMaxCurve(1.5f, 3f);
                smMain.startColor = new Color(0.16f, 0.16f, 0.17f, 0.42f);
                smMain.gravityModifier = -0.02f;
                var smShape = _smoke.shape;
                smShape.enabled = true;
                smShape.shapeType = ParticleSystemShapeType.Cone;
                smShape.angle = 8f;
                smShape.radius = 0.5f;
                var smCol = _smoke.colorOverLifetime;
                smCol.enabled = true;
                var g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(new Color(0.2f, 0.2f, 0.21f), 0f), new GradientColorKey(new Color(0.45f, 0.45f, 0.46f), 1f) },
                    new[] { new GradientAlphaKey(0.45f, 0f), new GradientAlphaKey(0.28f, 0.5f), new GradientAlphaKey(0f, 1f) });
                smCol.color = g;
                var smSize = _smoke.sizeOverLifetime;
                smSize.enabled = true;
                smSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.6f, 1.6f), new Keyframe(1f, 3.2f)));
                var smRot = _smoke.rotationOverLifetime;
                smRot.enabled = true;
                smRot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
                var smVel = _smoke.velocityOverLifetime;
                smVel.enabled = true;
                smVel.y = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
            }

            _sprayL?.Play(); _sprayR?.Play(); _bowSheet?.Play(); _sternWake?.Play(); _smoke?.Play();
        }

        private ParticleSystem MakeSpray(GameObject host, int maxParticles)
        {
            var ps = NewPS(host, maxParticles);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 11f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.8f);
            main.startColor = new Color(0.95f, 0.98f, 1f, 0.75f);
            main.gravityModifier = 1.1f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 0.4f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = AlphaFade(Color.white);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(1f, 1.6f)));
            return ps;
        }

        // ===================== EVENTS =====================

        private void HandleBowSlam(float intensity)
        {
            if (_sprayL == null || _sprayR == null) return;

            int count = Mathf.RoundToInt(Mathf.Lerp(14f, 46f, intensity) * densityScale);
            EmitBurst(_sprayL, count, intensity);
            EmitBurst(_sprayR, count, intensity);
        }

        private void EmitBurst(ParticleSystem ps, int count, float intensity)
        {
            for (int i = 0; i < count; i++)
            {
                var p = new ParticleSystem.EmitParams();
                p.position = ps.transform.position + Random.insideUnitSphere * 0.8f;
                p.velocity = ps.transform.rotation *
                    new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(0.55f, 1.15f), Random.Range(0.15f, 0.6f)).normalized *
                    Random.Range(6f, 14f) * (0.6f + intensity);
                p.startLifetime = Random.Range(0.5f, 1.1f);
                p.startSize = Random.Range(0.8f, 2.4f) * (0.7f + intensity * 0.6f);
                p.startColor = new Color(0.95f, 0.98f, 1f, Random.Range(0.5f, 0.9f));
                ps.Emit(p, 1);
            }
        }

        // ===================== FRAME UPDATE =====================

        private void Update()
        {
            if (_state == null) return;

            float speed01 = _state.Speed01;
            float load = _state.EngineLoad01;
            float seaChop = OceanWaves.Instance != null ? Mathf.Clamp01(OceanWaves.Instance.seaState / 8f) : 0.2f;

            if (_sprayL != null)
            {
                float rate = Mathf.Max(0f, (speed01 - 0.25f) * 90f) * densityScale + seaChop * speed01 * 25f;
                SetRate(_sprayL, rate);
                SetRate(_sprayR, rate);
            }
            if (_bowSheet != null)
                SetRate(_bowSheet, Mathf.Max(0f, (speed01 - 0.15f) * 55f) * densityScale);

            if (_sternWake != null)
                SetRate(_sternWake, Mathf.Max(0f, speed01 * 70f - 4f) * densityScale);

            if (_smoke != null)
                SetRate(_smoke, (2f + load * 14f) * densityScale);
        }

        private static void SetRate(ParticleSystem ps, float rate)
        {
            var em = ps.emission;
            em.rateOverTime = rate;
        }

        public void SetQualityDensity(float scale)
        {
            densityScale = Mathf.Clamp(scale, 0f, 2f);
            if (_sprayL != null) _sprayL.Simulate(0f, true, true);
        }
    }
}

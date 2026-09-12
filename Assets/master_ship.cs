using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TFOU.Ocean;
using TFOU.Ship;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// master_ship - naval vessel orchestrator (TFOU).
///
/// Attach to any ship root with a Rigidbody. Everything else bootstraps itself:
///   - OceanWaves      shared 8-wave Beaufort Gerstner sea (CPU + GPU in sync)
///   - water rings     near / mid / horizon GPU-displaced meshes (layer: Water)
///   - ShipBuoyancy    9-point hull sampling, plane-fit pitch/roll, bow slams
///   - ShipCameraRig   4 cinematic camera modes with collision + shake
///   - ShipEffects     procedural bow spray, wake foam, funnel smoke
///   - OceanQuality    HDR, SMAA, ACES tonemapping, bloom, vignette
///   - planar reflections (High/Ultra quality)
///
/// Controls:
///   W/S or sticks  - engine ahead / astern        Shift or RT - flank boost
///   A/D or sticks  - rudder                       Space or B  - all-stop brake
///   C / Tab / Y    - cycle camera mode            RMB drag    - free orbit
///   Wheel / dpad   - zoom                         O / P       - sea state -/+
///   F              - toggle telemetry HUD
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class master_ship : MonoBehaviour, IShipState
{
    public enum QualityTier { Low, Medium, High, Ultra }

    [Header("=== ENGINE ===")]
    public float maxSpeed = 36f;
    public float maxReverseSpeed = 12f;
    public float acceleration = 6f;
    public float deceleration = 6f;
    public float propulsionForce = 12f;
    [Tooltip("Flank speed multiplier while holding boost.")]
    public float boostMultiplier = 1.28f;
    [Tooltip("Lateral slip when carving hard turns (0 = train tracks).")]
    [Range(0f, 0.6f)] public float driftFactor = 0.22f;
    [Tooltip("Quadratic hydrodynamic drag while coasting (no throttle).")]
    public float hydrodynamicDrag = 0.02f;
    [Tooltip("Slow and shudder when the keel touches the seabed.")]
    public bool enableGrounding = true;

    [Header("=== TEST MAP ===")]
    [Tooltip("Generate a procedural island + slalom-course test map at runtime.")]
    public bool autoGenerateTestMap = true;
    public int mapSeed = 1337;

    [Header("=== STEERING FEEL ===")]
    public float maxTurnRate = 20f;
    public float rudderResponsiveness = 1.6f;
    [Range(0f, 1f)] public float highSpeedTurnPenalty = 0.25f;
    public float turnHeelDegrees = 12f;
    public float accelPitchDegrees = 5f;
    public float heelSmooth = 3f;
    public float pitchSmooth = 3f;

    [Header("=== FLOATING (multi-point buoyancy) ===")]
    public float verticalSpring = 2.5f;
    public float bobFollowSpeed = 3f;
    public float maxVerticalSpeed = 8f;
    [Range(0f, 1f)] public float tiltInfluence = 0.55f;
    public float tiltSmooth = 2.5f;
    public float rotationSmooth = 8f;

    [Header("=== SEA STATE (Beaufort) ===")]
    [Range(0f, 10f)]
    [Tooltip("0 = glass, 4 = moderate, 7 = high, 10 = hurricane. O/P keys adjust live.")]
    public float seaState = 4f;
    [Range(0f, 360f)] public float windDirectionDeg = 45f;
    [Tooltip("Legacy amplitude multiplier.")]
    public float waveAmplitude = 1.1f;
    [Range(0f, 1f)] public float waveChoppiness = 0.55f;
    public float waveLengthScale = 1f;
    public float waveSpeedMultiplier = 1f;

    [Header("=== WATER LOOK ===")]
    public Color deepColor = new Color(0.008f, 0.09f, 0.16f, 1f);
    public Color crestColor = new Color(0.02f, 0.22f, 0.30f, 1f);
    public Color foamColor = new Color(0.92f, 0.97f, 1f, 1f);
    public Color horizonSkyColor = new Color(0.45f, 0.62f, 0.72f, 1f);
    public Color zenithSkyColor = new Color(0.10f, 0.28f, 0.55f, 1f);
    public Color subsurfaceColor = new Color(0.05f, 0.45f, 0.42f, 1f);

    [Range(0.5f, 8f)] public float fresnelPower = 3f;
    [Range(0f, 2f)] public float reflectionStrength = 1f;
    [Range(8f, 2048f)] public float sunSpecPower = 256f;
    [Range(0f, 8f)] public float sunSpecIntensity = 2f;
    [Range(0f, 2f)] public float detailStrength = 0.5f;
    [Range(0.01f, 4f)] public float detailScale = 0.35f;
    [Range(0f, 4f)] public float detailSpeed = 1f;
    [Range(0f, 1f)] public float foamThreshold = 0.55f;
    [Range(0f, 3f)] public float foamAmount = 1.2f;
    [Range(0f, 3f)] public float hullFoamStrength = 1.4f;
    [Range(0f, 3f)] public float wakeStrength = 1.2f;
    [Range(0f, 3f)] public float subsurfaceStrength = 0.5f;
    [Range(0.5f, 80f)] public float absorptionDepth = 14f;
    [Range(0f, 2f)] public float refractionStrength = 0.5f;
    [Range(0f, 3f)] public float contactFoamStrength = 1.3f;

    [Header("=== WATER RINGS ===")]
    public bool autoGenerateWater = true;
    public bool autoSetupFog = true;
    public float waterLevel = 0f;
    [Tooltip("Fine ring around the ship.")]
    public float nearRingSize = 420f;
    public int nearRingResolution = 256;
    [Tooltip("Mid ring (legacy waterSize also feeds this).")]
    public float waterSize = 3000f;
    public int waterResolution = 96;
    public float horizonWaterSize = 30000f;
    public bool waterFollowsShip = true;

    [Header("=== QUALITY ===")]
    public QualityTier qualityTier = QualityTier.High;
    public bool planarReflections = true;
    [Range(0.125f, 1f)] public float reflectionResolutionScale = 0.5f;
    public bool bowSpray = true;
    public bool sternWakeFoam = true;
    public bool funnelSmoke = true;

    [Header("=== CAMERA ===")]
    public bool autoFitCameraToShipSize = true;
    public bool cameraAutoFollowShipYaw = true;
    public float yawFollowSpeed = 1.8f;
    public float defaultDistance = 55f;
    public float tacticalDistance = 120f;
    public float minDistance = 18f;
    public float baseFOV = 60f;
    public float maxSpeedFOV = 72f;
    public float cameraOrbitSpeed = 3f;
    public float cameraZoomSpeed = 10f;
    [Range(5f, 80f)] public float minPitch = 10f;
    [Range(5f, 80f)] public float maxPitch = 65f;

    [Header("=== HUD ===")]
    public bool showTelemetryHud = true;

    // ---------- runtime ----------
    private Rigidbody rb;
    private Camera mainCam;
    private Bounds localBounds;
    private Vector3 cameraPivotOffset;

    private OceanWaves waves;
    private ShipBuoyancy buoyancy;
    private ShipCameraRig camRig;
    private ShipEffects effects;
    private WaterPlanarReflections planarRefl;

    private Material waterMat;       // shared by all three rings
    private Material softParticleMat;
    private bool usingCustomShader;

    private Transform nearTransform, midTransform, horizonTransform;
    private float nearCell, midCell;

    private float currentSpeed;
    private float currentRudder;
    private float engineLoad;
    private float shipYaw;
    private float smoothedHeel;
    private float smoothedPitch;
    private bool grounded;
    private float hullRadius = 20f;
    private bool initialized;

    private GUIStyle hudStyle;

    private const string SHADER_DIR = "Assets/AutoGeneratedWater";
    private const string SHADER_RES_DIR = "Assets/AutoGeneratedWater/Resources";
    private const string SHADER_PATH = "Assets/AutoGeneratedWater/AutoOceanWater.shader";
    private const string MAT_PATH = "Assets/AutoGeneratedWater/Resources/AutoOceanWaterMat.mat";
    private const string SOFT_SHADER_PATH = "Assets/AutoGeneratedWater/AutoSoftParticle.shader";
    private const string SOFT_MAT_PATH = "Assets/AutoGeneratedWater/Resources/AutoSoftParticleMat.mat";

    // ================= IShipState =================
    public float Speed01 => Mathf.Clamp01(Mathf.Abs(currentSpeed) / Mathf.Max(1f, maxSpeed));
    public float Rudder01 => currentRudder;
    public float HeelDegrees => smoothedHeel;
    public float SpeedKnots => currentSpeed * 1.94384f;
    public float EngineLoad01 => engineLoad;
    public bool IsGrounded => grounded;

    // ================= LIFECYCLE =================

    private void Awake()
    {
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        rb = GetComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        rb.solverIterations = 6;
        rb.solverVelocityIterations = 4;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.constraints = RigidbodyConstraints.None;

        CalculateLocalBounds();
        hullRadius = Mathf.Max(localBounds.extents.x, localBounds.extents.z) * 0.85f;
        cameraPivotOffset = localBounds.center + Vector3.up * localBounds.size.y * 0.25f;

        // Hulls without colliders pass through the world; fit an invisible box.
        if (GetComponentInChildren<Collider>() == null)
        {
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = localBounds.center;
            box.size = Vector3.Scale(localBounds.size, new Vector3(0.8f, 0.85f, 0.92f));
        }

        EnsureCamera();
        SetupOceanWaves();
        SetupMaterials();
        if (autoGenerateWater) CreateWaterRings();
        SetupBuoyancy();
        SetupCameraRig();
        SetupEffects();
        if (autoGenerateTestMap) TFOU.World.TestMapGenerator.Ensure(mapSeed, transform);
        ApplyQualityTier();
        if (autoSetupFog) SetupFog();

        shipYaw = transform.eulerAngles.y;
        initialized = true;

        if (mainCam != null)
        {
            float neededFar = Mathf.Max(horizonWaterSize * 1.4f, 20000f);
            mainCam.farClipPlane = Mathf.Max(mainCam.farClipPlane, neededFar);
        }
    }

    private void OnDestroy()
    {
        if (planarRefl != null) Destroy(planarRefl.gameObject);
        if (nearTransform != null) Destroy(nearTransform.gameObject);
        if (midTransform != null) Destroy(midTransform.gameObject);
        if (horizonTransform != null) Destroy(horizonTransform.gameObject);
    }

    private void Update()
    {
        if (!initialized) return;

        // Live tuning keys
        if (ShipInput.KeyDown(KeyCode.O)) seaState = Mathf.Max(0f, seaState - 0.5f);
        if (ShipInput.KeyDown(KeyCode.P)) seaState = Mathf.Min(10f, seaState + 0.5f);
        if (ShipInput.KeyDown(KeyCode.F)) showTelemetryHud = !showTelemetryHud;
        if (waves != null && Mathf.Abs(waves.seaState - seaState) > 0.001f) waves.seaState = seaState;
    }

    private void FixedUpdate()
    {
        if (!initialized || rb == null) return;

        float dt = Time.fixedDeltaTime;

        // ---------- engine telegraph ----------
        float throttle = ShipInput.Throttle();
        float steer = ShipInput.Steer();
        bool boost = ShipInput.Boost();
        bool brake = ShipInput.Brake();

        float targetSpeed = 0f;
        if (brake) targetSpeed = 0f;
        else if (throttle > 0.01f) targetSpeed = boost ? maxSpeed * boostMultiplier : maxSpeed;
        else if (throttle < -0.01f) targetSpeed = -maxReverseSpeed;

        float rate = brake ? deceleration * 2.5f : (Mathf.Abs(throttle) > 0.01f ? acceleration : deceleration);
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * dt);

        // Hydrodynamic drag: coast down quadratically, not linearly.
        if (Mathf.Abs(throttle) < 0.01f && !brake)
            currentSpeed -= Mathf.Sign(currentSpeed) * hydrodynamicDrag * currentSpeed * currentSpeed * dt;
        if (Mathf.Abs(currentSpeed) < 0.03f) currentSpeed = 0f;

        float loadTarget = Mathf.Clamp01(Mathf.Abs(currentSpeed) / Mathf.Max(1f, maxSpeed) + (boost && throttle > 0.5f ? 0.25f : 0f));
        engineLoad = Mathf.MoveTowards(engineLoad, loadTarget, dt * 0.8f);

        currentRudder = Mathf.MoveTowards(currentRudder, steer, rudderResponsiveness * dt);

        // ---------- buoyancy (samples OceanWaves at 9 hull points) ----------
        if (buoyancy != null) buoyancy.Simulate(dt);

        // ---------- heading ----------
        float absSpeed = Mathf.Abs(currentSpeed);
        float speedRatio = Mathf.Clamp01(absSpeed / Mathf.Max(1f, maxSpeed));
        float moveFactor = Mathf.Clamp01(absSpeed / 2f);
        float turnPenalty = 1f - highSpeedTurnPenalty * speedRatio;

        // Rudder effect reverses when making sternway, and hard turns scrub speed.
        float steerSign = currentSpeed < -0.5f ? -1f : 1f;
        float turnRateDeg = currentRudder * maxTurnRate * moveFactor * turnPenalty * steerSign;
        currentSpeed *= 1f - Mathf.Clamp01(Mathf.Abs(turnRateDeg) / Mathf.Max(1f, maxTurnRate)) * speedRatio * 0.25f * dt;
        shipYaw += turnRateDeg * dt;
        shipYaw = Mathf.Repeat(shipYaw + 180f, 360f) - 180f;

        // ---------- orientation: wave plane + dynamic heel/pitch ----------
        Vector3 waterUp = buoyancy != null ? buoyancy.SmoothedWaveNormal : Vector3.up;
        Vector3 forwardYaw = Quaternion.Euler(0f, shipYaw, 0f) * Vector3.forward;
        Vector3 forwardOnWater = Vector3.ProjectOnPlane(forwardYaw, waterUp);
        if (forwardOnWater.sqrMagnitude < 0.0001f) forwardOnWater = transform.forward;
        forwardOnWater.Normalize();

        Quaternion waterRot = Quaternion.LookRotation(forwardOnWater, waterUp);

        float heelAuthority = Mathf.Clamp01(absSpeed / 8f + 0.1f);
        float targetHeel = -currentRudder * heelAuthority * turnHeelDegrees;
        float targetPitch = -throttle * accelPitchDegrees;

        smoothedHeel = Mathf.MoveTowards(smoothedHeel, targetHeel, heelSmooth * dt);
        smoothedPitch = Mathf.MoveTowards(smoothedPitch, targetPitch, pitchSmooth * dt);

        Quaternion targetRot = waterRot * Quaternion.Euler(smoothedPitch, 0f, smoothedHeel);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, rotationSmooth * dt));
        shipYaw = Mathf.Repeat(rb.rotation.eulerAngles.y + 180f, 360f) - 180f;

        // ---------- velocity: thrust + carve slip + heave ----------
        Vector3 vel = rb.linearVelocity;
        Vector3 horizontalVel = new Vector3(vel.x, 0f, vel.z);

        Vector3 targetVel = forwardOnWater * currentSpeed;
        targetVel.y = 0f;

        // inertia drift: the stern slides wide in hard turns
        float slip = -(turnRateDeg / Mathf.Max(1f, maxTurnRate)) * speedRatio * driftFactor * currentSpeed;
        targetVel += transform.right * slip;

        Vector3 newHorizontal = Vector3.MoveTowards(horizontalVel, targetVel, propulsionForce * dt);
        float heave = buoyancy != null ? buoyancy.HeaveVelocity : 0f;

        // Grounding: keel on the seabed -> stop sinking, scrub speed, shudder.
        float seabedY = float.NegativeInfinity;
        grounded = enableGrounding && CheckGrounding(out seabedY);
        if (grounded)
        {
            Vector3 keelP = transform.TransformPoint(new Vector3(localBounds.center.x, localBounds.min.y, localBounds.center.z));
            float penetration = seabedY + 0.35f - keelP.y;
            heave = penetration > 0f ? Mathf.Max(heave, penetration * 5f) : Mathf.Max(heave, 0f);
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, 8f * dt);
            if (Mathf.Abs(currentSpeed) > 4f && camRig != null && Time.frameCount % 10 == 0)
                camRig.AddShake(0.12f);
        }

        rb.linearVelocity = new Vector3(newHorizontal.x, heave, newHorizontal.z);
    }

    private bool CheckGrounding(out float seabed)
    {
        seabed = float.NegativeInfinity;

        Vector3 keelP = transform.TransformPoint(new Vector3(localBounds.center.x, localBounds.min.y, localBounds.center.z));
        float maxD = transform.position.y + 4f - keelP.y + 12f;
        if (maxD <= 0.5f) return false;

        Vector3 fwdFlat = transform.forward;
        fwdFlat.y = 0f;
        if (fwdFlat.sqrMagnitude < 1e-6f) fwdFlat = Vector3.forward;
        fwdFlat.Normalize();

        float reach = localBounds.extents.z * 0.6f;
        Vector3[] origins =
        {
            transform.position + fwdFlat * reach + Vector3.up * 4f,
            transform.position + Vector3.up * 4f,
            transform.position - fwdFlat * reach + Vector3.up * 4f
        };

        bool found = false;
        var hits = new RaycastHit[8];
        foreach (Vector3 o in origins)
        {
            int n = Physics.RaycastNonAlloc(o, Vector3.down, hits, maxD, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider == null) continue;
                if (hits[i].collider.transform.IsChildOf(transform)) continue;
                if (hits[i].point.y > seabed)
                {
                    seabed = hits[i].point.y;
                    found = true;
                }
            }
        }
        return found && seabed > keelP.y - 0.8f;
    }

    private void OnCollisionEnter(Collision collision)
    {
        float impact = collision.relativeVelocity.magnitude;
        if (impact < 1.5f) return;

        // Impacts bleed speed and punch the camera; beaching at flank speed hurts.
        currentSpeed *= Mathf.Max(0f, 1f - impact / 25f);
        if (camRig != null)
        {
            camRig.AddShake(Mathf.Min(impact / 10f, 1.2f));
            camRig.AddFovPunch(Mathf.Min(impact / 12f, 3f));
        }
    }

    private void LateUpdate()
    {
        if (!initialized) return;

        FollowWaterRings();
        PushShipUniforms();
    }

    // ================= SUBSYSTEM SETUP =================

    private void EnsureCamera()
    {
        mainCam = Camera.main;
        if (mainCam != null) return;

        var camObj = new GameObject("Main Camera");
        camObj.tag = "MainCamera";
        mainCam = camObj.AddComponent<Camera>();
        mainCam.clearFlags = CameraClearFlags.Skybox;
        mainCam.nearClipPlane = 0.3f;
        mainCam.farClipPlane = 45000f;

        if (FindFirstObjectByType<AudioListener>() == null)
            camObj.AddComponent<AudioListener>();
    }

    private void SetupOceanWaves()
    {
        waves = OceanWaves.Ensure();
        waves.seaState = seaState;
        waves.windDirectionDeg = windDirectionDeg;
        waves.amplitudeMultiplier = waveAmplitude;
        waves.choppiness = waveChoppiness;
        waves.lengthScale = waveLengthScale;
        waves.speedMultiplier = waveSpeedMultiplier;
        waves.waterLevel = waterLevel;
        waves.MarkDirty();
    }

    private void SetupBuoyancy()
    {
        buoyancy = GetComponent<ShipBuoyancy>();
        if (buoyancy == null) buoyancy = gameObject.AddComponent<ShipBuoyancy>();
        buoyancy.ConfigureFrom(localBounds, verticalSpring, bobFollowSpeed, maxVerticalSpeed, tiltInfluence, tiltSmooth);
        buoyancy.OnBowSlam += HandleBowSlam;
    }

    private void HandleBowSlam(float intensity)
    {
        if (camRig != null)
        {
            camRig.AddShake(intensity * 0.6f);
            camRig.AddFovPunch(intensity * 2f);
        }
    }

    private void SetupCameraRig()
    {
        if (mainCam == null) return;

        camRig = mainCam.GetComponent<ShipCameraRig>();
        if (camRig == null) camRig = mainCam.gameObject.AddComponent<ShipCameraRig>();

        if (autoFitCameraToShipSize)
        {
            defaultDistance = Mathf.Clamp(localBounds.size.z * 1.6f, 35f, 200f);
            tacticalDistance = defaultDistance * 2.5f;
            minDistance = Mathf.Clamp(localBounds.size.z * 0.45f, 15f, defaultDistance * 0.5f);
        }

        camRig.defaultDistance = defaultDistance;
        camRig.tacticalDistance = tacticalDistance;
        camRig.minDistance = minDistance;
        camRig.maxZoomOut = Mathf.Max(tacticalDistance * 3f, 400f);
        camRig.baseFOV = baseFOV;
        camRig.maxSpeedFOV = maxSpeedFOV;
        camRig.orbitSpeed = cameraOrbitSpeed;
        camRig.zoomSpeed = cameraZoomSpeed;
        camRig.minPitch = minPitch;
        camRig.maxPitch = maxPitch;
        camRig.yawFollowSpeed = yawFollowSpeed;
        camRig.autoFollowShipYaw = cameraAutoFollowShipYaw;
        camRig.Initialize(transform, this, cameraPivotOffset);
    }

    private void SetupEffects()
    {
        effects = GetComponent<ShipEffects>();
        if (effects == null) effects = gameObject.AddComponent<ShipEffects>();
        effects.bowSpray = bowSpray;
        effects.sternWake = sternWakeFoam;
        effects.funnelSmoke = funnelSmoke;
        effects.Initialize(transform, this, buoyancy, localBounds, softParticleMat);
    }

    private void SetupFog()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = Color.Lerp(horizonSkyColor, deepColor, 0.35f);
        RenderSettings.fogDensity = 0.0004f;
    }

    private void ApplyQualityTier()
    {
        OceanQuality.PatchPipelineAsset();

        bool postFX = qualityTier != QualityTier.Low;
        OceanQuality.PatchCamera(mainCam, postFX);
        if (postFX)
            OceanQuality.EnsurePostVolume(
                qualityTier == QualityTier.Ultra ? 0.75f : 0.55f,
                qualityTier == QualityTier.Low ? 0.2f : 0.28f);
        else
            OceanQuality.DestroyPostVolume();

        // --- planar reflections: High/Ultra only ---
        bool wantRefl = planarReflections && qualityTier >= QualityTier.High && usingCustomShader;
        if (wantRefl)
        {
            if (planarRefl == null)
            {
                var go = new GameObject("OceanPlanarReflections");
                planarRefl = go.AddComponent<WaterPlanarReflections>();
            }
            planarRefl.resolutionScale = qualityTier == QualityTier.Ultra
                ? Mathf.Max(reflectionResolutionScale, 0.7f)
                : reflectionResolutionScale;
            planarRefl.frameSkip = qualityTier == QualityTier.Ultra ? 0 : 1;
            planarRefl.enabled = true;
        }
        else if (planarRefl != null)
        {
            planarRefl.enabled = false;
        }

        if (waterMat != null)
        {
            SetKeyword(waterMat, WaterPlanarReflections.KeywordName, wantRefl);
            SetKeyword(waterMat, "_REFRACTION_ON",
                postFX && UniversalRenderPipeline.asset != null && UniversalRenderPipeline.asset.supportsCameraOpaqueTexture);
        }

        if (effects != null)
        {
            effects.SetQualityDensity(qualityTier switch
            {
                QualityTier.Low => 0.4f,
                QualityTier.Medium => 0.75f,
                QualityTier.High => 1f,
                _ => 1.5f
            });
        }
    }

    private static void SetKeyword(Material m, string keyword, bool on)
    {
        if (on) m.EnableKeyword(keyword);
        else m.DisableKeyword(keyword);
    }

    // ================= MATERIALS =================

    private void SetupMaterials()
    {
#if UNITY_EDITOR
        EnsureWaterMaterialAsset(SHADER_PATH, MAT_PATH, "AutoOceanWaterMat");
        EnsureWaterMaterialAsset(SOFT_SHADER_PATH, SOFT_MAT_PATH, "AutoSoftParticleMat");
#endif
        Material waterBase = LoadGeneratedMaterial("AutoOceanWaterMat", MAT_PATH);
        softParticleMat = LoadGeneratedMaterial("AutoSoftParticleMat", SOFT_MAT_PATH);

        if (waterBase != null && waterBase.shader != null && waterBase.shader.isSupported)
        {
            usingCustomShader = true;
            waterMat = new Material(waterBase) { name = "AutoOceanWater (runtime)" };
            SetWaterProps(waterMat);
        }
        else
        {
            usingCustomShader = false;
            waterMat = CreateFallbackMaterial();
            Debug.LogWarning("[master_ship] AutoOceanWater shader unavailable - using fallback material. " +
                             "Make sure Assets/AutoGeneratedWater/AutoOceanWater.shader exists and compiles.");
        }
    }

    private static Material LoadGeneratedMaterial(string resourceName, string assetPath)
    {
        Material m = Resources.Load<Material>(resourceName);
#if UNITY_EDITOR
        if (m == null) m = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
#endif
        return m;
    }

#if UNITY_EDITOR
    private static void EnsureWaterMaterialAsset(string shaderPath, string matPath, string matName)
    {
        try
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null) return;

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (existing != null) return;

            if (!AssetDatabase.IsValidFolder(SHADER_DIR)) AssetDatabase.CreateFolder("Assets", "AutoGeneratedWater");
            if (!AssetDatabase.IsValidFolder(SHADER_RES_DIR)) AssetDatabase.CreateFolder(SHADER_DIR, "Resources");

            var mat = new Material(shader) { name = matName };
            AssetDatabase.CreateAsset(mat, matPath);
            AssetDatabase.SaveAssets();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[master_ship] material asset generation failed: " + e.Message);
        }
    }
#endif

    private void SetWaterProps(Material m)
    {
        if (m == null) return;

        m.SetColor("_DeepColor", deepColor);
        m.SetColor("_CrestColor", crestColor);
        m.SetColor("_FoamColor", foamColor);
        m.SetColor("_HorizonSkyColor", horizonSkyColor);
        m.SetColor("_ZenithSkyColor", zenithSkyColor);
        m.SetColor("_SubsurfaceColor", subsurfaceColor);

        m.SetFloat("_FresnelPower", fresnelPower);
        m.SetFloat("_ReflectionStrength", reflectionStrength);
        m.SetFloat("_SunSpecPower", sunSpecPower);
        m.SetFloat("_SunSpecIntensity", sunSpecIntensity);
        m.SetFloat("_GlitterAmount", 0.7f);
        m.SetFloat("_DetailStrength", detailStrength);
        m.SetFloat("_DetailScale", detailScale);
        m.SetFloat("_DetailSpeed", detailSpeed);
        m.SetFloat("_DetailFadeDistance", 1400f);
        m.SetFloat("_FoamThreshold", foamThreshold);
        m.SetFloat("_FoamAmount", foamAmount);
        m.SetFloat("_FoamScale", 0.09f);
        m.SetFloat("_FoamScroll", 0.35f);
        m.SetFloat("_ContactFoamStrength", contactFoamStrength);
        m.SetFloat("_ContactFoamRange", 1.4f);
        m.SetFloat("_HullFoamStrength", hullFoamStrength);
        m.SetFloat("_HullFoamRadius", hullRadius);
        m.SetFloat("_WakeStrength", wakeStrength);
        m.SetFloat("_WakeLength", Mathf.Max(60f, localBounds.size.z * 5f));
        m.SetFloat("_WakeWidth", Mathf.Max(4f, localBounds.extents.x * 0.9f));
        m.SetFloat("_SubsurfaceStrength", subsurfaceStrength);
        m.SetFloat("_SSSPower", 6f);
        m.SetFloat("_AbsorptionDepth", absorptionDepth);
        m.SetFloat("_RefractionStrength", refractionStrength);
    }

    private Material CreateFallbackMaterial()
    {
        Shader urp = Shader.Find("Universal Render Pipeline/Lit");
        Shader chosen = urp != null ? urp : Shader.Find("Standard");
        if (chosen == null) chosen = Shader.Find("Unlit/Color");
        if (chosen == null) return null;

        var mat = new Material(chosen);
        var c = new Color(deepColor.r, deepColor.g, deepColor.b, 1f);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.95f);
        return mat;
    }

    // ================= WATER RINGS =================

    private void CreateWaterRings()
    {
        DestroyLegacy("AutoGenerated_Ocean");
        DestroyLegacy("AutoGenerated_Ocean_Near");
        DestroyLegacy("AutoGenerated_Ocean_Mid");
        DestroyLegacy("AutoGenerated_Ocean_Horizon");

        // Cell-size floors keep vertex density able to resolve the shortest waves.
        int nearRes = Mathf.Max(nearRingResolution, Mathf.CeilToInt(nearRingSize / 1.6f));
        nearRes = Mathf.Clamp(nearRes, 32, 512);
        float midSizeFloor = Mathf.Max(waterSize, nearRingSize * 2f);
        int midRes = Mathf.Max(waterResolution, Mathf.CeilToInt(midSizeFloor / 12f));
        midRes = Mathf.Clamp(midRes, 32, 384);

        nearCell = OceanGridBuilder.CellSize(nearRes, nearRingSize);
        midCell = OceanGridBuilder.CellSize(midRes, midSizeFloor);

        nearTransform = SpawnRing("AutoGenerated_Ocean_Near",
            OceanGridBuilder.BuildRing(nearRes, nearRingSize, true, 1f, "ProceduralOceanNear"), 0f);

        midTransform = SpawnRing("AutoGenerated_Ocean_Mid",
            OceanGridBuilder.BuildRing(midRes, midSizeFloor, true, 0.7f, "ProceduralOceanMid"), -0.02f);

        horizonTransform = SpawnRing("AutoGenerated_Ocean_Horizon",
            OceanGridBuilder.BuildRing(2, horizonWaterSize, false, 0f, "ProceduralOceanHorizon"), -0.15f);
    }

    private Transform SpawnRing(string name, Mesh mesh, float yOffset)
    {
        var go = new GameObject(name);
        go.layer = WaterPlanarReflections.WaterLayer;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = waterMat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

        go.transform.position = new Vector3(transform.position.x, waterLevel + yOffset, transform.position.z);
        return go.transform;
    }

    private static void DestroyLegacy(string name)
    {
        var old = GameObject.Find(name);
        if (old != null) Destroy(old);
    }

    private void FollowWaterRings()
    {
        if (!waterFollowsShip) return;

        Vector3 p = transform.position;
        if (nearTransform != null) nearTransform.position = Snap(p, nearCell, waterLevel);
        if (midTransform != null) midTransform.position = Snap(p, midCell, waterLevel - 0.02f);
        if (horizonTransform != null) horizonTransform.position = Snap(p, horizonWaterSize, waterLevel - 0.15f);
    }

    /// <summary>Snap ring centers to their vertex lattice so cells never "swim".</summary>
    private static Vector3 Snap(Vector3 pos, float cell, float y)
    {
        cell = Mathf.Max(0.01f, cell);
        return new Vector3(Mathf.Round(pos.x / cell) * cell, y, Mathf.Round(pos.z / cell) * cell);
    }

    private void PushShipUniforms()
    {
        if (!usingCustomShader || waterMat == null) return;

        float speed01 = Speed01;
        waterMat.SetVector("_ShipData", new Vector4(transform.position.x, transform.position.z, hullRadius, speed01));

        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        fwd.Normalize();
        waterMat.SetVector("_ShipForward", new Vector4(fwd.x, fwd.z, 0f, 0f));
    }

    // ================= BOUNDS =================

    private void CalculateLocalBounds()
    {
        bool found = false;

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds worldBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                worldBounds.Encapsulate(renderers[i].bounds);

            localBounds = new Bounds(transform.InverseTransformPoint(worldBounds.center), Vector3.zero);
            foreach (Renderer r in renderers)
                EncapsulateWorldBounds(ref localBounds, r.bounds);
            found = true;
        }
        else
        {
            Collider[] colliders = GetComponentsInChildren<Collider>();
            if (colliders.Length > 0)
            {
                Bounds worldBounds = colliders[0].bounds;
                for (int i = 1; i < colliders.Length; i++)
                    worldBounds.Encapsulate(colliders[i].bounds);

                localBounds = new Bounds(transform.InverseTransformPoint(worldBounds.center), Vector3.zero);
                foreach (Collider c in colliders)
                    EncapsulateWorldBounds(ref localBounds, c.bounds);
                found = true;
            }
        }

        if (!found || localBounds.size.sqrMagnitude < 0.01f)
            localBounds = new Bounds(Vector3.zero, new Vector3(14f, 8f, 60f));
    }

    private void EncapsulateWorldBounds(ref Bounds localBoundsToFill, Bounds worldBounds)
    {
        Vector3 center = worldBounds.center;
        Vector3 extents = worldBounds.extents;

        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = center + new Vector3(extents.x * x, extents.y * y, extents.z * z);
                    localBoundsToFill.Encapsulate(transform.InverseTransformPoint(corner));
                }
    }

    // ================= HUD =================

    private void BuildHudStyle()
    {
        hudStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 13,
            fontStyle = FontStyle.Normal,
            padding = new RectOffset(10, 10, 8, 8),
            wordWrap = false
        };
        hudStyle.normal.textColor = new Color(0.85f, 0.93f, 1f, 0.95f);
    }

    private void OnGUI()
    {
        if (!showTelemetryHud || !initialized || !Application.isPlaying) return;
        if (hudStyle == null) BuildHudStyle();
        if (hudStyle == null) return;

        float heading = Mathf.Repeat(shipYaw, 360f);
        string throttleState = brakeText();
        string rudderState = Mathf.Abs(currentRudder) < 0.02f ? "MIDSHIPS" :
            $"{Mathf.Abs(currentRudder) * 35f:0}° {(currentRudder > 0 ? "STBD" : "PORT")}";

        string text =
            $"<b>TFOU // {name}</b>\n" +
            $"SPEED   {Mathf.Abs(SpeedKnots):00.0} kn   ({throttleState})\n" +
            $"HEADING {heading:000}°\n" +
            $"RUDDER  {rudderState}\n" +
            $"SEA     Beaufort {seaState:0.0}  wind {windDirectionDeg:0}°\n" +
            $"CAM     {CamModeName()}   zoom wheel · C cycle\n" +
            $"<size=11>W/S engine · A/D rudder · Shift flank · Space brake · O/P sea state · F hud</size>";

        if (grounded)
            text = "!! GROUNDED - ease astern off the seabed\n" + text;

        GUI.Box(new Rect(12, 12, 330, grounded ? 148f : 128f), text, hudStyle);
    }

    private string brakeText()
    {
        if (ShipInput.Brake()) return "ALL STOP";
        if (currentSpeed > 0.5f)
            return ShipInput.Boost() ? "FLANK" : $"AHEAD {Mathf.Clamp01(currentSpeed / maxSpeed) * 100f:0}%";
        if (currentSpeed < -0.5f) return "ASTERNS";
        return "STOPPED";
    }

    private string CamModeName() => camRig != null ? camRig.CurrentMode.ToString().ToUpper() : "-";

    // ================= EDITOR =================

    private void OnValidate()
    {
        maxSpeed = Mathf.Max(1f, maxSpeed);
        nearRingSize = Mathf.Clamp(nearRingSize, 100f, 4000f);

        if (!Application.isPlaying || !initialized) return;

        SetupOceanWaves();
        SetWaterProps(waterMat);
        if (buoyancy != null)
            buoyancy.ConfigureFrom(localBounds, verticalSpring, bobFollowSpeed, maxVerticalSpeed, tiltInfluence, tiltSmooth);
        SetupCameraRig();
        ApplyQualityTier();
        if (autoSetupFog) SetupFog();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.4f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(localBounds.center, localBounds.size);
        Gizmos.matrix = Matrix4x4.identity;
    }
}

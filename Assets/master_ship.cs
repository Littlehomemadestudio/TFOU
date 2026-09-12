using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

[RequireComponent(typeof(Rigidbody))]
public class master_ship : MonoBehaviour
{
        void CalculateLocalBounds()
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
        {
            localBounds = new Bounds(Vector3.zero, new Vector3(14f, 8f, 60f));
        }
    }

    void EncapsulateWorldBounds(ref Bounds localBoundsToFill, Bounds worldBounds)
    {
        Vector3 center = worldBounds.center;
        Vector3 extents = worldBounds.extents;

        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = center + new Vector3(extents.x * x, extents.y * y, extents.z * z);
                    localBoundsToFill.Encapsulate(transform.InverseTransformPoint(corner));
                }
            }
        }
    }
    [Header("=== CRUISER MOVEMENT ===")]
    public float maxSpeed = 36f;
    public float maxReverseSpeed = 12f;
    public float acceleration = 6f;
    public float deceleration = 6f;
    public float propulsionForce = 12f;

    [Header("=== STEERING FEEL ===")]
    public float maxTurnRate = 20f;
    public float rudderResponsiveness = 1.6f;
    [Range(0f, 1f)] public float highSpeedTurnPenalty = 0.25f;
    public float turnHeelDegrees = 12f;
    public float accelPitchDegrees = 5f;
    public float heelSmooth = 3f;
    public float pitchSmooth = 3f;

    [Header("=== FLOATING ===")]
    public float verticalSpring = 2.5f;
    public float bobFollowSpeed = 3f;
    public float maxVerticalSpeed = 8f;
    [Range(0f, 1f)] public float tiltInfluence = 0.35f;
    public float tiltSmooth = 2.5f;
    public float rotationSmooth = 8f;

    [Header("=== GERSTNER OCEAN WAVES ===")]
    [Tooltip("Total wave height.")]
    public float waveAmplitude = 1.1f;
    [Tooltip("0 = round swell, 1 = sharp choppy crests.")]
    [Range(0f, 1f)] public float waveChoppiness = 0.55f;
    [Tooltip("Multiplier for wavelength.")]
    public float waveLengthScale = 1f;
    [Tooltip("Multiplier for wave travel speed.")]
    public float waveSpeedMultiplier = 1f;

    [Header("=== WATER LOOK (Custom URP Shader) ===")]
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

    [Header("=== WATER GENERATION ===")]
    public bool autoGenerateWater = true;
    public bool autoSetupFog = true;
    public float waterLevel = 0f;
    public float waterSize = 3000f;
    public float horizonWaterSize = 30000f;
    public int waterResolution = 96;
    public bool waterFollowsShip = true;

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

    // ---------- internals ----------
    private Rigidbody rb;
    private Camera mainCam;
    private Bounds localBounds;
    private Vector3 cameraPivotOffset;

    private float currentSpeed = 0f;
    private float currentRudder = 0f;

    private float camYaw = 0f;
    private float camPitch = 25f;
    private float currentDistance = 55f;
    private float targetDistance = 55f;
    private Vector3 camPosVel;
    private bool isTactical = false;

    private Mesh nearMesh;
    private Vector3[] nearVertices;
    private Color[] nearColors;
    private Transform nearTransform;
    private Transform horizonTransform;

    private Material nearMat;
    private Material horizonMat;
    private bool usingCustomShader = false;

    private float shipYaw = 0f;
    private Vector3 smoothedWaterUp = Vector3.up;
    private float smoothedTargetY = 0f;
    private bool floatingInitialized = false;
    private float smoothedHeel = 0f;
    private float smoothedPitch = 0f;

    // Gerstner wave data
    private const int WAVE_COUNT = 4;
    private float[] wDirX = new float[WAVE_COUNT];
    private float[] wDirZ = new float[WAVE_COUNT];
    private float[] wK = new float[WAVE_COUNT];
    private float[] wAmp = new float[WAVE_COUNT];
    private float[] wOmega = new float[WAVE_COUNT];
    private float[] wQ = new float[WAVE_COUNT];
    private float lastAmp = -1f, lastChop = -1f, lastScale = -1f, lastSpeed = -1f;

    private float hullRadius = 20f;

    private const string SHADER_DIR = "Assets/AutoGeneratedWater";
    private const string SHADER_RES_DIR = "Assets/AutoGeneratedWater/Resources";
    private const string SHADER_PATH = "Assets/AutoGeneratedWater/AutoOceanWater.shader";
    private const string MAT_PATH = "Assets/AutoGeneratedWater/Resources/AutoOceanWaterMat.mat";

    void Awake()
    {
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        rb = GetComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        rb.solverIterations = 12;
        rb.solverVelocityIterations = 6;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.constraints = RigidbodyConstraints.None;

        EnsureCamera();
        AutoSetup();

        shipYaw = transform.eulerAngles.y;
        smoothedTargetY = transform.position.y;
        smoothedWaterUp = Vector3.up;

        currentDistance = defaultDistance;
        targetDistance = defaultDistance;
        camYaw = transform.eulerAngles.y;
        camPitch = 25f;

        if (mainCam != null)
        {
            float neededFar = Mathf.Max(horizonWaterSize * 1.25f, 20000f);
            mainCam.farClipPlane = Mathf.Max(mainCam.farClipPlane, neededFar);
        }
    }

    void EnsureCamera()
    {
        mainCam = Camera.main;
        if (mainCam != null) return;

        GameObject camObj = new GameObject("Main Camera");
        camObj.tag = "MainCamera";
        mainCam = camObj.AddComponent<Camera>();
        mainCam.clearFlags = CameraClearFlags.Skybox;
        mainCam.nearClipPlane = 0.3f;
        mainCam.farClipPlane = 40000f;

        if (FindObjectOfType<AudioListener>() == null)
            camObj.AddComponent<AudioListener>();
    }

    void AutoSetup()
    {
        CalculateLocalBounds();
        SetupFog();
        BuildWaves(true);
        CreateWater();

        hullRadius = Mathf.Max(localBounds.extents.x, localBounds.extents.z) * 0.85f;
        cameraPivotOffset = localBounds.center + Vector3.up * localBounds.size.y * 0.25f;

        if (autoFitCameraToShipSize)
        {
            defaultDistance = Mathf.Clamp(localBounds.size.z * 1.6f, 35f, 200f);
            tacticalDistance = defaultDistance * 2.5f;
            minDistance = Mathf.Clamp(localBounds.size.z * 0.45f, 15f, defaultDistance * 0.5f);
        }
    }

    void SetupFog()
    {
        if (!autoSetupFog) return;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = Color.Lerp(horizonSkyColor, deepColor, 0.35f);
        RenderSettings.fogDensity = 0.00016f;
    }

    // ================= GERSTNER WAVES (CPU, shared by mesh + physics) =================

    void BuildWaves(bool force)
    {
        if (!force && lastAmp == waveAmplitude && lastChop == waveChoppiness &&
            lastScale == waveLengthScale && lastSpeed == waveSpeedMultiplier)
            return;

        lastAmp = waveAmplitude; lastChop = waveChoppiness;
        lastScale = waveLengthScale; lastSpeed = waveSpeedMultiplier;

        Vector2[] dirs = { new Vector2(1f, 0.15f), new Vector2(0.62f, 0.78f), new Vector2(-0.35f, 0.94f), new Vector2(-0.8f, -0.6f) };
        float[] lengths = { 63f, 34f, 19f, 10.5f };
        float[] fracs = { 0.45f, 0.28f, 0.17f, 0.10f };

        for (int i = 0; i < WAVE_COUNT; i++)
        {
            Vector2 d = dirs[i].normalized;
            wDirX[i] = d.x;
            wDirZ[i] = d.y;

            float L = Mathf.Max(1f, lengths[i] * waveLengthScale);
            wK[i] = Mathf.PI * 2f / L;
            wAmp[i] = Mathf.Max(0.001f, waveAmplitude * fracs[i]);

            float c = Mathf.Sqrt(9.81f / wK[i]) * waveSpeedMultiplier;
            wOmega[i] = wK[i] * c;

            wQ[i] = waveChoppiness / (wK[i] * wAmp[i] * WAVE_COUNT);
        }
    }

    void SampleGerstner(float x, float z, float t,
        out float height, out float dx, out float dz,
        out float nx, out float ny, out float nz)
    {
        height = 0f; dx = 0f; dz = 0f;
        nx = 0f; nz = 0f;
        ny = 1f;

        for (int i = 0; i < WAVE_COUNT; i++)
        {
            float phi = wK[i] * (wDirX[i] * x + wDirZ[i] * z) - wOmega[i] * t;
            float S = Mathf.Sin(phi);
            float C = Mathf.Cos(phi);

            height += wAmp[i] * S;
            dx += wQ[i] * wAmp[i] * wDirX[i] * C;
            dz += wQ[i] * wAmp[i] * wDirZ[i] * C;

            nx -= wDirX[i] * wK[i] * wAmp[i] * C;
            nz -= wDirZ[i] * wK[i] * wAmp[i] * C;
            ny -= wQ[i] * wK[i] * wAmp[i] * S;
        }

        float inv = 1f / Mathf.Max(0.0001f, Mathf.Sqrt(nx * nx + ny * ny + nz * nz));
        nx *= inv; ny *= inv; nz *= inv;
    }

    float GetWaveHeight(float x, float z)
    {
        SampleGerstner(x, z, Time.time, out float h, out _, out _, out _, out _, out _);
        return h;
    }

    // ================= WATER OBJECTS =================

    void CreateWater()
    {
        if (!autoGenerateWater) return;

        GameObject legacy = GameObject.Find("AutoGenerated_Ocean");
        if (legacy != null) legacy.SetActive(false);

        LoadOrCreateShaderMaterial();
        CreateNearWater();
        CreateHorizonWater();
    }

    void LoadOrCreateShaderMaterial()
    {
        Material baseMat = null;

#if UNITY_EDITOR
        try
        {
            EnsureWaterShaderAsset();
            baseMat = AssetDatabase.LoadAssetAtPath<Material>(MAT_PATH);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Water shader asset generation failed: " + e.Message);
        }
#else
        baseMat = Resources.Load<Material>("AutoOceanWaterMat");
#endif

        if (baseMat != null && baseMat.shader != null && baseMat.shader.isSupported)
        {
            usingCustomShader = true;
            nearMat = new Material(baseMat);
            horizonMat = new Material(baseMat);
            ApplyWaterMaterialSettings();
        }
        else
        {
            usingCustomShader = false;
            nearMat = CreateFallbackMaterial(true);
            horizonMat = CreateFallbackMaterial(false);
        }
    }

    void ApplyWaterMaterialSettings()
    {
        if (nearMat == null || horizonMat == null) return;

        SetWaterProps(nearMat);

        // Horizon variant: calmer micro detail, less foam
        SetWaterProps(horizonMat);
        horizonMat.SetFloat("_DetailScale", detailScale * 0.35f);
        horizonMat.SetFloat("_DetailStrength", detailStrength * 0.7f);
        horizonMat.SetFloat("_FoamAmount", foamAmount * 0.4f);
    }

    void SetWaterProps(Material m)
    {
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
        m.SetFloat("_DetailStrength", detailStrength);
        m.SetFloat("_DetailScale", detailScale);
        m.SetFloat("_DetailSpeed", detailSpeed);
        m.SetFloat("_FoamThreshold", foamThreshold);
        m.SetFloat("_FoamAmount", foamAmount);
        m.SetFloat("_HullFoamStrength", hullFoamStrength);
        m.SetFloat("_HullFoamRadius", hullRadius);
        m.SetFloat("_WakeStrength", wakeStrength);
        m.SetFloat("_WakeLength", Mathf.Max(20f, localBounds.size.z * 4f));
        m.SetFloat("_WakeWidth", Mathf.Max(4f, localBounds.extents.x * 1.6f));
        m.SetFloat("_SubsurfaceStrength", subsurfaceStrength);
        m.SetFloat("_DetailFadeDistance", 1200f);
    }

    Material CreateFallbackMaterial(bool transparent)
    {
        Shader urp = Shader.Find("Universal Render Pipeline/Lit");
        Shader standard = Shader.Find("Standard");
        Shader chosen = urp != null ? urp : standard;
        if (chosen == null) chosen = Shader.Find("Unlit/Color");
        if (chosen == null) return null;

        Material mat = new Material(chosen);
        Color c = new Color(deepColor.r, deepColor.g, deepColor.b, transparent ? 0.92f : 1f);

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.95f);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.95f);

        return mat;
    }

    void CreateNearWater()
    {
        string name = "AutoGenerated_Ocean_Near";
        GameObject water = GameObject.Find(name);

        MeshFilter mf;
        MeshRenderer mr;

        if (water == null)
        {
            water = new GameObject(name);
            mf = water.AddComponent<MeshFilter>();
            mr = water.AddComponent<MeshRenderer>();
        }
        else
        {
            mf = water.GetComponent<MeshFilter>();
            if (mf == null) mf = water.AddComponent<MeshFilter>();
            mr = water.GetComponent<MeshRenderer>();
            if (mr == null) mr = water.AddComponent<MeshRenderer>();
        }

        nearTransform = water.transform;
        nearTransform.rotation = Quaternion.identity;
        nearTransform.position = new Vector3(transform.position.x, waterLevel, transform.position.z);

        nearMesh = mf.sharedMesh;
        if (nearMesh == null || nearMesh.name != "ProceduralOceanNear")
        {
            nearMesh = new Mesh();
            nearMesh.name = "ProceduralOceanNear";
            nearMesh.MarkDynamic();
        }
        else
        {
            nearMesh.Clear();
        }

        int res = Mathf.Clamp(waterResolution, 8, 160);
        float size = Mathf.Max(100f, waterSize);

        BuildGridMesh(nearMesh, res, size, true);

        nearVertices = nearMesh.vertices;
        nearColors = nearMesh.colors;

        mf.sharedMesh = nearMesh;
        mr.sharedMaterial = nearMat;
    }

    void CreateHorizonWater()
    {
        string name = "AutoGenerated_Ocean_Horizon";
        GameObject horizon = GameObject.Find(name);

        MeshFilter mf;
        MeshRenderer mr;

        if (horizon == null)
        {
            horizon = new GameObject(name);
            mf = horizon.AddComponent<MeshFilter>();
            mr = horizon.AddComponent<MeshRenderer>();
        }
        else
        {
            Collider col = horizon.GetComponent<Collider>();
            if (col != null) Destroy(col);

            mf = horizon.GetComponent<MeshFilter>();
            if (mf == null) mf = horizon.AddComponent<MeshFilter>();
            mr = horizon.GetComponent<MeshRenderer>();
            if (mr == null) mr = horizon.AddComponent<MeshRenderer>();
        }

        horizonTransform = horizon.transform;
        horizonTransform.rotation = Quaternion.identity;
        horizonTransform.localScale = Vector3.one;
        horizonTransform.position = new Vector3(transform.position.x, waterLevel - 0.05f, transform.position.z);

        Mesh horizonMesh = mf.sharedMesh;
        if (horizonMesh == null || horizonMesh.name != "ProceduralOceanHorizon")
        {
            horizonMesh = new Mesh();
            horizonMesh.name = "ProceduralOceanHorizon";
        }
        else
        {
            horizonMesh.Clear();
        }

        BuildGridMesh(horizonMesh, 2, Mathf.Max(100f, horizonWaterSize), true);

        mf.sharedMesh = horizonMesh;
        mr.sharedMaterial = horizonMat;
    }

    void BuildGridMesh(Mesh mesh, int res, float size, bool withColors)
    {
        int vertCount = (res + 1) * (res + 1);
        Vector3[] verts = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        Color[] cols = new Color[vertCount];
        int[] triangles = new int[res * res * 6];

        int vi = 0, ti = 0;

        for (int z = 0; z <= res; z++)
        {
            for (int x = 0; x <= res; x++)
            {
                float px = (x / (float)res - 0.5f) * size;
                float pz = (z / (float)res - 0.5f) * size;

                verts[vi] = new Vector3(px, 0f, pz);
                uvs[vi] = new Vector2(x / (float)res, z / (float)res);
                cols[vi] = Color.clear;

                if (x < res && z < res)
                {
                    int a = z * (res + 1) + x;
                    int b = a + 1;
                    int c = a + (res + 1);
                    int d = c + 1;

                    triangles[ti++] = a; triangles[ti++] = c; triangles[ti++] = b;
                    triangles[ti++] = b; triangles[ti++] = c; triangles[ti++] = d;
                }

                vi++;
            }
        }

        mesh.vertices = verts;
        mesh.uv = uvs;
        if (withColors) mesh.colors = cols;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    // ================= SHADER ASSET GENERATION (EDITOR) =================

#if UNITY_EDITOR
    void EnsureWaterShaderAsset()
    {
        if (File.Exists(SHADER_PATH) && File.Exists(MAT_PATH)) return;

        if (!AssetDatabase.IsValidFolder(SHADER_DIR))
            AssetDatabase.CreateFolder("Assets", "AutoGeneratedWater");

        if (!AssetDatabase.IsValidFolder(SHADER_RES_DIR))
            AssetDatabase.CreateFolder(SHADER_DIR, "Resources");

        if (!File.Exists(SHADER_PATH))
        {
            File.WriteAllText(SHADER_PATH, WATER_SHADER_SOURCE);
            AssetDatabase.ImportAsset(SHADER_PATH, ImportAssetOptions.ForceSynchronousImport);
        }

        Shader sh = AssetDatabase.LoadAssetAtPath<Shader>(SHADER_PATH);
        if (sh == null || !sh.isSupported) return;

        if (!File.Exists(MAT_PATH))
        {
            Material m = new Material(sh);
            m.name = "AutoOceanWaterMat";
            AssetDatabase.CreateAsset(m, MAT_PATH);
            AssetDatabase.SaveAssets();
        }
    }
#endif

    // ================= SIM =================

    void FixedUpdate()
    {
        BuildWaves(false);
        HandleStableShip();
    }

    void LateUpdate()
    {
        UpdateWaterVisual();
        HandleCamera();
    }

    void HandleStableShip()
    {
        if (rb == null) return;

        float dt = Time.fixedDeltaTime;

        float throttle = GetVerticalInput();
        float steer = GetHorizontalInput();

        float targetSpeed = 0f;
        if (throttle > 0.01f) targetSpeed = maxSpeed;
        else if (throttle < -0.01f) targetSpeed = -maxReverseSpeed;

        float rate = Mathf.Abs(throttle) > 0.01f ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * dt);
        currentRudder = Mathf.MoveTowards(currentRudder, steer, rudderResponsiveness * dt);

        Vector3 center = transform.position;
        float surfaceHeight = waterLevel + GetWaveHeight(center.x, center.z);

        float draft = Mathf.Clamp(localBounds.size.y * 0.22f, 0.5f, 8f);
        float rawTargetY = surfaceHeight - draft - localBounds.min.y;

        if (!floatingInitialized)
        {
            smoothedTargetY = rawTargetY;
            floatingInitialized = true;
        }

        smoothedTargetY = Mathf.MoveTowards(smoothedTargetY, rawTargetY, bobFollowSpeed * dt);

        float verticalVel = Mathf.Clamp(
            (smoothedTargetY - transform.position.y) * verticalSpring,
            -maxVerticalSpeed, maxVerticalSpeed);

        Vector3 currentVel = rb.linearVelocity;
        Vector3 horizontalVel = new Vector3(currentVel.x, 0f, currentVel.z);

        Vector3 targetHorizontalVel = transform.forward * currentSpeed;
        targetHorizontalVel.y = 0f;

        Vector3 newHorizontalVel = Vector3.MoveTowards(horizontalVel, targetHorizontalVel, propulsionForce * dt);
        rb.linearVelocity = new Vector3(newHorizontalVel.x, verticalVel, newHorizontalVel.z);

        // wave tilt from the SAME gerstner math
        SampleGerstner(center.x, center.z, Time.time, out _, out _, out _, out float nx, out float ny, out float nz);
        Vector3 waveNormal = new Vector3(nx, ny, nz);
        Vector3 desiredUp = Vector3.Slerp(Vector3.up, waveNormal, tiltInfluence).normalized;
        smoothedWaterUp = Vector3.Slerp(smoothedWaterUp, desiredUp, tiltSmooth * dt).normalized;

        float absSpeed = Mathf.Abs(currentSpeed);
        float speedRatio = Mathf.Clamp01(absSpeed / maxSpeed);
        float moveFactor = Mathf.Clamp01(absSpeed / 2f);
        float turnPenalty = 1f - highSpeedTurnPenalty * speedRatio;

        float turnRateDeg = -currentRudder * maxTurnRate * moveFactor * turnPenalty;
        shipYaw += turnRateDeg * dt;
        shipYaw = Mathf.Repeat(shipYaw + 180f, 360f) - 180f;

        Vector3 forwardYaw = Quaternion.Euler(0f, shipYaw, 0f) * Vector3.forward;
        Vector3 forwardOnWater = Vector3.ProjectOnPlane(forwardYaw, smoothedWaterUp).normalized;
        if (forwardOnWater.sqrMagnitude < 0.0001f) forwardOnWater = transform.forward;

        Quaternion waterRot = Quaternion.LookRotation(forwardOnWater, smoothedWaterUp);

        float heelAuthority = Mathf.Clamp01(absSpeed / 8f + 0.1f);
        float targetHeel = -currentRudder * heelAuthority * turnHeelDegrees;
        float targetPitch = -throttle * accelPitchDegrees;

        smoothedHeel = Mathf.MoveTowards(smoothedHeel, targetHeel, heelSmooth * dt);
        smoothedPitch = Mathf.MoveTowards(smoothedPitch, targetPitch, pitchSmooth * dt);

        Quaternion targetRot = waterRot * Quaternion.Euler(smoothedPitch, 0f, smoothedHeel);
        Quaternion newRot = Quaternion.Slerp(rb.rotation, targetRot, rotationSmooth * dt);
        rb.MoveRotation(newRot);

        shipYaw = Mathf.Repeat(newRot.eulerAngles.y + 180f, 360f) - 180f;
    }

    void UpdateWaterVisual()
    {
        if (waterFollowsShip)
        {
            if (nearTransform != null)
                nearTransform.position = new Vector3(transform.position.x, waterLevel, transform.position.z);

            if (horizonTransform != null)
                horizonTransform.position = new Vector3(transform.position.x, waterLevel - 0.05f, transform.position.z);
        }

        if (nearMesh != null && nearVertices != null && nearColors != null && nearTransform != null)
        {
            float t = Time.time;
            Vector3 wp = nearTransform.position;
            float halfSize = Mathf.Max(1f, waterSize * 0.5f);
            float totalAmp = Mathf.Max(0.001f, waveAmplitude);

            for (int i = 0; i < nearVertices.Length; i++)
            {
                float bx = nearVertices[i].x;
                float bz = nearVertices[i].z;

                // NOTE: bx/bz keep their base grid values because we recompute from base each frame.
                float wx = wp.x + bx;
                float wz = wp.z + bz;

                SampleGerstner(wx, wz, t, out float h, out float dx, out float dz, out _, out _, out _);

                float radial = Mathf.Sqrt(bx * bx + bz * bz) / halfSize;
                float fade = 1f - Mathf.SmoothStep(0.8f, 1f, radial);

                nearVertices[i].y = h * fade;
                // horizontal gerstner displacement (x/z stored offsets would accumulate, so we
                // re-derive from base grid: base grid is rebuilt only on resize, so we store base in uv)
                float baseX = (nearMesh.uv[i].x - 0.5f) * waterSize;
                float baseZ = (nearMesh.uv[i].y - 0.5f) * waterSize;
                nearVertices[i].x = baseX + dx * fade;
                nearVertices[i].z = baseZ + dz * fade;

                float crest = Mathf.Clamp01(h / totalAmp * 0.5f + 0.5f);
                float foamMask = Mathf.SmoothStep(0.62f, 0.92f, crest);

                nearColors[i].r = foamMask;
                nearColors[i].g = crest;
                nearColors[i].b = 0f;
                nearColors[i].a = 1f;
            }

            nearMesh.vertices = nearVertices;
            nearMesh.colors = nearColors;
            nearMesh.RecalculateNormals();
            nearMesh.RecalculateBounds();
        }

        if (usingCustomShader && nearMat != null && horizonMat != null)
        {
            float speed01 = Mathf.Clamp01(Mathf.Abs(currentSpeed) / Mathf.Max(1f, maxSpeed));
            Vector4 shipData = new Vector4(transform.position.x, transform.position.z, hullRadius, speed01);
            Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
            Vector4 shipFwd = new Vector4(fwd.x, fwd.z, 0f, 0f);

            nearMat.SetVector("_ShipData", shipData);
            nearMat.SetVector("_ShipForward", shipFwd);
            horizonMat.SetVector("_ShipData", shipData);
            horizonMat.SetVector("_ShipForward", shipFwd);
        }
    }

    void HandleCamera()
    {
        if (mainCam == null)
        {
            EnsureCamera();
            if (mainCam == null) return;
        }

        Vector3 pivot = transform.TransformPoint(cameraPivotOffset);

        bool orbiting = GetOrbitButton();

        if (orbiting)
        {
            Vector2 mouseDelta = GetMouseDeltaInput();
            camYaw += mouseDelta.x * cameraOrbitSpeed * 0.08f;
            camPitch -= mouseDelta.y * cameraOrbitSpeed * 0.08f;
        }
        else if (cameraAutoFollowShipYaw)
        {
            float followSpeed = isTactical ? yawFollowSpeed * 0.5f : yawFollowSpeed;
            camYaw = Mathf.LerpAngle(camYaw, shipYaw, Time.deltaTime * followSpeed);
        }

        camPitch = Mathf.Clamp(camPitch, minPitch, maxPitch);

        float scroll = GetScrollInput();
        if (Mathf.Abs(scroll) > 0.001f)
        {
            targetDistance -= scroll * cameraZoomSpeed;
            targetDistance = Mathf.Clamp(targetDistance, minDistance, tacticalDistance + 50f);
        }

        if (GetCameraToggleDown())
        {
            isTactical = !isTactical;
            targetDistance = isTactical ? tacticalDistance : defaultDistance;
            if (isTactical) camPitch = Mathf.Max(camPitch, 55f);
        }

        currentDistance = Mathf.Lerp(currentDistance, targetDistance, Time.deltaTime * 4f);

        float speedRatio = Mathf.Clamp01(Mathf.Abs(currentSpeed) / Mathf.Max(1f, maxSpeed));
        mainCam.fieldOfView = Mathf.Lerp(mainCam.fieldOfView, Mathf.Lerp(baseFOV, maxSpeedFOV, speedRatio), Time.deltaTime * 3f);

        Quaternion orbitRot = Quaternion.Euler(camPitch, camYaw, 0f);
        Vector3 desiredPos = pivot - (orbitRot * Vector3.forward * currentDistance);

        float lagOffset = Mathf.Clamp(-currentRudder * speedRatio * 6f, -10f, 10f);
        desiredPos += transform.right * lagOffset;

        Vector3 dir = desiredPos - pivot;
        float dist = dir.magnitude;

        if (dist < 0.01f) { dir = -transform.forward; dist = currentDistance; }
        else dir /= dist;

        float allowedDist = dist;
        Ray ray = new Ray(pivot, dir);
        RaycastHit[] hits = Physics.SphereCastAll(ray, 1.2f, dist, ~0, QueryTriggerInteraction.Ignore);

        float nearest = Mathf.Infinity;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.collider.transform == transform) continue;
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.distance > 0.5f && hit.distance < nearest) nearest = hit.distance;
        }
        if (nearest < Mathf.Infinity) allowedDist = Mathf.Max(2f, nearest - 1.5f);

        Vector3 finalPos = pivot + dir * Mathf.Min(dist, allowedDist);
        mainCam.transform.position = Vector3.SmoothDamp(mainCam.transform.position, finalPos, ref camPosVel, 0.18f);

        Quaternion lookRot = Quaternion.LookRotation(pivot - mainCam.transform.position);
        mainCam.transform.rotation = Quaternion.Slerp(mainCam.transform.rotation, lookRot, Time.deltaTime * 8f);
    }

    // ================= INPUT =================

    float GetVerticalInput()
    {
        float v = 0f;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v -= 1f;
        }
        var gp = Gamepad.current;
        if (gp != null) v += gp.leftStick.y.ReadValue();
        return Mathf.Clamp(v, -1f, 1f);
    }

    float GetHorizontalInput()
    {
        float h = 0f;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) h -= 1f;
        }
        var gp = Gamepad.current;
        if (gp != null) h += gp.leftStick.x.ReadValue();
        return Mathf.Clamp(h, -1f, 1f);
    }

    bool GetOrbitButton()
    {
        var m = Mouse.current;
        return m != null && m.rightButton.isPressed;
    }

    Vector2 GetMouseDeltaInput()
    {
        var m = Mouse.current;
        return m != null ? m.delta.ReadValue() : Vector2.zero;
    }

    float GetScrollInput()
    {
        var m = Mouse.current;
        return m != null ? Mathf.Clamp(m.scroll.ReadValue().y / 120f, -1f, 1f) : 0f;
    }

    bool GetCameraToggleDown()
    {
        var kb = Keyboard.current;
        return kb != null && (kb.cKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame);
    }

    // ================= THE GENERATED URP OCEAN SHADER =================

    private const string WATER_SHADER_SOURCE = @"Shader ""Custom/AutoOceanWater""
{
    Properties
    {
        _DeepColor(""Deep Color"", Color) = (0.008, 0.09, 0.16, 1)
        _CrestColor(""Crest Color"", Color) = (0.02, 0.22, 0.30, 1)
        _FoamColor(""Foam Color"", Color) = (0.92, 0.97, 1, 1)
        _HorizonSkyColor(""Horizon Sky Color"", Color) = (0.45, 0.62, 0.72, 1)
        _ZenithSkyColor(""Zenith Sky Color"", Color) = (0.10, 0.28, 0.55, 1)
        _SubsurfaceColor(""Subsurface Color"", Color) = (0.05, 0.45, 0.42, 1)
        _FresnelPower(""Fresnel Power"", Range(0.5, 8)) = 3
        _ReflectionStrength(""Reflection Strength"", Range(0, 2)) = 1
        _SunSpecPower(""Sun Specular Power"", Range(8, 2048)) = 256
        _SunSpecIntensity(""Sun Specular Intensity"", Range(0, 8)) = 2
        _DetailStrength(""Detail Normal Strength"", Range(0, 2)) = 0.5
        _DetailScale(""Detail Scale"", Range(0.01, 4)) = 0.35
        _DetailSpeed(""Detail Speed"", Range(0, 4)) = 1
        _FoamThreshold(""Crest Foam Threshold"", Range(0, 1)) = 0.55
        _FoamAmount(""Foam Amount"", Range(0, 3)) = 1.2
        _HullFoamStrength(""Hull Foam Strength"", Range(0, 3)) = 1.4
        _HullFoamRadius(""Hull Foam Radius"", Range(1, 300)) = 40
        _WakeStrength(""Wake Strength"", Range(0, 3)) = 1.2
        _WakeLength(""Wake Length"", Range(10, 800)) = 220
        _WakeWidth(""Wake Width"", Range(1, 100)) = 18
        _SubsurfaceStrength(""Subsurface Strength"", Range(0, 3)) = 0.5
        _DetailFadeDistance(""Detail Fade Distance"", Range(50, 5000)) = 1200
    }
    SubShader
    {
        Tags { ""RenderType""=""Opaque"" ""Queue""=""Geometry"" }
        LOD 100

        Pass
        {
            Name ""ForwardLit""
            Tags { ""LightMode""=""UniversalForward"" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl""

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _CrestColor;
                half4 _FoamColor;
                half4 _HorizonSkyColor;
                half4 _ZenithSkyColor;
                half4 _SubsurfaceColor;
                float _FresnelPower;
                half _ReflectionStrength;
                float _SunSpecPower;
                half _SunSpecIntensity;
                half _DetailStrength;
                half _DetailScale;
                half _DetailSpeed;
                half _FoamThreshold;
                half _FoamAmount;
                half _HullFoamStrength;
                half _HullFoamRadius;
                half _WakeStrength;
                half _WakeLength;
                half _WakeWidth;
                half _SubsurfaceStrength;
                half _DetailFadeDistance;
                float4 _ShipData;
                float4 _ShipForward;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float2 crest : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            float DetailH(float2 p, float t)
            {
                float h = sin(p.x * 1.0 + t * 1.7) * 0.50;
                h += sin(p.y * 1.3 - t * 1.1) * 0.40;
                h += sin((p.x + p.y) * 0.7 + t * 2.3) * 0.30;
                h += sin((p.x - p.y) * 2.1 - t * 1.9) * 0.20;
                h += sin(p.x * 3.9 + p.y * 3.1 + t * 3.3) * 0.10;
                return h;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = v.uv;
                o.crest = v.color.rg;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 positionWS = i.positionWS;
                float3 camPos = GetCameraPositionWS();
                float3 V = normalize(camPos - positionWS);

                float dist = distance(camPos, positionWS);
                float detailFade = saturate(1.0 - dist / max(_DetailFadeDistance, 1.0));

                float2 p = positionWS.xz * _DetailScale;
                float t = _Time.y * _DetailSpeed;

                float e = 0.4;
                float h0 = DetailH(p, t);
                float hx = DetailH(p + float2(e, 0.0), t);
                float hz = DetailH(p + float2(0.0, e), t);
                float3 detailN = normalize(float3(-(hx - h0) / e, 1.0, -(hz - h0) / e));

                float strength = _DetailStrength * (0.25 + 0.75 * detailFade);
                float3 N = normalize(float3(detailN.xz * strength, 1.0));

                float ndv = saturate(dot(N, V));
                float fresnel = pow(1.0 - ndv, _FresnelPower);

                float3 R = reflect(-V, N);
                float skyT = saturate(R.y * 1.6 + 0.08);
                half3 skyCol = lerp(_HorizonSkyColor.rgb, _ZenithSkyColor.rgb, skyT);

                Light mainLight = GetMainLight();
                half3 H = normalize(mainLight.direction + V);
                float spec = pow(saturate(dot(N, H)), _SunSpecPower) * _SunSpecIntensity;

                half3 waterCol = lerp(_DeepColor.rgb, _CrestColor.rgb, saturate(i.crest.g));

                float sss = pow(saturate(dot(V, -mainLight.direction)), 3.0) * saturate(i.crest.g) * _SubsurfaceStrength;
                waterCol += _SubsurfaceColor.rgb * sss;

                half3 col = lerp(waterCol, skyCol, saturate(fresnel * _ReflectionStrength));
                col += mainLight.color * spec;

                float foamNoise = DetailH(p * 2.7 + 13.7, t * 0.55) * 0.5 + 0.5;
                float crestFoam = smoothstep(_FoamThreshold, 1.0, i.crest.r) * _FoamAmount;
                float foam = crestFoam * (0.55 + 0.9 * foamNoise);

                float dShip = distance(positionWS.xz, _ShipData.xz);
                float ring = smoothstep(_HullFoamRadius * 2.1, _HullFoamRadius * 0.75, dShip) * _HullFoamStrength;
                foam += ring * (0.45 + 0.75 * foamNoise);

                float2 fwd = normalize(_ShipForward.xz + 1e-5);
                float2 toP = positionWS.xz - _ShipData.xz;
                float along = clamp(dot(toP, -fwd), 0.0, _WakeLength);
                float2 closest = _ShipData.xz - fwd * along;
                float dWake = distance(positionWS.xz, closest);
                float wakeFade = 1.0 - along / max(_WakeLength, 1.0);
                float wake = smoothstep(_WakeWidth, _WakeWidth * 0.2, dWake) * wakeFade * wakeFade * _WakeStrength * _ShipData.w;
                foam += wake * (0.35 + 0.85 * foamNoise);

                col = lerp(col, _FoamColor.rgb, saturate(foam));

                col = MixFog(col, i.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
";
}
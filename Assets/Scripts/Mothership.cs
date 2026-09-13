using UnityEngine;
using TFOU.Ship;
using Suimono.Core;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════
///  MOTHERSHIP — one-attach naval assembler (plug & play)
/// ═══════════════════════════════════════════════════════════════════════
/// Attach this SINGLE script to any ship model root. It builds the whole
/// stack around it:
///
///   1. Rigidbody + master_ship (engine, rudder, camera, FX, HUD)
///   2. Detects a scene SUIMONO_Module (Suimono 2 water):
///        FOUND  -> ship floats on SUIMONO waves (their system = original)
///        MISSING-> falls back to the built-in procedural ocean
///   3. Buoyancy style:
///        HybridMasterShip -> proven velocity-driven handling sampling
///                            Suimono's surface via SuimonoBridge
///        PureSuimono      -> spawns an fx_buoyancy cluster (bow/stern/
///                            port/starboard/center, applyToParent) and
///                            master_ship only feeds thrust + rudder;
///                            PhysX and Suimono own heave/roll/pitch
///   4. Wires Suimono's module to your main camera + sun light
///   5. fx_soundModule holder (assign clips in the inspector)
///   6. optional experimental fx_EffectTrail wake (see README)
///
/// Everything is idempotent: call Setup() again any time (component
/// context menu -> "Setup Ship").
/// </summary>
public class Mothership : MonoBehaviour
{
    public enum WaterPreference { Auto, Procedural, Suimono }
    public enum BuoyancyStyle { HybridMasterShip, PureSuimono }

    [Header("=== WATER BACKEND ===")]
    [Tooltip("Auto = use Suimono when a SUIMONO_Module exists in the scene, else procedural ocean.")]
    public WaterPreference waterPreference = WaterPreference.Auto;

    [Header("=== BUOYANCY ===")]
    [Tooltip("Hybrid = master_ship handling on Suimono waves. Pure = Suimono fx_buoyancy force cluster (needs SUIMONO_Module).")]
    public BuoyancyStyle buoyancyStyle = BuoyancyStyle.HybridMasterShip;

    [Range(3, 9)]
    [Tooltip("fx_buoyancy points spawned under the hull (Pure style).")]
    public int buoyancyPoints = 5;

    [Header("=== OPTIONAL MODULES ===")]
    public bool autoAddMasterShip = true;
    [Tooltip("Point Suimono's module at Camera.main and the scene sun light.")]
    public bool wireSuimonoCamera = true;
    [Tooltip("Add fx_soundModule + AudioSource holder for water/engine clips.")]
    public bool addSoundHolder = true;
    [Tooltip("fx_EffectTrail wake. The committed trail script is partially commented out and needs a Suimono trail material - experimental.")]
    public bool experimentalWakeTrail = false;

    [HideInInspector] public string statusLog = "";

    private master_ship _ship;
    private Rigidbody _rb;

    private void Start()
    {
        // Start (not Awake): when master_ship is a scene component too, its
        // Awake - and with it the hull bounds calculation - is guaranteed to
        // have run by the time any Start executes.
        Setup();
    }

    private void Update()
    {
        // Keep Suimono pointed at a live camera (it may spawn after us).
        if (!SuimonoBridge.Available || !wireSuimonoCamera) return;
        var module = SuimonoBridge.Module;
        if (module.setCamera == null)
        {
            var cam = Camera.main;
            if (cam != null) module.setCamera = cam.transform;
        }
    }

    [ContextMenu("Setup Ship")]
    public void Setup()
    {
        // ---- 1. physics body ----
        _rb = GetComponent<Rigidbody>();
        if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();

        // ---- 2. master controller ----
        _ship = GetComponent<master_ship>();
        if (_ship == null && autoAddMasterShip) _ship = gameObject.AddComponent<master_ship>();
        if (_ship == null)
        {
            statusLog = "Mothership: no master_ship and autoAddMasterShip is off.";
            Debug.LogWarning("[Mothership] " + statusLog);
            return;
        }

        // ---- 3. water backend ----
        SuimonoBridge.Rescan();
        var want = waterPreference switch
        {
            WaterPreference.Procedural => master_ship.WaterBackend.Procedural,
            WaterPreference.Suimono => master_ship.WaterBackend.Suimono,
            _ => master_ship.WaterBackend.Auto
        };

        if (_ship.waterBackend != want) _ship.waterBackend = want;
        _ship.ReconfigureWater();

        bool suimonoLive = _ship.ResolvedBackend == master_ship.WaterBackend.Suimono;

        // ---- 4. Suimono module wiring ----
        if (suimonoLive) WireSuimono();

        // ---- 5. buoyancy style ----
        if (buoyancyStyle == BuoyancyStyle.PureSuimono && suimonoLive)
        {
            SpawnSuimonoBuoyancyCluster();
            _ship.SetExternalBuoyancy(true);
        }
        else
        {
            if (buoyancyStyle == BuoyancyStyle.PureSuimono && !suimonoLive)
                Debug.LogWarning("[Mothership] PureSuimono requested but no SUIMONO_Module in scene - staying on hybrid buoyancy.");
            _ship.SetExternalBuoyancy(false);
        }

        // ---- 6. extras ----
        if (addSoundHolder) EnsureSoundHolder();
        if (experimentalWakeTrail && suimonoLive) TryWakeTrail();

        statusLog = suimonoLive
            ? $"SUIMONO water active | buoyancy: {(buoyancyStyle == BuoyancyStyle.PureSuimono ? "fx_buoyancy cluster" : "hybrid")}"
            : "SUIMONO_Module not found -> procedural ocean fallback";
        Debug.Log("[Mothership] " + statusLog);
    }

    // ===================== Suimono wiring =====================

    private void WireSuimono()
    {
        var module = SuimonoBridge.Module;
        if (module == null) return;

        if (wireSuimonoCamera)
        {
            var cam = Camera.main;
            if (cam != null && module.setCamera == null) module.setCamera = cam.transform;

            if (module.setLight == null)
            {
                var sun = FindFirstObjectByType<Light>();
                if (sun != null) module.setLight = sun;
            }
        }
    }

    private void SpawnSuimonoBuoyancyCluster()
    {
        if (transform.Find("BuoyPoint_0") != null) return; // already spawned

        Bounds lb = _ship != null ? _ship.LocalBounds : new Bounds(Vector3.zero, new Vector3(14f, 8f, 60f));
        if (lb.size.sqrMagnitude < 0.01f) lb = new Bounds(Vector3.zero, new Vector3(14f, 8f, 60f));
        float L = lb.size.z;
        float B = lb.size.x;
        float pointY = lb.min.y + lb.size.y * 0.35f;

        int count = Mathf.Clamp(buoyancyPoints, 3, 9);
        Vector3[] layout = BuildLayout(count, lb.center, L, B, pointY);

        for (int i = 0; i < layout.Length; i++)
        {
            var go = new GameObject("BuoyPoint_" + i);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = layout[i];

            var fb = go.AddComponent<fx_buoyancy>();
            fb.applyToParent = true;      // forces go to the ship root Rigidbody
            fb.engageBuoyancy = true;
            fb.activationRange = 0f;      // always active (no camera gating)
            fb.inheritForce = true;       // currents/waves push the hull
            fb.buoyancyStrength = 1f;
            fb.forceAmount = 1f;
        }

        Debug.Log($"[Mothership] spawned {layout.Length} fx_buoyancy points (splitFac auto-distributes force).");
    }

    private static Vector3[] BuildLayout(int count, Vector3 center, float L, float B, float y)
    {
        switch (count)
        {
            case 3:
                return new[]
                {
                    new Vector3(center.x, y, center.z + L * 0.40f),
                    new Vector3(center.x, y, center.z - L * 0.40f),
                    new Vector3(center.x, y, center.z),
                };
            case 4:
                return new[]
                {
                    new Vector3(center.x, y, center.z + L * 0.40f),
                    new Vector3(center.x, y, center.z - L * 0.40f),
                    new Vector3(center.x + B * 0.40f, y, center.z),
                    new Vector3(center.x - B * 0.40f, y, center.z),
                };
            case 5:
                return new[]
                {
                    new Vector3(center.x, y, center.z + L * 0.42f),
                    new Vector3(center.x, y, center.z - L * 0.42f),
                    new Vector3(center.x + B * 0.42f, y, center.z),
                    new Vector3(center.x - B * 0.42f, y, center.z),
                    new Vector3(center.x, y, center.z),
                };
            default: // 6-9: perimeter ring + center + bow/stern
                var pts = new System.Collections.Generic.List<Vector3>
                {
                    new Vector3(center.x, y, center.z + L * 0.42f),
                    new Vector3(center.x, y, center.z - L * 0.42f),
                    new Vector3(center.x, y, center.z),
                };
                int pairs = (count - 3) / 2;
                for (int i = 0; i < pairs; i++)
                {
                    float t = pairs == 1 ? 0f : (i / (float)(pairs - 1) - 0.5f) * 2f; // -1..1 along hull
                    float z = center.z + t * L * 0.30f;
                    pts.Add(new Vector3(center.x + B * 0.42f, y, z));
                    pts.Add(new Vector3(center.x - B * 0.42f, y, z));
                }
                while (pts.Count > count) pts.RemoveAt(pts.Count - 1);
                return pts.ToArray();
        }
    }

    // ===================== extras =====================

    private void EnsureSoundHolder()
    {
        if (GetComponentInChildren<fx_soundModule>() != null) return;

        var go = new GameObject("ShipSound");
        go.transform.SetParent(transform, false);
        go.AddComponent<fx_soundModule>();

        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = true;
        src.spatialBlend = 1f;
    }

    private void TryWakeTrail()
    {
        if (transform.Find("WakeTrail") != null) return;

        Bounds lb = _ship != null ? _ship.LocalBounds : new Bounds(Vector3.zero, new Vector3(14f, 8f, 60f));
        if (lb.size.sqrMagnitude < 0.01f) lb = new Bounds(Vector3.zero, new Vector3(14f, 8f, 60f));

        var go = new GameObject("WakeTrail");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(lb.center.x, lb.min.y + lb.size.y * 0.15f, lb.center.z - lb.extents.z);

        go.AddComponent<MeshFilter>().sharedMesh = new Mesh { name = "WakeTrailMesh" };
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        go.AddComponent<fx_EffectTrail>();
        Debug.LogWarning("[Mothership] fx_EffectTrail added at the stern. Assign a Suimono trail material " +
                         "to the MeshRenderer to see it (the committed trail script builds meshes only when " +
                         "its Suimono module hooks are active).");
    }
}

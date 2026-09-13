using System.Collections.Generic;
using UnityEngine;
using TFOU.Ocean;
using TFOU.Ship;

namespace TFOU.World
{
    /// <summary>
    /// Runtime-generated naval test map: islands with a lighthouse, rock hazards,
    /// and a glowing slalom course with split timers to test ship handling.
    /// Fully procedural (seeded), zero asset dependencies. Buoys bob on the
    /// shared OceanWaves Gerstner field, and island coastlines automatically
    /// get contact foam / shallow absorption from the ocean shader's depth pass.
    ///
    /// Course: sail through the glowing gate buoys in order. R = reset.
    /// </summary>
    public class TestMapGenerator : MonoBehaviour
    {
        public static TestMapGenerator Instance { get; private set; }

        public int seed = 1337;
        public Transform ship;

        [Header("Slalom Course")]
        public int gateCount = 6;
        public float gateSpacing = 260f;
        public float gateHalfWidth = 45f;
        public float gateOffset = 70f;
        public float gateTriggerRadius = 60f;

        [Header("Islands")]
        public bool generateIslands = true;

        // ---------- course state ----------
        private readonly List<Vector3> _gates = new List<Vector3>();
        private readonly List<Renderer[]> _gateRenderers = new List<Renderer[]>();
        private readonly List<Material[]> _gateMaterials = new List<Material[]>();
        private int _nextGate;
        private bool _courseStarted;
        private float _startTime, _lastSplit, _lastSplitKn, _totalTime = -1f, _bestTotal = -1f;
        private float _splitDist, _totalDist, _lastGateTime;
        private Vector3 _lastGatePos;
        private GUIStyle _style;

        // ---------- bobbing decorations ----------
        private class Bobber
        {
            public Transform Transform;
            public float BaseOffset;
        }
        private readonly List<Bobber> _bobbers = new List<Bobber>();

        private Material _terrainMat, _rockMat, _towerMat, _lampMat;
        private Transform _lighthouseBeam;
        private OceanWaves _waves;
        private float _waterLevel;

        // ===================== bootstrap =====================

        public static TestMapGenerator Ensure(int seed, Transform ship)
        {
            if (Instance == null) Instance = FindFirstObjectByType<TestMapGenerator>();
            if (Instance == null)
            {
                var go = new GameObject("TestMap");
                Instance = go.AddComponent<TestMapGenerator>();
                Instance.seed = seed;
                Instance.ship = ship;
                Instance.Build();
            }
            return Instance;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Build()
        {
            _waves = OceanWaves.Instance;
            _waterLevel = _waves != null ? _waves.waterLevel : 0f;

            CreateMaterials();
            BuildCourse();
            if (generateIslands)
            {
                BuildIslands();
                BuildRocks();
            }
        }

        // ===================== materials =====================

        private void CreateMaterials()
        {
            Shader terrain = Shader.Find("Custom/AutoTerrainLit");
            if (terrain == null)
            {
                Debug.LogWarning("[TestMap] AutoTerrainLit shader not found - map will use magenta fallback.");
                terrain = Shader.Find("Universal Render Pipeline/Lit");
            }

            _terrainMat = new Material(terrain) { name = "MapTerrain" };
            _rockMat = new Material(terrain) { name = "MapRock" };
            _rockMat.SetColor("_BaseColor", new Color(0.32f, 0.31f, 0.30f));

            _towerMat = new Material(terrain) { name = "MapTower" };
            _towerMat.SetColor("_BaseColor", new Color(0.92f, 0.92f, 0.90f));

            _lampMat = new Material(terrain) { name = "MapLamp" };
            _lampMat.SetColor("_BaseColor", new Color(1f, 0.85f, 0.6f));
            _lampMat.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.45f));
            _lampMat.SetFloat("_EmissionStrength", 3.5f);
        }

        private Material MakeBuoyMaterial(Color baseCol, Color emissive, string name)
        {
            var m = new Material(_terrainMat.shader) { name = name };
            m.SetColor("_BaseColor", baseCol);
            m.SetColor("_EmissionColor", emissive);
            m.SetFloat("_EmissionStrength", 1.6f);
            return m;
        }

        /// <summary>Primitives lack a COLOR stream; inject white vertex colors so the terrain shader renders them correctly.</summary>
        private static void EnsureVertexColors(GameObject go)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            var mesh = Instantiate(mf.sharedMesh);
            mesh.name = mf.sharedMesh.name + " (VC)";
            var cols = new Color[mesh.vertexCount];
            for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
            mesh.colors = cols;
            mf.sharedMesh = mesh;
        }

        // ===================== course =====================

        private void BuildCourse()
        {
            if (ship == null) return;

            Vector3 origin = ship.position;
            Vector3 fwd = ship.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            var root = new GameObject("Course");
            root.transform.SetParent(transform, false);

            for (int i = 0; i < gateCount; i++)
            {
                // Per-gate material instances so only the NEXT gate pulses.
                var portMat = MakeBuoyMaterial(new Color(0.85f, 0.15f, 0.12f), new Color(1f, 0.18f, 0.1f), "BuoyPort" + i);
                var stbdMat = MakeBuoyMaterial(new Color(0.1f, 0.7f, 0.2f), new Color(0.15f, 1f, 0.3f), "BuoyStarboard" + i);

                float along = 300f + i * gateSpacing;
                float side = i == 0 ? 0f : (i % 2 == 0 ? 1f : -1f) * gateOffset;
                Vector3 center = origin + fwd * along + right * side;
                _gates.Add(center);

                var renderers = new List<Renderer>();
                var materials = new List<Material>();
                SpawnGateBuoy(center - right * gateHalfWidth, portMat, root.transform, renderers, materials);
                SpawnGateBuoy(center + right * gateHalfWidth, stbdMat, root.transform, renderers, materials);
                _gateRenderers.Add(renderers.ToArray());
                _gateMaterials.Add(materials.ToArray());
            }
        }

        private void SpawnGateBuoy(Vector3 pos, Material mat, Transform parent, List<Renderer> renderers, List<Material> materials)
        {
            var buoy = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            buoy.name = "GateBuoy";
            buoy.transform.SetParent(parent, false);
            buoy.transform.localScale = new Vector3(1.6f, 1.4f, 1.6f);
            EnsureVertexColors(buoy);

            var mr = buoy.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderers.Add(mr);
            materials.Add(mat);

            // glowing top marker
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lamp.name = "BuoyLamp";
            lamp.transform.SetParent(buoy.transform, false);
            lamp.transform.localPosition = new Vector3(0f, 1.35f, 0f);
            lamp.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            EnsureVertexColors(lamp);
            var lampMr = lamp.GetComponent<MeshRenderer>();
            lampMr.sharedMaterial = mat;
            lampMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderers.Add(lampMr);
            materials.Add(mat);

            float surf = SurfaceYAt(pos.x, pos.z);
            buoy.transform.position = new Vector3(pos.x, surf + 1.4f, pos.z);
            _bobbers.Add(new Bobber { Transform = buoy.transform, BaseOffset = 1.4f });
        }

        // ===================== islands =====================

        private void BuildIslands()
        {
            Vector3 origin = ship != null ? ship.position : Vector3.zero;
            Vector3 fwd = ship != null ? ship.forward : Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            // Lighthouse island: ahead-right of the course.
            var main = BuildIsland(origin + fwd * 950f + right * 640f, 400f, 58f, seed + 1, "Island_Lighthouse");
            PlaceLighthouse(main);

            // Second island: left of the course, mid-distance landmark.
            BuildIsland(origin + fwd * 380f - right * 880f, 280f, 32f, seed + 7, "Island_West");

            // Small island behind the spawn: reference point for return legs.
            BuildIsland(origin - fwd * 720f + right * 260f, 200f, 24f, seed + 13, "Island_South");
        }

        private GameObject BuildIsland(Vector3 center, float radius, float peakHeight, int islandSeed, string name)
        {
            const int res = 90;
            float size = radius * 2f;

            int vertCount = (res + 1) * (res + 1);
            var verts = new Vector3[vertCount];
            var cols = new Color[vertCount];
            var tris = new int[res * res * 6];

            float seedX = (islandSeed % 97) * 17.31f;
            float seedZ = (islandSeed % 61) * 29.77f;

            int vi = 0, ti = 0;
            var heights = new float[vertCount];

            for (int z = 0; z <= res; z++)
            {
                for (int x = 0; x <= res; x++)
                {
                    float fx = x / (float)res - 0.5f;
                    float fz = z / (float)res - 0.5f;
                    float r = Mathf.Sqrt(fx * fx + fz * fz) * 2f; // 0 center, 1 edge

                    float n = Fbm(fx * size * 0.011f + seedX, fz * size * 0.011f + seedZ);
                    float shape = 1f - Smoothstep(0.30f, 0.92f, r);

                    float h = shape * peakHeight * (0.45f + 0.85f * n) - (1f - shape) * 14f - 4f;
                    heights[vi] = h;
                    verts[vi] = new Vector3(fx * size, h, fz * size);
                    vi++;

                    if (x < res && z < res)
                    {
                        int a = z * (res + 1) + x;
                        int b = a + 1;
                        int c = a + (res + 1);
                        int d = c + 1;
                        tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                        tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
                    }
                }
            }

            var mesh = new Mesh { name = name + "Mesh" };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.uv = new Vector2[vertCount];

            // Vertex colors from height + slope (normals are computed by now).
            var normals = mesh.normals;
            for (int i = 0; i < vertCount; i++)
            {
                float h = heights[i];
                float slope = 1f - Mathf.Clamp01(normals[i].y);
                float nz = Fbm(verts[i].x * 0.05f + seedX, verts[i].z * 0.05f + seedZ);

                Color c;
                if (h < 0.6f) c = new Color(0.66f, 0.58f, 0.40f);                       // wet sand
                else if (h < 2.5f) c = new Color(0.76f, 0.70f, 0.50f);                  // beach
                else if (h < peakHeight * 0.55f && slope < 0.42f)
                    c = Color.Lerp(new Color(0.26f, 0.38f, 0.18f), new Color(0.34f, 0.44f, 0.22f), nz); // grass
                else
                    c = Color.Lerp(new Color(0.40f, 0.38f, 0.35f), new Color(0.30f, 0.29f, 0.27f), nz); // rock

                if (slope > 0.5f) c = Color.Lerp(c, new Color(0.38f, 0.36f, 0.33f), Smoothstep(0.5f, 0.75f, slope));
                cols[i] = c;
            }
            mesh.colors = cols;

            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(center.x, _waterLevel, center.z);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _terrainMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;

            return go;
        }

        private void PlaceLighthouse(GameObject island)
        {
            var mf = island.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;

            var mesh = mf.sharedMesh;
            var verts = mesh.vertices;
            int best = 0;
            for (int i = 1; i < verts.Length; i++)
                if (verts[i].y > verts[best].y) best = i;

            Vector3 top = island.transform.position + verts[best];

            var tower = new GameObject("Lighthouse");
            tower.transform.SetParent(transform, false);
            tower.transform.position = top;

            var baseCyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseCyl.name = "TowerBase";
            baseCyl.transform.SetParent(tower.transform, false);
            baseCyl.transform.localPosition = new Vector3(0f, 9f, 0f);
            baseCyl.transform.localScale = new Vector3(5.5f, 9f, 5.5f);
            EnsureVertexColors(baseCyl);
            baseCyl.GetComponent<MeshRenderer>().sharedMaterial = _towerMat;

            var topCyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            topCyl.name = "TowerTop";
            topCyl.transform.SetParent(tower.transform, false);
            topCyl.transform.localPosition = new Vector3(0f, 20f, 0f);
            topCyl.transform.localScale = new Vector3(3.6f, 3f, 3.6f);
            EnsureVertexColors(topCyl);
            topCyl.GetComponent<MeshRenderer>().sharedMaterial = _towerMat;

            var lampRoom = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lampRoom.name = "LampRoom";
            lampRoom.transform.SetParent(tower.transform, false);
            lampRoom.transform.localPosition = new Vector3(0f, 24f, 0f);
            lampRoom.transform.localScale = Vector3.one * 3.4f;
            EnsureVertexColors(lampRoom);
            lampRoom.GetComponent<MeshRenderer>().sharedMaterial = _lampMat;

            var beamGO = new GameObject("LighthouseBeam");
            beamGO.transform.SetParent(tower.transform, false);
            beamGO.transform.localPosition = new Vector3(0f, 24f, 0f);
            var beam = beamGO.AddComponent<Light>();
            beam.type = LightType.Spot;
            beam.range = 2500f;
            beam.spotAngle = 9f;
            beam.intensity = 5f;
            beam.color = new Color(1f, 0.92f, 0.7f);
            beam.shadows = LightShadows.None;
            _lighthouseBeam = beamGO.transform;
        }

        // ===================== rocks =====================

        private void BuildRocks()
        {
            if (ship == null) return;
            var rnd = new System.Random(seed + 777);

            Vector3 origin = ship.position;
            Vector3 fwd = ship.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            // Hazard clusters just outside the slalom gates - punish sloppy lines.
            for (int i = 0; i < _gates.Count; i++)
            {
                Vector3 g = _gates[i];
                int count = rnd.Next(2, 4);
                for (int j = 0; j < count; j++)
                {
                    float side = (j % 2 == 0 ? 1f : -1f);
                    float off = gateHalfWidth + 70f + (float)rnd.NextDouble() * 90f;
                    float alongJ = ((float)rnd.NextDouble() - 0.5f) * 80f;
                    Vector3 p = g + right * (side * off) + fwd * alongJ;
                    SpawnRock(p, 3.5f + (float)rnd.NextDouble() * 5f, rnd);
                }
            }

            // A few lonely seamounts around the basin.
            for (int i = 0; i < 6; i++)
            {
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float dist = 500f + (float)rnd.NextDouble() * 900f;
                Vector3 p = origin + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist;
                SpawnRock(p, 4f + (float)rnd.NextDouble() * 7f, rnd);
            }
        }

        private void SpawnRock(Vector3 pos, float scale, System.Random rnd)
        {
            var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rock.name = "Rock";
            rock.transform.SetParent(transform, false);
            float sy = scale * (0.7f + (float)rnd.NextDouble() * 0.9f);
            rock.transform.localScale = new Vector3(scale, sy, scale * (0.8f + (float)rnd.NextDouble() * 0.4f));
            rock.transform.localEulerAngles = new Vector3(0f, (float)rnd.NextDouble() * 360f, 0f);

            EnsureVertexColors(rock);
            var mr = rock.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _rockMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            float surf = SurfaceYAt(pos.x, pos.z);
            rock.transform.position = new Vector3(pos.x, surf - sy * 0.25f, pos.z);
        }

        // ===================== runtime =====================

        private float SurfaceYAt(float x, float z)
        {
            if (_waves == null) _waves = OceanWaves.Instance;
            if (_waves != null) return _waves.SurfaceY(x, z, Time.time);
            if (SuimonoBridge.Available) return SuimonoBridge.GetSurfaceY(new Vector3(x, 0f, z));
            return _waterLevel;
        }

        private void Update()
        {
            BobDecorations();
            PulseGates();

            if (_lighthouseBeam != null)
                _lighthouseBeam.Rotate(0f, 22f * Time.deltaTime, 0f, Space.Self);

            UpdateCourse();
        }

        private void BobDecorations()
        {
            if (_waves == null) _waves = OceanWaves.Instance;
            if (_waves == null && !SuimonoBridge.Available) return;

            float t = Time.time;
            for (int i = 0; i < _bobbers.Count; i++)
            {
                var b = _bobbers[i];
                if (b.Transform == null) continue;
                Vector3 p = b.Transform.position;
                float surf = SurfaceYAt(p.x, p.z);
                b.Transform.position = new Vector3(p.x, surf + b.BaseOffset, p.z);

                Vector3 n = _waves != null ? _waves.SampleNormal(p.x, p.z, t) : Vector3.up;
                Quaternion tilt = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, n, 0.8f));
                b.Transform.rotation = Quaternion.Slerp(b.Transform.rotation, tilt, Time.deltaTime * 3f);
            }
        }

        private void PulseGates()
        {
            float pulse = 1.1f + Mathf.Sin(Time.time * 3.5f) * 0.7f;
            for (int i = 0; i < _gateMaterials.Count; i++)
            {
                bool isNext = _courseStarted ? (i == _nextGate) : (i == 0 && _totalTime < 0f);
                float strength = isNext ? pulse * 2.2f : 0.35f;
                var mats = _gateMaterials[i];
                for (int m = 0; m < mats.Length; m++)
                    if (mats[m] != null) mats[m].SetFloat("_EmissionStrength", strength);
            }
        }

        private void UpdateCourse()
        {
            if (ship == null || _gates.Count == 0) return;

            if (ShipInput.KeyDown(KeyCode.R)) ResetCourse();

            Vector3 sp = ship.position; sp.y = 0f;

            if (_totalTime >= 0f || !_courseStarted)
            {
                Vector3 g0 = _gates[0]; g0.y = 0f;
                if (!_courseStarted && _totalTime < 0f && Vector3.Distance(sp, g0) < gateTriggerRadius)
                {
                    _courseStarted = true;
                    _startTime = Time.time;
                    _lastGateTime = _startTime;
                    _lastSplit = 0f;
                    _nextGate = 1;
                    _lastGatePos = _gates[0];
                    _splitDist = 0f;
                    _totalDist = 0f;
                }
            }
            else if (_nextGate < _gates.Count)
            {
                Vector3 g = _gates[_nextGate];
                Vector3 gp = g; gp.y = 0f;
                if (Vector3.Distance(sp, gp) < gateTriggerRadius)
                {
                    float now = Time.time;
                    _lastSplit = now - _lastGateTime;
                    _lastGateTime = now;

                    _splitDist = Vector3.Distance(Flatten(_lastGatePos), g);
                    _totalDist += _splitDist;
                    _lastSplitKn = _splitDist / Mathf.Max(0.01f, _lastSplit) * 1.94384f;
                    _lastGatePos = g;
                    _nextGate++;

                    if (_nextGate >= _gates.Count)
                    {
                        _totalTime = now - _startTime;
                        if (_bestTotal < 0f || _totalTime < _bestTotal) _bestTotal = _totalTime;
                        _courseStarted = false;
                    }
                }
            }
        }

        private static Vector3 Flatten(Vector3 v) { v.y = 0f; return v; }

        public void ResetCourse()
        {
            _courseStarted = false;
            _nextGate = 0;
            _totalTime = -1f;
            _lastSplit = 0f;
            _lastSplitKn = 0f;
            _totalDist = 0f;
        }

        // ===================== HUD =====================

        private void OnGUI()
        {
            if (ship == null || _gates.Count == 0) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 13,
                    padding = new RectOffset(10, 10, 8, 8)
                };
                _style.normal.textColor = new Color(0.9f, 1f, 0.85f, 0.95f);
            }

            string s;
            if (_totalTime >= 0f)
            {
                float avgKn = _totalDist / Mathf.Max(0.01f, _totalTime) * 1.94384f;
                s = $"<b>COURSE FINISHED</b>\n" +
                    $"total {_totalTime:0.00}s   avg {avgKn:0.0} kn\n" +
                    $"best {(_bestTotal < 0f ? "—" : _bestTotal.ToString("0.00") + "s")}   <i>R to run again</i>";
            }
            else if (_courseStarted)
            {
                s = $"<b>COURSE</b>  gate {_nextGate}/{_gates.Count}\n" +
                    $"last split {_lastSplit:0.0}s ({_lastSplitKn:0.0} kn)\n" +
                    $"elapsed {(Time.time - _startTime):0.0}s   best {(_bestTotal < 0f ? "—" : _bestTotal.ToString("0.00") + "s")}";
            }
            else
            {
                s = $"<b>SLALOM COURSE</b>  {_gates.Count} gates\n" +
                    $"sail into the glowing buoys to start\n" +
                    $"<i>R = reset</i>";
            }

            GUI.Box(new Rect(Screen.width - 322f, 12f, 310f, 84f), s, _style);
        }

        // ===================== noise helpers =====================

        private static float Smoothstep(float a, float b, float t)
        {
            float x = Mathf.Clamp01((t - a) / Mathf.Max(1e-5f, b - a));
            return x * x * (3f - 2f * x);
        }

        private static float Hash21(Vector2 p)
        {
            float h = Mathf.Sin(Vector2.Dot(p, new Vector2(127.1f, 311.7f))) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        private static float ValueNoise(Vector2 p)
        {
            Vector2 i = new Vector2(Mathf.Floor(p.x), Mathf.Floor(p.y));
            Vector2 f = p - i;
            f = new Vector2(f.x * f.x * (3f - 2f * f.x), f.y * f.y * (3f - 2f * f.y));

            float a = Hash21(i);
            float b = Hash21(i + Vector2.right);
            float c = Hash21(i + Vector2.up);
            float d = Hash21(i + Vector2.one);
            return Mathf.Lerp(Mathf.Lerp(a, b, f.x), Mathf.Lerp(c, d, f.x), f.y);
        }

        private static float Fbm(float x, float y) => Fbm(new Vector2(x, y));

        private static float Fbm(Vector2 p)
        {
            float v = 0f, a = 0.5f;
            for (int i = 0; i < 4; i++)
            {
                v += a * ValueNoise(p);
                p = new Vector2(p.x * 2.03f + 11.7f, p.y * 2.01f - 7.3f);
                a *= 0.5f;
            }
            return v;
        }
    }
}

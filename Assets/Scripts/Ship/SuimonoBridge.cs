using UnityEngine;
using Suimono.Core;

namespace TFOU.Ship
{
    /// <summary>
    /// Single point of contact between the TFOU ship systems and the Suimono 2
    /// water module (Assets/Scripts/SCRIPTS). Everything Suimono-specific lives
    /// here so the rest of the codebase stays backend-agnostic.
    ///
    /// Primary API used: SuimonoModule.SuimonoGetHeightAll(pos)
    ///   [0] = absolute surface Y (what fx_buoyancy itself uses)
    ///   [2] = base water level
    ///   [3] = object depth
    ///   [4] = is over water (1/0)
    ///   [6] = flow direction (deg)   [7] = flow speed   [8] = normalized wave height
    /// </summary>
    public static class SuimonoBridge
    {
        private static SuimonoModule _cached;

        public static SuimonoModule Module
        {
            get
            {
                if (_cached != null) return _cached;
                _cached = Object.FindFirstObjectByType<SuimonoModule>();
                return _cached;
            }
        }

        public static bool Available => Module != null;

        /// <summary>Forces a fresh scene scan (call after adding/removing the module).</summary>
        public static void Rescan() => _cached = null;

        /// <summary>Absolute water-surface Y at a world position (waves included).</summary>
        public static float GetSurfaceY(Vector3 worldPos)
        {
            var m = Module;
            if (m == null) return 0f;
            float[] v = m.SuimonoGetHeightAll(worldPos);
            return (v != null && v.Length > 0) ? v[0] : 0f;
        }

        /// <summary>Calm base water level of the Suimono system.</summary>
        public static float GetBaseLevel()
        {
            var m = Module;
            if (m == null) return 0f;
            float lvl = m.currentSurfaceLevel;
            if (float.IsNaN(lvl)) lvl = 0f;
            return lvl;
        }

        /// <summary>True when the position is above a registered water surface.</summary>
        public static bool IsOverWater(Vector3 worldPos)
        {
            var m = Module;
            if (m == null) return false;
            float[] v = m.SuimonoGetHeightAll(worldPos);
            return v != null && v.Length > 4 && v[4] == 1f;
        }

        /// <summary>Surface normal via central differences (Suimono has no direct normal API).</summary>
        public static Vector3 GetSurfaceNormal(Vector3 worldPos, float epsilon = 1.5f)
        {
            if (Module == null) return Vector3.up;
            float hXp = GetSurfaceY(worldPos + Vector3.right * epsilon);
            float hXn = GetSurfaceY(worldPos - Vector3.right * epsilon);
            float hZp = GetSurfaceY(worldPos + Vector3.forward * epsilon);
            float hZn = GetSurfaceY(worldPos - Vector3.forward * epsilon);
            return new Vector3(hXn - hXp, 2f * epsilon, hZn - hZp).normalized;
        }

        /// <summary>World-space flow (current) velocity at a position.</summary>
        public static Vector3 GetFlowVector(Vector3 worldPos)
        {
            var m = Module;
            if (m == null) return Vector3.zero;
            float[] v = m.SuimonoGetHeightAll(worldPos);
            if (v == null || v.Length < 8) return Vector3.zero;
            Vector2 dir = m.SuimonoConvertAngleToVector(v[6]);
            return new Vector3(dir.x, 0f, dir.y) * v[7];
        }
    }
}

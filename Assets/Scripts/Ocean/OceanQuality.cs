using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TFOU.Ocean
{
    /// <summary>
    /// Enforces the render settings the ocean needs and grades the whole frame.
    /// Everything is applied at runtime - no project assets are touched, so this
    /// survives quality-level switches (Mobile/PC URP assets both get patched).
    ///
    /// ACES tonemapping + HDR bloom + vignette + subtle grain is the "free AAA"
    /// stack: it makes sun glitter on the water bloom naturally and gives the
    /// image filmic contrast instead of the flat default URP look.
    /// </summary>
    public static class OceanQuality
    {
        private const string VolumeObjectName = "OceanPostFX_Volume";
        private static GameObject _volumeGO;

        public static void PatchPipelineAsset()
        {
            var urp = UniversalRenderPipeline.asset;
            if (urp == null) return;

            urp.supportsCameraDepthTexture = true;    // contact foam, depth absorption, refraction mask
            urp.supportsCameraOpaqueTexture = true;   // hull refraction under the waterline
        }

        public static void PatchCamera(Camera cam, bool postProcessing)
        {
            if (cam == null) return;

            cam.allowHDR = true;

            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = postProcessing;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
                data.renderShadows = true;
            }
        }

        /// <summary>Creates (once) a high-priority global volume with the cinematic grade.</summary>
        public static void EnsurePostVolume(float bloomIntensity, float vignetteIntensity)
        {
            if (_volumeGO != null) return;

            _volumeGO = GameObject.Find(VolumeObjectName);
            if (_volumeGO == null) _volumeGO = new GameObject(VolumeObjectName);
            _volumeGO.hideFlags = HideFlags.HideAndDontSave;

            var volume = _volumeGO.GetComponent<Volume>();
            if (volume == null) volume = _volumeGO.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f; // wins over the scene's template Global Volume

            if (volume.profile == null || volume.profile.name != "OceanCinematicProfile")
            {
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "OceanCinematicProfile";
                profile.hideFlags = HideFlags.HideAndDontSave;

                var tonemap = profile.Add<Tonemapping>(true);
                tonemap.mode.Override(TonemappingMode.Aces);

                var bloom = profile.Add<Bloom>(true);
                bloom.threshold.Override(0.9f);
                bloom.intensity.Override(bloomIntensity);
                bloom.scatter.Override(0.65f);
                bloom.highQualityFiltering.Override(true);

                var vig = profile.Add<Vignette>(true);
                vig.intensity.Override(Mathf.Clamp(vignetteIntensity, 0f, 0.6f));
                vig.smoothness.Override(0.35f);

                var ca = profile.Add<ChromaticAberration>(true);
                ca.intensity.Override(0.12f);

                var grade = profile.Add<ColorAdjustments>(true);
                grade.postExposure.Override(0.08f);
                grade.contrast.Override(12f);
                grade.saturation.Override(8f);

                var grain = profile.Add<FilmGrain>(true);
                grain.intensity.Override(0.14f);
                grain.response.Override(0.6f);

                volume.profile = profile;
            }
        }

        public static void DestroyPostVolume()
        {
            if (_volumeGO != null)
            {
                Object.Destroy(_volumeGO);
                _volumeGO = null;
            }
        }
    }
}

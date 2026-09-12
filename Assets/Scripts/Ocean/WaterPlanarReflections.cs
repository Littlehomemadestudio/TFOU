using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TFOU.Ocean
{
    /// <summary>
    /// Planar (mirror) reflections for the ocean, URP-safe.
    /// Renders the world from a camera mirrored across the water plane into a
    /// RenderTexture and exposes it as the global _PlanarReflectionTexture.
    /// The ocean shader blends it over the procedural sky reflection using
    /// Fresnel, which is what makes a hull "sit in" the water instead of on it.
    ///
    /// Uses the classic beginCameraRendering + GL.invertCulling pattern that
    /// works under scriptable pipelines. The reflection camera culls the Water
    /// layer so it never renders itself (no recursion).
    /// </summary>
    [ExecuteAlways]
    public class WaterPlanarReflections : MonoBehaviour
    {
        public static WaterPlanarReflections Instance { get; private set; }

        [Range(0.125f, 1f)]
        [Tooltip("Reflection RT resolution as a fraction of the main camera.")]
        public float resolutionScale = 0.5f;

        [Tooltip("Raises the mirror plane slightly to avoid waterline clipping artifacts.")]
        public float clipPlaneOffset = 0.07f;

        [Range(0f, 1f)]
        public float reflectionStrength = 0.85f;

        [Tooltip("Render reflections every N frames (1 = every frame).")]
        public int frameSkip = 0;

        private Camera _reflectionCamera;
        private RenderTexture _rt;
        private int _rtWidth, _rtHeight;
        private int _frameCounter;
        private bool _insideRender;

        private static readonly int PlanarTexId = Shader.PropertyToID("_PlanarReflectionTexture");
        private static readonly int PlanarStrengthId = Shader.PropertyToID("_PlanarReflectionStrength");

        public const string KeywordName = "_PLANAR_REFLECTION_ON";
        public const int WaterLayer = 4; // built-in "Water" layer

        private void OnEnable()
        {
            Instance = this;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            Shader.SetGlobalFloat(PlanarStrengthId, reflectionStrength);
            EnsureCamera();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            Shader.SetGlobalFloat(PlanarStrengthId, 0f);
            Shader.SetGlobalTexture(PlanarTexId, Texture2D.blackTexture);
            if (Instance == this) Instance = null;

            if (_reflectionCamera != null)
            {
                var go = _reflectionCamera.gameObject;
                if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
                _reflectionCamera = null;
            }
            ReleaseRT();
        }

        private void OnDestroy()
        {
            ReleaseRT();
        }

        private void EnsureCamera()
        {
            if (_reflectionCamera != null) return;

            var go = new GameObject("OceanPlanarReflectionCam");
            go.hideFlags = HideFlags.HideAndDontSave;
            _reflectionCamera = go.AddComponent<Camera>();
            _reflectionCamera.enabled = false; // rendered manually
            _reflectionCamera.clearFlags = CameraClearFlags.Skybox;
            _reflectionCamera.cullingMask = ~(1 << WaterLayer);
            _reflectionCamera.allowHDR = true;
            _reflectionCamera.allowMSAA = false;

            var data = _reflectionCamera.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = false;
                data.renderShadows = true;
                data.requiresColorOption = CameraOverrideOption.Off;
                data.requiresDepthOption = CameraOverrideOption.Off;
            }
        }

        private void EnsureRT(Camera source)
        {
            int w = Mathf.Max(64, Mathf.RoundToInt(source.pixelWidth * resolutionScale));
            int h = Mathf.Max(64, Mathf.RoundToInt(source.pixelHeight * resolutionScale));
            if (_rt != null && w == _rtWidth && h == _rtHeight) return;

            ReleaseRT();
            _rtWidth = w; _rtHeight = h;
            _rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32)
            {
                name = "OceanPlanarReflectionRT",
                antiAliasing = 1,
                useMipMap = false,
                hideFlags = HideFlags.HideAndDontSave
            };
            _reflectionCamera.targetTexture = _rt;
            Shader.SetGlobalTexture(PlanarTexId, _rt);
        }

        private void ReleaseRT()
        {
            if (_rt != null)
            {
                _rt.Release();
                if (Application.isPlaying) Destroy(_rt); else DestroyImmediate(_rt);
                _rt = null;
            }
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (_insideRender) return;
            if (_reflectionCamera == null || cam == _reflectionCamera) return;
            if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            if (cam.targetTexture != null && cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;

            if (frameSkip > 0)
            {
                _frameCounter = (_frameCounter + 1) % (frameSkip + 1);
                if (_frameCounter != 0) return;
            }

            float waterY = OceanWaves.Instance != null ? OceanWaves.Instance.waterLevel : 0f;

            EnsureRT(cam);

            // Mirror the camera transform across y = waterY (+ small offset).
            Vector3 camPos = cam.transform.position;
            Vector3 mirrorPos = new Vector3(camPos.x, 2f * waterY - camPos.y, camPos.z);
            Quaternion camRot = cam.transform.rotation;
            Vector3 e = camRot.eulerAngles;
            Quaternion mirrorRot = Quaternion.Euler(-e.x, e.y, -e.z);

            _reflectionCamera.transform.SetPositionAndRotation(mirrorPos, mirrorRot);
            _reflectionCamera.fieldOfView = cam.fieldOfView;
            _reflectionCamera.nearClipPlane = cam.nearClipPlane;
            _reflectionCamera.farClipPlane = cam.farClipPlane;
            _reflectionCamera.projectionMatrix = cam.projectionMatrix;
            _reflectionCamera.cullingMask = ~(1 << WaterLayer);

            // Oblique near plane: clip everything below the water surface so
            // geometry under the plane never bleeds into the reflection.
            Vector4 clipPlane = CameraSpacePlane(_reflectionCamera,
                new Vector3(0f, waterY + clipPlaneOffset, 0f), Vector3.down, -1f);
            _reflectionCamera.projectionMatrix = _reflectionCamera.CalculateObliqueMatrix(clipPlane);

            // Mirrored winding: invert culling for the manual render only.
            _insideRender = true;
            GL.invertCulling = true;
            try
            {
                _reflectionCamera.Render();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WaterPlanarReflections] reflection render failed: " + ex.Message);
            }
            finally
            {
                GL.invertCulling = false;
                _insideRender = false;
            }

            Shader.SetGlobalFloat(PlanarStrengthId, reflectionStrength);
        }

        private static Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
        {
            Matrix4x4 m = cam.worldToCameraMatrix;
            Vector3 cpos = m.MultiplyPoint3x4(pos);
            Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
            return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
        }
    }
}

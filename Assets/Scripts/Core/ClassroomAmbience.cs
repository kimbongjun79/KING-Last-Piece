using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace KingLastPiece
{
    /// <summary>
    /// Light of the classroom scene (EndlessMode). No lamps: a low afternoon sun comes in from the windows in front of
    /// the battlefield — warm light only, it casts no shadows. The pieces' shadows come from a separate, faint
    /// "shadow light" high above, which is the scene's main light (URP shadows from the main light only), so their
    /// length and direction are set apart from the sun. The rest is a dim ambient. Applied after the GameManager's
    /// camera setup (which defaults to the dark hall), so the values here win; the dark hall's brazier lights go off.
    /// </summary>
    public class ClassroomAmbience : MonoBehaviour
    {
        [Header("Sun through the windows (no shadows)")]
        public Color sunColor = new Color(1f, 0.9f, 0.76f);
        public float sunIntensity = 1.4f;
        [Tooltip("Low sun from the window wall (+Z).")]
        public Vector3 sunAngles = new Vector3(14f, 176f, 0f);

        [Header("Overhead shadow light (main light, casts the shadows)")]
        public Light shadowLight;
        public Color shadowLightColor = new Color(0.92f, 0.94f, 1f);
        public float shadowLightIntensity = 0.45f;
        [Tooltip("Steep and a little toward the camera's side: shadows about half a piece's height, falling back-right.")]
        public Vector3 shadowLightAngles = new Vector3(64f, 150f, 0f);
        [Range(0f, 1f)] public float shadowStrength = 0.75f;
        [Tooltip("Shadow depth / normal biases (flat squares side by side streak without them).")]
        public float shadowDepthBias = 1f, shadowNormalBias = 1.2f;
        [Tooltip("Shadow distance in this scene: only the battlefield needs shadows.")]
        public float shadowDistance = 90f;

        [Header("Room")]
        [Tooltip("Sky seen through the windows (3D Skybox ▸ 05-Green Fields, SDR).")]
        public Material skybox;
        public Color ambient = new Color(0.26f, 0.27f, 0.3f);
        public Color sky = new Color(0.66f, 0.77f, 0.9f);
        public Color fog = new Color(0.5f, 0.54f, 0.6f);
        public float fogStart = 250f, fogEnd = 1600f;
        [Tooltip("The room is ~360 m across and the trees further out.")]
        public float farClip = 2500f;
        [Tooltip("Board squares' smoothness here (the hall's polished squares glare in the low sun).")]
        public float boardSmoothness = 0.28f;

        float savedShadowDistance = -1f;
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        void Start() => Apply();

        public void Apply()
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l == shadowLight) continue;
                if (l.type == LightType.Directional)
                {
                    l.transform.rotation = Quaternion.Euler(sunAngles);
                    l.color = sunColor;
                    l.intensity = sunIntensity;
                    l.shadows = LightShadows.None;
                }
                else if (l.name == "Brazier Light") l.enabled = false;
            }
            if (shadowLight != null)
            {
                shadowLight.type = LightType.Directional;
                shadowLight.transform.rotation = Quaternion.Euler(shadowLightAngles);
                shadowLight.color = shadowLightColor;
                shadowLight.intensity = shadowLightIntensity;
                shadowLight.shadows = LightShadows.Soft;
                shadowLight.shadowStrength = shadowStrength;
                var data = shadowLight.GetComponent<UniversalAdditionalLightData>();
                if (data == null) data = shadowLight.gameObject.AddComponent<UniversalAdditionalLightData>();
                data.usePipelineSettings = false;
                shadowLight.shadowBias = shadowDepthBias;
                shadowLight.shadowNormalBias = shadowNormalBias;
                RenderSettings.sun = shadowLight;   // the main light: the one URP casts shadows from
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fog;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            if (skybox != null) RenderSettings.skybox = skybox;
            var cam = Camera.main;
            if (cam != null)
            {
                cam.backgroundColor = sky;
                cam.clearFlags = skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                cam.farClipPlane = farClip;
            }
            if (Application.isPlaying && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                if (savedShadowDistance < 0f) savedShadowDistance = urp.shadowDistance;
                urp.shadowDistance = shadowDistance;
            }
            if (Application.isPlaying) CalmTheBoard();
        }

        /// <summary>
        /// The board's squares (flat, side by side) stop casting shadows on one another (pieces still shade the board),
        /// and lose some polish (per renderer — the shared materials stay as they are).
        /// </summary>
        public void CalmTheBoard()
        {
            var board = FindFirstObjectByType<ArenaBoard>();
            if (board == null) return;
            var mpb = new MaterialPropertyBlock();
            foreach (var r in board.GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform.IsChildOf(transform)) continue;   // the classroom itself
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.GetPropertyBlock(mpb);
                mpb.SetFloat(SmoothnessId, boardSmoothness);
                r.SetPropertyBlock(mpb);
            }
        }

        void OnDestroy()
        {
            if (savedShadowDistance >= 0f && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.shadowDistance = savedShadowDistance;
        }
    }
}

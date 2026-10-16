using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace KingLastPiece
{
    /// <summary>
    /// Daylight of the classroom scene (EndlessMode): no lamps — a low afternoon sun comes in through the windows in
    /// front of the battlefield and crosses the room, the rest is a dim ambient. The walls and ceiling only shade the
    /// room if the shadows reach that far, so the scene raises the URP shadow distance while it runs (and puts it
    /// back after). Applied after the GameManager's camera setup (which defaults to the dark hall), so the values
    /// here win. Also switches off the dark hall's brazier lights if any are left.
    /// </summary>
    public class ClassroomAmbience : MonoBehaviour
    {
        public Color sunColor = new Color(1f, 0.9f, 0.76f);
        public float sunIntensity = 1.6f;
        [Tooltip("Low sun from the window wall (+Z), through the middle window onto the chess table: pitch 14° clears the sill and the lintel, yaw 176° keeps the beam inside the middle window (the piers either side would shade the board).")]
        public Vector3 sunAngles = new Vector3(14f, 176f, 0f);
        public Color ambient = new Color(0.26f, 0.27f, 0.3f);
        public Color sky = new Color(0.66f, 0.77f, 0.9f);
        public Color fog = new Color(0.5f, 0.54f, 0.6f);
        public float fogStart = 250f, fogEnd = 1600f;
        [Tooltip("The room is ~360 m across and the trees further out.")]
        public float farClip = 2500f;
        [Tooltip("Shadow distance while in the classroom: far enough for the walls and ceiling to shade the room.")]
        public float shadowDistance = 520f;

        float savedShadowDistance = -1f;

        void Start() => Apply();

        public void Apply()
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional)
                {
                    l.transform.rotation = Quaternion.Euler(sunAngles);
                    l.color = sunColor;
                    l.intensity = sunIntensity;
                    l.shadows = LightShadows.Soft;
                }
                else if (l.name == "Brazier Light") l.enabled = false;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fog;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            var cam = Camera.main;
            if (cam != null)
            {
                cam.backgroundColor = sky;
                cam.farClipPlane = farClip;
            }
            if (Application.isPlaying && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                if (savedShadowDistance < 0f) savedShadowDistance = urp.shadowDistance;
                urp.shadowDistance = shadowDistance;
            }
            if (Application.isPlaying) CalmTheBoard();
        }

        [Tooltip("Board squares' smoothness under the low sun (the hall's polished squares glare in it).")]
        public float boardSmoothness = 0.08f;
        [Tooltip("Sun shadow biases: the low sun over a long shadow distance streaks flat surfaces without them.")]
        public float sunDepthBias = 1.2f, sunNormalBias = 1.4f;

        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        /// <summary>
        /// The board under a 14° sun: its squares (flat, side by side) shadowing one another drew stripes across them,
        /// and their polish threw a hard glare. Squares and frame stop casting shadows (pieces still shade the board),
        /// the squares go matte (per renderer, the shared materials stay as they are), and the sun gets more shadow
        /// bias.
        /// </summary>
        public void CalmTheBoard()
        {
            var board = FindFirstObjectByType<ArenaBoard>();
            if (board != null)
            {
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
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                var data = l.GetComponent<UniversalAdditionalLightData>();
                if (data != null) data.usePipelineSettings = false;
                l.shadowBias = sunDepthBias;
                l.shadowNormalBias = sunNormalBias;
            }
        }

        void OnDestroy()
        {
            if (savedShadowDistance >= 0f && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.shadowDistance = savedShadowDistance;
        }
    }
}

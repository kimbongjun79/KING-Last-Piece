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
        }

        void OnDestroy()
        {
            if (savedShadowDistance >= 0f && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.shadowDistance = savedShadowDistance;
        }
    }
}

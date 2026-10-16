using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Daylight of the classroom scene (EndlessMode): the sun through the windows, a bright ambient, a pale sky and a
    /// long haze sized for the giant room. Applied after the GameManager's camera setup (which defaults to the dark
    /// hall), so the values here win. Also switches off the dark hall's brazier lights if any are left.
    /// </summary>
    public class ClassroomAmbience : MonoBehaviour
    {
        public Color sunColor = new Color(1f, 0.96f, 0.88f);
        public float sunIntensity = 1.25f;
        public Vector3 sunAngles = new Vector3(48f, -62f, 0f);
        public Color ambient = new Color(0.58f, 0.6f, 0.64f);
        public Color sky = new Color(0.72f, 0.83f, 0.95f);
        public Color fog = new Color(0.82f, 0.88f, 0.94f);
        public float fogStart = 250f, fogEnd = 1600f;
        [Tooltip("The room is ~360 m across and the trees further out.")]
        public float farClip = 2500f;

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
        }
    }
}

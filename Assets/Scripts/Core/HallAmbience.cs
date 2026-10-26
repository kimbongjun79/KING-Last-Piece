using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KingLastPiece
{
    /// <summary>
    /// Light of the dark hall (NormalMode): a battle fought in the dark. A faint, cold moonlight from high above is the
    /// main light (soft shadows); a warm spotlight hangs over the board so the fight reads as a lit stage in the dark,
    /// shading the pieces with its own soft shadows; a cold light low behind the far ranks outlines the black pieces;
    /// each gem tower glows violet from its crown; a soft warm light follows the King. Almost no ambient, a near-black
    /// fog swallowing the hall's edges, and a post-processing look (bloom on the glows, a heavy vignette, deeper
    /// contrast, a soft blur beyond the fight). Added by the look pass (so the scene view shows it) and applied again
    /// at start.
    /// </summary>
    [ExecuteAlways]
    public class HallAmbience : MonoBehaviour
    {
        [Header("Moonlight (main light, soft shadows)")]
        public Color moonColor = new Color(0.55f, 0.63f, 0.95f);
        public float moonIntensity = 0.6f;
        public Vector3 moonAngles = new Vector3(62f, -28f, 0f);
        [Range(0f, 1f)] public float moonShadowStrength = 0.55f;

        [Header("Stage light over the board")]
        public Color stageColor = new Color(1f, 0.9f, 0.74f);
        public float stageIntensity = 380f, stageHeight = 16f, stageAngle = 84f;
        [Range(0f, 1f)] public float stageShadowStrength = 0.8f;

        [Header("Rim light behind the black army (cold, no shadows)")]
        public Color rimColor = new Color(0.62f, 0.72f, 1f);
        public float rimIntensity = 290f;


        [Header("King's light")]
        public Color kingColor = new Color(1f, 0.86f, 0.62f);
        public float kingIntensity = 7f, kingRange = 7f, kingHeight = 3.2f;

        [Header("Hall")]
        public Color ambient = new Color(0.2f, 0.2f, 0.26f);
        public Color fog = new Color(0.012f, 0.012f, 0.025f);
        public float fogStart = 40f, fogEnd = 95f;
        [Tooltip("Post-processing look of the hall (made by the look pass; built in memory if missing).")]
        public VolumeProfile profile;

        Light king;

        /// <summary>The hall's light on the arena (added if the scene has none yet).</summary>
        public static HallAmbience Ensure(Transform arena)
        {
            var h = arena.GetComponent<HallAmbience>();
            if (h == null) h = arena.gameObject.AddComponent<HallAmbience>();
            else h.Apply();
            return h;
        }

        void OnEnable() => Apply();

        void LateUpdate()
        {
            // The King's light follows him.
            if (king == null || !Application.isPlaying) return;
            var g = GameManager.Instance;
            if (g != null && g.Player != null) king.transform.position = g.Player.FlatPos + Vector3.up * kingHeight;
        }

        public void Apply()
        {
            Light moon = null;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional && moon == null) moon = l;
                else if (l.name == "Brazier Light") l.enabled = false;   // the brazier bowls are gone: the gems glow instead
            }
            if (moon != null)
            {
                moon.transform.rotation = Quaternion.Euler(moonAngles);
                moon.color = moonColor;
                moon.intensity = moonIntensity;
                moon.shadows = LightShadows.Soft;
                moon.shadowStrength = moonShadowStrength;
                moon.shadowBias = 1f;
                moon.shadowNormalBias = 1.2f;
                RenderSettings.sun = moon;
            }

            var stage = Child("Stage Light", LightType.Spot);
            stage.transform.position = new Vector3(0f, stageHeight, 0f);
            stage.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -stageHeight, 3f));
            stage.spotAngle = stageAngle;
            stage.innerSpotAngle = stageAngle * 0.55f;
            stage.range = stageHeight * 2.5f;
            stage.color = stageColor;
            stage.intensity = stageIntensity;
            stage.shadows = LightShadows.Soft;
            stage.shadowStrength = stageShadowStrength;
            stage.shadowBias = 0.6f;
            stage.shadowNormalBias = 0.8f;

            // From behind the far ranks, low: outlines the black pieces against the dark.
            var rim = Child("Rim Light", LightType.Spot);
            rim.transform.position = new Vector3(0f, 7f, 17f);
            rim.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1.2f, 0f) - rim.transform.position);
            rim.spotAngle = 95f;
            rim.innerSpotAngle = 50f;
            rim.range = 40f;
            rim.color = rimColor;
            rim.intensity = rimIntensity;
            rim.shadows = LightShadows.None;

            king = Child("King Light", LightType.Point);
            king.range = kingRange;
            king.color = kingColor;
            king.intensity = kingIntensity;
            king.shadows = LightShadows.None;

            GemLights();

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fog;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = fog;
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data != null) data.renderPostProcessing = true;
            }
            Look();
        }

        /// <summary>The gems themselves glow now (emission on the tower material): the old point lights at the crowns go.</summary>
        void GemLights()
        {
            var pillars = transform.Find("Pillars");
            if (pillars == null) return;
            foreach (Transform pillar in pillars)
            {
                var old = pillar.Find("Gem Light");
                if (old == null) continue;
                if (Application.isPlaying) Destroy(old.gameObject);
                else DestroyImmediate(old.gameObject);
            }
        }

        Light Child(string name, LightType type)
        {
            var t = transform.Find(name);
            var l = t != null ? t.GetComponent<Light>() : null;
            if (l == null)
            {
                l = new GameObject(name).AddComponent<Light>();
                l.transform.SetParent(transform, false);
            }
            l.type = type;
            return l;
        }

        /// <summary>The hall's post-processing, on its own global volume (above the scene's default one).</summary>
        void Look()
        {
            if (profile == null && !Application.isPlaying) return;   // the look pass gives it its profile asset
            var t = transform.Find("Hall Look");
            var volume = t != null ? t.GetComponent<Volume>() : null;
            if (volume == null)
            {
                volume = new GameObject("Hall Look").AddComponent<Volume>();
                volume.transform.SetParent(transform, false);
            }
            volume.isGlobal = true;
            volume.priority = 5f;
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "Hall Look (runtime)";
                Fill(profile);
            }
            volume.sharedProfile = profile;
        }

        /// <summary>Bloom on the glows, a heavy vignette, deeper contrast, ACES, and a soft blur beyond the fight.</summary>
        public static void Fill(VolumeProfile p)
        {
            T Get<T>() where T : VolumeComponent
            {
                if (!p.TryGet(out T c)) c = p.Add<T>(true);
                c.active = true;
                return c;
            }
            var bloom = Get<Bloom>();
            bloom.threshold.Override(0.85f);
            bloom.intensity.Override(0.9f);
            bloom.scatter.Override(0.7f);
            var vignette = Get<Vignette>();
            vignette.intensity.Override(0.42f);
            vignette.smoothness.Override(0.5f);
            vignette.color.Override(Color.black);
            var color = Get<ColorAdjustments>();
            color.postExposure.Override(-0.1f);
            color.contrast.Override(18f);
            color.saturation.Override(-8f);
            color.colorFilter.Override(new Color(0.96f, 0.95f, 1f));
            var tone = Get<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);
            var dof = Get<DepthOfField>();
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(26f);
            dof.gaussianEnd.Override(60f);
            dof.gaussianMaxRadius.Override(1.2f);
        }
    }
}

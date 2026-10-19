using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KingLastPiece
{
    /// <summary>
    /// Low health, as URP post-processing: below 35% health a global volume (above the scene's own) pulls in a
    /// blood-red vignette, drains colour toward red and adds a touch of chromatic aberration, all beating like a
    /// heart — the lower the health, the deeper, wider and faster. Created by the HUD; it makes sure the camera
    /// renders post-processing.
    /// </summary>
    public class LowHealthFx : MonoBehaviour
    {
        /// <summary>0 = healthy (volume off) … 1 = at death's door.</summary>
        public float Level { get; private set; }

        Volume volume;
        VolumeProfile profile;
        Vignette vignette;
        ColorAdjustments color;
        ChromaticAberration aberration;
        Camera cameraSeen;

        static readonly Color Blood = new Color(0.45f, 0f, 0.02f);

        void Awake()
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Low Health (runtime)";
            vignette = profile.Add<Vignette>(true);
            vignette.color.Override(Blood);
            vignette.rounded.Override(false);
            color = profile.Add<ColorAdjustments>(true);
            aberration = profile.Add<ChromaticAberration>(true);

            var go = new GameObject("Low Health Volume");
            go.transform.SetParent(transform, false);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;   // over the scene's own volumes (the classroom's focus blur keeps working: other overrides)
            volume.sharedProfile = profile;
            volume.weight = 0f;
        }

        void OnDestroy()
        {
            if (profile != null) Destroy(profile);
        }

        void Update()
        {
            var g = GameManager.Instance;
            var p = g != null ? g.Player : null;
            bool inFight = g != null && p != null && g.State != GameManager.GameState.Title && g.State != GameManager.GameState.Victory;
            float frac = p != null && p.MaxHealth > 0f ? p.Health / p.MaxHealth : 1f;
            float target = inFight && !p.IsDead ? Mathf.Clamp01((0.35f - frac) / 0.3f) : 0f;
            Level = Mathf.MoveTowards(Level, target, Time.unscaledDeltaTime * 1.5f);

            volume.weight = Level > 0.001f ? 1f : 0f;
            if (Level <= 0.001f) return;
            EnsurePostProcessing();

            // A heartbeat: sharp pulses, quicker as health runs out.
            float beat = Mathf.Pow(Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.Lerp(3.2f, 5.5f, Level))), 6f);
            vignette.intensity.Override(Mathf.Lerp(0.2f, 0.5f, Level) + 0.08f * beat * Level);
            vignette.smoothness.Override(Mathf.Lerp(0.35f, 0.75f, Level));
            color.saturation.Override(-35f * Level);
            color.colorFilter.Override(Color.Lerp(Color.white, new Color(1f, 0.78f, 0.76f), Level));
            aberration.intensity.Override((0.12f + 0.25f * beat) * Level);
        }

        /// <summary>The volume only shows on a camera that renders post-processing.</summary>
        void EnsurePostProcessing()
        {
            var cam = Camera.main;
            if (cam == null || cam == cameraSeen) return;
            cameraSeen = cam;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
        }
    }
}

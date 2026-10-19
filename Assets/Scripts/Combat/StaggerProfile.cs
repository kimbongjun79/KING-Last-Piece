using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Per-enemy stagger tuning (see stagger_system.md). Can be authored as an asset
    /// (Create ▸ KING ▸ Stagger Profile) or created at runtime with <see cref="Create"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "KING/Stagger Profile", fileName = "StaggerProfile")]
    public class StaggerProfile : ScriptableObject
    {
        [Tooltip("Gauge needed to become Vulnerable.")]
        public float maxGauge = 40f;
        [Tooltip("Seconds without being hit before the gauge starts to drain.")]
        public float decayDelay = 1.2f;
        [Tooltip("Gauge drained per second once the delay has passed.")]
        public float decayPerSecond = 16f;
        [Tooltip("How long the Vulnerable state lasts if the enemy isn't launched.")]
        public float vulnerableDuration = 2.5f;
        [Tooltip("Upward speed of the first launch. Each further air hit lifts less.")]
        public float launchForce = 7f;
        [Tooltip("Length of the flinch motion on a normal hit.")]
        public float flinchDuration = 0.35f;
        [Tooltip("Seconds after the guard recovers during which hits fill no gauge (no back-to-back breaks).")]
        public float recoveryImmunity = 3f;

        public static StaggerProfile Create(float maxGauge, float vulnerableDuration, float launchForce, float flinchDuration,
            float decayDelay = 1.2f, float decayFraction = 0.4f)
        {
            var p = CreateInstance<StaggerProfile>();
            p.name = "StaggerProfile";
            p.maxGauge = maxGauge;
            p.vulnerableDuration = vulnerableDuration;
            p.launchForce = launchForce;
            p.flinchDuration = flinchDuration;
            p.decayDelay = decayDelay;
            p.decayPerSecond = maxGauge * decayFraction;
            return p;
        }
    }
}

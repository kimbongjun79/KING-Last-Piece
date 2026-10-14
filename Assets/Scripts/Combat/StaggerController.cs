using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Stagger gauge for regular (non-boss) enemies. Bosses don't get this component (they keep their own poise).
    /// Normal: hits flinch and fill the gauge. Attacking (super armor): no reaction, gauge still fills.
    /// Full gauge → Vulnerable: each hit pops the enemy up a little and cancels its action. Airborne: hits juggle it
    /// with shrinking, capped boosts. The gauge is frozen while Vulnerable/airborne and resets on get-up or timeout.
    /// </summary>
    [DisallowMultipleComponent]
    public class StaggerController : MonoBehaviour
    {
        public enum Reaction { None, Flinch, Launch }

        public StaggerProfile Profile;
        public float Gauge { get; private set; }
        public bool Vulnerable => vulnerableTimer > 0f;
        /// <summary>True for the hit that filled the gauge (used to play the "vulnerable" effect once).</summary>
        public bool JustBecameVulnerable { get; private set; }

        float decayTimer, vulnerableTimer;

        public Reaction OnHit(float staggerDamage, bool superArmor, bool airborne)
        {
            JustBecameVulnerable = false;
            if (Vulnerable || airborne) return Reaction.Launch;  // gauge frozen; every hit launches

            Gauge += staggerDamage;
            decayTimer = Profile.decayDelay;
            if (Gauge >= Profile.maxGauge)
            {
                Gauge = Profile.maxGauge;
                vulnerableTimer = Profile.vulnerableDuration;
                JustBecameVulnerable = true;
            }
            return superArmor ? Reaction.None : Reaction.Flinch;
        }

        /// <summary>Back to normal (after getting up, or when Vulnerable times out).</summary>
        public void ResetGauge()
        {
            Gauge = 0f;
            vulnerableTimer = 0f;
            decayTimer = 0f;
        }

        /// <summary>Advance timers. <paramref name="frozen"/> while airborne / getting up.</summary>
        public void Tick(float dt, bool frozen)
        {
            if (frozen) return;
            if (vulnerableTimer > 0f)
            {
                vulnerableTimer -= dt;
                if (vulnerableTimer <= 0f) ResetGauge();
                return;
            }
            if (decayTimer > 0f) decayTimer -= dt;
            else Gauge = Mathf.Max(0f, Gauge - Profile.decayPerSecond * dt);
        }
    }
}

using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Stagger gauge for regular (non-boss) enemies. Bosses don't get this component (they keep their own poise).
    /// Normal: hits flinch and fill the gauge. Attacking (super armor): no reaction, gauge still fills.
    /// Full gauge → Vulnerable: each hit pops the enemy up a little and cancels its action. Airborne: hits juggle it
    /// with shrinking, capped boosts. The gauge is frozen while Vulnerable/airborne and resets on get-up or timeout.
    /// When the guard recovers, hits fill no gauge for <see cref="StaggerProfile.recoveryImmunity"/> seconds, so a
    /// piece can't be broken again right away.
    /// </summary>
    [DisallowMultipleComponent]
    public class StaggerController : MonoBehaviour
    {
        public enum Reaction { None, Flinch, Launch }

        public StaggerProfile Profile;
        public float Gauge { get; private set; }
        public bool Vulnerable => vulnerableTimer > 0f;
        /// <summary>Just recovered from a broken guard: hits fill no gauge for now.</summary>
        public bool Immune => immuneTimer > 0f;
        /// <summary>True for the hit that filled the gauge (used to play the "vulnerable" effect once).</summary>
        public bool JustBecameVulnerable { get; private set; }

        float decayTimer, vulnerableTimer, immuneTimer;

        public Reaction OnHit(float staggerDamage, bool superArmor, bool airborne)
        {
            JustBecameVulnerable = false;
            if (Vulnerable || airborne) return Reaction.Launch;  // gauge frozen; every hit launches
            if (!Immune && Fill(staggerDamage)) JustBecameVulnerable = true;
            return superArmor ? Reaction.None : Reaction.Flinch;
        }

        /// <summary>Fills the gauge without a hit reaction (e.g. a thrown piece crashing down). True if it broke.</summary>
        public bool Add(float staggerDamage)
        {
            JustBecameVulnerable = false;
            if (Vulnerable || Immune) return false;
            return JustBecameVulnerable = Fill(staggerDamage);
        }

        /// <summary>Adds to the gauge; true when that fills it (the guard breaks).</summary>
        bool Fill(float staggerDamage)
        {
            Gauge += staggerDamage;
            decayTimer = Profile.decayDelay;
            if (Gauge < Profile.maxGauge) return false;
            Gauge = Profile.maxGauge;
            vulnerableTimer = Profile.vulnerableDuration;
            return true;
        }

        /// <summary>Back to normal (after getting up, or when Vulnerable times out).</summary>
        public void ResetGauge()
        {
            Gauge = 0f;
            vulnerableTimer = 0f;
            decayTimer = 0f;
        }

        /// <summary>Advance timers. <paramref name="frozen"/> while airborne.</summary>
        public void Tick(float dt, bool frozen)
        {
            if (immuneTimer > 0f) immuneTimer -= dt;
            if (frozen) return;
            if (vulnerableTimer > 0f)
            {
                vulnerableTimer -= dt;
                if (vulnerableTimer <= 0f)
                {
                    // Out of the daze: a few seconds before the guard can be broken again.
                    ResetGauge();
                    immuneTimer = Profile.recoveryImmunity;
                }
                return;
            }
            if (decayTimer > 0f) decayTimer -= dt;
            else Gauge = Mathf.Max(0f, Gauge - Profile.decayPerSecond * dt);
        }
    }
}

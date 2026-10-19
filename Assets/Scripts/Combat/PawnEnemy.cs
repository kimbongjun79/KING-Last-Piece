using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Pawn: foot soldier, outside the board-slot system. It walks straight at the King, stops about one square away,
    /// and while it waits for its turn circles him at that distance; then it thrusts along the nearest of the 8 chess
    /// directions toward him — fixed the moment the wind-up starts. Pawns have their own attack turns (two at a time),
    /// so they keep pressing while the bigger pieces take theirs. Slow wind-up: dangerous in numbers, punishable alone.
    /// Dark I: a longer thrust from farther out. Dark II: a second thrust right after, re-aimed at the King.
    /// </summary>
    public class PawnEnemy : EnemyBase
    {
        const float Windup = 1.2f, Reach = 1.7f, Hold = 1.9f;
        const float DarkReach = 3.4f, DarkHold = 2.7f, FollowWindup = 0.6f;
        float circleSign;
        int thrustsLeft;

        protected override void Configure()
        {
            DisplayName = "흑색 폰";
            SetHealth(100f);   // ~1.5 light-combo cycles (one cycle = 66)
            SetRadius(0.45f);
            moveSpeed = 2.6f;
            circleSign = Random.value < 0.5f ? -1f : 1f;
        }

        protected override StaggerProfile CreateStaggerProfile() =>
            StaggerProfile.Create(maxGauge: 32f, vulnerableDuration: 3f, launchForce: 7.5f, flinchDuration: 0.4f);   // one heavy (42) or one light combo (42)

        float HoldDistance => EliteTier >= 1 ? DarkHold : Hold;
        float ReachLength => EliteTier >= 1 ? DarkReach : Reach;

        protected override void Think(float dt)
        {
            Vector3 to = ToPlayer;
            float d = to.magnitude;
            float hold = HoldDistance;
            FaceTowards(to, 300f);

            if (d > hold + 0.3f)
            {
                // Close in, aiming for the point one square short of the King.
                MoveTowards(Player.FlatPos - to.normalized * hold, moveSpeed, dt);
                return;
            }

            if (CanAttackNow)
            {
                thrustsLeft = EliteTier >= 2 ? 1 : 0;
                Thrust(Windup, thrustsLeft > 0 ? 0.3f : 0.8f);
                cooldown = Random.Range(2.6f, 3.8f);
                return;
            }

            // Waiting for a turn: circle the King at about a square's distance (drift back out if crowded in).
            Vector3 r = d > 0.01f ? -to / d : Vector3.back;
            Vector3 tangent = Vector3.Cross(Vector3.up, r) * circleSign;
            Vector3 drift = r * Mathf.Clamp(hold - d, -0.5f, 0.5f);
            MoveTowards(FlatPos + (tangent * 0.5f + drift) * 2f, moveSpeed * 0.45f, dt);
        }

        /// <summary>Thrust along the nearest of the 8 chess directions (straight or diagonal), fixed from here on.</summary>
        void Thrust(float windup, float recover)
        {
            Vector3 to = ToPlayer;
            BeginAttack(new AttackPlan
            {
                kind = AtkKind.Lunge, dir = BoardSlots.Snap8(to), windup = windup, active = 0.2f, recover = recover,
                length = Mathf.Max(ReachLength, Mathf.Min(to.magnitude + 0.4f, ReachLength + 1.2f)), damage = 12f, poise = 15f, radius = 0.7f,
                trackUntil = 0f
            });
        }

        protected override bool ContinueCombo()
        {
            if (thrustsLeft <= 0) return false;
            thrustsLeft--;
            Thrust(FollowWindup, 0.9f);
            cooldown = Mathf.Max(cooldown, Random.Range(2.2f, 3.2f));   // a breather after the pair
            return true;
        }
    }
}

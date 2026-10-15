using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Pawn: foot soldier, outside the board-slot system. It walks straight at the King, stops about one square away,
    /// and while it waits for its turn circles him at that distance; then it thrusts along the nearest of the 8 chess
    /// directions toward him — fixed the moment the wind-up starts. Pawns have their own attack turns (two at a time),
    /// so they keep pressing while the bigger pieces take theirs. Slow wind-up: dangerous in numbers, punishable alone.
    /// </summary>
    public class PawnEnemy : EnemyBase
    {
        const float Windup = 1.2f, Reach = 1.7f, Hold = 1.9f;
        float circleSign;

        protected override void Configure()
        {
            DisplayName = "Black Pawn";
            SetHealth(100f);   // ~1.5 light-combo cycles (one cycle = 66)
            SetRadius(0.45f);
            moveSpeed = 2.6f;
            circleSign = Random.value < 0.5f ? -1f : 1f;
        }

        protected override StaggerProfile CreateStaggerProfile() =>
            StaggerProfile.Create(maxGauge: 26f, vulnerableDuration: 3f, launchForce: 7.5f, flinchDuration: 0.4f);

        protected override void Think(float dt)
        {
            Vector3 to = ToPlayer;
            float d = to.magnitude;
            FaceTowards(to, 300f);

            if (d > Hold + 0.3f)
            {
                // Close in, aiming for the point one square short of the King.
                MoveTowards(Player.FlatPos - to.normalized * Hold, moveSpeed, dt);
                return;
            }

            if (CanAttackNow)
            {
                // Thrust along the nearest of the 8 chess directions (straight or diagonal), fixed from here on.
                BeginAttack(new AttackPlan
                {
                    kind = AtkKind.Lunge, dir = BoardSlots.Snap8(to), windup = Windup, active = 0.2f, recover = 0.8f,
                    length = Mathf.Max(Reach, d + 0.4f), damage = 12f, poise = 15f, radius = 0.7f, trackUntil = 0f
                });
                cooldown = Random.Range(2.6f, 3.8f);
                return;
            }

            // Waiting for a turn: circle the King at about a square's distance (drift back out if crowded in).
            Vector3 r = d > 0.01f ? -to / d : Vector3.back;
            Vector3 tangent = Vector3.Cross(Vector3.up, r) * circleSign;
            Vector3 drift = r * Mathf.Clamp(Hold - d, -0.5f, 0.5f);
            MoveTowards(FlatPos + (tangent * 0.5f + drift) * 2f, moveSpeed * 0.45f, dt);
        }
    }
}

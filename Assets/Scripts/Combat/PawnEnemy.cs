using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Pawn: walks to a square diagonally in front of the King (black pawns capture toward rank 1, so the squares
    /// above him come first) and thrusts along that diagonal — straight through the King's square, no tracking.
    /// </summary>
    public class PawnEnemy : EnemyBase
    {
        protected override void Configure()
        {
            DisplayName = "Black Pawn";
            SetHealth(100f);   // ~1.5 light-combo cycles (one cycle = 66)
            SetRadius(0.45f);
            moveSpeed = 2.6f;
        }

        protected override StaggerProfile CreateStaggerProfile() =>
            StaggerProfile.Create(maxGauge: 26f, vulnerableDuration: 3f, launchForce: 7.5f, flinchDuration: 0.4f);

        protected override void Think(float dt)
        {
            UpdateSlot(BoardSlots.PawnSlots, o => o.y < 0 ? 2.5f : 0f);
            FaceTowards(ToPlayer, 300f);

            if (ReadyAtAttackSlot)
            {
                if (CanAttackNow)
                {
                    // Diagonal capture: from this square through the King's square.
                    Vector3 dir = BoardSlots.LineDirection(MySquare, KingSquare);
                    float length = (BoardSlots.Center(KingSquare) - FlatPos).magnitude + 0.6f;
                    BeginAttack(new AttackPlan
                    {
                        kind = AtkKind.Lunge, dir = dir, windup = 0.95f, active = 0.24f, recover = 0.8f,
                        length = length, damage = 12f, poise = 15f, radius = 0.7f, trackUntil = 0f
                    });
                    cooldown = Random.Range(2.6f, 3.8f);
                }
                return;
            }
            MoveToSlot(Gait.Direct, moveSpeed, dt);
        }
    }
}

using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Pawn: takes one of the squares around the King (black pawns face rank 1, so the squares above him come first)
    /// and strikes by where it stands — from a diagonal square a capture thrust straight along that diagonal through
    /// the King's square (no tracking); from a straight neighbour a short push that follows the King.
    /// Slow, long wind-up: dangerous in numbers, punishable alone.
    /// </summary>
    public class PawnEnemy : EnemyBase
    {
        const float Windup = 1.6f;

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
                    Vector2Int off = MySquare - KingSquare;
                    bool diagonal = off.x != 0 && off.y != 0;
                    if (diagonal)
                    {
                        // Diagonal capture: from this square through the King's square.
                        float length = (BoardSlots.Center(KingSquare) - FlatPos).magnitude + 0.6f;
                        BeginAttack(new AttackPlan
                        {
                            kind = AtkKind.Lunge, dir = BoardSlots.LineDirection(MySquare, KingSquare), windup = Windup,
                            active = 0.24f, recover = 0.8f, length = length, damage = 12f, poise = 15f, radius = 0.7f, trackUntil = 0f
                        });
                    }
                    else
                    {
                        // Straight push into the next square, turning to follow the King for most of the wind-up.
                        BeginAttack(new AttackPlan
                        {
                            kind = AtkKind.Lunge, dir = ToPlayer, windup = Windup, active = 0.18f, recover = 0.8f,
                            length = 1.7f, damage = 12f, poise = 15f, radius = 0.7f, trackUntil = 0.7f
                        });
                    }
                    cooldown = Random.Range(2.6f, 3.8f);
                }
                return;
            }
            MoveToSlot(Gait.Direct, moveSpeed, dt);
        }
    }
}

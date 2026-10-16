using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Bishop: glides along diagonals to the nearest square on one of the King's diagonals (from 2 squares out up to the
    /// far edge), then charges down that diagonal through the King's square to the board edge.
    /// </summary>
    public class BishopEnemy : EnemyBase
    {
        bool wasReady;

        protected override void Configure()
        {
            DisplayName = "Black Bishop";
            SetHealth(265f);   // ~4 light-combo cycles
            SetRadius(0.45f);
            moveSpeed = 3.4f;
        }

        protected override StaggerProfile CreateStaggerProfile() =>
            StaggerProfile.Create(maxGauge: 60f, vulnerableDuration: 2.5f, launchForce: 6.5f, flinchDuration: 0.35f);

        protected override void Think(float dt)
        {
            UpdateSlot(BoardSlots.BishopSlots);
            FaceTowards(ToPlayer, 360f);

            bool ready = ReadyAtAttackSlot;
            if (ready && !wasReady) Sfx.Play("BISHOP_Align", transform.position);
            wasReady = ready;

            if (ready)
            {
                if (CanAttackNow)
                {
                    BeginAttack(new AttackPlan
                    {
                        kind = AtkKind.Dash, dir = BoardSlots.LineDirection(MySquare, KingSquare), windup = 0.9f, recover = 0.85f,
                        length = FullLine, speed = 20f, damage = 18f, poise = 30f, radius = 0.75f
                    });
                    cooldown = Random.Range(3.0f, 4.0f);
                }
                return;
            }
            MoveToSlot(Gait.Diagonal, moveSpeed, dt);
        }
    }
}

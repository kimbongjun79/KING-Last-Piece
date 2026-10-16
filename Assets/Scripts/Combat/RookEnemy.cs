using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Rook: heavy, armored. Slides along ranks and files to the nearest square on the King's rank or file (2 squares
    /// out or more), then rams down that line. When the King steps next to it, it slams the squares around itself.
    /// </summary>
    public class RookEnemy : EnemyBase
    {
        bool wasReady;

        protected override void Configure()
        {
            DisplayName = "흑색 룩";
            SetHealth(380f);   // tougher than knight/bishop (~5.8 cycles)
            SetRadius(0.55f);
            moveSpeed = 2.8f;
        }

        protected override StaggerProfile CreateStaggerProfile() =>
            StaggerProfile.Create(maxGauge: 110f, vulnerableDuration: 2f, launchForce: 4.8f, flinchDuration: 0.3f);

        protected override void Think(float dt)
        {
            Vector3 to = ToPlayer;
            FaceTowards(to, 240f);

            // Crowded: the King is on a neighbouring square — hold ground and slam around itself.
            if (BoardSlots.Chebyshev(MySquare, KingSquare) <= 1 && to.magnitude < 2.6f)
            {
                if (!CanAttackNow) return;
                BeginAttack(new AttackPlan
                {
                    kind = AtkKind.Slam, windup = 0.75f, active = 0.14f, recover = 1.0f,
                    damage = 22f, poise = 40f, radius = 2.6f
                });
                cooldown = Random.Range(1.6f, 2.4f);
                return;
            }

            UpdateSlot(BoardSlots.RookSlots);
            bool ready = ReadyAtAttackSlot;
            if (ready && !wasReady) Sfx.Play("ROOK_Align", transform.position);
            wasReady = ready;

            if (ready)
            {
                if (CanAttackNow)
                {
                    BeginAttack(new AttackPlan
                    {
                        kind = AtkKind.Dash, dir = BoardSlots.LineDirection(MySquare, KingSquare), windup = 0.75f, recover = 1.1f,
                        length = FullLine, speed = 22f, damage = 26f, poise = 50f, radius = 0.9f
                    });
                    cooldown = Random.Range(2f, 3f);
                }
                return;
            }
            MoveToSlot(Gait.Orthogonal, moveSpeed, dt);
        }
    }
}

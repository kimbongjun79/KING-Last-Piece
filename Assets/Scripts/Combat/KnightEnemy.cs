using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Knight: hops square to square in real L-moves (2 + 1 squares) along the shortest knight route to an L-square
    /// of the King, then leaps onto the King's square.
    /// </summary>
    public class KnightEnemy : EnemyBase
    {
        const float HopTime = 0.42f;
        float hopTimer;
        bool hopping;
        Vector3 hopFrom, hopTo;
        float hopT;

        protected override void Configure()
        {
            DisplayName = "Black Knight";
            SetHealth(265f);   // ~4 light-combo cycles
            SetRadius(0.5f);
            moveSpeed = 3f;
            hopTimer = Random.Range(0.3f, 0.8f);
        }

        protected override StaggerProfile CreateStaggerProfile() =>
            StaggerProfile.Create(maxGauge: 60f, vulnerableDuration: 2.5f, launchForce: 6.5f, flinchDuration: 0.35f);

        protected override void Think(float dt)
        {
            Vector3 to = ToPlayer;

            if (hopping)
            {
                hopT += dt / HopTime;
                transform.position = Vector3.Lerp(hopFrom, hopTo, hopT);
                moving = true;
                FaceTowards(to, 500f);
                if (hopT >= 1f) hopping = false;
                return;
            }

            UpdateSlot(BoardSlots.KnightSlots);
            FaceTowards(to, 400f);

            if (ReadyAtAttackSlot)
            {
                if (CanAttackNow)
                {
                    // The knight's capture: jump from the L-square onto the King's square.
                    BeginAttack(new AttackPlan
                    {
                        kind = AtkKind.Leap, target = BoardSlots.Center(KingSquare), windup = 0.88f, active = 0.55f, recover = 0.9f,
                        damage = 20f, poise = 35f, radius = 1.5f
                    });
                    cooldown = Random.Range(3.4f, 4.6f);
                }
                return;
            }

            if (!HasSlot) return;
            // Off the square centre (knocked back, pushed): settle onto the square first.
            if ((BoardSlots.Center(MySquare) - FlatPos).magnitude > 0.3f && MySquare != SlotSquare)
            {
                MoveTowards(BoardSlots.Center(MySquare), moveSpeed, dt);
                return;
            }
            hopTimer -= dt;
            if (hopTimer > 0f) return;
            if (BoardSlots.KnightStep(MySquare, SlotSquare, this, out var next))
            {
                hopTimer = Random.Range(0.25f, 0.5f);
                hopFrom = FlatPos;
                hopTo = BoardSlots.Center(next);
                hopT = 0f;
                hopping = true;
                Sfx.Play("KNIGHT_Hop", transform.position);
            }
            else MoveToSlot(Gait.Direct, moveSpeed, dt);   // no knight route (boxed in): just slide over
        }
    }
}

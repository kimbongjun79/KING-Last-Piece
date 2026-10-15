using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Knight: bounds over to an L-square of the King in quick free leaps (straight at it, not square by square),
    /// then makes its real move — the leap onto the King's square.
    /// </summary>
    public class KnightEnemy : EnemyBase
    {
        const float HopTime = 0.34f, HopReach = 4f, HopHeight = 0.9f;
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
            hopTimer = Random.Range(0.1f, 0.4f);
        }

        protected override StaggerProfile CreateStaggerProfile() =>
            StaggerProfile.Create(maxGauge: 60f, vulnerableDuration: 2.5f, launchForce: 6.5f, flinchDuration: 0.35f);

        protected override void Think(float dt)
        {
            Vector3 to = ToPlayer;

            if (hopping)
            {
                hopT = Mathf.Min(1f, hopT + dt / HopTime);
                transform.position = Vector3.Lerp(hopFrom, hopTo, hopT);
                ModelLift = Mathf.Sin(hopT * Mathf.PI) * HopHeight;
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
                        kind = AtkKind.Leap, target = BoardSlots.Center(KingSquare), windup = 1.2f, active = 0.55f, recover = 0.9f,
                        damage = 20f, poise = 35f, radius = 1.5f
                    });
                    cooldown = Random.Range(3.4f, 4.6f);
                }
                return;
            }

            if (!HasSlot) return;
            Vector3 target = BoardSlots.Center(SlotSquare);
            Vector3 d = target - FlatPos;
            if (d.magnitude < 0.6f)
            {
                MoveToSlot(Gait.Direct, moveSpeed, dt);   // settle onto the square
                return;
            }
            hopTimer -= dt;
            if (hopTimer > 0f) return;
            hopTimer = Random.Range(0.08f, 0.2f);
            hopFrom = FlatPos;
            hopTo = FlatPos + d.normalized * Mathf.Min(d.magnitude, HopReach);
            hopT = 0f;
            hopping = true;
            Sfx.Play("KNIGHT_Hop", transform.position);
        }
    }
}

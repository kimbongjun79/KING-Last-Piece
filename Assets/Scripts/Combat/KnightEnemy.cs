using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Knight: bounds over to an L-square of the King in quick free leaps (straight at it, not square by square),
    /// then makes its real move — the leap onto the King's square, striking it and the four squares beside it.
    /// Dark I: the landing strikes the whole block of nine squares (the diagonals too). Dark II: right after landing
    /// it leaps again onto wherever the King has gone.
    /// </summary>
    public class KnightEnemy : EnemyBase
    {
        const float HopTime = 0.34f, HopReach = 4f, HopHeight = 0.9f;
        float hopTimer;
        bool hopping;
        Vector3 hopFrom, hopTo;
        float hopT;
        int leapsLeft;

        protected override void Configure()
        {
            DisplayName = "흑색 나이트";
            SetHealth(265f);   // ~4 light-combo cycles
            SetRadius(0.5f);
            moveSpeed = 3f;
            hopTimer = Random.Range(0.1f, 0.4f);
        }

        protected override StaggerProfile CreateStaggerProfile() =>
            StaggerProfile.Create(maxGauge: 40f, vulnerableDuration: 2.5f, launchForce: 6.5f, flinchDuration: 0.35f);   // one heavy (42) breaks it

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
                    leapsLeft = EliteTier >= 2 ? 1 : 0;
                    Leap(1.05f, leapsLeft > 0 ? 0.3f : 0.9f);
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

        /// <summary>Leap onto the King's square: a cross of squares, or (Dark) the block of nine.</summary>
        void Leap(float windup, float recover)
        {
            var land = KingSquare;
            bool block = EliteTier >= 1;
            BeginAttack(new AttackPlan
            {
                kind = AtkKind.Leap, target = BoardSlots.Center(land), windup = windup, active = 0.55f, recover = recover,
                damage = 20f, poise = 35f, radius = block ? 2.4f : 1.5f, squares = block ? BlockOf(land, 1) : CrossOf(land),
            });
        }

        protected override bool ContinueCombo()
        {
            if (leapsLeft <= 0) return false;
            leapsLeft--;
            Leap(0.62f, 1.0f);
            cooldown = Mathf.Max(cooldown, Random.Range(3f, 4f));   // a breather after the double leap
            return true;
        }
    }
}

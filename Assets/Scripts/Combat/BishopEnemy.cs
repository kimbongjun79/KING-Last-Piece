using UnityEngine;

namespace KingLastPiece
{
    /// <summary>Bishop: slides onto one of the King's diagonals, then charges along it to the board edge.</summary>
    public class BishopEnemy : EnemyBase
    {
        float alignTimer;
        bool wasAligned;

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
            Vector3 to = ToPlayer;
            FaceTowards(to, 360f);
            alignTimer += dt;

            Vector3 q = LineAligner.Nearest(FlatPos, Player.FlatPos, true);
            bool aligned = (q - FlatPos).magnitude < 0.45f;
            if (aligned && !wasAligned) Sfx.Play("BISHOP_Align", transform.position);
            wasAligned = aligned;

            if (CanAttackNow && (aligned || alignTimer > 3f))
            {
                alignTimer = 0f;
                Vector3 dir = aligned ? LineAligner.Snap(to, true) : to;
                BeginAttack(new AttackPlan
                {
                    kind = AtkKind.Dash, dir = dir, windup = 0.82f, recover = 0.85f,
                    length = 13f, speed = 15f, damage = 18f, poise = 30f, radius = 0.75f
                });
                cooldown = Random.Range(3.0f, 4.0f);
            }
            else MoveTowards(q, moveSpeed, dt);
        }
    }
}

using UnityEngine;

namespace KingLastPiece
{
    /// <summary>Rook: heavy, armored. Aligns on the King's rank/file and rams; slams when crowded.</summary>
    public class RookEnemy : EnemyBase
    {
        float alignTimer;
        bool wasAligned;

        protected override void Configure()
        {
            DisplayName = "Black Rook";
            SetHealth(380f);   // tougher than knight/bishop (~5.8 cycles)
            SetRadius(0.55f);
            moveSpeed = 2.8f;
        }

        protected override StaggerProfile CreateStaggerProfile() =>
            StaggerProfile.Create(maxGauge: 110f, vulnerableDuration: 2f, launchForce: 4.8f, flinchDuration: 0.3f);

        protected override void Think(float dt)
        {
            Vector3 to = ToPlayer;
            float d = to.magnitude;
            FaceTowards(to, 240f);
            alignTimer += dt;

            if (CanAttackNow && d < 2.4f)
            {
                BeginAttack(new AttackPlan
                {
                    kind = AtkKind.Slam, windup = 0.98f, active = 0.14f, recover = 1.0f,
                    damage = 22f, poise = 40f, radius = 2.6f
                });
                cooldown = Random.Range(1.6f, 2.4f);
                return;
            }

            Vector3 q = LineAligner.Nearest(FlatPos, Player.FlatPos, false);
            bool aligned = (q - FlatPos).magnitude < 0.5f;
            if (aligned && !wasAligned) Sfx.Play("ROOK_Align", transform.position);
            wasAligned = aligned;
            if (CanAttackNow && (aligned || alignTimer > 3.5f))
            {
                alignTimer = 0f;
                Vector3 dir = aligned ? LineAligner.Snap(to, false) : to;
                BeginAttack(new AttackPlan
                {
                    kind = AtkKind.Dash, dir = dir, windup = 1.08f, recover = 1.1f,
                    length = 14f, speed = 17f, damage = 26f, poise = 50f, radius = 0.9f
                });
                cooldown = Random.Range(2f, 3f);
            }
            else MoveTowards(q, moveSpeed, dt);
        }
    }
}

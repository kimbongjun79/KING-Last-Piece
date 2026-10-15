using UnityEngine;

namespace KingLastPiece
{
    /// <summary>Pawn: slow, approaches and thrusts forward. Fodder that punishes greed in groups.</summary>
    public class PawnEnemy : EnemyBase
    {
        float strafeSign;

        protected override void Configure()
        {
            DisplayName = "Black Pawn";
            SetHealth(100f);   // ~1.5 light-combo cycles (one cycle = 66)
            SetRadius(0.45f);
            moveSpeed = 2.6f;
            strafeSign = Random.value < 0.5f ? -1f : 1f;
        }

        protected override StaggerProfile CreateStaggerProfile() =>
            StaggerProfile.Create(maxGauge: 26f, vulnerableDuration: 3f, launchForce: 7.5f, flinchDuration: 0.4f);

        protected override void Think(float dt)
        {
            Vector3 to = ToPlayer;
            float d = to.magnitude;
            FaceTowards(to, 300f);

            if (d > 2.1f) MoveTowards(Player.FlatPos - to.normalized * 1.7f, moveSpeed, dt);
            else if (CanAttackNow)
            {
                BeginAttack(new AttackPlan
                {
                    kind = AtkKind.Lunge, dir = to, windup = 0.95f, active = 0.18f, recover = 0.8f,
                    length = 1.7f, damage = 12f, poise = 15f, radius = 0.7f, trackUntil = 0.7f
                });
                cooldown = Random.Range(2.6f, 3.8f);
            }
            else Strafe(strafeSign, moveSpeed * 0.5f, dt);
        }
    }
}

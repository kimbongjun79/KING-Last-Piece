using UnityEngine;

namespace KingLastPiece
{
    /// <summary>Passive target for the Attack Lab: never attacks, heals back to full when left alone.</summary>
    public class TrainingDummy : EnemyBase
    {
        protected override void Configure()
        {
            DisplayName = "Training Dummy";
            SetHealth(160f);
            SetRadius(0.45f);
            moveSpeed = 0f;
        }

        protected override StaggerProfile CreateStaggerProfile() =>
            StaggerProfile.Create(maxGauge: 45f, vulnerableDuration: 3f, launchForce: 7f, flinchDuration: 0.35f);

        protected override void Think(float dt)
        {
            FaceTowards(ToPlayer, 90f);
            if (Time.time - LastHitTime > 2.5f) Health = MaxHealth;
        }
    }
}

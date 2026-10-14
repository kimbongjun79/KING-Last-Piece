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
            maxPoise = 30f;
            staggerDuration = 0.8f;
        }

        protected override void Think(float dt)
        {
            FaceTowards(ToPlayer, 90f);
            if (Time.time - LastHitTime > 2.5f) Health = MaxHealth;
        }
    }
}

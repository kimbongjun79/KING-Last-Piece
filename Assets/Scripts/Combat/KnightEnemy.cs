using UnityEngine;

namespace KingLastPiece
{
    /// <summary>Knight: repositions with L-shaped hops, then leaps onto the King's square.</summary>
    public class KnightEnemy : EnemyBase
    {
        float hopTimer;
        bool hopping;
        Vector3 hopFrom, hopTo;
        float hopT;

        static readonly Vector2[] LMoves =
        {
            new Vector2(2, 1), new Vector2(1, 2), new Vector2(-1, 2), new Vector2(-2, 1),
            new Vector2(-2, -1), new Vector2(-1, -2), new Vector2(1, -2), new Vector2(2, -1)
        };

        protected override void Configure()
        {
            DisplayName = "Black Knight";
            SetHealth(265f);   // ~4 light-combo cycles
            SetRadius(0.5f);
            moveSpeed = 3f;
            hopTimer = Random.Range(0.4f, 1f);
        }

        protected override StaggerProfile CreateStaggerProfile() =>
            StaggerProfile.Create(maxGauge: 60f, vulnerableDuration: 2.5f, launchForce: 6.5f, flinchDuration: 0.35f);

        protected override void Think(float dt)
        {
            Vector3 to = ToPlayer;
            float d = to.magnitude;

            if (hopping)
            {
                hopT += dt / 0.42f;
                transform.position = Vector3.Lerp(hopFrom, hopTo, hopT);
                moving = true;
                FaceTowards(to, 500f);
                if (hopT >= 1f) hopping = false;
                return;
            }

            FaceTowards(to, 400f);

            if (CanAttackNow && d < 9f)
            {
                BeginAttack(new AttackPlan
                {
                    kind = AtkKind.Leap, target = Player.FlatPos, windup = 0.88f, active = 0.55f, recover = 0.9f,
                    damage = 20f, poise = 35f, radius = 1.5f
                });
                cooldown = Random.Range(3.4f, 4.6f);
                return;
            }

            hopTimer -= dt;
            if (hopTimer <= 0f)
            {
                hopTimer = Random.Range(0.9f, 1.5f);
                // Pick the L-move that keeps a ~4.5u distance from the King.
                Vector3 best = FlatPos;
                float bestScore = float.MaxValue;
                foreach (var m in LMoves)
                {
                    Vector3 c = FlatPos + new Vector3(m.x, 0f, m.y);
                    if (Mathf.Abs(c.x) > GameManager.PlayableHalf || Mathf.Abs(c.z) > GameManager.PlayableHalf) continue;
                    float score = Mathf.Abs((Player.FlatPos - c).magnitude - 4.5f) + Random.value * 0.5f;
                    if (score < bestScore) { bestScore = score; best = c; }
                }
                hopFrom = FlatPos;
                hopTo = best;
                hopT = 0f;
                hopping = true;
                Sfx.Play("KNIGHT_Hop", transform.position);
            }
        }
    }
}

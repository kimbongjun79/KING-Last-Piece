using UnityEngine;

namespace KingLastPiece
{
    /// <summary>The Black Queen (boss): chained dashes in any direction, leaps, and a radial "Check" slam.
    /// Below half health she summons pawns and attacks faster.</summary>
    public class QueenEnemy : EnemyBase
    {
        int dashesLeft;
        bool phase2;
        float strafeSign = 1f;

        protected override void Configure()
        {
            DisplayName = "The Black Queen";
            IsBoss = true;
            SetHealth(700f);
            SetRadius(0.75f);
            SetModelScale(1.35f);
            moveSpeed = 3.2f;
            maxPoise = 110f;
            staggerDuration = 1.6f;
        }

        float Speed => phase2 ? 0.78f : 1f;

        protected override void Think(float dt)
        {
            if (!phase2 && Health < MaxHealth * 0.5f)
            {
                phase2 = true;
                Sfx.Play("QUEEN_Phase2");
                Sfx.Play("QUEEN_Summon", transform.position);
                Flash(new Color(2f, 0.2f, 0.1f), 0.4f);
                Effects.Shockwave(transform.position, 5f, new Color(0.8f, 0.1f, 0.5f), 0.7f);
                Game.Rig.Shake(0.5f);
                Game.ShowBanner("The Queen summons her pawns");
                Vector3 side = transform.right * 3f;
                Game.SpawnEnemy(PieceType.Pawn, ClampPoint(FlatPos + side));
                Game.SpawnEnemy(PieceType.Pawn, ClampPoint(FlatPos - side));
                cooldown = 1.2f;
                return;
            }

            Vector3 to = ToPlayer;
            float d = to.magnitude;
            FaceTowards(to, 360f);

            if (cooldown <= 0f)
            {
                float r = Random.value;
                if (d < 3.6f && r < 0.5f)
                {
                    BeginAttack(new AttackPlan
                    {
                        kind = AtkKind.Slam, windup = 1.18f * Speed, active = 0.14f, recover = 1.0f,
                        damage = 26f, poise = 50f, radius = 3.8f
                    });
                }
                else if (d > 5f && r < 0.45f)
                {
                    BeginAttack(new AttackPlan
                    {
                        kind = AtkKind.Leap, target = Player.FlatPos, windup = 0.97f * Speed, active = 0.6f, recover = 1.0f,
                        damage = 28f, poise = 45f, radius = 2.2f
                    });
                }
                else
                {
                    dashesLeft = phase2 ? 2 : 1;
                    Dash(0.86f * Speed);
                }
                cooldown = Random.Range(1.4f, 2.2f) * Speed;
                if (Random.value < 0.3f) strafeSign = -strafeSign;
                return;
            }

            if (d > 3.2f) MoveTowards(Player.FlatPos, moveSpeed, dt);
            else Strafe(strafeSign, moveSpeed * 0.6f, dt);
        }

        void Dash(float windup)
        {
            Vector3 to = ToPlayer;
            BeginAttack(new AttackPlan
            {
                kind = AtkKind.Dash, dir = to, windup = windup, recover = dashesLeft > 0 ? 0.25f : 1.1f,
                length = Mathf.Clamp(to.magnitude + 4f, 6f, 12f), speed = 19f, damage = 22f, poise = 40f, radius = 1.0f
            });
        }

        protected override bool ContinueCombo()
        {
            if (dashesLeft <= 0) return false;
            dashesLeft--;
            Dash(0.48f * Speed);
            return true;
        }
    }
}

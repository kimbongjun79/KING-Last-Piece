using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// The Black Queen (boss): takes a square on one of the King's 8 lines and charges along it (chained dashes stay
    /// on chess lines), leaps onto the King's square from afar, and slams a radial "Check" up close.
    /// Below half health she summons pawns and attacks faster.
    /// </summary>
    public class QueenEnemy : EnemyBase
    {
        int dashesLeft;
        bool phase2;

        protected override void Configure()
        {
            DisplayName = "The Black Queen";
            IsBoss = true;
            SetHealth(700f);
            SetRadius(0.75f);
            SetModelScale(1.35f);
            moveSpeed = 3.2f;
            maxPoise = 220f;   // twice the rook's gauge: the hardest piece to stagger
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
                Effects.DarkWave(transform.position, 5f, 0.7f);
                Game.Rig.Shake(0.5f);
                // Two pawns in the classic game; in the endless mode more and stronger pieces as the bosses go on.
                Game.ShowBanner(Game.IsEndless && Game.Round >= 20 ? "The Queen summons her army" : "The Queen summons her pawns");
                Game.SummonFor(this);
                cooldown = 1.2f;
                return;
            }

            Vector3 to = ToPlayer;
            float d = to.magnitude;
            FaceTowards(to, 360f);
            UpdateSlot(BoardSlots.QueenSlots);

            if (cooldown <= 0f)
            {
                // The Queen moves like a rook and a bishop together: she charges along whichever line she stands on.
                float r = Random.value;
                bool acted = true;
                if (d < 3.6f && r < 0.5f)
                {
                    BeginAttack(new AttackPlan
                    {
                        kind = AtkKind.Slam, windup = 1.08f * Speed, active = 0.14f, recover = 1.0f,
                        damage = 26f, poise = 50f, radius = 3.8f
                    });
                }
                else if (ReadyAtAttackSlot)
                {
                    dashesLeft = phase2 ? 2 : 1;
                    Dash(0.8f * Speed, BoardSlots.LineDirection(MySquare, KingSquare));
                }
                else if (d > 5f && r < 0.2f)
                {
                    BeginAttack(new AttackPlan
                    {
                        kind = AtkKind.Leap, target = BoardSlots.Center(KingSquare), windup = 0.9f * Speed, active = 0.6f, recover = 1.0f,
                        damage = 28f, poise = 45f, radius = 2.2f
                    });
                }
                else acted = false;

                if (acted)
                {
                    cooldown = Random.Range(1.4f, 2.2f) * Speed;
                    return;
                }
                cooldown = 0.35f;   // nothing fit yet: keep heading for a line and decide again shortly
            }

            if (!ReadyAtAttackSlot) MoveToSlot(Gait.Direct, moveSpeed, dt);
        }

        void Dash(float windup, Vector3 dir, float length = FullLine)
        {
            BeginAttack(new AttackPlan
            {
                kind = AtkKind.Dash, dir = dir, windup = windup, recover = dashesLeft > 0 ? 0.25f : 1.1f,
                length = length, speed = 24f, damage = 22f, poise = 40f, radius = 1.0f
            });
        }

        protected override bool ContinueCombo()
        {
            if (dashesLeft <= 0) return false;
            dashesLeft--;
            // Follow-up dashes stay on chess lines too (nearest of the 8 directions), and stay short: just past the King.
            Dash(0.44f * Speed, BoardSlots.Snap8(ToPlayer), Mathf.Clamp(ToPlayer.magnitude + 4f, 6f, 12f));
            return true;
        }
    }
}

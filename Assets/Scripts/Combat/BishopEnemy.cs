using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Bishop: glides along diagonals to the nearest square on one of the King's diagonals (from 2 squares out up to the
    /// far edge), then charges down that diagonal through the King's square to the board edge.
    /// Dark I: after the charge it turns and charges straight back to where it started. Dark II: it may instead hold
    /// its square and strike both of its diagonals end to end at once.
    /// </summary>
    public class BishopEnemy : EnemyBase
    {
        bool wasReady, returnLeg, lastZone;

        protected override void Configure()
        {
            DisplayName = "흑색 비숍";
            SetHealth(265f);   // ~4 light-combo cycles
            SetRadius(0.45f);
            moveSpeed = 3.4f;
        }

        protected override StaggerProfile CreateStaggerProfile() =>
            StaggerProfile.Create(maxGauge: 40f, vulnerableDuration: 2.5f, launchForce: 6.5f, flinchDuration: 0.35f);   // one heavy (42) breaks it

        protected override void Think(float dt)
        {
            UpdateSlot(BoardSlots.BishopSlots);
            FaceTowards(ToPlayer, 360f);

            bool ready = ReadyAtAttackSlot;
            if (ready && !wasReady) Sfx.Play("BISHOP_Align", transform.position);
            wasReady = ready;

            if (ready)
            {
                if (CanAttackNow)
                {
                    returnLeg = false;
                    // Never the line strike twice in a row.
                    if (EliteTier >= 2 && (lastZone = !lastZone && Random.value < 0.4f))
                    {
                        // Both diagonals through its square, end to end: step off them (a straight step is enough).
                        BeginAttack(new AttackPlan
                        {
                            kind = AtkKind.Zone, windup = 1.2f, active = 0.12f, recover = 1.0f,
                            damage = 20f, poise = 35f, squares = LinesOf(MySquare, false, true),
                        });
                    }
                    else
                    {
                        returnLeg = EliteTier >= 1;
                        Charge(BoardSlots.LineDirection(MySquare, KingSquare), FullLine, 0.9f, returnLeg ? 0.3f : 0.85f);
                    }
                    cooldown = Random.Range(3.0f, 4.0f);
                }
                return;
            }
            MoveToSlot(Gait.Diagonal, moveSpeed, dt);
        }

        void Charge(Vector3 dir, float length, float windup, float recover) => BeginAttack(new AttackPlan
        {
            kind = AtkKind.Dash, dir = dir, windup = windup, recover = recover,
            length = length, speed = 20f, damage = 18f, poise = 30f, radius = 0.75f
        });

        protected override bool ContinueCombo()
        {
            if (!returnLeg) return false;
            returnLeg = false;
            // Straight back along the same diagonal to the square it charged from.
            Vector3 back = AttackOrigin - FlatPos;
            back.y = 0f;
            if (back.magnitude < 1f) return false;
            Charge(back, back.magnitude, 0.45f, 0.9f);
            cooldown = Mathf.Max(cooldown, Random.Range(2.6f, 3.6f));   // a breather after the round trip
            return true;
        }
    }
}

using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Rook: heavy, armored. Slides along ranks and files to the nearest square on the King's rank or file (2 squares
    /// out or more), then rams down that line. When the King steps next to it, it slams the squares around itself.
    /// Dark I: the ram comes straight back after it, and the slam strikes two squares out (a 5×5 block, corners cut).
    /// Dark II: it may instead hold its square and strike its whole rank and file at once.
    /// </summary>
    public class RookEnemy : EnemyBase
    {
        bool wasReady, returnLeg, lastZone;

        protected override void Configure()
        {
            DisplayName = "흑색 룩";
            SetHealth(380f);   // tougher than knight/bishop (~5.8 cycles)
            SetRadius(0.55f);
            moveSpeed = 2.8f;
        }

        protected override StaggerProfile CreateStaggerProfile() =>
            StaggerProfile.Create(maxGauge: 80f, vulnerableDuration: 2f, launchForce: 4.8f, flinchDuration: 0.3f);   // a heavy and a light combo (84)

        protected override void Think(float dt)
        {
            Vector3 to = ToPlayer;
            FaceTowards(to, 240f);
            bool dark = EliteTier >= 1;

            // Crowded: the King is close (a neighbouring square; two squares for a Dark rook) — hold ground and slam.
            if (dark ? BoardSlots.Chebyshev(MySquare, KingSquare) <= 2 && to.magnitude < 4.8f
                     : BoardSlots.Chebyshev(MySquare, KingSquare) <= 1 && to.magnitude < 2.6f)
            {
                if (!CanAttackNow) return;
                returnLeg = false;
                BeginAttack(new AttackPlan
                {
                    kind = AtkKind.Slam, windup = dark ? 0.85f : 0.75f, active = 0.14f, recover = 1.0f,
                    damage = 22f, poise = 40f, radius = dark ? 4.6f : 2.6f, squares = dark ? BlockOf(MySquare, 2, true) : null,
                });
                cooldown = Random.Range(1.6f, 2.4f);
                return;
            }

            UpdateSlot(BoardSlots.RookSlots);
            bool ready = ReadyAtAttackSlot;
            if (ready && !wasReady) Sfx.Play("ROOK_Align", transform.position);
            wasReady = ready;

            if (ready)
            {
                if (CanAttackNow)
                {
                    returnLeg = false;
                    // Never the line strike twice in a row.
                    if (EliteTier >= 2 && (lastZone = !lastZone && Random.value < 0.4f))
                    {
                        // Its whole rank and file at once: step off them (a diagonal step is enough).
                        BeginAttack(new AttackPlan
                        {
                            kind = AtkKind.Zone, windup = 1.25f, active = 0.12f, recover = 1.1f,
                            damage = 24f, poise = 45f, squares = LinesOf(MySquare, true, false),
                        });
                    }
                    else
                    {
                        returnLeg = dark;
                        Ram(BoardSlots.LineDirection(MySquare, KingSquare), FullLine, 0.75f, returnLeg ? 0.35f : 1.1f);
                    }
                    cooldown = Random.Range(2f, 3f);
                }
                return;
            }
            MoveToSlot(Gait.Orthogonal, moveSpeed, dt);
        }

        void Ram(Vector3 dir, float length, float windup, float recover) => BeginAttack(new AttackPlan
        {
            kind = AtkKind.Dash, dir = dir, windup = windup, recover = recover,
            length = length, speed = 22f, damage = 26f, poise = 50f, radius = 0.9f
        });

        protected override bool ContinueCombo()
        {
            if (!returnLeg) return false;
            returnLeg = false;
            // Straight back along the same line to the square it rammed from.
            Vector3 back = AttackOrigin - FlatPos;
            back.y = 0f;
            if (back.magnitude < 1f) return false;
            Ram(back, back.magnitude, 0.5f, 1.1f);
            cooldown = Mathf.Max(cooldown, Random.Range(2.6f, 3.6f));   // a breather after the round trip
            return true;
        }
    }
}

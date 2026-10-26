using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// The Black Queen (boss): takes a square on one of the King's 8 lines and charges along it (chained dashes stay
    /// on chess lines), leaps onto the King's square from afar, and slams a radial "Check" up close.
    /// Below half health she summons pawns and attacks faster.
    /// Dark I: "Royal Star" — all eight of her lines struck end to end at once.
    /// Dark II: also "Checkmate" (the eight lines through the King's square), a rain of squares around the King in
    /// three waves, and "Purge" — she leaps off the board and strikes the whole of it but for three safe squares,
    /// then leaps back in onto the King.
    /// </summary>
    public class QueenEnemy : EnemyBase
    {
        int dashesLeft, rainLeft;
        bool phase2, purging;
        float purgeTimer = 8f;
        Vector3 purgeFrom, purgeTo;

        protected override void Configure()
        {
            DisplayName = "흑색 여왕";
            IsBoss = true;
            SetHealth(700f);
            SetRadius(0.75f);
            SetModelScale(1.35f);
            moveSpeed = 3.2f;
            // Very hard to stagger: about two breaks over a whole round-10 fight (its poise refills only slowly).
            maxPoise = 300f;
            staggerDuration = 1.6f;
        }

        float Speed => phase2 ? 0.78f : 1f;

        protected override void Think(float dt)
        {
            purgeTimer -= dt;
            if (!phase2 && Health < MaxHealth * 0.5f)
            {
                phase2 = true;
                Sfx.Play("QUEEN_Phase2");
                Sfx.Play("QUEEN_Summon", transform.position);
                Flash(new Color(2f, 0.2f, 0.1f), 0.4f);
                Effects.DarkWave(transform.position, 5f, 0.7f);
                Game.Rig.Shake(0.5f);
                // Two pawns in the classic game; in the endless mode more and stronger pieces as the bosses go on.
                Game.ShowBanner(Game.IsEndless && Game.Round >= 20 ? "여왕이 군대를 부른다" : "여왕이 폰을 부른다");
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
                dashesLeft = rainLeft = 0;
                purging = false;
                // A Dark queen opens with one of her own attacks now and then.
                if (EliteTier >= 1 && Random.value < (EliteTier >= 2 ? 0.45f : 0.3f))
                {
                    Special();
                    cooldown = Random.Range(1.6f, 2.4f) * Speed;
                    return;
                }

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
                else if (d > 5f && r < 0.2f) LeapOntoKing(0.9f * Speed);
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

        /// <summary>Lab: start one of the Dark attacks now (0 Royal Star, 1 Checkmate, 2 rain, 3 Purge).</summary>
        public void DebugSpecial(int which)
        {
            dashesLeft = rainLeft = 0;
            purging = false;
            if (which == 0) RoyalStar();
            else if (which == 1) Checkmate();
            else if (which == 2) { rainLeft = 2; Rain(1.0f * Speed); }
            else Purge();
            cooldown = 99f;
        }

        void Special()
        {
            if (EliteTier < 2) { RoyalStar(); return; }
            float r = Random.value;
            if (purgeTimer <= 0f && r < 0.3f) Purge();
            else if (r < 0.55f) { rainLeft = 2; Rain(1.0f * Speed); }
            else if (r < 0.8f) Checkmate();
            else RoyalStar();
        }

        /// <summary>All eight of her lines, end to end: get off them.</summary>
        void RoyalStar() => BeginAttack(new AttackPlan
        {
            kind = AtkKind.Zone, windup = 1.15f * Speed, active = 0.12f, recover = 1.0f,
            damage = 24f, poise = 45f, squares = LinesOf(MySquare, true, true),
        });

        /// <summary>The eight lines through the King's square: a knight's step takes him off all of them.</summary>
        void Checkmate()
        {
            Game.ShowBanner("체크메이트");
            BeginAttack(new AttackPlan
            {
                kind = AtkKind.Zone, target = BoardSlots.Center(KingSquare), windup = 1.35f * Speed, active = 0.12f, recover = 1.0f,
                damage = 26f, poise = 45f, squares = LinesOf(KingSquare, true, true),
            });
        }

        /// <summary>One wave of the rain: the King's square and about a third of the squares around him (3 out).</summary>
        void Rain(float windup)
        {
            var king = KingSquare;
            var list = new List<Vector2Int> { king };
            foreach (var s in BlockOf(king, 3))
                if (s != king && Random.value < 0.35f) list.Add(s);
            BeginAttack(new AttackPlan
            {
                kind = AtkKind.Zone, target = BoardSlots.Center(king), windup = windup, active = 0.1f, recover = rainLeft > 0 ? 0.08f : 1.0f,
                damage = 18f, poise = 30f, squares = list.ToArray(),
            });
        }

        /// <summary>
        /// Leaps off the board (out of reach) and strikes all of it but three safe squares — one two squares from the
        /// King, one a little farther, one anywhere — then leaps back in onto him.
        /// </summary>
        void Purge()
        {
            purging = true;
            purgeTimer = 16f;
            OffBoard = true;
            purgeFrom = FlatPos;
            // Out past the nearer pair of the area's edges (relative to its centre).
            float hx = BoardSlots.HalfX - GameManager.EdgeInset, hz = BoardSlots.HalfZ - GameManager.EdgeInset;
            Vector3 o = BoardSlots.Origin, p = FlatPos - o;
            purgeTo = o + (Mathf.Abs(p.x) / Mathf.Max(0.01f, hx) >= Mathf.Abs(p.z) / Mathf.Max(0.01f, hz)
                ? new Vector3((p.x < 0f ? -1f : 1f) * (BoardSlots.HalfX + 1.8f), 0f, Mathf.Clamp(p.z, -hz, hz))
                : new Vector3(Mathf.Clamp(p.x, -hx, hx), 0f, (p.z < 0f ? -1f : 1f) * (BoardSlots.HalfZ + 1.8f)));

            var king = KingSquare;
            var safe = new List<Vector2Int>();
            void PickSafe(int minRing, int maxRing)
            {
                var options = new List<Vector2Int>();
                for (int x = 0; x < BoardSlots.Width; x++)
                for (int y = 0; y < BoardSlots.Height; y++)
                {
                    var s = new Vector2Int(x, y);
                    int ring = BoardSlots.Chebyshev(s, king);
                    if (ring >= minRing && ring <= maxRing && !safe.Contains(s)) options.Add(s);
                }
                if (options.Count > 0) safe.Add(options[Random.Range(0, options.Count)]);
            }
            PickSafe(2, 2);
            PickSafe(3, 4);
            PickSafe(2, 99);
            var strikes = new List<Vector2Int>();
            for (int x = 0; x < BoardSlots.Width; x++)
            for (int y = 0; y < BoardSlots.Height; y++)
            {
                var s = new Vector2Int(x, y);
                if (!safe.Contains(s)) strikes.Add(s);
            }
            Game.ShowBanner("여왕의 숙청 — 하얀 칸으로!");
            BoardSlots.Release(this);
            BeginAttack(new AttackPlan
            {
                kind = AtkKind.Zone, windup = 2.6f * Speed, active = 0.15f, recover = 0.45f,
                damage = 34f, poise = 60f, squares = strikes.ToArray(), safe = safe.ToArray(),
            });
        }

        protected override void OnWindup(float p)
        {
            if (!purging || !OffBoard) return;
            // The leap off the board in the first part of the wind-up, then she hangs in the air beyond its edge.
            float k = Mathf.Clamp01(p / 0.3f);
            transform.position = Vector3.Lerp(purgeFrom, purgeTo, PoseTrack.Curve(Ease.Out, k));
            ModelLift = Mathf.Sin(k * Mathf.PI) * 3.5f + k * 2.2f;
        }

        void LeapOntoKing(float windup) => BeginAttack(new AttackPlan
        {
            kind = AtkKind.Leap, target = BoardSlots.Center(KingSquare), windup = windup, active = 0.6f, recover = 1.0f,
            damage = 28f, poise = 45f, radius = 2.2f
        });

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
            if (purging)
            {
                // Back in from beyond the edge, straight onto the King.
                purging = false;
                LeapOntoKing(0.75f * Speed);
                return true;
            }
            if (rainLeft > 0)
            {
                rainLeft--;
                Rain(0.72f * Speed);
                return true;
            }
            if (dashesLeft <= 0) return false;
            dashesLeft--;
            // Follow-up dashes stay on chess lines too (nearest of the 8 directions), and stay short: just past the King.
            Dash(0.44f * Speed, BoardSlots.Snap8(ToPlayer), Mathf.Clamp(ToPlayer.magnitude + 4f, 6f, 12f));
            return true;
        }
    }
}

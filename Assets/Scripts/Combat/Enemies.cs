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
                    kind = AtkKind.Lunge, dir = to, windup = 0.6f, active = 0.18f, recover = 0.8f,
                    length = 1.7f, damage = 12f, poise = 15f, radius = 0.7f, trackUntil = 0.7f
                });
                cooldown = Random.Range(2.6f, 3.8f);
            }
            else Strafe(strafeSign, moveSpeed * 0.5f, dt);
        }
    }

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
                    kind = AtkKind.Leap, target = Player.FlatPos, windup = 0.75f, active = 0.55f, recover = 0.9f,
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

    /// <summary>Bishop: slides onto one of the King's diagonals, then charges along it to the board edge.</summary>
    public class BishopEnemy : EnemyBase
    {
        float alignTimer;
        bool wasAligned;

        protected override void Configure()
        {
            DisplayName = "Black Bishop";
            SetHealth(265f);   // ~4 light-combo cycles
            SetRadius(0.45f);
            moveSpeed = 3.4f;
        }

        protected override StaggerProfile CreateStaggerProfile() =>
            StaggerProfile.Create(maxGauge: 60f, vulnerableDuration: 2.5f, launchForce: 6.5f, flinchDuration: 0.35f);

        protected override void Think(float dt)
        {
            Vector3 to = ToPlayer;
            FaceTowards(to, 360f);
            alignTimer += dt;

            Vector3 q = LineAligner.Nearest(FlatPos, Player.FlatPos, true);
            bool aligned = (q - FlatPos).magnitude < 0.45f;
            if (aligned && !wasAligned) Sfx.Play("BISHOP_Align", transform.position);
            wasAligned = aligned;

            if (CanAttackNow && (aligned || alignTimer > 3f))
            {
                alignTimer = 0f;
                Vector3 dir = aligned ? LineAligner.Snap(to, true) : to;
                BeginAttack(new AttackPlan
                {
                    kind = AtkKind.Dash, dir = dir, windup = 0.7f, recover = 0.85f,
                    length = 13f, speed = 15f, damage = 18f, poise = 30f, radius = 0.75f
                });
                cooldown = Random.Range(3.0f, 4.0f);
            }
            else MoveTowards(q, moveSpeed, dt);
        }
    }

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
                    kind = AtkKind.Slam, windup = 0.85f, active = 0.14f, recover = 1.0f,
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
                    kind = AtkKind.Dash, dir = dir, windup = 0.95f, recover = 1.1f,
                    length = 14f, speed = 17f, damage = 26f, poise = 50f, radius = 0.9f
                });
                cooldown = Random.Range(2f, 3f);
            }
            else MoveTowards(q, moveSpeed, dt);
        }
    }

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
                        kind = AtkKind.Slam, windup = 1.05f * Speed, active = 0.14f, recover = 1.0f,
                        damage = 26f, poise = 50f, radius = 3.8f
                    });
                }
                else if (d > 5f && r < 0.45f)
                {
                    BeginAttack(new AttackPlan
                    {
                        kind = AtkKind.Leap, target = Player.FlatPos, windup = 0.85f * Speed, active = 0.6f, recover = 1.0f,
                        damage = 28f, poise = 45f, radius = 2.2f
                    });
                }
                else
                {
                    dashesLeft = phase2 ? 2 : 1;
                    Dash(0.75f * Speed);
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
            Dash(0.42f * Speed);
            return true;
        }
    }

    /// <summary>Helpers for rook/bishop style line alignment.</summary>
    public static class LineAligner
    {
        static readonly Vector3[] Ortho = { Vector3.right, Vector3.forward };
        static readonly Vector3[] Diag = { new Vector3(1, 0, 1).normalized, new Vector3(1, 0, -1).normalized };

        /// <summary>Closest point on any line through the target (ranks/files or diagonals), kept at a striking distance.</summary>
        public static Vector3 Nearest(Vector3 self, Vector3 target, bool diagonal)
        {
            Vector3 best = self;
            float bestDist = float.MaxValue;
            foreach (var dir in diagonal ? Diag : Ortho)
            {
                float s = Vector3.Dot(self - target, dir);
                if (Mathf.Abs(s) < 3.5f) s = (s >= 0f ? 1f : -1f) * 3.5f;
                Vector3 q = target + dir * s;
                float h = GameManager.PlayableHalf;
                if (Mathf.Abs(q.x) > h || Mathf.Abs(q.z) > h)
                {
                    s = -s; // try the other side of the target
                    q = target + dir * s;
                }
                q = new Vector3(Mathf.Clamp(q.x, -h, h), 0f, Mathf.Clamp(q.z, -h, h));
                float d = (q - self).magnitude;
                if (d < bestDist) { bestDist = d; best = q; }
            }
            return best;
        }

        public static Vector3 Snap(Vector3 v, bool diagonal)
        {
            if (diagonal) return new Vector3(Mathf.Sign(v.x), 0f, Mathf.Sign(v.z)).normalized;
            return Mathf.Abs(v.x) > Mathf.Abs(v.z) ? new Vector3(Mathf.Sign(v.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(v.z));
        }
    }
}

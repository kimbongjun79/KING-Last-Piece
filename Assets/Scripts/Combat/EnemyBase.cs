using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Black piece AI base: telegraphed attacks (wind-up → active → recovery), poise/stagger,
    /// spawn drop-in and "captured" topple on death. Subclasses only decide what to do in Think().
    /// </summary>
    public abstract class EnemyBase : Actor
    {
        protected enum EState { Spawning, Move, Windup, Attack, Recover, Stagger, Airborne, Getup, Grabbed, Launched, Dead }
        protected enum AtkKind { Lunge, Dash, Leap, Slam }

        protected struct AttackPlan
        {
            public AtkKind kind;
            public Vector3 dir, target;
            public float windup, active, recover;
            public float length, speed, damage, poise, radius;
            public float trackUntil;
        }

        public PieceType Type { get; private set; }
        /// <summary>The piece's model (lifts, tilts and flies with it; hit effects follow this).</summary>
        public Transform Visual => model != null ? model : transform;
        public string DisplayName { get; protected set; } = "Pawn";
        public bool IsBoss { get; protected set; }
        public float LastHitTime { get; private set; } = -100f;
        public bool Targetable => state != EState.Spawning && !IsDead;
        public bool IsAttacking => state == EState.Windup || state == EState.Attack;
        public float WindupProgress => state == EState.Windup ? Mathf.Clamp01(stateTime / Mathf.Max(0.01f, plan.windup)) : 0f;

        /// <summary>Seconds until the current attack can first connect (∞ when not attacking).</summary>
        public float TimeToImpact
        {
            get
            {
                bool delayed = plan.kind == AtkKind.Leap || plan.kind == AtkKind.Slam;  // hit at the end of the active phase
                float travel = 0f;
                if (!delayed && Player != null)
                {
                    // Dashes/lunges connect once they've covered the distance to the King.
                    float along = Vector3.Dot(Player.FlatPos - FlatPos, plan.dir) - plan.radius - Player.Radius;
                    float speed = plan.length / Mathf.Max(0.01f, plan.active);
                    travel = Mathf.Max(0f, along) / Mathf.Max(0.01f, speed);
                }
                if (state == EState.Windup) return plan.windup - stateTime + (delayed ? plan.active : travel);
                if (state == EState.Attack) return delayed ? Mathf.Max(0f, plan.active - stateTime) : travel;
                return float.PositiveInfinity;
            }
        }
        /// <summary>Direction of the current attack (dashes/lunges travel along it).</summary>
        public Vector3 AttackDirection => plan.dir;

        /// <summary>
        /// If this piece's wind-up/attack is about to hit <paramref name="playerPos"/>, gives the way out:
        /// perpendicular to a dash/lunge line (<paramref name="isLine"/>), or radially out of a leap/slam circle.
        /// Used to auto-steer the King's roll.
        /// </summary>
        public bool TryGetThreat(Vector3 playerPos, float playerRadius, out Vector3 escape, out bool isLine, out float timeToImpact)
        {
            const float Margin = 1.2f, MaxLead = 0.9f;
            escape = Vector3.zero;
            isLine = false;
            timeToImpact = TimeToImpact;
            if (IsDead || (state != EState.Windup && state != EState.Attack) || timeToImpact > MaxLead) return false;
            playerPos.y = 0f;

            if (plan.kind == AtkKind.Dash || plan.kind == AtkKind.Lunge)
            {
                Vector3 d = plan.dir;
                float s = Mathf.Clamp(Vector3.Dot(playerPos - atkStart, d), 0f, plan.length + plan.radius);
                Vector3 off = playerPos - (atkStart + d * s);
                off.y = 0f;
                if (off.magnitude > plan.radius + playerRadius + Margin) return false;
                Vector3 perp = Vector3.Cross(Vector3.up, d);
                float side = Vector3.Dot(off, perp);
                escape = perp * (Mathf.Abs(side) > 0.05f ? Mathf.Sign(side) : 1f);
                isLine = true;
                return true;
            }

            Vector3 center = plan.kind == AtkKind.Leap ? plan.target : atkStart;
            Vector3 radial = playerPos - new Vector3(center.x, 0f, center.z);
            float edge = StrikesCross ? CrossGap(radial) : radial.magnitude - plan.radius;
            if (edge > playerRadius + Margin) return false;
            escape = radial.sqrMagnitude > 0.01f ? radial.normalized : (playerPos - FlatPos).normalized;
            if (escape.sqrMagnitude < 0.01f) escape = -transform.forward;
            return true;
        }

        public bool IgnoresCollision =>
            IsDead || state == EState.Spawning || state == EState.Grabbed || state == EState.Launched || state == EState.Airborne ||
            (state == EState.Attack && (plan.kind == AtkKind.Leap || plan.kind == AtkKind.Dash));

        protected EState state;
        protected float stateTime, cooldown;
        protected float moveSpeed = 2.5f, maxPoise = 25f, poise, staggerDuration = 1.1f;
        protected bool hyperArmor;
        protected AttackPlan plan;
        protected PlayerController Player => Game != null ? Game.Player : null;
        protected bool moving;
        /// <summary>Raise the model this frame (e.g. a hop); the highest request wins.</summary>
        protected float ModelLift { set => modelY = Mathf.Max(modelY, value); }

        Transform model;
        Vector3 modelBaseScale;
        Telegraph telegraph;
        Vector3 atkStart;
        bool atkHit, landed;
        float poiseRegenTimer, hopPhase, modelY, squash = 1f;
        Quaternion modelTilt = Quaternion.identity, vulnWobble = Quaternion.identity;
        StaggerController stagger;
        float currentStagger, airHeight, airVy, vulnFxTimer;
        int airHits;
        bool wasMoving;  // for the slide sound when a piece starts moving
        float modelHeight = 1.5f;
        float baseRadius = 0.4f;     // model base radius: a tilted piece pivots on its rim
        float lastHitDamage;         // the killing blow sets how hard the rubble flies
        // Fighting-game hit reel: the top snaps back (and twists) on each hit, then springs upright.
        float reelPitch, reelPitchVel, reelYaw, reelYawVel;
        readonly System.Collections.Generic.HashSet<EnemyBase> shoved = new System.Collections.Generic.HashSet<EnemyBase>();
        Vector3 flinchPush, airKnock;
        static readonly Color WindupGlow = new Color(1.6f, 0.12f, 0.04f);
        /// <summary>
        /// Flashes in a deep crimson — darker and cooler than the bright orange-red wind-up glow, so a struck or broken
        /// piece never reads as one about to attack, and still stands out against the white hit effects.
        /// Struck: a faint crimson pulse. Guard broken (Vulnerable): the full crimson flash right after it.
        /// </summary>
        static readonly Color HitFlash = new Color(0.5f, 0.02f, 0.06f);
        static readonly Color VulnerableFlash = new Color(1.15f, 0.03f, 0.12f);

        public void Setup(PieceType type, Transform modelRoot)
        {
            Type = type;
            model = modelRoot;
            Configure();
            if (!IsBoss)
            {
                stagger = gameObject.AddComponent<StaggerController>();
                stagger.Profile = CreateStaggerProfile();
            }
            modelBaseScale = model.localScale;
            var rends = model.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                modelHeight = b.size.y;
                baseRadius = Mathf.Max(0.15f, Mathf.Min(b.extents.x, b.extents.z) * 0.85f);
            }
            InitActor(MaxHealth);
            poise = maxPoise;
            state = EState.Spawning;
            stateTime = 0f;
            cooldown = Random.Range(0.8f, 1.6f);
        }

        /// <summary>Set MaxHealth, Radius, DisplayName, speeds, poise and model scale.</summary>
        protected abstract void Configure();

        /// <summary>Movement / attack selection while in the Move state.</summary>
        protected abstract void Think(float dt);

        /// <summary>Called when an attack's recovery ends. Return true if another attack was started (combo).</summary>
        protected virtual bool ContinueCombo() => false;

        /// <summary>Stagger tuning for regular enemies (bosses don't use the stagger system).</summary>
        protected virtual StaggerProfile CreateStaggerProfile() => StaggerProfile.Create(40f, 2.5f, 7f, 0.35f);

        public bool IsVulnerable => stagger != null && stagger.Vulnerable;
        public bool IsAirborne => state == EState.Airborne;
        public float AirHeight => state == EState.Airborne ? Mathf.Max(0f, airHeight) : 0f;
        /// <summary>One-letter state code for lab timelines.</summary>
        public char StateCode => state switch
        {
            EState.Move => 'm', EState.Windup => 'w', EState.Attack => 'a', EState.Recover => 'r',
            EState.Stagger => 's', EState.Airborne => 'U', EState.Getup => 'g', EState.Dead => 'x', _ => '.',
        };

        protected void SetHealth(float hp) => MaxHealth = hp;
        protected void SetRadius(float r) => Radius = r;
        protected void SetModelScale(float s) => model.localScale = Vector3.one * s;

        // ---------------------------------------------------------------- empowered ("dark") pieces

        /// <summary>0 = a normal piece; 1–3 = an empowered piece wrapped in a black aura, stronger at every tier.</summary>
        public int EliteTier { get; private set; }

        // Per tier (index 0 = normal): health, damage, stagger resistance, attack tempo (wind-up/recovery time — barely
        // faster: attack speed grows very little) and move speed.
        static readonly float[] EliteHealth = { 1f, 1.6f, 2.3f, 3.2f };
        static readonly float[] EliteDamage = { 1f, 1.25f, 1.5f, 1.8f };
        static readonly float[] EliteStagger = { 1f, 1.5f, 2f, 2.6f };
        static readonly float[] EliteTempo = { 1f, 0.97f, 0.94f, 0.91f };
        static readonly float[] EliteMove = { 1f, 1.05f, 1.1f, 1.15f };

        float damageMult = 1f, tempo = 1f;
        GameObject aura;

        /// <summary>
        /// Makes this piece an empowered piece of the given tier (0 leaves it normal) and scales its health by
        /// <paramref name="healthMult"/> on top (the endless mode's every-40-rounds boost). Call right after spawning.
        /// </summary>
        public void Empower(int tier, float healthMult = 1f)
        {
            int t = Mathf.Clamp(tier, 0, 3);
            EliteTier = t;
            MaxHealth *= EliteHealth[t] * healthMult;
            Health = MaxHealth;
            damageMult = EliteDamage[t];
            tempo = EliteTempo[t];
            moveSpeed *= EliteMove[t];
            maxPoise *= EliteStagger[t];
            poise = maxPoise;
            if (stagger != null)
            {
                stagger.Profile.maxGauge *= EliteStagger[t];
                stagger.Profile.decayPerSecond *= EliteStagger[t];
            }
            if (t == 0) return;
            DisplayName = $"{DisplayName}  ·  Dark {new string('I', t)}";
            aura = EliteAura.Attach(model, Type, t);
        }

        protected virtual void Update()
        {
            float dt = Time.deltaTime;
            stateTime += dt;

            if (state == EState.Dead) { AnimateDeath(); return; }
            if (state == EState.Spawning) { AnimateSpawn(); return; }
            if (Game == null || Game.State != GameManager.GameState.Playing || Player == null) return;

            cooldown -= dt;
            moving = false;
            modelY = 0f;
            squash = 1f;
            modelTilt = Quaternion.identity;
            vulnWobble = Quaternion.identity;

            if (stagger != null)
            {
                stagger.Tick(dt, state == EState.Airborne || state == EState.Getup);
                if (stagger.Vulnerable && state != EState.Airborne) AnimateVulnerable(dt);
            }
            else if (poiseRegenTimer > 0f) poiseRegenTimer -= dt;
            else poise = Mathf.Min(maxPoise, poise + maxPoise * 0.5f * dt);

            switch (state)
            {
                case EState.Move: Think(dt); break;
                case EState.Windup: UpdateWindup(); break;
                case EState.Attack: UpdateAttack(); break;
                case EState.Recover:
                    modelTilt = Quaternion.Euler(Mathf.Lerp(12f, 0f, stateTime / Mathf.Max(0.01f, plan.recover)), 0f, 0f);
                    if (stateTime >= plan.recover)
                    {
                        SetGlow(Color.black);
                        if (!ContinueCombo()) Enter(EState.Move);
                    }
                    break;
                case EState.Grabbed:
                    modelY = grabLift;
                    modelTilt = Quaternion.Euler(grabTilt, 0f, 0f);
                    break;
                case EState.Launched:
                    UpdateLaunched(dt);
                    break;
                case EState.Stagger:
                    transform.position += flinchPush * dt;
                    flinchPush = Vector3.Lerp(flinchPush, Vector3.zero, 1f - Mathf.Exp(-10f * dt));
                    modelTilt = FallTilt(20f * Mathf.Sin(Mathf.Clamp01(stateTime / currentStagger) * Mathf.PI));
                    if (stateTime >= currentStagger)
                    {
                        Enter(EState.Move);
                        cooldown = Mathf.Max(cooldown, 0.5f);
                    }
                    break;
                case EState.Airborne:
                    UpdateAirborne(dt);
                    break;
                case EState.Getup:
                    UpdateGetup();
                    break;
            }

            if (moving && !wasMoving) Sfx.Play("EN_Move_Slide", transform.position);
            wasMoving = moving;
            if (moving)
            {
                hopPhase += dt * 9f;
                modelY = Mathf.Max(modelY, Mathf.Abs(Mathf.Sin(hopPhase)) * 0.14f);
            }

            UpdateReel(dt);
            modelTilt *= FallTilt(reelPitch) * Quaternion.Euler(0f, reelYaw, 0f);
            // A tilted piece pivots on its base rim: lift it so it never sinks into the board.
            float tilt = Quaternion.Angle(Quaternion.identity, modelTilt);
            modelY += baseRadius * Mathf.Sin(Mathf.Min(tilt, 90f) * Mathf.Deg2Rad);

            model.localPosition = new Vector3(0f, modelY, 0f);
            model.localRotation = modelTilt * vulnWobble;
            model.localScale = new Vector3(modelBaseScale.x / Mathf.Sqrt(squash), modelBaseScale.y * squash, modelBaseScale.z / Mathf.Sqrt(squash));
            ClampToBoard();
        }

        protected void Enter(EState s)
        {
            state = s;
            stateTime = 0f;
        }

        // ---------------------------------------------------------------- helpers

        protected Vector3 ToPlayer => Player.FlatPos - FlatPos;
        protected float DistToPlayer => ToPlayer.magnitude;

        /// <summary>Limits how many pieces press the attack at once (the boss ignores this).</summary>
        // Fair turns: among the pieces ready to strike, the one that has waited longest goes first.
        float readySince = float.MaxValue;
        int readyFrame = -10;
        /// <summary>When this piece started waiting for its turn to strike (∞ when it isn't waiting right now).</summary>
        public float ReadySince => Time.frameCount - readyFrame <= 1 ? readySince : float.MaxValue;

        /// <summary>
        /// Ready to attack: off cooldown, and (the boss excepted) fewer than two pieces already attacking and no other
        /// piece has been waiting longer. Ask only when actually in position — asking starts the wait.
        /// </summary>
        protected bool CanAttackNow
        {
            get
            {
                if (cooldown > 0f) { readyFrame = -10; return false; }
                if (Time.frameCount - readyFrame > 1) readySince = Time.time;
                readyFrame = Time.frameCount;
                return IsBoss || (Game.ActiveAttackers(this) < 2 && Game.IsNextInLine(this));
            }
        }

        protected void MoveTowards(Vector3 target, float speed, float dt)
        {
            target.y = 0f;
            Vector3 d = target - FlatPos;
            float dist = d.magnitude;
            if (dist < 0.05f) return;
            float step = Mathf.Min(dist, speed * dt);
            transform.position += d / dist * step;
            moving = true;
        }

        /// <summary>Dash length meaning "no range limit": BeginAttack cuts it at the board edge.</summary>
        protected const float FullLine = 100f;

        protected static float DistanceToEdge(Vector3 from, Vector3 dir)
        {
            float h = GameManager.PlayableHalf;
            float t = 100f;
            if (Mathf.Abs(dir.x) > 1e-4f) t = Mathf.Min(t, ((dir.x > 0f ? h : -h) - from.x) / dir.x);
            if (Mathf.Abs(dir.z) > 1e-4f) t = Mathf.Min(t, ((dir.z > 0f ? h : -h) - from.z) / dir.z);
            return Mathf.Max(0f, t);
        }

        protected static Vector3 ClampPoint(Vector3 p)
        {
            float h = GameManager.PlayableHalf;
            return new Vector3(Mathf.Clamp(p.x, -h, h), 0f, Mathf.Clamp(p.z, -h, h));
        }

        // ---------------------------------------------------------------- board slots (see BoardSlots)

        /// <summary>How a piece travels to its square: straight, along ranks/files (rook), or along diagonals (bishop).</summary>
        protected enum Gait { Direct, Orthogonal, Diagonal }

        int slotVersion = -1;
        float slotRecheck;

        protected bool HasSlot => BoardSlots.TryGet(this, out _);
        /// <summary>True when the reserved square is one the piece attacks from (false: a waiting square).</summary>
        protected bool SlotIsAttack { get; private set; }
        protected Vector2Int SlotSquare => BoardSlots.TryGet(this, out var s) ? s : MySquare;
        protected Vector2Int MySquare => BoardSlots.ToSquare(FlatPos);
        protected Vector2Int KingSquare => BoardSlots.KingSquare;
        protected bool AtSlot => HasSlot && (BoardSlots.Center(SlotSquare) - FlatPos).magnitude < 0.25f;
        protected bool ReadyAtAttackSlot => AtSlot && SlotIsAttack;
        /// <summary>Holds a square it attacks from (for the slot view).</summary>
        public bool HoldsAttackSlot => HasSlot && SlotIsAttack;

        /// <summary>
        /// Keeps this piece's reserved square: the nearest free square its <paramref name="pattern"/> (offsets from the
        /// King's square) attacks from — taking it from a lower-priority piece if need be — or, when those are all
        /// taken, a waiting square 2–3 squares out, re-checked
        /// often so a freed slot is taken over. <paramref name="penalty"/> adds a cost per offset.
        /// </summary>
        protected void UpdateSlot(Vector2Int[] pattern, System.Func<Vector2Int, float> penalty = null)
        {
            slotRecheck -= Time.deltaTime;
            bool waiting = HasSlot && !SlotIsAttack;
            if (slotVersion == BoardSlots.Version && HasSlot && !(waiting && slotRecheck <= 0f)) return;
            slotVersion = BoardSlots.Version;
            slotRecheck = 0.3f;
            var king = KingSquare;

            // Keep the current attack square while it still is one.
            if (HasSlot && SlotIsAttack && System.Array.IndexOf(pattern, SlotSquare - king) >= 0) return;

            Vector2Int best = default;
            float bestCost = float.MaxValue;
            foreach (var o in pattern)
            {
                var s = king + o;
                if (!BoardSlots.OnBoard(s) || !BoardSlots.CanTake(s, this, out bool bumps)) continue;
                float cost = (BoardSlots.Center(s) - FlatPos).magnitude + (penalty != null ? penalty(o) : 0f) + (bumps ? 1.5f : 0f);
                if (cost < bestCost) { bestCost = cost; best = s; }
            }
            if (bestCost < float.MaxValue)
            {
                BoardSlots.Reserve(this, best);
                SlotIsAttack = true;
                return;
            }

            // Every attack square is taken: wait close by (2–3 squares out), as near to where we are as possible.
            for (int x = 0; x < BoardSlots.Size; x++)
            for (int y = 0; y < BoardSlots.Size; y++)
            {
                var s = new Vector2Int(x, y);
                int ring = BoardSlots.Chebyshev(s, king);
                if (ring < 2 || ring > 3 || !BoardSlots.IsFreeFor(s, this)) continue;
                float cost = (BoardSlots.Center(s) - FlatPos).magnitude;
                if (cost < bestCost) { bestCost = cost; best = s; }
            }
            if (bestCost < float.MaxValue) BoardSlots.Reserve(this, best);
            else BoardSlots.Release(this);
            SlotIsAttack = false;
        }

        /// <summary>Travels toward the reserved square in this piece's gait.</summary>
        protected void MoveToSlot(Gait gait, float speed, float dt)
        {
            if (!HasSlot) return;
            Vector3 target = BoardSlots.Center(SlotSquare);
            Vector3 d = target - FlatPos;
            bool both = Mathf.Abs(d.x) > 0.05f && Mathf.Abs(d.z) > 0.05f;
            if (gait == Gait.Orthogonal && both)
                target = FlatPos + new Vector3(d.x, 0f, 0f);                       // along the rank first, then the file
            else if (gait == Gait.Diagonal && both)
            {
                float step = Mathf.Min(Mathf.Abs(d.x), Mathf.Abs(d.z));
                target = FlatPos + new Vector3(Mathf.Sign(d.x) * step, 0f, Mathf.Sign(d.z) * step);   // along the diagonal
            }
            else if (gait == Gait.Direct && DetourAroundKing) target = AroundKing(target);
            if (DetourAroundKing) target = AroundPieces(target);
            MoveTowards(target, speed, dt);
        }

        /// <summary>
        /// Another piece standing in the way: step around it on the side away from the King instead of shoving into
        /// it (and getting pushed back). Once past it, the piece heads for its target again.
        /// </summary>
        Vector3 AroundPieces(Vector3 target)
        {
            Vector3 pos = FlatPos, to = target - pos;
            float len = to.magnitude;
            if (len < 0.05f || Game == null) return target;
            Vector3 dir = to / len;
            EnemyBase block = null;
            float nearest = float.MaxValue;
            foreach (var o in Game.Enemies)
            {
                if (o == null || o == this || o.IsDead || o.IgnoresCollision) continue;
                Vector3 rel = o.FlatPos - pos;
                float along = Vector3.Dot(rel, dir);
                if (along <= 0.05f || along > Mathf.Min(len + 0.3f, 2.5f)) continue;
                float across = Mathf.Abs(Vector3.Cross(dir, rel).y);
                if (across > Radius + o.Radius + 0.25f) continue;
                if (along < nearest) { nearest = along; block = o; }
            }
            if (block == null) return target;

            Vector3 side = Vector3.Cross(Vector3.up, dir);
            float clear = Radius + block.Radius + 0.45f;
            Vector3 a = block.FlatPos + side * clear, b = block.FlatPos - side * clear;
            Vector3 king = Player != null ? Player.FlatPos : pos;
            return (a - king).sqrMagnitude >= (b - king).sqrMagnitude ? a : b;   // the side farther from the King
        }

        /// <summary>
        /// Walking to a square on the far side of the King: instead of cutting straight through him, curve around him
        /// (keeping about <see cref="KingClearance"/> away) on the side the target lies on.
        /// </summary>
        Vector3 AroundKing(Vector3 target)
        {
            if (Player == null) return target;
            Vector3 k = Player.FlatPos, pos = FlatPos, to = target - pos;
            float len2 = to.sqrMagnitude;
            if (len2 < 0.01f) return target;
            float t = Mathf.Clamp01(Vector3.Dot(k - pos, to) / len2);
            Vector3 closest = pos + to * t;
            if (t <= 0.02f || t >= 0.98f || (closest - k).magnitude >= KingClearance) return target;

            Vector3 radial = pos - k;
            radial.y = 0f;
            if (radial.sqrMagnitude < 0.01f) radial = -to;
            Vector3 r = radial.normalized;
            // Go round on the side the target is on.
            float side = Mathf.Sign(Vector3.Cross(r, target - k).y);
            if (side == 0f) side = 1f;
            Vector3 tangent = Vector3.Cross(Vector3.up, r) * side;
            // Hold the orbit radius: drift out when too close, in when too far.
            float push = Mathf.Clamp((KingClearance + 0.2f - radial.magnitude) * 1.5f, -0.6f, 1.2f);
            return pos + (tangent + r * push).normalized * 1.5f;
        }

        const float KingClearance = 1.7f;
        /// <summary>Lab switch for A/B checks of the detour (on in the game).</summary>
        public static bool DetourAroundKing = true;

        // ---------------------------------------------------------------- attacks

        protected void BeginAttack(AttackPlan p)
        {
            shoved.Clear();
            readyFrame = -10;
            ClearTelegraph();
            plan = p;
            // Empowered pieces hit harder and strike a touch sooner.
            plan.damage *= damageMult;
            plan.windup *= tempo;
            plan.recover *= tempo;
            atkStart = FlatPos;
            atkHit = false;
            landed = false;
            plan.dir.y = 0f;
            if (plan.dir.sqrMagnitude < 0.001f) plan.dir = transform.forward;
            plan.dir.Normalize();

            switch (plan.kind)
            {
                case AtkKind.Lunge:
                case AtkKind.Dash:
                    plan.length = Mathf.Min(plan.length, DistanceToEdge(atkStart, plan.dir));
                    if (plan.kind == AtkKind.Dash) plan.active = Mathf.Max(0.12f, plan.length / Mathf.Max(1f, plan.speed));
                    // Pawns keep the classic strip; the line pieces light the board squares along their charge.
                    telegraph = Type == PieceType.Pawn
                        ? Telegraph.Strip(atkStart, plan.dir, plan.length + plan.radius, plan.radius * 2f, plan.windup)
                        : Telegraph.Line(atkStart, plan.dir, plan.length + plan.radius, plan.radius * 2f, plan.windup);
                    break;
                case AtkKind.Leap:
                    plan.target = ClampPoint(plan.target);
                    // Knights show their L route and a ghost on the landing square; the queen just the landing area.
                    telegraph = Type == PieceType.Knight
                        ? Telegraph.Leap(atkStart, plan.target, plan.windup, model)
                        : Telegraph.Area(plan.target, plan.radius, plan.windup);
                    break;
                case AtkKind.Slam:
                    telegraph = Telegraph.Area(atkStart, plan.radius, plan.windup, atkStart);
                    break;
            }
            Enter(EState.Windup);
            Sfx.Play("EN_Windup", transform.position);
            Sfx.Play("EN_Telegraph", plan.kind == AtkKind.Leap ? plan.target : atkStart);
            if (Type == PieceType.Queen && plan.kind == AtkKind.Slam) Sfx.Play("QUEEN_Check_Windup", transform.position);
        }

        // Sound ids (Docs/Sound/sounds.json) for this piece's attack release and landing.
        string ReleaseSfx => (Type, plan.kind) switch
        {
            (PieceType.Pawn, _) => "PAWN_Thrust",
            (PieceType.Knight, AtkKind.Leap) => "KNIGHT_Leap",
            (PieceType.Bishop, _) => "BISHOP_Charge",
            (PieceType.Rook, AtkKind.Dash) => "ROOK_Ram",
            (PieceType.Queen, AtkKind.Dash) => "QUEEN_Dash",
            (PieceType.Queen, AtkKind.Leap) => "QUEEN_Leap",
            _ => null,
        };

        string LandSfx => (Type, plan.kind) switch
        {
            (PieceType.Knight, AtkKind.Leap) => "KNIGHT_Land",
            (PieceType.Queen, AtkKind.Leap) => "QUEEN_Land",
            (PieceType.Rook, AtkKind.Slam) => "ROOK_Slam",
            (PieceType.Queen, AtkKind.Slam) => "QUEEN_Check_Slam",
            _ => null,
        };

        void UpdateWindup()
        {
            float p = Mathf.Clamp01(stateTime / plan.windup);

            if (plan.kind == AtkKind.Lunge && p < plan.trackUntil)
            {
                Vector3 to = ToPlayer;
                if (to.sqrMagnitude > 0.01f) plan.dir = to.normalized;
                telegraph.SetLine(atkStart, plan.dir, plan.length + plan.radius, plan.radius * 2f);
            }

            Vector3 faceDir = plan.kind == AtkKind.Leap ? plan.target - FlatPos : plan.dir;
            if (plan.kind != AtkKind.Slam) FaceTowards(faceDir, 720f);
            else FaceTowards(ToPlayer, 200f);

            if (telegraph != null) telegraph.SetProgress(p);

            // Ramp a red glow, then a bright "glint" right before release.
            SetGlow(p > 0.82f ? WindupGlow * 1.8f : WindupGlow * (p * 0.7f));

            switch (plan.kind)
            {
                case AtkKind.Slam:
                    modelY = Mathf.Sin(p * Mathf.PI * 0.5f) * 1.2f;
                    break;
                case AtkKind.Leap:
                    squash = 1f - 0.18f * p;
                    break;
                default:
                    modelTilt = Quaternion.Euler(-14f * p, 0f, 0f);
                    squash = 1f - 0.08f * p;
                    break;
            }

            if (p >= 1f)
            {
                ClearTelegraph();
                Enter(EState.Attack);
                // The piece leaves its square (a slam stays put): free the slot for the others.
                if (plan.kind != AtkKind.Slam) BoardSlots.Release(this);
                Sfx.Play(ReleaseSfx, transform.position);
            }
        }

        void UpdateAttack()
        {
            float t = stateTime;
            float p = Mathf.Clamp01(t / plan.active);

            switch (plan.kind)
            {
                case AtkKind.Lunge:
                case AtkKind.Dash:
                {
                    float e = plan.kind == AtkKind.Lunge ? 1f - (1f - p) * (1f - p) : p;
                    transform.position = atkStart + plan.dir * (plan.length * e);
                    modelTilt = Quaternion.Euler(16f, 0f, 0f);
                    TryHitPlayer(FlatPos, plan.radius);
                    if (Type == PieceType.Rook && plan.kind == AtkKind.Dash) ShovePawns();
                    if (plan.kind == AtkKind.Dash && Random.value < 0.5f)
                        Effects.Sparks(transform.position + Vector3.up * 0.1f, new Color(0.6f, 0.1f, 0.05f), 1, 2f);
                    if (p >= 1f) FinishAttack();
                    break;
                }
                case AtkKind.Leap:
                    if (t < plan.active)
                    {
                        transform.position = Vector3.Lerp(atkStart, plan.target, p);
                        modelY = Mathf.Sin(p * Mathf.PI) * 2.8f;
                        modelTilt = Quaternion.Euler(Mathf.Lerp(-25f, 25f, p), 0f, 0f);
                    }
                    else
                    {
                        if (!landed)
                        {
                            landed = true;
                            transform.position = plan.target;
                            Sfx.Play(LandSfx, plan.target);
                            Vfx.GroundImpact(plan.target, plan.radius, 0.9f, hostile: true);
                            Game.Rig.Shake(0.25f);
                        }
                        squash = 0.8f;
                        TryHitPlayer(plan.target, plan.radius);
                        if (t >= plan.active + 0.08f) FinishAttack();
                    }
                    break;
                case AtkKind.Slam:
                    modelY = Mathf.Lerp(1.2f, 0f, p * p);
                    if (p >= 1f)
                    {
                        if (!landed)
                        {
                            landed = true;
                            Vfx.GroundImpact(atkStart, plan.radius, 1.1f, hostile: true);
                            Sfx.Play(LandSfx, atkStart);
                            Game.Rig.Shake(0.4f);
                        }
                        squash = 0.75f;
                        TryHitPlayer(atkStart, plan.radius);
                        if (t >= plan.active + 0.1f) FinishAttack();
                    }
                    break;
            }
        }

        void FinishAttack()
        {
            // A rook ram that ran all the way into the board's edge.
            if (Type == PieceType.Rook && plan.kind == AtkKind.Dash && DistanceToEdge(FlatPos, plan.dir) < 0.5f)
                Sfx.Play("ROOK_WallHit", transform.position);
            SetGlow(Color.black);
            Enter(EState.Recover);
        }

        /// <summary>The knight lands on the King's square and strikes it and the four squares beside it (a cross).</summary>
        bool StrikesCross => Type == PieceType.Knight && plan.kind == AtkKind.Leap;

        /// <summary>Distance from an offset (from the landing square's centre) to the cross of five squares (0 inside).</summary>
        static float CrossGap(Vector3 d)
        {
            float t = GameManager.TileSize;
            float Box(float hx, float hz) => new Vector2(Mathf.Max(Mathf.Abs(d.x) - hx, 0f), Mathf.Max(Mathf.Abs(d.z) - hz, 0f)).magnitude;
            return Mathf.Min(Box(1.5f * t, 0.5f * t), Box(0.5f * t, 1.5f * t));
        }

        void TryHitPlayer(Vector3 center, float radius)
        {
            if (atkHit || Player == null || Player.IsDead) return;
            Vector3 d = Player.FlatPos - new Vector3(center.x, 0f, center.z);
            // Gap between the King's centre and the strike's edge (the knight's landing strikes a cross of squares).
            float gap = StrikesCross ? CrossGap(d) : d.magnitude - radius;
            if (gap <= Player.Radius) atkHit = Player.TakeHitFrom(this, plan.damage, plan.poise);
            // A near miss still counts for a perfect dodge (rolling out of the hit at the last moment).
            else if (gap <= Player.Radius + PlayerController.NearMissMargin) Player.NotifyNearMiss(this);
        }

        void ClearTelegraph()
        {
            if (telegraph != null) Destroy(telegraph.gameObject);
            telegraph = null;
        }

        // ---------------------------------------------------------------- grab / throw (King's Royal Throw)

        float grabTilt, grabLift, launchHeight, launchVy, launchSpin, launchDamage;
        Vector3 launchVelocity;
        bool wallHit;
        readonly System.Collections.Generic.HashSet<EnemyBase> launchHits = new System.Collections.Generic.HashSet<EnemyBase>();

        public bool CanBeGrabbed => !IsDead && state != EState.Spawning && state != EState.Launched;

        public void BeginGrab()
        {
            BoardSlots.Release(this);
            ClearTelegraph();
            SetGlow(Color.black);
            grabTilt = 0f;
            grabLift = 0f;
            Enter(EState.Grabbed);
        }

        /// <summary>Called every frame by the King while swinging this piece around.</summary>
        public void SetGrabbedPose(Vector3 position, Vector3 outward, float tilt, float lift)
        {
            transform.position = new Vector3(position.x, 0f, position.z);
            outward.y = 0f;
            if (outward.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(outward);
            grabTilt = tilt;
            grabLift = lift;
        }

        /// <summary>Throws the piece: it flies, bowls over other pieces, slams walls and takes damage on landing.</summary>
        public void Launch(Vector3 direction, float speed, float upSpeed, float landingDamage)
        {
            direction.y = 0f;
            launchVelocity = direction.normalized * speed;
            launchHeight = Mathf.Max(grabLift, 0.3f);
            launchVy = upSpeed;
            launchDamage = landingDamage;
            launchSpin = grabTilt;
            wallHit = false;
            launchHits.Clear();
            launchHits.Add(this);
            if (launchVelocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(launchVelocity);
            Enter(EState.Launched);
        }

        void UpdateLaunched(float dt)
        {
            Vector3 p = transform.position + launchVelocity * dt;
            float h = GameManager.PlayableHalf;
            if (!wallHit && (Mathf.Abs(p.x) > h || Mathf.Abs(p.z) > h))
            {
                // Slam into the board's edge.
                wallHit = true;
                launchVelocity = Vector3.zero;
                SquareRingFx.Spawn(p, 1.6f, Vfx.Accent, 0.35f, 45f);
                Effects.Sparks(p + Vector3.up * launchHeight, Vfx.Accent, 16, 6f);
                Game.Rig.Shake(0.35f);
            }
            transform.position = p;

            launchHeight += launchVy * dt;
            launchVy -= 24f * dt;
            launchSpin += 900f * dt;
            modelY = Mathf.Max(0f, launchHeight);
            modelTilt = Quaternion.Euler(launchSpin, 0f, 0f);

            // Bowl over any piece in the flight path.
            foreach (var e in Game.Enemies)
            {
                if (e == null || e.IsDead || launchHits.Contains(e)) continue;
                if ((e.FlatPos - FlatPos).magnitude > Radius + e.Radius + 0.4f) continue;
                launchHits.Add(e);
                if (e.TakeHit(22f, 45f, transform.position))
                {
                    Game.ReportDamage(e, 22f);
                    Sfx.Play("HIT_Heavy", e.transform.position + Vector3.up);
                    Effects.Sparks(e.transform.position + Vector3.up, Vfx.Accent, 12, 6f);
                    Game.Rig.Shake(0.25f);
                }
            }

            if (launchHeight <= 0f && launchVy < 0f)
            {
                modelY = 0f;
                Vfx.GroundImpact(FlatPos, 2f, 1f);
                AimFallAwayFromKing();
                Sfx.Play("THR_Land", transform.position);
                Game.Rig.Shake(0.4f);
                // Crash down and lie on the board before getting up (damage only, no stagger reaction).
                Enter(EState.Getup);
                if (TakeHit(launchDamage, 0f, transform.position - launchVelocity)) Game.ReportDamage(this, launchDamage);
            }
        }

        // ---------------------------------------------------------------- damage / death

        public override bool TakeHit(float damage, float poiseDamage, Vector3 source)
        {
            if (IsDead || state == EState.Spawning) return false;

            hitFrame = Time.frameCount;
            Health -= damage;
            LastHitTime = Time.time;
            Flash(HitFlash, 0.1f);
            lastHitDamage = damage;

            if (Health <= 0f)
            {
                Health = 0f;
                Die(source);
                return true;
            }

            // Being swung, thrown or getting up: damage only, no reaction.
            if (state == EState.Grabbed || state == EState.Launched || state == EState.Getup) return true;

            // Airborne (or about to be popped up): a big snap-back and twist; on the ground a smaller one.
            Reel(state == EState.Airborne || (stagger != null && stagger.Vulnerable) ? 1f : 0.55f);

            if (stagger != null)
            {
                // Super armor from wind-up through the end of the hit; recovery is unarmored (punish window).
                bool superArmor = state == EState.Windup || state == EState.Attack;
                var reaction = stagger.OnHit(poiseDamage, superArmor, state == EState.Airborne);
                if (stagger.JustBecameVulnerable) VulnerableBurst();
                else if (superArmor && reaction == StaggerController.Reaction.None) Sfx.Play("HIT_Armor", transform.position + Vector3.up);
                if (reaction == StaggerController.Reaction.Launch) LaunchUp(source);
                else if (reaction == StaggerController.Reaction.Flinch) Flinch(source, stagger.Profile.flinchDuration);
                return true;
            }

            // Bosses: classic poise (separate from the stagger system).
            poise -= poiseDamage;
            poiseRegenTimer = 2f;
            bool armored = hyperArmor && (state == EState.Attack || state == EState.Windup);
            if (poise <= 0f && !armored)
            {
                poise = maxPoise;
                Flinch(source, staggerDuration);
                Effects.Sparks(transform.position + Vector3.up * 1.5f, new Color(1f, 0.9f, 0.4f), 8, 3f);
            }
            else if (state == EState.Move)
            {
                Vector3 away = FlatPos - new Vector3(source.x, 0f, source.z);
                if (away.sqrMagnitude > 0.001f) transform.position += away.normalized * 0.25f;
            }
            return true;
        }

        // ---------------------------------------------------------------- stagger reactions

        static readonly Color VulnerableColor = new Color(1f, 0.98f, 0.94f);
        const float AirGravity = 22f, GetupLie = 0.45f, GetupRise = 0.4f, ForcedLaunchLie = 0.9f;
        // Launch / juggle tuning: a Vulnerable hit pops the piece a little, the heavy launcher sends it high, and air hits
        // keep it up with shrinking boosts (capped) plus a brief hover so the combo can follow.
        const float VulnerablePopScale = 0.6f, ForcedLaunchScale = 1.15f;
        const float JuggleLift = 4.6f, JuggleFalloff = 0.85f, JuggleCeiling = 2.6f, JuggleHover = 0.12f;
        const int MaxJuggleHits = 7;
        float airHover;  // seconds of reduced gravity after a juggle hit
        float bonusLie;  // extra time lying down after a forced launch

        /// <summary>Cancels whatever the piece was doing (attack, telegraph, glow) — frees its attack token too.</summary>
        void CancelAction()
        {
            ClearTelegraph();
            SetGlow(Color.black);
        }

        void Flinch(Vector3 source, float duration)
        {
            AimFallAwayFromKing();
            Sfx.Play(IsBoss ? "QUEEN_Stagger" : "STG_Flinch", transform.position);
            CancelAction();
            currentStagger = duration;
            Vector3 away = FlatPos - new Vector3(source.x, 0f, source.z);
            flinchPush = away.sqrMagnitude > 0.001f ? away.normalized * 2.5f : Vector3.zero;
            Enter(EState.Stagger);
        }

        /// <summary>
        /// Heavy launcher: pops a non-boss piece into the air whatever its gauge or armor, and keeps it down a while longer
        /// after it lands (neutralized). Bosses shrug it off.
        /// </summary>
        public void ForceLaunch(Vector3 source)
        {
            if (stagger == null || IsDead) return;
            if (state == EState.Spawning || state == EState.Grabbed || state == EState.Launched) return;
            bonusLie = ForcedLaunchLie;
            if (state != EState.Airborne) LaunchUp(source, true);
        }

        /// <summary>
        /// Knock the piece into the air. A Vulnerable piece only pops up a little; the heavy launcher sends it high.
        /// Hits while airborne juggle it: each one gives a small, shrinking lift (and a gentle hover at the top) so the
        /// piece stays up a bit longer, up to <see cref="MaxJuggleHits"/> boosts — after that it just falls.
        /// </summary>
        void LaunchUp(Vector3 source, bool forced = false)
        {
            AimFallAwayFromKing();
            CancelAction();
            bool alreadyAirborne = state == EState.Airborne;
            Vector3 away = FlatPos - new Vector3(source.x, 0f, source.z);
            if (!alreadyAirborne)
            {
                airHits = 0;
                Sfx.Play(forced ? "STG_Launch" : "STG_Pop", transform.position);
                airHeight = 0f;
                airHover = 0f;
                airVy = stagger.Profile.launchForce * (forced ? ForcedLaunchScale : VulnerablePopScale);
                airKnock = away.sqrMagnitude > 0.001f ? away.normalized * (forced ? 2.2f : 1.4f) : Vector3.zero;
            }
            else if (airHits < MaxJuggleHits)
            {
                Sfx.Play("STG_Juggle", transform.position + Vector3.up * (airHeight + 0.8f));
                // Juggle: a small boost that shrinks per hit and lifts less the higher the piece already is.
                float height = Mathf.Clamp01(1f - airHeight / JuggleCeiling);
                float boost = JuggleLift * Mathf.Pow(JuggleFalloff, airHits) * (0.35f + 0.65f * height);
                airVy = Mathf.Max(airVy, boost);
                airHover = JuggleHover;
                airKnock = away.sqrMagnitude > 0.001f ? away.normalized * 0.5f : Vector3.zero;  // stay in reach
            }
            airHits++;
            Enter(EState.Airborne);
            Effects.Sparks(transform.position + Vector3.up * (airHeight + 1f), VulnerableColor, 10, 5f);
            if (!alreadyAirborne)
            {
                GroundDiamond(forced ? 1.2f : 0.8f, 0.3f);
                // A blow already plays its own hit effect: the launch slash only marks launches without one.
                if (!StruckThisFrame)
                    Vfx.Slash(SlashFxKind.SlashBVertical, transform.position + Vector3.up * 0.8f, Quaternion.LookRotation(airKnock.sqrMagnitude > 0.01f ? airKnock : transform.forward),
                        forced ? 1.1f : 0.75f, Vfx.Accent);
            }
        }

        void UpdateAirborne(float dt)
        {
            airHeight += airVy * dt;
            // Brief hover after a juggle hit: gravity eases off so the piece hangs at the top of its pop.
            float gravity = airHover > 0f ? AirGravity * 0.35f : AirGravity;
            airHover -= dt;
            airVy -= gravity * dt;
            transform.position += airKnock * dt;
            airKnock = Vector3.Lerp(airKnock, Vector3.zero, 1f - Mathf.Exp(-2f * dt));
            modelY = Mathf.Max(0f, airHeight);
            // Tumble backwards while in the air.
            modelTilt = FallTilt(Mathf.Clamp(25f + 40f * airHits - airVy * 4f, 0f, 110f));

            if (airHeight <= 0f && airVy < 0f)
            {
                modelY = 0f;
                GroundDiamond(1.4f, 0.35f);
                Sfx.Play("STG_AirLand", transform.position);
                Effects.Sparks(transform.position + Vector3.up * 0.2f, new Color(0.6f, 0.55f, 0.5f), 8, 3f);
                Enter(EState.Getup);
            }
        }

        // Falls, reels and knock-downs always tip the piece away from the King: tilting about this axis (in the piece's
        // own space, set when it is struck) moves the top away from him.
        Vector3 fallAxis = Vector3.left;

        void AimFallAwayFromKing()
        {
            Vector3 away = Player != null ? FlatPos - Player.FlatPos : -transform.forward;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
            Vector3 local = transform.InverseTransformDirection(away.normalized);
            local.y = 0f;
            if (local.sqrMagnitude < 0.0001f) local = Vector3.back;
            fallAxis = Vector3.Cross(Vector3.up, local.normalized);
        }

        /// <summary>Tilts the top <paramref name="degrees"/> away from the King (see <see cref="AimFallAwayFromKing"/>).</summary>
        Quaternion FallTilt(float degrees) => Quaternion.AngleAxis(degrees, fallAxis);

        // One hit, one set of effects: a heavy that also breaks the guard and launches would otherwise stack them.
        int hitFrame = -1, diamondFrame = -1;
        bool StruckThisFrame => hitFrame == Time.frameCount;

        /// <summary>Expanding hollow white diamond on the board under the piece (at most one per frame).</summary>
        void GroundDiamond(float radius, float duration)
        {
            if (diamondFrame == Time.frameCount) return;
            diamondFrame = Time.frameCount;
            SquareRingFx.Spawn(FlatPos, radius, VulnerableColor, duration, 45f);
        }

        /// <summary>Kicks the hit reel: the top snaps back away from the King and twists (stronger in the air).</summary>
        void Reel(float strength)
        {
            AimFallAwayFromKing();
            reelPitchVel += 950f * strength;
            reelYawVel += Random.Range(-1f, 1f) * 800f * strength;
        }

        /// <summary>Damped springs that bring the reel back upright.</summary>
        void UpdateReel(float dt)
        {
            const float Stiffness = 260f, Damping = 19f;
            dt = Mathf.Min(dt, 0.05f);
            reelPitchVel += (-Stiffness * reelPitch - Damping * reelPitchVel) * dt;
            reelPitch += reelPitchVel * dt;
            reelYawVel += (-Stiffness * reelYaw - Damping * reelYawVel) * dt;
            reelYaw += reelYawVel * dt;
        }

        /// <summary>
        /// A rook's ram shoulders pawns out of its way: any pawn just ahead on the line is knocked aside (to the side
        /// it stands on) and the rook keeps charging.
        /// </summary>
        void ShovePawns()
        {
            Vector3 side = Vector3.Cross(Vector3.up, plan.dir);
            foreach (var e in Game.Enemies)
            {
                if (e == null || e == this || e.GetType() != typeof(PawnEnemy) || shoved.Contains(e)) continue;
                Vector3 rel = e.FlatPos - FlatPos;
                float along = Vector3.Dot(rel, plan.dir), across = Vector3.Dot(rel, side);
                if (along < -0.3f || along > Radius + e.Radius + 1.2f || Mathf.Abs(across) > plan.radius + e.Radius + 0.3f) continue;
                shoved.Add(e);
                float s = Mathf.Abs(across) > 0.05f ? Mathf.Sign(across) : (Random.value < 0.5f ? -1f : 1f);
                e.Shove(side * (s * 16f) + plan.dir * 3f);
            }
        }

        /// <summary>Knocked aside (by a charging rook): cancels what it was doing and slides off with a stagger.</summary>
        public void Shove(Vector3 push)
        {
            if (IsDead || state == EState.Spawning || state == EState.Grabbed || state == EState.Launched ||
                state == EState.Airborne || state == EState.Getup) return;
            CancelAction();
            BoardSlots.Release(this);
            AimFallAwayFromKing();
            flinchPush = push;
            currentStagger = 0.45f;
            Enter(EState.Stagger);
            Reel(0.8f);
            Effects.Sparks(transform.position + Vector3.up * 0.6f, new Color(0.6f, 0.55f, 0.5f), 8, 4f);
            Sfx.Play("STG_Flinch", transform.position);
        }

        /// <summary>
        /// Lie on the board, then spring back up like a jump (the piece pops off the floor while it swings upright and
        /// lands with a squash). Getting up resets the gauge.
        /// </summary>
        void UpdateGetup()
        {
            float t = stateTime;
            float lieTime = GetupLie + bonusLie;
            if (t >= lieTime && t - Time.deltaTime < lieTime) Sfx.Play("STG_Getup", transform.position);
            if (t < lieTime)
            {
                modelTilt = FallTilt(85f);
            }
            else
            {
                float p = Mathf.Clamp01((t - lieTime) / GetupRise);
                float upright = PoseTrack.Curve(Ease.Out, Mathf.Clamp01(p / 0.6f));
                modelTilt = FallTilt(85f * (1f - upright));
                modelY = Mathf.Sin(p * Mathf.PI) * 0.75f;
                if (p > 0.8f) squash = Mathf.Lerp(0.8f, 1f, (p - 0.8f) / 0.2f);
            }
            if (t >= lieTime + GetupRise)
            {
                bonusLie = 0f;
                stagger?.ResetGauge();
                airHits = 0;
                Enter(EState.Move);
                cooldown = Mathf.Max(cooldown, 0.6f);
            }
        }

        /// <summary>The gauge just filled: hidden gauge, so make Vulnerable obvious with a burst.</summary>
        void VulnerableBurst()
        {
            Sfx.Play("STG_Vulnerable", transform.position + Vector3.up * modelHeight);
            Sfx.Play("STG_Vulnerable_Loop", transform.position + Vector3.up * modelHeight);
            FlashAfter(VulnerableFlash, 0.2f);   // after the struck flash, so both read
            GroundDiamond(1.6f, 0.35f);
            // Always shown, even on top of the hit effect: it is the guard-break signal.
            Vfx.Burst(SlashFxKind.SlashC, transform.position + Vector3.up * (modelHeight + 0.2f), Quaternion.identity, 0.9f, VulnerableColor, "flare", "spark");
            Effects.Sparks(transform.position + Vector3.up * 2f, VulnerableColor, 16, 4f);
            Game.Rig.Shake(0.15f);
        }

        /// <summary>While Vulnerable: dizzy wobble and white sparks circling the head.</summary>
        void AnimateVulnerable(float dt)
        {
            float w = Time.time * 9f;
            vulnWobble = Quaternion.Euler(Mathf.Sin(w) * 7f, 0f, Mathf.Cos(w) * 7f);
            vulnFxTimer -= dt;
            if (vulnFxTimer <= 0f)
            {
                vulnFxTimer = 0.12f;
                float a = Time.time * 6f;
                Vector3 head = transform.position + Vector3.up * (modelHeight + 0.25f);
                Effects.Sparks(head + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.45f, VulnerableColor, 1, 0.6f);
            }
        }

        void Die(Vector3 source)
        {
            IsDead = true;
            if (aura != null) Destroy(aura);
            BoardSlots.Release(this);
            ClearTelegraph();
            SetGlow(Color.black);
            Vector3 away = FlatPos - new Vector3(source.x, 0f, source.z);
            if (away.sqrMagnitude < 0.001f) away = -transform.forward;
            deathAway = away.normalized;
            crumbled = false;
            Enter(EState.Dead);
            Flash(HitFlash * 1.2f, CrackTime);
            Sfx.Play("EN_Death_Crack", transform.position);
            if (IsBoss) Sfx.Play("QUEEN_Death", transform.position);
            Effects.Sparks(transform.position + Vector3.up, new Color(0.4f, 0.4f, 0.45f), 18, 6f);
        }

        const float CrackTime = 0.14f;
        Vector3 deathAway;
        bool crumbled;

        /// <summary>Captured: the piece cracks (shudder + flash), then collapses into rubble like a castle wall.</summary>
        void AnimateDeath()
        {
            float t = stateTime;
            if (!crumbled)
            {
                if (t < CrackTime)
                {
                    model.localPosition = new Vector3(Random.Range(-0.05f, 0.05f), model.localPosition.y, Random.Range(-0.05f, 0.05f));
                    return;
                }
                crumbled = true;
                // Rubble flies off along the killing blow; harder blows throw it farther. Black inside.
                Crumble.Spawn(model.GetComponentsInChildren<Renderer>(), deathAway, new Color(0.02f, 0.02f, 0.025f),
                    Mathf.Clamp(lastHitDamage / 18f, 0.8f, 2.5f));
                Sfx.Play("EN_Crumble", transform.position);
                Sfx.Play("EN_Debris", transform.position);
                model.gameObject.SetActive(false);
                Game.Rig.Shake(IsBoss ? 0.6f : 0.2f);
            }
            if (t > CrackTime + 0.3f) Destroy(gameObject);
        }

        void AnimateSpawn()
        {
            const float dur = 0.6f;
            float p = Mathf.Clamp01(stateTime / dur);
            model.localPosition = new Vector3(0f, (1f - p * p) * 6f, 0f);
            if (p >= 1f)
            {
                model.localPosition = Vector3.zero;
                Effects.DarkWave(transform.position, 2.4f);
                Sfx.Play("EN_Spawn", transform.position);
                if (IsBoss) Sfx.Play("QUEEN_Intro");
                Vfx.Dust(transform.position, 1.1f);  // set down on the board like a chess move
                Enter(EState.Move);
            }
        }

        protected virtual void OnDestroy()
        {
            ClearTelegraph();
            BoardSlots.Release(this);
        }
    }
}

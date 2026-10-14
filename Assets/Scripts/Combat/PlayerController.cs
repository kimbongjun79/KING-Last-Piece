using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// The White King: fights with its own body (lean, spin, topple, stomp…) defined by an AttackStyle.
    /// Stamina-gated light/heavy attacks, charge-limited i-frame rolls with a perfect dodge (slow motion →
    /// Royal Throw counter), lock-on and healing flasks. All motion is procedural.
    /// </summary>
    public class PlayerController : Actor
    {
        enum State { Normal, Attack, Dodge, Heal, Stagger, Counter, Dead }
        public enum Buffered { None, Light, Heavy, Dodge, Heal }

        public const float MaxStamina = 100f;
        public const int MaxFlasks = 3;
        const float MoveSpeed = 5.2f, LockedMoveSpeed = 4.3f;
        const float DodgeDuration = 0.5f, IFrameStart = 0.03f, IFrameEnd = 0.36f, DodgeCancelAt = 0.4f;

        // Dodge charges replace stamina for rolling: a short burst of rolls, refilled quickly.
        public const int MaxDodges = 3;
        const float DodgeRechargeTime = 0.7f, DodgeRechargeDelay = 0.35f;

        // Perfect dodge: an attack that lands (or narrowly misses) within this long after the roll starts.
        const float JustWindow = 0.2f;
        public const float NearMissMargin = 1.0f;
        const float SlowScale = 0.25f, SlowDuration = 1.6f;

        // Royal Throw (counter after a perfect dodge): approach, swing the piece around twice, hurl it.
        const float CounterApproach = 0.12f, CounterRelease = 0.74f, CounterDuration = 1.0f;
        const float SwingRadius = 1.8f, ThrowDamage = 55f, LandingDamage = 20f, SwingHitDamage = 25f;
        const float HealDuration = 1.1f, HealAt = 0.6f, HealAmount = 45f;
        const float StaminaRegen = 45f, StaminaDelay = 0.5f;
        const float BufferWindow = 0.4f, LockRange = 20f;
        const float ModelHeight = 2.1f, CenterHeight = 0.95f, BaseRadius = 0.42f;

        public float Stamina { get; private set; }
        public int Flasks { get; private set; }
        public EnemyBase LockTarget { get; private set; }
        public AttackStyle Style { get; set; } = AttackStyles.Default;
        public bool IsInvulnerable =>
            (state == State.Dodge && stateTime >= IFrameStart && stateTime <= IFrameEnd) || state == State.Counter;
        public int DodgeCharges { get; private set; }
        public float DodgeRechargeProgress => DodgeCharges >= MaxDodges ? 1f : Mathf.Clamp01(dodgeRecharge / DodgeRechargeTime);
        public int JustDodgeCount { get; private set; }
        public int ThrowCount { get; private set; }
        /// <summary>A perfect dodge happened and the dodged piece can still be grabbed (shown as a prompt).</summary>
        public bool CounterReady => Game != null && Game.SlowMoActive && justTarget != null && justTarget.CanBeGrabbed;
        public bool IsCountering => state == State.Counter;
        public bool IsDodging => state == State.Dodge;
        public bool IsAttacking => state == State.Attack;
        public bool IsIdle => state == State.Normal;
        public AttackDef CurrentAttack => state == State.Attack ? atk : null;
        public float StateTime => stateTime;
        /// <summary>For automated tests: ignore damage.</summary>
        public bool GodMode { get; set; }
        /// <summary>Attack Lab: actions cost no stamina.</summary>
        public bool InfiniteStamina { get; set; }

        State state;
        float stateTime, staminaDelay;
        AttackDef atk;
        int comboIndex;
        bool hitStopUsed;
        bool healed;
        HashSet<EnemyBase>[] hitSets = new HashSet<EnemyBase>[0];
        bool[] windowStarted = new bool[0];
        readonly List<EnemyBase> scratch = new List<EnemyBase>();
        Vector3 dodgeDir, knockback;
        Buffered buffer;
        float bufferTimer, bobPhase;
        float prevMove;
        float dodgeRecharge;
        bool justThisDodge;
        EnemyBase justTarget, counterTarget;
        Vector3 counterFrom, counterTo;
        float counterAngle0;
        bool counterReleased;
        readonly HashSet<EnemyBase> counterHits = new HashSet<EnemyBase>();

        Transform body;

        protected override float DeltaTime => Game != null ? Game.PlayerDeltaTime : Time.deltaTime;
        TrailRenderer crownTrail, baseTrail;

        public void Build()
        {
            body = new GameObject("Body").transform;
            body.SetParent(transform, false);

            var model = PieceFactory.BuildPlayerKing(body, ModelHeight);
            model.localPosition = new Vector3(0f, -CenterHeight, 0f);

            crownTrail = CreateTrail(new Vector3(0f, ModelHeight - CenterHeight - 0.05f, 0f), 0.42f, new Color(1f, 0.78f, 0.35f) * 3.5f);
            baseTrail = CreateTrail(new Vector3(0f, -CenterHeight + 0.15f, 0f), 0.22f, new Color(1f, 0.85f, 0.6f) * 1.5f);

            Radius = 0.45f;
        }

        TrailRenderer CreateTrail(Vector3 localPos, float width, Color emission)
        {
            var go = new GameObject("Trail");
            go.transform.SetParent(body, false);
            go.transform.localPosition = localPos;
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.16f;
            tr.minVertexDistance = 0.04f;
            tr.widthMultiplier = width;
            tr.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            tr.sharedMaterial = MaterialLib.Get(Color.black, 0f, 0f, emission);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.emitting = false;
            return tr;
        }

        public void ResetForWave(Vector3 pos, float yaw)
        {
            InitActor(100f);
            Stamina = MaxStamina;
            Flasks = MaxFlasks;
            state = State.Normal;
            stateTime = 0f;
            buffer = Buffered.None;
            LockTarget = null;
            DodgeCharges = MaxDodges;
            dodgeRecharge = 0f;
            justTarget = counterTarget = null;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            ApplyPose(default);
            crownTrail.emitting = baseTrail.emitting = false;
            crownTrail.Clear();
            baseTrail.Clear();
            SetGlow(Color.black);
        }

        /// <summary>Between waves: refill flasks and recover some health.</summary>
        public void Rest()
        {
            Flasks = MaxFlasks;
            Health = Mathf.Min(MaxHealth, Health + 35f);
            Stamina = MaxStamina;
            LockTarget = null;
            Flash(new Color(1f, 0.8f, 0.3f) * 1.5f, 0.4f);
        }

        public void RefillForLab()
        {
            Health = MaxHealth;
            Flasks = MaxFlasks;
        }

        /// <summary>Queue an action as if the button had been pressed (used by the Attack Lab automation).</summary>
        public void Press(Buffered action)
        {
            buffer = action;
            bufferTimer = BufferWindow;
        }

        void ApplyPose(BodyPose p) => p.Apply(body, CenterHeight, BaseRadius);

        void Update()
        {
            float dt = Time.deltaTime;
            if (state == State.Dead)
            {
                stateTime += dt;
                float p = PoseTrack.Curve(Ease.Out, stateTime / 0.9f);
                ApplyPose(BodyPose.P(tilt: -90f * p + 6f * Mathf.Sin(Mathf.Clamp01(stateTime / 0.9f) * Mathf.PI)));
                return;
            }
            if (Game == null || Game.State != GameManager.GameState.Playing) return;

            dt = DeltaTime;
            ReadInput(dt);
            stateTime += dt;
            UpdateStamina(dt);
            UpdateDodgeCharges(dt);
            UpdateLockTarget();
            if (justTarget != null && !Game.SlowMoActive) justTarget = null;

            switch (state)
            {
                case State.Normal: UpdateNormal(dt); break;
                case State.Attack: UpdateAttack(dt); break;
                case State.Dodge: UpdateDodge(dt); break;
                case State.Heal: UpdateHeal(dt); break;
                case State.Stagger: UpdateStagger(dt); break;
                case State.Counter: UpdateCounter(dt); break;
            }

            if (state != State.Attack && state != State.Counter) crownTrail.emitting = baseTrail.emitting = false;
            ClampToBoard();
        }

        // ---------------------------------------------------------------- input

        void ReadInput(float dt)
        {
            if (GameInput.LightAttack) Press(Buffered.Light);
            if (GameInput.HeavyAttack) Press(Buffered.Heavy);
            if (GameInput.Dodge) Press(Buffered.Dodge);
            if (GameInput.Heal) Press(Buffered.Heal);
            // Holding an attack button keeps re-queuing it, so the combo continues at each cancel point.
            if (buffer == Buffered.None)
            {
                if (GameInput.LightHeld) Press(Buffered.Light);
                else if (GameInput.HeavyHeld) Press(Buffered.Heavy);
            }
            if (GameInput.LockOn) ToggleLock();

            bufferTimer -= dt;
            if (bufferTimer <= 0f) buffer = Buffered.None;
        }

        bool TryAct()
        {
            switch (buffer)
            {
                case Buffered.Light:
                    if (CounterReady) { StartCounter(); return true; }
                    if (Stamina < 1f) return false;
                    StartAttack(false);
                    return true;
                case Buffered.Heavy:
                    if (Stamina < 1f) return false;
                    StartAttack(true);
                    return true;
                case Buffered.Dodge:
                    if (DodgeCharges <= 0) return false;
                    StartDodge();
                    return true;
                case Buffered.Heal:
                    if (Flasks <= 0) { buffer = Buffered.None; return false; }
                    StartHeal();
                    return true;
            }
            return false;
        }

        void Consume(float cost)
        {
            buffer = Buffered.None;
            if (!InfiniteStamina) Stamina = Mathf.Max(0f, Stamina - cost);
            staminaDelay = StaminaDelay;
        }

        void UpdateDodgeCharges(float dt)
        {
            if (DodgeCharges >= MaxDodges) { dodgeRecharge = 0f; return; }
            dodgeRecharge += dt;
            if (dodgeRecharge >= DodgeRechargeTime)
            {
                DodgeCharges++;
                dodgeRecharge = 0f;
            }
        }

        void UpdateStamina(float dt)
        {
            if (state == State.Attack || state == State.Dodge) return;
            if (staminaDelay > 0f) { staminaDelay -= dt; return; }
            float rate = state == State.Heal ? StaminaRegen * 0.5f : StaminaRegen;
            Stamina = Mathf.Min(MaxStamina, Stamina + rate * dt);
        }

        Vector3 InputDirection()
        {
            Vector2 mi = GameInput.Move;
            var rig = Game.Rig;
            return rig.FlatRight * mi.x + rig.FlatForward * mi.y;
        }

        // ---------------------------------------------------------------- lock-on

        void ToggleLock()
        {
            if (LockTarget != null) { LockTarget = null; return; }
            LockTarget = FindLockTarget(true);
        }

        void UpdateLockTarget()
        {
            if (LockTarget == null) return;
            if (LockTarget.IsDead || (LockTarget.FlatPos - FlatPos).magnitude > LockRange * 1.3f)
                LockTarget = FindLockTarget(false);
        }

        EnemyBase FindLockTarget(bool preferView)
        {
            EnemyBase best = null;
            float bestScore = float.MaxValue;
            Vector3 camFwd = Game.Rig.FlatForward;
            foreach (var e in Game.Enemies)
            {
                if (e == null || e.IsDead || !e.Targetable) continue;
                Vector3 to = e.FlatPos - FlatPos;
                float d = to.magnitude;
                if (d > LockRange) continue;
                float score = d + (preferView ? Vector3.Angle(camFwd, to) * 0.08f : 0f);
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        // ---------------------------------------------------------------- states

        void Enter(State s)
        {
            state = s;
            stateTime = 0f;
        }

        void UpdateNormal(float dt)
        {
            if (TryAct()) return;

            Vector3 dir = InputDirection();
            float speed = LockTarget != null ? LockedMoveSpeed : MoveSpeed;
            transform.position += dir * (speed * dt);

            if (LockTarget != null) FaceTowards(LockTarget.FlatPos - FlatPos, 900f);
            else if (dir.sqrMagnitude > 0.01f) FaceTowards(dir, 720f);

            // Chess-piece hop while walking, leaning into the movement.
            float m = dir.magnitude;
            prevMove = Mathf.Lerp(prevMove, m, 1f - Mathf.Exp(-10f * dt));
            bobPhase += dt * 10f * m;
            float hop = m > 0.05f ? Mathf.Abs(Mathf.Sin(bobPhase)) * 0.12f : 0f;
            float heading = 0f;
            if (m > 0.05f) heading = Vector3.SignedAngle(transform.forward, dir, Vector3.up);
            ApplyPose(BodyPose.P(tilt: 7f * prevMove, heading: heading, lift: hop));
        }

        void StartAttack(bool heavy)
        {
            var style = Style ?? AttackStyles.Default;
            bool chain = state == State.Attack && !atk.heavy && !heavy;
            if (heavy) atk = style.heavy;
            else
            {
                comboIndex = chain ? (comboIndex + 1) % style.lights.Length : 0;
                atk = style.lights[comboIndex];
            }
            Consume(atk.cost);
            hitStopUsed = false;

            if (hitSets.Length < atk.hits.Length)
            {
                hitSets = new HashSet<EnemyBase>[atk.hits.Length];
                windowStarted = new bool[atk.hits.Length];
            }
            for (int i = 0; i < hitSets.Length; i++)
            {
                hitSets[i] ??= new HashSet<EnemyBase>();
                hitSets[i].Clear();
                windowStarted[i] = false;
            }
            Enter(State.Attack);

            Vector3 dir = InputDirection();
            if (LockTarget != null) dir = LockTarget.FlatPos - FlatPos;
            if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z));
        }

        void UpdateAttack(float dt)
        {
            float t = stateTime, prevT = t - dt;

            if (t < atk.FirstHit && LockTarget != null) FaceTowards(LockTarget.FlatPos - FlatPos, 540f);

            // Root motion from move segments.
            bool blocked = LockTarget != null && (LockTarget.FlatPos - FlatPos).magnitude < Radius + LockTarget.Radius + 0.3f;
            if (!blocked)
            {
                foreach (var m in atk.moves)
                {
                    float a = PoseTrack.Curve(m.ease, (prevT - m.start) / (m.end - m.start));
                    float b = PoseTrack.Curve(m.ease, (t - m.start) / (m.end - m.start));
                    transform.position += transform.forward * (m.distance * (b - a));
                }
            }

            ApplyPose(atk.pose.Eval(t));

            bool trail = t >= atk.FirstHit - 0.07f && t <= atk.LastHit + 0.03f;
            crownTrail.emitting = trail;
            baseTrail.emitting = trail && atk.heavy;

            for (int i = 0; i < atk.hits.Length; i++)
            {
                var w = atk.hits[i];
                if (t < w.start || t > w.end + 0.02f) continue;
                if (!windowStarted[i])
                {
                    windowStarted[i] = true;
                    if (w.slash)
                        SlashFx.Spawn(FlatPos + transform.forward * w.forward, transform.forward, w.range * 0.82f,
                            w.sweepFrom, w.sweepTo, w.end - w.start, new Color(1f, 0.82f, 0.45f));
                    if (w.kick > 0f) Game.Rig.Kick(w.kick);
                    if (w.impact)
                    {
                        Vector3 c = transform.position + transform.forward * w.forward;
                        Effects.Shockwave(c, w.range, new Color(1f, 0.8f, 0.4f), 0.45f);
                        Effects.Sparks(c + Vector3.up * 0.1f, new Color(0.75f, 0.7f, 0.6f), 14, 5f);
                        Game.Rig.Shake(w.shake);
                    }
                }
                if (w.sparkTrail && Random.value < 0.8f)
                    Effects.Sparks(crownTrail.transform.position, new Color(1f, 0.8f, 0.4f), 2, 6f);
                DoHits(w, hitSets[i], Mathf.Clamp01((t - w.start) / Mathf.Max(0.001f, w.end - w.start)));
            }

            if (t >= atk.cancelAt && TryAct()) return;
            if (t >= atk.duration)
            {
                ApplyPose(default);
                Enter(State.Normal);
            }
        }

        void DoHits(HitWindow w, HashSet<EnemyBase> already, float progress)
        {
            bool landed = false;
            Vector3 center = FlatPos + transform.forward * w.forward;
            scratch.Clear();
            scratch.AddRange(Game.Enemies);
            foreach (var e in scratch)
            {
                if (e == null || e.IsDead || already.Contains(e)) continue;
                Vector3 to = e.FlatPos - center;
                float d = to.magnitude;
                if (d > w.range + e.Radius) continue;
                if (w.shape == HitShape.Arc && d > 0.4f && Vector3.Angle(transform.forward, to) > w.arc * 0.5f) continue;
                if (w.shape == HitShape.Arc && w.sweep && d > 0.4f)
                {
                    // Only what the body has already swept through gets hit.
                    float ang = Vector3.SignedAngle(transform.forward, to, Vector3.up);
                    float cur = Mathf.Lerp(w.sweepFrom, w.sweepTo, progress);
                    float lo = Mathf.Min(w.sweepFrom, cur) - 15f, hi = Mathf.Max(w.sweepFrom, cur) + 15f;
                    if (ang < lo || ang > hi) continue;
                }
                already.Add(e);
                if (e.TakeHit(w.damage, w.poise, transform.position))
                {
                    landed = true;
                    Game.ReportDamage(e, w.damage);
                    Vector3 toE = (e.FlatPos - FlatPos).normalized;
                    Effects.Sparks(e.transform.position + Vector3.up * 1.0f - toE * e.Radius,
                        new Color(1f, 0.85f, 0.5f), atk.heavy ? 16 : 10, atk.heavy ? 7f : 5f);
                }
            }
            if (landed)
            {
                // One hit-stop per attack: sweeps hit enemies on successive frames and would otherwise stutter.
                if (!hitStopUsed) Game.HitStop(w.hitStop);
                hitStopUsed = true;
                if (!w.impact) Game.Rig.Shake(w.shake);
            }
        }

        void StartDodge()
        {
            buffer = Buffered.None;
            DodgeCharges--;
            dodgeRecharge = -DodgeRechargeDelay;
            justThisDodge = false;
            Vector3 dir = InputDirection();
            bool backstep = dir.sqrMagnitude <= 0.01f;
            dodgeDir = backstep ? -transform.forward : dir.normalized;
            if (!backstep) transform.rotation = Quaternion.LookRotation(dodgeDir);
            Enter(State.Dodge);
        }

        void UpdateDodge(float dt)
        {
            float p = Mathf.Clamp01(stateTime / DodgeDuration);
            bool backstep = Vector3.Dot(dodgeDir, transform.forward) < -0.5f;
            float speed = Mathf.Lerp(backstep ? 9f : 13f, 2f, p);
            transform.position += dodgeDir * (speed * dt);

            if (backstep) ApplyPose(BodyPose.P(tilt: -15f * Mathf.Sin(p * Mathf.PI)));
            else ApplyPose(BodyPose.P(flip: 360f * PoseTrack.Curve(Ease.Out, p), lift: -0.35f * Mathf.Sin(p * Mathf.PI), squash: -0.1f * Mathf.Sin(p * Mathf.PI)));

            // After a perfect dodge the roll can be cancelled almost immediately into the Royal Throw.
            float cancelAt = CounterReady ? 0.1f : DodgeCancelAt;
            if (stateTime >= cancelAt && (buffer == Buffered.Light || buffer == Buffered.Heavy) && TryAct()) return;
            if (p >= 1f)
            {
                ApplyPose(default);
                Enter(State.Normal);
            }
        }

        void StartHeal()
        {
            buffer = Buffered.None;
            Flasks--;
            healed = false;
            Enter(State.Heal);
            SetGlow(new Color(0.9f, 0.6f, 0.15f) * 0.6f);
        }

        void UpdateHeal(float dt)
        {
            Vector3 dir = InputDirection();
            transform.position += dir * (MoveSpeed * 0.3f * dt);
            if (LockTarget != null) FaceTowards(LockTarget.FlatPos - FlatPos, 360f);
            ApplyPose(BodyPose.P(tilt: -8f * Mathf.Sin(Mathf.Clamp01(stateTime / HealDuration) * Mathf.PI)));

            if (!healed && stateTime >= HealAt)
            {
                healed = true;
                Health = Mathf.Min(MaxHealth, Health + HealAmount);
                Flash(new Color(1f, 0.8f, 0.3f) * 2f, 0.25f);
                Effects.Sparks(transform.position + Vector3.up * 1.2f, new Color(1f, 0.8f, 0.3f), 14, 3f);
            }
            if (stateTime >= HealDuration)
            {
                SetGlow(Color.black);
                Enter(State.Normal);
            }
        }

        void UpdateStagger(float dt)
        {
            transform.position += knockback * dt;
            knockback = Vector3.Lerp(knockback, Vector3.zero, 1f - Mathf.Exp(-8f * dt));
            float p = Mathf.Clamp01(stateTime / 0.4f);
            ApplyPose(BodyPose.P(tilt: -18f * Mathf.Sin(p * Mathf.PI)));
            if (stateTime >= 0.4f)
            {
                ApplyPose(default);
                Enter(State.Normal);
            }
        }

        // ---------------------------------------------------------------- perfect dodge

        bool InJustWindow => state == State.Dodge && stateTime <= JustWindow;

        /// <summary>An enemy attack reached the King. Handles perfect dodges before normal damage.</summary>
        public bool TakeHitFrom(EnemyBase attacker, float damage, float poiseDamage)
        {
            if (InJustWindow && IsInvulnerable)
            {
                TriggerJustDodge(attacker);
                return false;
            }
            return TakeHit(damage, poiseDamage, attacker.transform.position);
        }

        /// <summary>An enemy attack narrowly missed the King.</summary>
        public void NotifyNearMiss(EnemyBase attacker)
        {
            if (InJustWindow) TriggerJustDodge(attacker);
        }

        void TriggerJustDodge(EnemyBase attacker)
        {
            if (justThisDodge || IsDead) return;
            justThisDodge = true;
            justTarget = attacker;
            JustDodgeCount++;
            DodgeCharges = Mathf.Min(MaxDodges, DodgeCharges + 1);
            Game.SlowMo(SlowScale, SlowDuration);
            Game.Rig.Kick(0.45f);
            Flash(new Color(0.6f, 1.2f, 2.2f), 0.25f);
            var cyan = new Color(0.45f, 0.8f, 1f);
            Effects.Shockwave(FlatPos, 1.8f, cyan, 0.4f);
            Effects.Sparks(transform.position + Vector3.up * 1.1f, cyan, 18, 5f);
        }

        // ---------------------------------------------------------------- royal throw (counter)

        void StartCounter()
        {
            buffer = Buffered.None;
            counterTarget = justTarget;
            justTarget = null;
            counterTarget.BeginGrab();
            counterReleased = false;
            counterHits.Clear();
            counterHits.Add(counterTarget);
            LockTarget = counterTarget;

            Vector3 to = counterTarget.FlatPos - FlatPos;
            if (to.sqrMagnitude < 0.01f) to = transform.forward;
            counterFrom = FlatPos;
            counterTo = counterTarget.FlatPos - to.normalized * SwingRadius;
            counterAngle0 = Vector3.SignedAngle(Vector3.forward, to, Vector3.up);
            transform.rotation = Quaternion.LookRotation(to);
            Enter(State.Counter);
        }

        void UpdateCounter(float dt)
        {
            float t = stateTime;
            if (counterTarget == null || (counterTarget.IsDead && !counterReleased))
            {
                Game.EndSlowMo();
                ApplyPose(default);
                Enter(State.Normal);
                return;
            }

            if (t < CounterApproach)
            {
                // Lunge next to the piece and pick it up.
                float p = PoseTrack.Curve(Ease.Out, t / CounterApproach);
                transform.position = Vector3.Lerp(counterFrom, counterTo, p);
                Vector3 outward = counterTarget.FlatPos - FlatPos;
                counterTarget.SetGrabbedPose(counterTarget.FlatPos, outward, 75f * p, 0.7f * p);
                ApplyPose(BodyPose.P(tilt: 20f * p, squash: -0.12f * p));
                crownTrail.emitting = false;
            }
            else if (t < CounterRelease)
            {
                // Swing the piece around twice, accelerating, hitting everything it sweeps through.
                float p = (t - CounterApproach) / (CounterRelease - CounterApproach);
                float angle = counterAngle0 + 720f * Mathf.Pow(p, 1.7f);
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 piecePos = FlatPos + dir * SwingRadius;
                counterTarget.SetGrabbedPose(piecePos, dir, 75f, 0.7f + 0.3f * Mathf.Sin(p * Mathf.PI));
                transform.rotation = Quaternion.LookRotation(dir);
                ApplyPose(BodyPose.P(tilt: -18f, squash: -0.06f));
                crownTrail.emitting = true;

                if (Random.value < 0.6f)
                    Effects.Sparks(piecePos + Vector3.up * 1.2f, new Color(1f, 0.8f, 0.4f), 2, 4f);

                foreach (var e in Game.Enemies)
                {
                    if (e == null || e.IsDead || counterHits.Contains(e)) continue;
                    if ((e.FlatPos - piecePos).magnitude > counterTarget.Radius + e.Radius + 0.5f) continue;
                    counterHits.Add(e);
                    if (e.TakeHit(SwingHitDamage, 50f, piecePos))
                    {
                        Game.ReportDamage(e, SwingHitDamage);
                        Effects.Sparks(e.transform.position + Vector3.up, new Color(1f, 0.85f, 0.5f), 14, 6f);
                        Game.Rig.Shake(0.3f);
                    }
                }
            }
            else
            {
                if (!counterReleased)
                {
                    // Hurl it outward.
                    counterReleased = true;
                    ThrowCount++;
                    Vector3 dir = counterTarget.FlatPos - FlatPos;
                    Game.EndSlowMo();
                    if (counterTarget.TakeHit(ThrowDamage, 0f, transform.position)) Game.ReportDamage(counterTarget, ThrowDamage);
                    if (!counterTarget.IsDead) counterTarget.Launch(dir, 17f, 7f, LandingDamage);
                    Game.HitStop(0.12f);
                    Game.Rig.Kick(1f);
                    Game.Rig.Shake(0.5f);
                    Effects.Shockwave(FlatPos, 2.2f, new Color(1f, 0.8f, 0.4f), 0.4f);
                    LockTarget = null;
                }
                float p = Mathf.Clamp01((t - CounterRelease) / (CounterDuration - CounterRelease));
                ApplyPose(BodyPose.Lerp(BodyPose.P(tilt: 28f), default, PoseTrack.Curve(Ease.OutBack, p)));
                crownTrail.emitting = false;
                if (t >= CounterDuration)
                {
                    counterTarget = null;
                    ApplyPose(default);
                    Enter(State.Normal);
                }
            }
        }

        // ---------------------------------------------------------------- damage

        public override bool TakeHit(float damage, float poiseDamage, Vector3 source)
        {
            if (IsDead || IsInvulnerable || GodMode || Game.State != GameManager.GameState.Playing) return false;

            Health -= damage;
            Flash(new Color(2f, 0.1f, 0.05f), 0.15f);
            SetGlow(Color.black);
            Game.HitStop(0.08f);
            Game.Rig.Shake(0.35f);
            Effects.Sparks(transform.position + Vector3.up * 1.2f, new Color(1f, 0.2f, 0.1f), 12, 5f);

            Vector3 away = FlatPos - new Vector3(source.x, 0f, source.z);
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            away.Normalize();

            if (Health <= 0f)
            {
                Health = 0f;
                IsDead = true;
                LockTarget = null;
                transform.rotation = Quaternion.LookRotation(-away);
                Enter(State.Dead);
                Game.OnPlayerDied();
            }
            else
            {
                knockback = away * (4f + poiseDamage * 0.06f);
                Enter(State.Stagger);
            }
            return true;
        }
    }
}

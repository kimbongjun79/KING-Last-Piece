using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// The White King: fights with its own body (lean, spin, topple, stomp…) defined by an AttackStyle.
    /// Stamina-gated light/heavy attacks, i-frame dodge roll, lock-on and healing flasks. All motion is procedural.
    /// </summary>
    public class PlayerController : Actor
    {
        enum State { Normal, Attack, Dodge, Heal, Stagger, Dead }
        public enum Buffered { None, Light, Heavy, Dodge, Heal }

        public const float MaxStamina = 100f;
        public const int MaxFlasks = 3;
        const float MoveSpeed = 5.2f, LockedMoveSpeed = 4.3f;
        const float DodgeDuration = 0.5f, IFrameStart = 0.03f, IFrameEnd = 0.36f, DodgeCost = 20f, DodgeCancelAt = 0.4f;
        const float HealDuration = 1.1f, HealAt = 0.6f, HealAmount = 45f;
        const float StaminaRegen = 45f, StaminaDelay = 0.5f;
        const float BufferWindow = 0.4f, LockRange = 20f;
        const float ModelHeight = 2.1f, CenterHeight = 0.95f, BaseRadius = 0.42f;

        public float Stamina { get; private set; }
        public int Flasks { get; private set; }
        public EnemyBase LockTarget { get; private set; }
        public AttackStyle Style { get; set; } = AttackStyles.Default;
        public bool IsInvulnerable => state == State.Dodge && stateTime >= IFrameStart && stateTime <= IFrameEnd;
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
        bool healed;
        HashSet<EnemyBase>[] hitSets = new HashSet<EnemyBase>[0];
        bool[] windowStarted = new bool[0];
        readonly List<EnemyBase> scratch = new List<EnemyBase>();
        Vector3 dodgeDir, knockback;
        Buffered buffer;
        float bufferTimer, bobPhase;
        float prevMove;

        Transform body;
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

            ReadInput(dt);
            stateTime += dt;
            UpdateStamina(dt);
            UpdateLockTarget();

            switch (state)
            {
                case State.Normal: UpdateNormal(dt); break;
                case State.Attack: UpdateAttack(dt); break;
                case State.Dodge: UpdateDodge(dt); break;
                case State.Heal: UpdateHeal(dt); break;
                case State.Stagger: UpdateStagger(dt); break;
            }

            if (state != State.Attack) crownTrail.emitting = baseTrail.emitting = false;
            ClampToBoard();
        }

        // ---------------------------------------------------------------- input

        void ReadInput(float dt)
        {
            if (GameInput.LightAttack) Press(Buffered.Light);
            if (GameInput.HeavyAttack) Press(Buffered.Heavy);
            if (GameInput.Dodge) Press(Buffered.Dodge);
            if (GameInput.Heal) Press(Buffered.Heal);
            if (GameInput.LockOn) ToggleLock();

            bufferTimer -= dt;
            if (bufferTimer <= 0f) buffer = Buffered.None;
        }

        bool TryAct()
        {
            switch (buffer)
            {
                case Buffered.Light:
                    if (Stamina < 1f) return false;
                    StartAttack(false);
                    return true;
                case Buffered.Heavy:
                    if (Stamina < 1f) return false;
                    StartAttack(true);
                    return true;
                case Buffered.Dodge:
                    if (Stamina < 1f) return false;
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
                    if (w.slash && w.sweep)
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
                Game.HitStop(w.hitStop);
                if (!w.impact) Game.Rig.Shake(w.shake);
            }
        }

        void StartDodge()
        {
            Consume(DodgeCost);
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

            if (stateTime >= DodgeCancelAt && (buffer == Buffered.Light || buffer == Buffered.Heavy) && TryAct()) return;
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

using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// The White King: stamina-gated light/heavy attacks, i-frame dodge roll, lock-on and healing flasks.
    /// No rig — all motion is procedural (body roll, sword swing).
    /// </summary>
    public class PlayerController : Actor
    {
        enum State { Normal, Attack, Dodge, Heal, Stagger, Dead }
        enum Buffered { None, Light, Heavy, Dodge, Heal }

        struct AttackDef
        {
            public float duration, activeStart, activeEnd, cancelAt;
            public float damage, poise, range, arc, cost, lunge;
            public bool heavy;
        }

        static readonly AttackDef LightAtk = new AttackDef
        {
            duration = 0.55f, activeStart = 0.16f, activeEnd = 0.28f, cancelAt = 0.36f,
            damage = 14f, poise = 12f, range = 2.4f, arc = 130f, cost = 14f, lunge = 0.7f
        };

        static readonly AttackDef HeavyAtk = new AttackDef
        {
            duration = 1.05f, activeStart = 0.5f, activeEnd = 0.62f, cancelAt = 0.78f,
            damage = 34f, poise = 45f, range = 2.7f, arc = 150f, cost = 28f, lunge = 1.1f, heavy = true
        };

        public const float MaxStamina = 100f;
        public const int MaxFlasks = 3;
        const float MoveSpeed = 5.2f, LockedMoveSpeed = 4.3f;
        const float DodgeDuration = 0.5f, IFrameStart = 0.03f, IFrameEnd = 0.36f, DodgeCost = 20f, DodgeCancelAt = 0.4f;
        const float HealDuration = 1.1f, HealAt = 0.6f, HealAmount = 45f;
        const float StaminaRegen = 45f, StaminaDelay = 0.5f;
        const float BufferWindow = 0.4f, LockRange = 20f;
        const float BodyPivotHeight = 0.95f;

        public float Stamina { get; private set; }
        public int Flasks { get; private set; }
        public EnemyBase LockTarget { get; private set; }
        public bool IsInvulnerable => state == State.Dodge && stateTime >= IFrameStart && stateTime <= IFrameEnd;
        public bool IsDodging => state == State.Dodge;

        State state;
        float stateTime, staminaDelay;
        AttackDef atk;
        int combo;
        bool healed;
        readonly HashSet<EnemyBase> hitThisSwing = new HashSet<EnemyBase>();
        readonly List<EnemyBase> scratch = new List<EnemyBase>();
        Vector3 dodgeDir, knockback;
        Buffered buffer;
        float bufferTimer, bobPhase;

        Transform body, weaponPivot;
        Vector3 weaponPose = new Vector3(-25f, 25f, 0f);
        static readonly Vector3 RestPose = new Vector3(-25f, 25f, 0f);

        public void Build()
        {
            body = new GameObject("Body").transform;
            body.SetParent(transform, false);
            body.localPosition = new Vector3(0f, BodyPivotHeight, 0f);

            var model = PieceFactory.BuildPlayerKing(body, 2.1f);
            model.localPosition = new Vector3(0f, -BodyPivotHeight, 0f);

            // Royal sword held at the right side.
            weaponPivot = new GameObject("WeaponPivot").transform;
            weaponPivot.SetParent(body, false);
            weaponPivot.localPosition = new Vector3(0.5f, 0.15f, 0.15f);
            var steel = MaterialLib.Get(new Color(0.8f, 0.82f, 0.86f), 0.9f, 0.9f);
            var gold = MaterialLib.Get(new Color(0.85f, 0.65f, 0.2f), 0.8f, 1f);
            PieceFactory.Part(PrimitiveType.Cube, weaponPivot, new Vector3(0, 0, 0.9f), new Vector3(0.09f, 0.03f, 1.45f), steel);
            PieceFactory.Part(PrimitiveType.Cube, weaponPivot, new Vector3(0, 0, 0.14f), new Vector3(0.36f, 0.07f, 0.07f), gold);
            PieceFactory.Part(PrimitiveType.Cube, weaponPivot, new Vector3(0, 0, -0.02f), new Vector3(0.06f, 0.06f, 0.28f), gold);

            Radius = 0.45f;
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
            body.localPosition = new Vector3(0f, BodyPivotHeight, 0f);
            body.localRotation = Quaternion.identity;
            weaponPose = RestPose;
            weaponPivot.localRotation = Quaternion.Euler(RestPose);
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

        void Update()
        {
            float dt = Time.deltaTime;
            if (state == State.Dead)
            {
                stateTime += dt;
                float p = Mathf.Clamp01(stateTime / 0.9f);
                float e = 1f - (1f - p) * (1f - p) * (1f - p);
                body.localRotation = Quaternion.Euler(-88f * e, 0f, 8f * e);
                body.localPosition = new Vector3(0f, Mathf.Lerp(BodyPivotHeight, 0.4f, e), -0.8f * e);
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

            weaponPivot.localRotation = Quaternion.Euler(weaponPose);
            ClampToBoard();
        }

        // ---------------------------------------------------------------- input

        void ReadInput(float dt)
        {
            if (GameInput.LightAttack) SetBuffer(Buffered.Light);
            if (GameInput.HeavyAttack) SetBuffer(Buffered.Heavy);
            if (GameInput.Dodge) SetBuffer(Buffered.Dodge);
            if (GameInput.Heal) SetBuffer(Buffered.Heal);
            if (GameInput.LockOn) ToggleLock();

            bufferTimer -= dt;
            if (bufferTimer <= 0f) buffer = Buffered.None;
        }

        void SetBuffer(Buffered b)
        {
            buffer = b;
            bufferTimer = BufferWindow;
        }

        bool TryAct()
        {
            switch (buffer)
            {
                case Buffered.Light:
                    if (Stamina < 1f) return false;
                    StartAttack(LightAtk);
                    return true;
                case Buffered.Heavy:
                    if (Stamina < 1f) return false;
                    StartAttack(HeavyAtk);
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
            Stamina = Mathf.Max(0f, Stamina - cost);
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

            // Chess-piece "hop" while walking.
            float m = dir.magnitude;
            bobPhase += dt * 10f * m;
            float hop = m > 0.05f ? Mathf.Abs(Mathf.Sin(bobPhase)) * 0.12f : 0f;
            body.localPosition = new Vector3(0f, Mathf.Lerp(body.localPosition.y, BodyPivotHeight + hop, 1f - Mathf.Exp(-20f * dt)), 0f);
            body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.Euler(6f * m, 0f, 0f), 1f - Mathf.Exp(-10f * dt));
            weaponPose = Vector3.Lerp(weaponPose, RestPose, 1f - Mathf.Exp(-12f * dt));
        }

        void StartAttack(AttackDef def)
        {
            bool chain = state == State.Attack && !atk.heavy && !def.heavy;
            combo = chain ? 1 - combo : 0;
            atk = def;
            Consume(def.cost);
            hitThisSwing.Clear();
            Enter(State.Attack);

            Vector3 dir = InputDirection();
            if (LockTarget != null) dir = LockTarget.FlatPos - FlatPos;
            if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z));
            body.localPosition = new Vector3(0f, BodyPivotHeight, 0f);
        }

        void UpdateAttack(float dt)
        {
            float t = stateTime;

            if (t < atk.activeStart && LockTarget != null) FaceTowards(LockTarget.FlatPos - FlatPos, 540f);

            if (t < atk.activeEnd)
            {
                bool blocked = LockTarget != null && (LockTarget.FlatPos - FlatPos).magnitude < 1.4f;
                if (!blocked) transform.position += transform.forward * (atk.lunge / atk.activeEnd * dt);
            }

            if (t >= atk.activeStart && t <= atk.activeEnd + 0.02f) DoHits();

            AnimateSwing(t);

            if (t >= atk.cancelAt && TryAct()) return;
            if (t >= atk.duration) Enter(State.Normal);
        }

        void AnimateSwing(float t)
        {
            Vector3 wind, end;
            if (atk.heavy)
            {
                wind = new Vector3(-135f, 8f, 0f);
                end = new Vector3(45f, 0f, 0f);
            }
            else if (combo == 0)
            {
                wind = new Vector3(0f, 105f, -20f);
                end = new Vector3(12f, -85f, -20f);
            }
            else
            {
                wind = new Vector3(0f, -95f, 20f);
                end = new Vector3(12f, 95f, 20f);
            }

            if (t < atk.activeStart) weaponPose = Vector3.Lerp(RestPose, wind, Smooth(t / atk.activeStart));
            else if (t < atk.activeEnd) weaponPose = Vector3.Lerp(wind, end, (t - atk.activeStart) / (atk.activeEnd - atk.activeStart));
            else weaponPose = Vector3.Lerp(end, RestPose, Smooth((t - atk.activeEnd) / (atk.duration - atk.activeEnd)));

            float lean = atk.heavy
                ? (t < atk.activeStart ? -10f * Smooth(t / atk.activeStart) : Mathf.Lerp(18f, 0f, (t - atk.activeStart) / (atk.duration - atk.activeStart)))
                : (t < atk.activeEnd ? 10f : Mathf.Lerp(10f, 0f, (t - atk.activeEnd) / (atk.duration - atk.activeEnd)));
            body.localRotation = Quaternion.Euler(lean, 0f, 0f);
        }

        void DoHits()
        {
            bool landed = false;
            scratch.Clear();
            scratch.AddRange(Game.Enemies);
            foreach (var e in scratch)
            {
                if (e == null || e.IsDead || hitThisSwing.Contains(e)) continue;
                Vector3 to = e.FlatPos - FlatPos;
                float d = to.magnitude;
                if (d > atk.range + e.Radius) continue;
                if (d > 0.4f && Vector3.Angle(transform.forward, to) > atk.arc * 0.5f) continue;
                hitThisSwing.Add(e);
                float dmg = atk.damage * (combo == 1 ? 1.1f : 1f);
                if (e.TakeHit(dmg, atk.poise, transform.position))
                {
                    landed = true;
                    Effects.Sparks(e.transform.position + Vector3.up * 1.0f - to.normalized * e.Radius,
                        new Color(1f, 0.85f, 0.5f), atk.heavy ? 16 : 9, atk.heavy ? 7f : 5f);
                }
            }
            if (landed)
            {
                Game.HitStop(atk.heavy ? 0.09f : 0.045f);
                Game.Rig.Shake(atk.heavy ? 0.3f : 0.12f);
            }
        }

        void StartDodge()
        {
            Consume(DodgeCost);
            Vector3 dir = InputDirection();
            dodgeDir = dir.sqrMagnitude > 0.01f ? dir.normalized : -transform.forward;
            bool backstep = dir.sqrMagnitude <= 0.01f;
            if (!backstep) transform.rotation = Quaternion.LookRotation(dodgeDir);
            Enter(State.Dodge);
        }

        void UpdateDodge(float dt)
        {
            float p = Mathf.Clamp01(stateTime / DodgeDuration);
            bool backstep = Vector3.Dot(dodgeDir, transform.forward) < -0.5f;
            float speed = Mathf.Lerp(backstep ? 9f : 13f, 2f, p);
            transform.position += dodgeDir * (speed * dt);

            if (backstep)
            {
                body.localRotation = Quaternion.Euler(-15f * Mathf.Sin(p * Mathf.PI), 0f, 0f);
                body.localPosition = new Vector3(0f, BodyPivotHeight, 0f);
            }
            else
            {
                float e = 1f - (1f - p) * (1f - p);
                body.localRotation = Quaternion.Euler(360f * e, 0f, 0f);
                body.localPosition = new Vector3(0f, Mathf.Lerp(BodyPivotHeight, 0.55f, Mathf.Sin(p * Mathf.PI)), 0f);
            }
            weaponPose = Vector3.Lerp(weaponPose, RestPose, 1f - Mathf.Exp(-12f * dt));

            if (stateTime >= DodgeCancelAt && (buffer == Buffered.Light || buffer == Buffered.Heavy))
            {
                body.localRotation = Quaternion.identity;
                if (TryAct()) return;
            }
            if (p >= 1f)
            {
                body.localRotation = Quaternion.identity;
                body.localPosition = new Vector3(0f, BodyPivotHeight, 0f);
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
            weaponPose = Vector3.Lerp(weaponPose, new Vector3(-70f, 0f, 0f), 1f - Mathf.Exp(-10f * dt));

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
            body.localRotation = Quaternion.Euler(-18f * Mathf.Sin(p * Mathf.PI), 0f, 0f);
            if (stateTime >= 0.4f)
            {
                body.localRotation = Quaternion.identity;
                Enter(State.Normal);
            }
        }

        // ---------------------------------------------------------------- damage

        public override bool TakeHit(float damage, float poiseDamage, Vector3 source)
        {
            if (IsDead || IsInvulnerable || Game.State != GameManager.GameState.Playing) return false;

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

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}

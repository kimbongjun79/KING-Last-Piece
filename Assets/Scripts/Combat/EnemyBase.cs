using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Black piece AI base: telegraphed attacks (wind-up → active → recovery), poise/stagger,
    /// spawn drop-in and "captured" topple on death. Subclasses only decide what to do in Think().
    /// </summary>
    public abstract class EnemyBase : Actor
    {
        protected enum EState { Spawning, Move, Windup, Attack, Recover, Stagger, Dead }
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
        public string DisplayName { get; protected set; } = "Pawn";
        public bool IsBoss { get; protected set; }
        public float LastHitTime { get; private set; } = -100f;
        public bool Targetable => state != EState.Spawning && !IsDead;
        public bool IsAttacking => state == EState.Windup || state == EState.Attack;
        public bool IgnoresCollision =>
            IsDead || state == EState.Spawning ||
            (state == EState.Attack && (plan.kind == AtkKind.Leap || plan.kind == AtkKind.Dash));

        protected EState state;
        protected float stateTime, cooldown;
        protected float moveSpeed = 2.5f, maxPoise = 25f, poise, staggerDuration = 1.1f;
        protected bool hyperArmor;
        protected AttackPlan plan;
        protected PlayerController Player => Game != null ? Game.Player : null;
        protected bool moving;

        Transform model;
        Vector3 modelBaseScale;
        Telegraph telegraph;
        Vector3 atkStart;
        bool atkHit, landed;
        float poiseRegenTimer, hopPhase, modelY, squash = 1f;
        Quaternion modelTilt = Quaternion.identity;
        Vector3 fallAxis;
        static readonly Color WindupGlow = new Color(1.6f, 0.12f, 0.04f);

        public void Setup(PieceType type, Transform modelRoot)
        {
            Type = type;
            model = modelRoot;
            Configure();
            modelBaseScale = model.localScale;
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

        protected void SetHealth(float hp) => MaxHealth = hp;
        protected void SetRadius(float r) => Radius = r;
        protected void SetModelScale(float s) => model.localScale = Vector3.one * s;

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

            if (poiseRegenTimer > 0f) poiseRegenTimer -= dt;
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
                case EState.Stagger:
                    modelTilt = Quaternion.Euler(-20f * Mathf.Sin(Mathf.Clamp01(stateTime / staggerDuration) * Mathf.PI), 0f, 0f);
                    if (stateTime >= staggerDuration)
                    {
                        Enter(EState.Move);
                        cooldown = Mathf.Max(cooldown, 0.5f);
                    }
                    break;
            }

            if (moving)
            {
                hopPhase += dt * 9f;
                modelY = Mathf.Max(modelY, Mathf.Abs(Mathf.Sin(hopPhase)) * 0.14f);
            }

            model.localPosition = new Vector3(0f, modelY, 0f);
            model.localRotation = modelTilt;
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
        protected bool CanAttackNow => cooldown <= 0f && (IsBoss || Game.ActiveAttackers(this) < 2);

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

        protected void Strafe(float sign, float speed, float dt)
        {
            Vector3 to = ToPlayer;
            if (to.sqrMagnitude < 0.01f) return;
            Vector3 side = Vector3.Cross(Vector3.up, to.normalized) * sign;
            transform.position += side * (speed * dt);
            moving = true;
        }

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

        // ---------------------------------------------------------------- attacks

        protected void BeginAttack(AttackPlan p)
        {
            ClearTelegraph();
            plan = p;
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
                    telegraph = Telegraph.Rect(atkStart, plan.dir, plan.length + plan.radius, plan.radius * 2f);
                    break;
                case AtkKind.Leap:
                    plan.target = ClampPoint(plan.target);
                    telegraph = Telegraph.Circle(plan.target, plan.radius);
                    break;
                case AtkKind.Slam:
                    telegraph = Telegraph.Circle(atkStart, plan.radius);
                    break;
            }
            Enter(EState.Windup);
        }

        void UpdateWindup()
        {
            float p = Mathf.Clamp01(stateTime / plan.windup);

            if (plan.kind == AtkKind.Lunge && p < plan.trackUntil)
            {
                Vector3 to = ToPlayer;
                if (to.sqrMagnitude > 0.01f) plan.dir = to.normalized;
                telegraph.SetRect(atkStart, plan.dir, plan.length + plan.radius, plan.radius * 2f);
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
                            Effects.Shockwave(plan.target, plan.radius, new Color(1f, 0.25f, 0.1f));
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
                            Effects.Shockwave(atkStart, plan.radius, new Color(1f, 0.25f, 0.1f), 0.5f);
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
            SetGlow(Color.black);
            Enter(EState.Recover);
        }

        void TryHitPlayer(Vector3 center, float radius)
        {
            if (atkHit || Player == null || Player.IsDead) return;
            Vector3 d = Player.FlatPos - new Vector3(center.x, 0f, center.z);
            if (d.magnitude > radius + Player.Radius) return;
            atkHit = Player.TakeHit(plan.damage, plan.poise, transform.position);
        }

        void ClearTelegraph()
        {
            if (telegraph != null) Destroy(telegraph.gameObject);
            telegraph = null;
        }

        // ---------------------------------------------------------------- damage / death

        public override bool TakeHit(float damage, float poiseDamage, Vector3 source)
        {
            if (IsDead || state == EState.Spawning) return false;

            Health -= damage;
            LastHitTime = Time.time;
            Flash(new Color(1.6f, 1.5f, 1.3f), 0.09f);

            poise -= poiseDamage;
            poiseRegenTimer = 2f;

            if (Health <= 0f)
            {
                Health = 0f;
                Die(source);
                return true;
            }

            bool armored = hyperArmor && (state == EState.Attack || state == EState.Windup);
            if (poise <= 0f && !armored)
            {
                poise = maxPoise;
                ClearTelegraph();
                SetGlow(Color.black);
                Enter(EState.Stagger);
                Effects.Sparks(transform.position + Vector3.up * 1.5f, new Color(1f, 0.9f, 0.4f), 8, 3f);
            }
            else if (state == EState.Move)
            {
                Vector3 away = FlatPos - new Vector3(source.x, 0f, source.z);
                if (away.sqrMagnitude > 0.001f) transform.position += away.normalized * 0.25f;
            }
            return true;
        }

        void Die(Vector3 source)
        {
            IsDead = true;
            ClearTelegraph();
            SetGlow(Color.black);
            Vector3 away = FlatPos - new Vector3(source.x, 0f, source.z);
            if (away.sqrMagnitude < 0.001f) away = -transform.forward;
            fallAxis = transform.InverseTransformDirection(Vector3.Cross(Vector3.up, away.normalized));
            Enter(EState.Dead);
            Effects.Sparks(transform.position + Vector3.up, new Color(0.4f, 0.4f, 0.45f), 18, 6f);
        }

        void AnimateDeath()
        {
            float t = stateTime;
            float fall = Mathf.Clamp01(t / 0.5f);
            fall = fall * fall;
            model.localRotation = Quaternion.AngleAxis(88f * fall, fallAxis);
            float sink = Mathf.Clamp01((t - 0.9f) / 1.2f);
            model.localPosition = new Vector3(0f, -1.5f * sink, 0f);
            if (t > 2.2f) Destroy(gameObject);
        }

        void AnimateSpawn()
        {
            const float dur = 0.6f;
            float p = Mathf.Clamp01(stateTime / dur);
            model.localPosition = new Vector3(0f, (1f - p * p) * 6f, 0f);
            if (p >= 1f)
            {
                model.localPosition = Vector3.zero;
                Effects.Shockwave(transform.position, 1.2f, new Color(0.5f, 0.1f, 0.4f), 0.3f);
                Enter(EState.Move);
            }
        }

        protected virtual void OnDestroy() => ClearTelegraph();
    }
}

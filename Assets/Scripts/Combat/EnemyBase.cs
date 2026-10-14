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
            if (radial.magnitude > plan.radius + playerRadius + Margin) return false;
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
        float modelHeight = 1.5f;
        Vector3 flinchPush, airKnock;
        Vector3 fallAxis;
        static readonly Color WindupGlow = new Color(1.6f, 0.12f, 0.04f);

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
        /// <summary>One-letter state code for lab timelines.</summary>
        public char StateCode => state switch
        {
            EState.Move => 'm', EState.Windup => 'w', EState.Attack => 'a', EState.Recover => 'r',
            EState.Stagger => 's', EState.Airborne => 'U', EState.Getup => 'g', EState.Dead => 'x', _ => '.',
        };

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
                    modelTilt = Quaternion.Euler(-20f * Mathf.Sin(Mathf.Clamp01(stateTime / currentStagger) * Mathf.PI), 0f, 0f);
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

            if (moving)
            {
                hopPhase += dt * 9f;
                modelY = Mathf.Max(modelY, Mathf.Abs(Mathf.Sin(hopPhase)) * 0.14f);
            }

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
            float dist = d.magnitude;
            if (dist <= radius + Player.Radius) atkHit = Player.TakeHitFrom(this, plan.damage, plan.poise);
            // A near miss still counts for a perfect dodge (rolling out of the hit at the last moment).
            else if (dist <= radius + Player.Radius + PlayerController.NearMissMargin) Player.NotifyNearMiss(this);
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
                Effects.Shockwave(p, 1.6f, new Color(1f, 0.7f, 0.35f), 0.35f);
                Effects.Sparks(p + Vector3.up * launchHeight, new Color(1f, 0.8f, 0.4f), 16, 6f);
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
                    Effects.Sparks(e.transform.position + Vector3.up, new Color(1f, 0.85f, 0.5f), 12, 6f);
                    Game.Rig.Shake(0.25f);
                }
            }

            if (launchHeight <= 0f && launchVy < 0f)
            {
                modelY = 0f;
                Effects.Shockwave(FlatPos, 2f, new Color(1f, 0.6f, 0.25f), 0.45f);
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

            Health -= damage;
            LastHitTime = Time.time;
            Flash(new Color(1.6f, 1.5f, 1.3f), 0.09f);

            if (Health <= 0f)
            {
                Health = 0f;
                Die(source);
                return true;
            }

            // Being swung, thrown or getting up: damage only, no reaction.
            if (state == EState.Grabbed || state == EState.Launched || state == EState.Getup) return true;

            if (stagger != null)
            {
                // Super armor from wind-up through the end of the hit; recovery is unarmored (punish window).
                bool superArmor = state == EState.Windup || state == EState.Attack;
                var reaction = stagger.OnHit(poiseDamage, superArmor, state == EState.Airborne);
                if (stagger.JustBecameVulnerable) VulnerableBurst();
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

        static readonly Color VulnerableColor = new Color(1f, 0.85f, 0.25f);
        const float AirGravity = 22f, AirLiftFalloff = 0.6f, GetupLie = 0.45f, GetupRise = 0.4f;

        /// <summary>Cancels whatever the piece was doing (attack, telegraph, glow) — frees its attack token too.</summary>
        void CancelAction()
        {
            ClearTelegraph();
            SetGlow(Color.black);
        }

        void Flinch(Vector3 source, float duration)
        {
            CancelAction();
            currentStagger = duration;
            Vector3 away = FlatPos - new Vector3(source.x, 0f, source.z);
            flinchPush = away.sqrMagnitude > 0.001f ? away.normalized * 2.5f : Vector3.zero;
            Enter(EState.Stagger);
        }

        /// <summary>Knock the piece into the air. Each further air hit lifts less (no infinite juggles).</summary>
        void LaunchUp(Vector3 source)
        {
            CancelAction();
            bool alreadyAirborne = state == EState.Airborne;
            if (!alreadyAirborne)
            {
                airHits = 0;
                airHeight = 0f;
            }
            float lift = stagger.Profile.launchForce * Mathf.Pow(AirLiftFalloff, airHits);
            airHits++;
            airVy = Mathf.Max(lift, alreadyAirborne ? airVy : 0f);
            Vector3 away = FlatPos - new Vector3(source.x, 0f, source.z);
            airKnock = away.sqrMagnitude > 0.001f ? away.normalized * (alreadyAirborne ? 1.2f : 2.2f) : Vector3.zero;
            Enter(EState.Airborne);
            Effects.Sparks(transform.position + Vector3.up * (airHeight + 1f), VulnerableColor, 10, 5f);
            if (!alreadyAirborne) Effects.Shockwave(FlatPos, 1.2f, VulnerableColor, 0.3f);
        }

        void UpdateAirborne(float dt)
        {
            airHeight += airVy * dt;
            airVy -= AirGravity * dt;
            transform.position += airKnock * dt;
            airKnock = Vector3.Lerp(airKnock, Vector3.zero, 1f - Mathf.Exp(-2f * dt));
            modelY = Mathf.Max(0f, airHeight);
            // Tumble backwards while in the air.
            modelTilt = Quaternion.Euler(Mathf.Clamp(-25f - 40f * airHits + airVy * 4f, -110f, 0f), 0f, 0f);

            if (airHeight <= 0f && airVy < 0f)
            {
                modelY = 0f;
                Effects.Shockwave(FlatPos, 1.4f, new Color(0.7f, 0.65f, 0.55f), 0.35f);
                Effects.Sparks(transform.position + Vector3.up * 0.2f, new Color(0.6f, 0.55f, 0.5f), 8, 3f);
                Enter(EState.Getup);
            }
        }

        /// <summary>Lie on the board, then stand back up. Getting up resets the gauge.</summary>
        void UpdateGetup()
        {
            float t = stateTime;
            float lie = t < GetupLie ? 1f : 1f - PoseTrack.Curve(Ease.InOut, (t - GetupLie) / GetupRise);
            modelTilt = Quaternion.Euler(-85f * lie, 0f, 0f);
            if (t >= GetupLie + GetupRise)
            {
                stagger?.ResetGauge();
                airHits = 0;
                Enter(EState.Move);
                cooldown = Mathf.Max(cooldown, 0.6f);
            }
        }

        /// <summary>The gauge just filled: hidden gauge, so make Vulnerable obvious with a burst.</summary>
        void VulnerableBurst()
        {
            Flash(VulnerableColor * 2.2f, 0.18f);
            Effects.Shockwave(FlatPos, 1.6f, VulnerableColor, 0.35f);
            Effects.Sparks(transform.position + Vector3.up * 2f, VulnerableColor, 16, 4f);
            Game.Rig.Shake(0.15f);
        }

        /// <summary>While Vulnerable: dizzy wobble and golden sparks circling the head.</summary>
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

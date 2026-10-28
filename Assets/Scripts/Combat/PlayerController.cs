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

        PlayerStats stats = PlayerStats.Classic();
        /// <summary>The King's stats; setting a fresh set (a new run) also forgets the items picked.</summary>
        public PlayerStats Stats { get => stats; set { stats = value; Items.Clear(); Momentum = 0; } }
        public float MaxStamina => Stats.maxStamina;
        public int MaxFlasks => Stats.maxFlasks;
        public float HealAmount => Stats.healAmount;
        const float MoveSpeed = 5.2f, LockedMoveSpeed = 4.3f;
        const float SprintSpeed = 8.2f, SprintDrain = 14f, SprintRecoverAt = 20f;
        /// <summary>Light (combo) attacks cost only this fraction of their listed stamina.</summary>
        const float LightCostScale = 0.35f;
        // Auto-aim: only pieces within this range (scaled by the attack's reach) are considered.
        const float AssistMinRange = 2.5f, AssistMaxRange = 4.5f;
        // Gap closer: a target within this angle of the aim, and where to stop in front of it.
        const float GapAngle = 100f, GapInputAngle = 35f, GapStopDistance = 0.5f;
        // The dash aims past the contact point (a piece in the way stops it there anyway), so one stepping back is still caught.
        const float GapOvershoot = 1.3f;
        const float DodgeDuration = 0.5f, IFrameStart = 0.03f, IFrameEnd = 0.36f, DodgeCancelAt = 0.4f;

        // Dodge charges replace stamina for rolling: a short burst of rolls, refilled quickly.
        public int MaxDodges => Stats.maxDodges;
        const float BaseDodgeRechargeTime = 0.7f, DodgeRechargeDelay = 0.35f;
        /// <summary>Seconds to refill one roll charge.</summary>
        public float DodgeRechargeTime => BaseDodgeRechargeTime / Mathf.Max(0.1f, Stats.dodgeRecharge);

        // Perfect dodge: an attack that lands (or narrowly misses) within this long after the roll starts.
        const float JustWindow = 0.2f;
        public const float NearMissMargin = 1.0f;
        const float SlowScale = 0.25f, SlowDuration = 1.6f;

        // Royal Throw (counter after a perfect dodge): approach, swing the piece around twice, hurl it.
        const float CounterApproach = 0.12f, CounterRelease = 0.74f, CounterDuration = 1.0f;
        // All of the throw damage lands on impact, so the piece always gets to fly first.
        const float SwingRadius = 1.8f, ThrowLandingDamage = 75f, SwingHitDamage = 25f;
        const float HealDuration = 1.1f, HealAt = 0.6f;
        const float StaminaRegen = 45f, StaminaDelay = 0.5f;
        const float BufferWindow = 0.4f, LockRange = 20f;
        const float ModelHeight = 2.1f, CenterHeight = 0.95f, BaseRadius = 0.42f;

        public float Stamina { get; private set; }
        public int Flasks { get; private set; }
        public EnemyBase LockTarget { get; private set; }
        public AttackStyle Style { get; set; } = AttackStyles.Default;
        /// <summary>Effects Lab only: a heavy motion trial, without changing the shared attack style.</summary>
        public AttackDef HeavyMotionTrial { get; set; }
        public bool IsInvulnerable =>
            (state == State.Dodge && stateTime >= IFrameStart && stateTime <= IFrameEnd) || state == State.Counter || stalemateTimer > 0f ||
            perfectGuard > 0f;
        /// <summary>Seconds of invincibility left from a perfect dodge (its whole slow motion).</summary>
        float perfectGuard;
        public int DodgeCharges { get; private set; }
        public float DodgeRechargeProgress => DodgeCharges >= MaxDodges ? 1f : Mathf.Clamp01(dodgeRecharge / DodgeRechargeTime);
        public int JustDodgeCount { get; private set; }
        public int ThrowCount { get; private set; }
        /// <summary>A perfect dodge happened and the dodged piece can still be grabbed (shown as a prompt).</summary>
        public bool CounterReady => Game != null && Game.SlowMoActive && justTarget != null && justTarget.CanBeGrabbed;
        public bool IsCountering => state == State.Counter;
        public bool IsSprinting { get; private set; }
        public Vector3 LastDodgeDirection { get; private set; }
        bool InputEnabled { get; set; } = true;
        public bool IsDodging => state == State.Dodge;
        public bool IsAttacking => state == State.Attack;
        public bool IsIdle => state == State.Normal;
        public AttackDef CurrentAttack => state == State.Attack ? atk : null;
        public float StateTime => stateTime;
        /// <summary>For automated tests: ignore damage.</summary>
        public bool GodMode { get; set; }
        /// <summary>Attack Lab: actions cost no stamina.</summary>
        public bool InfiniteStamina { get; set; }
        /// <summary>Movement injected by lab automation (world space, length ≤ 1); used instead of the stick when set.</summary>
        public Vector3 DebugMove { get; set; }
        public bool DebugSprint { get; set; }

        State state;
        float stateTime, staminaDelay;
        AttackDef atk;
        int comboIndex;
        bool hitStopUsed, atkUsesSlashFx;
        bool atkIsGapCloser;   // the dash covers an exact distance: no dynamism scaling of its root motion
        bool healed;
        GameObject healFx;   // looping healing aura, while the flask is being drunk
        HashSet<EnemyBase>[] hitSets = new HashSet<EnemyBase>[0];
        bool[] windowStarted = new bool[0];
        int[] sparksEmitted = new int[0];
        /// <summary>White sparks a spark-trail window throws off its crown over the whole window (whatever the frame rate or attack speed).</summary>
        const int SparkTrailCount = 60;
        readonly List<EnemyBase> scratch = new List<EnemyBase>();
        Vector3 dodgeDir, knockback;
        Buffered buffer;
        float bufferTimer, bobPhase;
        float prevMove;
        float dodgeRecharge, sprintBlend;
        bool sprintExhausted;
        bool justThisDodge;
        EnemyBase justTarget, counterTarget;
        readonly List<EnemyBase> assistNearby = new List<EnemyBase>();
        readonly List<Vector3> assistCandidates = new List<Vector3>();
        Vector3 counterFrom, counterTo;
        float counterAngle0, counterSpin = 1f;  // spin: +1 clockwise, -1 counter-clockwise (random per throw)
        // Sound-event bookkeeping (footstep phase, edge-triggered warnings, lock changes).
        int lastStep;
        bool prevSprint, lowHpWarned, lowStaminaWarned, swingSfx;
        int inkArcs;   // ink arcs drawn so far along the Royal Throw swing
        EnemyBase prevLock;
        bool counterReleased;
        readonly HashSet<EnemyBase> counterHits = new HashSet<EnemyBase>();

        // Built once (in edit mode by the world bake, or at runtime) and kept in the scene.
        [SerializeField, HideInInspector] Transform body;

        protected override float DeltaTime => Game != null ? Game.PlayerDeltaTime : Time.deltaTime;
        [SerializeField, HideInInspector] TrailRenderer crownTrail, baseTrail;

        /// <summary>Builds the King's model and trails unless they are already there (baked into the scene).</summary>
        public void Build()
        {
            if (body == null)
            {
                body = new GameObject("Body").transform;
                body.SetParent(transform, false);

                var model = PieceFactory.BuildPlayerKing(body, ModelHeight);
                model.localPosition = new Vector3(0f, -CenterHeight, 0f);

                crownTrail = CreateTrail(new Vector3(0f, ModelHeight - CenterHeight - 0.05f, 0f), 0.42f, new Color(1f, 0.78f, 0.35f) * 3.5f);
                baseTrail = CreateTrail(new Vector3(0f, -CenterHeight + 0.15f, 0f), 0.22f, new Color(1f, 0.85f, 0.6f) * 1.5f);
            }
            // The white crown/base trails are retired (authored slashes replace them); the crown transform is still
            // used as an effect anchor, so only the rendering is switched off.
            crownTrail.enabled = baseTrail.enabled = false;

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
            body.gameObject.SetActive(true);   // shattered on death
            body.localPosition = Vector3.zero;
            crumbled = false;
            InitActor(Stats.maxHealth);
            Stamina = MaxStamina;
            stalemateReadyAt = gambitReadyAt = 0f;
            stalemateTimer = 0f;
            if (crownInFlight != null) { Destroy(crownInFlight.gameObject); crownInFlight = null; }
            Flasks = MaxFlasks;
            Enter(State.Normal);
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

        /// <summary>Stamina a light combo hit / the heavy attack costs (for the stats screen).</summary>
        public float LightAttackCost => (Style ?? AttackStyles.Default).lights[0].cost * LightCostScale * Mathf.Max(0f, 1f - Stats.attackCostCut);
        public float HeavyAttackCost => (Style ?? AttackStyles.Default).heavy.cost * Mathf.Max(0f, 1f - Stats.attackCostCut);
        bool HeavyAffordable => InfiniteStamina || Stamina >= HeavyAttackCost;

        /// <summary>Rolls a critical hit: true with <see cref="PlayerStats.critChance"/>; <paramref name="mult"/> is the damage factor.</summary>
        // Stagger (guard damage) per attack, whatever the style: the combo opens light and finishes heavy, the heavy
        // breaks guards, the Royal Throw (after a perfect dodge) most of all — its swing hits and its crash landing.
        const float StaggerLightEarly = 6f, StaggerLightMid = 10f, StaggerLightFinish = 20f;   // hits 1–2, 3, 4 (a full combo: 42)
        const float StaggerHeavy = 42f, StaggerCounterSwing = 55f, StaggerCounterThrow = 75f;
        float atkStagger;   // per hit window of the current attack

        /// <summary>Combo hit <paramref name="i"/> of <paramref name="count"/>: the last one heavy, the one before medium, the rest light.</summary>
        static float ComboStagger(int i, int count) =>
            i >= count - 1 ? StaggerLightFinish : i == count - 2 && count >= 3 ? StaggerLightMid : StaggerLightEarly;

        /// <summary>Critical hits stagger twice as much; attack power above 100% adds a little (+15% per +100%).</summary>
        float StaggerScale(bool crit) => (1f + 0.15f * Mathf.Max(0f, Stats.attackPower - 1f)) * Stats.staggerPower * (crit ? 2f : 1f);

        bool RollCrit(out float mult)
        {
            bool crit = Random.value < Stats.critChance;
            mult = crit ? Stats.critDamage : 1f;
            return crit;
        }

        /// <summary>Regains health (life steal, healing on a capture), up to the maximum.</summary>
        public void Restore(float hp)
        {
            if (IsDead || hp <= 0f) return;
            Health = Mathf.Min(MaxHealth, Health + hp);
        }

        /// <summary>Items picked this run, in order.</summary>
        public readonly List<Item> Items = new List<Item>();

        /// <summary>
        /// Stalemate: the lethal blow leaves the King at 1 health, invincible for <see cref="StalemateGuard"/> seconds, and
        /// every piece within <see cref="StalemateRadius"/> is stunned. Once a round.
        /// </summary>
        void TriggerStalemate()
        {
            stalemateReadyAt = Time.time + StalemateRecharge;
            stalemateTimer = StalemateGuard;
            Health = 1f;
            Flash(new Color(1.6f, 1.5f, 1.2f), 0.5f);
            Game.ShowBanner("스테일메이트!");
            Sfx.Play("PL_PerfectDodge");
            Game.HitStop(0.12f);
            Game.Rig.Shake(0.5f);
            Effects.Shockwave(FlatPos, StalemateRadius, Vfx.Accent, 0.5f);
            Effects.Sparks(transform.position + Vector3.up, Vfx.Accent, 30, 7f);
            foreach (var e in Game.Enemies)
            {
                if (e == null || e.IsDead || !e.Targetable) continue;
                if ((e.FlatPos - FlatPos).magnitude <= StalemateRadius + e.Radius) e.AddStagger(9999f, transform.position);
            }
        }

        // ---------------------------------------------------------------- Momentum (special item)

        const int MomentumMax = 5;
        const float MomentumStep = 0.05f;
        /// <summary>Momentum stacks: each capture without being hit (up to 5), lost when hit.</summary>
        public int Momentum { get; private set; }
        /// <summary>Attack and move speed factor from Momentum (×1 … ×1.25).</summary>
        float MomentumSpeed => 1f + MomentumStep * Momentum;

        /// <summary>A piece was captured (any way the King did it).</summary>
        public void OnCapture()
        {
            if (Has(Special.Momentum) && Momentum < MomentumMax) Momentum++;
        }

        // ---------------------------------------------------------------- Boomerang Crown (special item)

        const float CrownRange = 9f, CrownDamage = 30f, StaggerCrown = 20f, CrownInterval = 4f;
        CrownBoomerang crownInFlight;
        public bool CrownOut => crownInFlight != null;
        /// <summary>Seconds until the crown flies again (it counts from the moment it comes back).</summary>
        public float CrownCooldown { get; private set; }

        /// <summary>Every <see cref="CrownInterval"/> seconds after the catch, the crown flies at the nearest piece in reach.</summary>
        void UpdateCrown(float dt)
        {
            if (!Has(Special.BoomerangCrown) || crownInFlight != null) return;
            CrownCooldown = Mathf.Max(0f, CrownCooldown - dt);
            if (CrownCooldown > 0f) return;
            float reach = CrownRange * Stats.attackRange;
            EnemyBase target = null;
            float best = float.MaxValue;
            foreach (var e in Game.Enemies)
            {
                if (e == null || e.IsDead || !e.Targetable) continue;
                float d = (e.FlatPos - FlatPos).magnitude;
                if (d < best && d <= reach + e.Radius) { best = d; target = e; }
            }
            if (target == null) return;   // nothing in reach: thrown as soon as something is
            crownInFlight = CrownBoomerang.Spawn(this, target.FlatPos - FlatPos, reach);
            Sfx.Play("THR_Release", FlatPos);
        }

        /// <summary>The crown is back in the King's hand: the next throw in <see cref="CrownInterval"/> seconds.</summary>
        public void CatchCrown()
        {
            crownInFlight = null;
            CrownCooldown = CrownInterval;
            Sfx.Play("UI_Confirm", FlatPos);
            Effects.Sparks(transform.position + Vector3.up * 1.6f, Vfx.Accent, 6, 3f);
        }

        /// <summary>The flying crown passes through a piece.</summary>
        public void CrownStrike(EnemyBase e, Vector3 at)
        {
            bool crit = RollCrit(out float mult);
            float damage = CrownDamage * DamageMult * mult;
            if (!e.TakeHit(damage, StaggerCrown * StaggerScale(crit), at)) return;
            Game.ReportDamage(e, damage, crit);
            Vector3 dir = e.FlatPos - new Vector3(at.x, 0f, at.z);
            Vfx.Hit(e.Visual.position + Vector3.up * 1.0f, dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward, false, e.Visual);
            Sfx.Play(e.IsBoss ? "HIT_Boss" : "HIT_Light", e.transform.position + Vector3.up);
        }

        // ---------------------------------------------------------------- Black Aurora (special item)

        // A wide field around the King: pieces in it walk 40% slower and keep taking a little damage — (max health × 4%)
        // per second × the damage bonus. No crits, no stagger, no knockback.
        const float AuroraRadius = 6f, AuroraTick = 0.5f, AuroraSlow = 0.6f, AuroraHealthShare = 0.04f;
        AuroraFieldFx aurora;
        float auroraTimer;

        void UpdateAurora(float dt)
        {
            bool on = Has(Special.BlackAurora);
            if (!on)
            {
                if (aurora != null) Destroy(aurora.gameObject);
                return;
            }
            if (aurora == null) aurora = AuroraFieldFx.Spawn(transform, AuroraRadius);

            auroraTimer -= dt;
            if (auroraTimer > 0f) return;
            auroraTimer = AuroraTick;
            float damage = MaxHealth * AuroraHealthShare * (1f + Stats.damage) * AuroraTick;
            foreach (var e in Game.Enemies.ToArray())
            {
                if (e == null || e.IsDead || !e.Targetable) continue;
                if ((e.FlatPos - FlatPos).magnitude > AuroraRadius + e.Radius) continue;
                e.Slow(AuroraSlow, AuroraTick + 0.15f);
                // Tick damage shows its number (quietly: no popup sound every tick).
                e.TakeAuraDamage(damage, transform.position);
                Game.ReportDamage(e, damage, false, quiet: true);
            }
        }

        // ---------------------------------------------------------------- Thorn Armor (special item)

        const float ThornRadius = 4f, ThornBase = 10f, StaggerThorns = 10f, ThornCooldown = 2f;
        float thornReadyAt;   // the thorns fire at most once every 2 seconds

        /// <summary>
        /// Hit, the King bristles like a hedgehog: every piece within two squares takes (10 + defense) × the damage bonus
        /// (the 데미지 stat, not attack power) — the tougher and the harder-hitting the King, the more it hurts.
        /// </summary>
        void ThornBurst()
        {
            float damage = (ThornBase + Mathf.Max(0f, Stats.defense)) * (1f + Stats.damage);
            if (damage <= 0f) return;
            ThornSpikesFx.Spawn(transform, ThornRadius);
            Sfx.Play("HIT_Armor", transform.position + Vector3.up);
            foreach (var e in Game.Enemies)
            {
                if (e == null || e.IsDead || !e.Targetable) continue;
                if ((e.FlatPos - FlatPos).magnitude > ThornRadius + e.Radius) continue;
                if (!e.TakeHit(damage, StaggerThorns, transform.position)) continue;
                Game.ReportDamage(e, damage);
                Vector3 toE = (e.FlatPos - FlatPos).normalized;
                Vfx.Hit(e.Visual.position + Vector3.up * 1.0f, toE, false, e.Visual);
            }
        }

        // ---------------------------------------------------------------- Sword Wave (special item)

        const float SwordWaveReach = 7f, SwordWaveShare = 0.5f, StaggerSwordWave = 12f;
        bool atkSwordWave;   // the current attack sends sword waves (Sword Wave held, not the gap-closer dash)
        int atkComboHit;     // which light hit the current attack is (0-based), -1 for the heavy
        float swordWaveDamage;

        /// <summary>
        /// Every attack sends crescents of blade light through every piece in their path, sized by the attack range:
        /// hits 1–2 straight ahead, hit 3 the bishop's four diagonals, hit 4 the rook's four lines (along the board),
        /// the heavy all eight.
        /// </summary>
        void LaunchSwordWave()
        {
            float total = 0f;
            foreach (var h in atk.hits) total += h.damage;
            swordWaveDamage = total * SwordWaveShare * DamageMult * atkDamageMul;
            float reach = SwordWaveReach * Stats.attackRange;
            void Wave(Vector3 d) => SwordWaveFx.Spawn(this, FlatPos + d * 0.6f, d, reach, Stats.attackRange);
            if (atkComboHit < 0 || atkComboHit >= 2)
            {
                // Board directions: diagonals start at 45°, lines at 0°; the heavy fires both.
                bool lines = atkComboHit < 0 || atkComboHit == 3, diagonals = atkComboHit < 0 || atkComboHit == 2;
                for (int k = 0; k < 8; k++)
                {
                    bool diagonal = k % 2 == 1;
                    if (diagonal ? diagonals : lines) Wave(Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward);
                }
            }
            else Wave(transform.forward);
            Sfx.Play("THR_Release", FlatPos);
        }

        /// <summary>The flying sword wave passes through a piece.</summary>
        public void SwordWaveStrike(EnemyBase e, Vector3 at)
        {
            bool crit = RollCrit(out float mult);
            float damage = swordWaveDamage * mult;
            if (!e.TakeHit(damage, StaggerSwordWave * StaggerScale(crit), at)) return;
            Game.ReportDamage(e, damage, crit);
            Vector3 dir = e.FlatPos - new Vector3(at.x, 0f, at.z);
            Vfx.Hit(e.Visual.position + Vector3.up * 1.0f, dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward, false, e.Visual);
            RangedFx.Burst(e.Visual.position + Vector3.up * 1.0f, 1.6f, Color.white, 0.18f);
            Sfx.Play(e.IsBoss ? "HIT_Boss" : "HIT_Light", e.transform.position + Vector3.up);
        }

        // ---------------------------------------------------------------- Tempest (special item)

        // The 3rd light hit (the cyclone) becomes the finisher: far wider and harder, at twice its stamina; no 4th hit.
        const int WhirlHit = 2;
        const float WhirlRange = 1.8f, WhirlDamage = 1.6f, WhirlCost = 2f;
        // The Tempest cyclone is drawn out: a slow wind-up, then the spin a little slower than usual.
        const float WhirlWindupTempo = 0.4f, WhirlSpinTempo = 0.8f;
        float WhirlTempo => stateTime < atk.FirstHit ? WhirlWindupTempo : WhirlSpinTempo;
        bool atkWhirl;
        float atkRangeMul = 1f, atkDamageMul = 1f;   // the current attack's reach and damage factors

        /// <summary>Light hits in the combo before it starts over (the Tempest ends it at the cyclone).</summary>
        int ComboLength(AttackStyle style) => Has(Special.Tempest) && style.lights.Length > WhirlHit + 1 ? WhirlHit + 1 : style.lights.Length;

        /// <summary>
        /// A storm over the whole reach, in the NamuFX prefabs' own colours: a huge area slash layered twice (mirrored)
        /// and three big slashes around it, with dust, a shockwave and a heavier shake.
        /// </summary>
        void WhirlFx(HitWindow w, float reach)
        {
            float r = w.range * reach;
            Vector3 c = FlatPos;
            Quaternion face = transform.rotation;
            Vfx.Slash(SlashFxKind.AoeA, c + Vector3.up * 0.9f, face, 1.05f * reach);
            Vfx.Slash(SlashFxKind.AoeA, c + Vector3.up * 1.3f, face * Quaternion.Euler(0f, 90f, 0f), 0.9f * reach, null, true);
            float spin = Random.value * 360f;
            for (int k = 0; k < 3; k++)
            {
                Quaternion rot = Quaternion.Euler(0f, spin + k * 120f, 0f);
                Vfx.Slash(k % 2 == 0 ? SlashFxKind.SlashB : SlashFxKind.SlashA, c + rot * Vector3.forward * (r * 0.45f) + Vector3.up * 0.9f,
                    rot, 1.4f * reach, null, k % 2 == 1);
            }
            Vfx.Dust(c, r);
            Effects.Shockwave(c, r, Vfx.Accent, 0.35f);
            Game.Rig.Shake(0.45f);
        }

        /// <summary>
        /// The cyclone's second window: three more big slashes low over the board between the first ones, left lying
        /// on the squares as blade marks.
        /// </summary>
        void WhirlGroundFx(HitWindow w, float reach)
        {
            float r = w.range * reach;
            Vector3 c = FlatPos;
            float spin = transform.eulerAngles.y + 60f;
            for (int k = 0; k < 3; k++)
            {
                Quaternion rot = Quaternion.Euler(0f, spin + k * 120f, 0f);
                Vfx.Slash(k % 2 == 0 ? SlashFxKind.SlashA : SlashFxKind.SlashB, c + rot * Vector3.forward * (r * 0.5f) + Vector3.up * 0.25f,
                    rot, 1.4f * reach, null, k % 2 == 0);
            }
        }

        // ---------------------------------------------------------------- Chain Lightning (special item)

        const float ChainRange = 5f, ChainShare = 0.6f, StaggerChain = 8f, ChainCooldown = 0.25f;
        const int ChainJumps = 2;
        float chainReadyAt;

        /// <summary>
        /// A critical hit sends lightning leaping from the struck piece to the nearest other piece, and on from there
        /// (<see cref="ChainJumps"/> jumps, each <see cref="ChainShare"/> of the hit's base damage; the bolts never crit).
        /// </summary>
        void ChainLightning(EnemyBase from, float baseDamage)
        {
            if (Time.time < chainReadyAt) return;
            chainReadyAt = Time.time + ChainCooldown;
            var hit = new HashSet<EnemyBase> { from };
            var current = from;
            float damage = baseDamage * ChainShare;
            bool any = false;
            for (int j = 0; j < ChainJumps; j++)
            {
                EnemyBase next = null;
                float best = float.MaxValue;
                foreach (var e in Game.Enemies)
                {
                    if (e == null || e.IsDead || !e.Targetable || hit.Contains(e)) continue;
                    float d = (e.FlatPos - current.FlatPos).magnitude;
                    if (d < best && d <= ChainRange + e.Radius) { best = d; next = e; }
                }
                if (next == null) break;
                hit.Add(next);
                Vector3 a = current.Visual.position + Vector3.up * 1.1f, b = next.Visual.position + Vector3.up * 1.1f;
                RangedFx.Bolt(a, b);
                RangedFx.Burst(b, 1.5f, new Color(0.75f, 0.88f, 1f), 0.24f);
                if (j == 0) RangedFx.Burst(a, 1.2f, new Color(0.75f, 0.88f, 1f), 0.2f);
                if (next.TakeHit(damage, StaggerChain * Stats.staggerPower, current.transform.position))
                {
                    Game.ReportDamage(next, damage);
                    Effects.Sparks(b, new Color(0.75f, 0.88f, 1f), 5, 5f);
                }
                any = true;
                current = next;
            }
            if (any) Sfx.Play("HIT_Armor", from.transform.position + Vector3.up);
        }

        /// <summary>Holds the special item <paramref name="s"/>.</summary>
        public bool Has(Special s)
        {
            foreach (var it in Items) if (it.special == s) return true;
            return false;
        }

        /// <summary>
        /// What every blow is multiplied by: attack power × damage bonus, and with the Martyr's Crown up to ×1.5 as the
        /// King's health runs out.
        /// </summary>
        float DamageMult
        {
            get
            {
                float m = Stats.DamageMultiplier;
                if (Has(Special.MartyrCrown) && MaxHealth > 0f) m *= 1f + 0.5f * (1f - Mathf.Clamp01(Health / MaxHealth));
                return m;
            }
        }

        // Stalemate (special item): once a round a lethal blow leaves the King at 1 health, invincible for a moment.
        const float StalemateGuard = 2f, StalemateRadius = 4f;
        // Stalemate and Gambit (special items) work once, then are ready again after three minutes / a minute.
        const float StalemateRecharge = 180f, GambitRecharge = 60f;
        float stalemateReadyAt, gambitReadyAt;   // Gambit: a blow is ignored (once a minute)
        bool stalemateUsed => Time.time < stalemateReadyAt;
        bool gambitUsed => Time.time < gambitReadyAt;
        float stalemateTimer;

        /// <summary>
        /// A round-end item, kept for good: its gains apply right away (more health heals, a new flask or roll is
        /// ready, more stamina fills up); a drawback that lowers the maximum health cuts the health down with it.
        /// </summary>
        public void ApplyItem(Item item)
        {
            int flasksBefore = MaxFlasks, dodgesBefore = MaxDodges;
            float staminaBefore = MaxStamina;
            Upgrades.Apply(Stats, item);
            Items.Add(item);
            float hpGain = Stats.maxHealth - MaxHealth;
            MaxHealth = Stats.maxHealth;
            Health = hpGain > 0f ? Mathf.Min(MaxHealth, Health + hpGain) : Mathf.Min(MaxHealth, Health);
            Flasks = Mathf.Min(MaxFlasks, Flasks + Mathf.Max(0, MaxFlasks - flasksBefore));
            DodgeCharges = Mathf.Min(MaxDodges, DodgeCharges + Mathf.Max(0, MaxDodges - dodgesBefore));
            Stamina = MaxStamina > staminaBefore ? MaxStamina : Mathf.Min(Stamina, MaxStamina);
            // Potions handed over by the item (회복약 획득), up to what the belt holds.
            foreach (var fx in item.effects)
                if (fx.kind == UpgradeKind.Flask && fx.steps > 0) Flasks = Mathf.Min(MaxFlasks, Flasks + fx.steps);
        }
        /// <summary>무쌍 level-up: a fifth of the maximum health back and stamina full.</summary>
        public void OnLevelUp()
        {
            Restore(MaxHealth * 0.2f);
            Stamina = MaxStamina;
        }

        /// <summary>
        /// Between waves: one flask back and full stamina. No health back as such — but with every flask slot already
        /// full, the flask that would have come back is drunk at once (its heal amount restored).
        /// </summary>
        public void Rest()
        {
            stalemateReadyAt = gambitReadyAt = 0f;   // Stalemate and Gambit are ready again for the next round
            if (Flasks >= MaxFlasks && Health < MaxHealth)
            {
                Restore(HealAmount);
                Effects.Sparks(transform.position + Vector3.up, HealGreen, 18, 3f);
                Sfx.Play("PL_Heal", FlatPos);
            }
            Flasks = Mathf.Min(MaxFlasks, Flasks + 1);
            Stamina = MaxStamina;
            LockTarget = null;
            Flash(new Color(1f, 0.8f, 0.3f) * 1.5f, 0.4f);
        }

        /// <summary>Between rounds: warp back to the starting square (with a little sparkle at both ends).</summary>
        public void ReturnToStart(Vector3 pos, float yaw)
        {
            var gold = new Color(1f, 0.85f, 0.45f);
            Sfx.Play("PL_ReturnToStart", transform.position);
            Effects.Sparks(transform.position + Vector3.up, gold, 14, 3f);
            transform.SetPositionAndRotation(new Vector3(pos.x, 0f, pos.z), Quaternion.Euler(0f, yaw, 0f));
            if (!IsDead) Enter(State.Normal);
            ApplyPose(default);
            crownTrail.emitting = baseTrail.emitting = false;
            crownTrail.Clear();
            baseTrail.Clear();
            LockTarget = null;
            // Set down on e1 like a piece: the same ripple the black pieces make, in white.
            Effects.Wave(FlatPos, 2.4f, Color.white);
            Effects.Sparks(transform.position + Vector3.up, gold, 14, 3f);
        }

        /// <summary>Lab: start over from <paramref name="s"/> (items forgotten), full health, stamina, flasks and rolls.</summary>
        public void ResetStatsForLab(PlayerStats s)
        {
            Stats = s;
            InitActor(s.maxHealth);
            Stamina = MaxStamina;
            Flasks = MaxFlasks;
            DodgeCharges = MaxDodges;
            stalemateReadyAt = gambitReadyAt = 0f;
            stalemateTimer = 0f;
        }

        public void RefillForLab()
        {
            Health = MaxHealth;
            Flasks = MaxFlasks;
        }

        /// <summary>Queue an action as if the button had been pressed (used by the Attack Lab automation).</summary>
        /// <param name="held">Re-queued by a held button (no "denied" feedback sound for those).</param>
        public void Press(Buffered action, bool held = false)
        {
            if (!held)
            {
                // Denied presses get their own feedback sound.
                if (action == Buffered.Light && Stamina < 1f && !CounterReady) Sfx.Play("ATK_NoStamina");
                if (action == Buffered.Heavy && !HeavyAffordable) Sfx.Play("ATK_NoStamina");
                if (action == Buffered.Dodge && DodgeCharges <= 0) Sfx.Play("PL_Dodge_Empty");
                if (action == Buffered.Heal && Flasks <= 0) Sfx.Play("PL_Heal_Empty");
            }
            buffer = action;
            bufferTimer = BufferWindow;
        }

        void ApplyPose(BodyPose p) => p.Apply(body, CenterHeight, BaseRadius);

        void Update()
        {
            float dt = Time.deltaTime;
            if (stalemateTimer > 0f) stalemateTimer -= dt;
            // The perfect dodge's guard runs on the King's own clock (slow motion doesn't stretch it).
            if (perfectGuard > 0f) perfectGuard -= Game != null ? Game.PlayerDeltaTime : dt;
            if (state == State.Dead)
            {
                stateTime += dt;
                UpdateDeath();
                return;
            }
            if (Game == null) return;
            // After a round is cleared the King finishes whatever he was doing (input ignored) instead of freezing.
            bool roundOver = Game.State == GameManager.GameState.WaveClear;
            if (Game.State != GameManager.GameState.Playing && !roundOver) return;
            InputEnabled = !roundOver;

            dt = DeltaTime;
            if (InputEnabled) ReadInput(dt);
            else { buffer = Buffered.None; LockTarget = null; }
            // Attack speed plays the whole attack (wind-up, swing, recovery, cancel points) faster.
            float atkDt = state == State.Attack ? dt * Stats.attackSpeed * MomentumSpeed * (atkWhirl ? WhirlTempo : 1f) : dt;
            stateTime += atkDt;
            UpdateStamina(dt);
            UpdateDodgeCharges(dt);
            UpdateLockTarget();
            if (!roundOver)
            {
                UpdateCrown(dt);
                UpdateAurora(dt);
            }
            if (justTarget != null && !Game.SlowMoActive) justTarget = null;
            UpdateWarnings();

            switch (state)
            {
                case State.Normal: UpdateNormal(dt); break;
                case State.Attack: UpdateAttack(atkDt); break;
                case State.Dodge: UpdateDodge(dt); break;
                case State.Heal: UpdateHeal(dt); break;
                case State.Stagger: UpdateStagger(dt); break;
                case State.Counter: UpdateCounter(dt); break;
            }

            if (state != State.Normal) IsSprinting = false;
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
                if (GameInput.LightHeld) Press(Buffered.Light, true);
                else if (GameInput.HeavyHeld) Press(Buffered.Heavy, true);
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
                    // King's Dance (special item): no heavy button — the heavy comes as the light combo's 5th hit.
                    if (Has(Special.KingsDance)) { buffer = Buffered.None; return false; }
                    // The heavy needs its whole stamina cost up front (the light combo only needs some left).
                    if (!HeavyAffordable) return false;
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

        /// <summary>Edge-triggered sounds: lock-on changes, low health, low stamina.</summary>
        void UpdateWarnings()
        {
            if (LockTarget != prevLock)
            {
                Sfx.Play(LockTarget == null ? "UI_LockOff" : prevLock == null ? "UI_LockOn" : "UI_LockSwitch");
                prevLock = LockTarget;
            }
            bool lowHp = Health < MaxHealth * 0.3f;
            if (lowHp && !lowHpWarned) Sfx.Play("PL_LowHP_Loop");
            lowHpWarned = lowHp;
            bool lowSt = Stamina < MaxStamina * 0.25f;
            if (lowSt && !lowStaminaWarned) Sfx.Play("UI_StaminaLow");
            lowStaminaWarned = lowSt;
        }

        void UpdateDodgeCharges(float dt)
        {
            if (DodgeCharges >= MaxDodges) { dodgeRecharge = 0f; return; }
            dodgeRecharge += dt;
            if (dodgeRecharge >= DodgeRechargeTime)
            {
                DodgeCharges++;
                dodgeRecharge = 0f;
                Sfx.Play("PL_Dodge_Recharge");
            }
        }

        void UpdateStamina(float dt)
        {
            if (state == State.Attack || state == State.Dodge) return;
            if (staminaDelay > 0f) { staminaDelay -= dt; return; }
            float rate = (state == State.Heal ? StaminaRegen * 0.5f : StaminaRegen) * Stats.staminaRegen;
            Stamina = Mathf.Min(MaxStamina, Stamina + rate * dt);
        }

        Vector3 InputDirection()
        {
            if (!InputEnabled) return Vector3.zero;
            if (DebugMove.sqrMagnitude > 0.0001f) return DebugMove;
            Vector2 mi = GameInput.Move;
            var rig = Game.Rig;
            return rig.FlatRight * mi.x + rig.FlatForward * mi.y;
        }

        // ---------------------------------------------------------------- lock-on

        /// <summary>Lock onto a specific piece (used by the Attack Lab automation).</summary>
        public void LockOn(EnemyBase target) => LockTarget = target;

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
            if (state == State.Heal && s != State.Heal)
            {
                Vfx.Release(healFx);
                healFx = null;
            }
            state = s;
            stateTime = 0f;
        }

        void UpdateNormal(float dt)
        {
            if (TryAct()) return;

            Vector3 dir = InputDirection();
            float m = dir.magnitude;
            // Sprint: hold Shift / L3 while moving. Drains stamina; stops when it runs out.
            IsSprinting = (GameInput.SprintHeld || DebugSprint) && m > 0.3f && Stamina > 0f && (!sprintExhausted || Stamina > SprintRecoverAt);
            if (IsSprinting)
            {
                sprintExhausted = false;
                if (!InfiniteStamina) Stamina = Mathf.Max(0f, Stamina - SprintDrain * dt);
                staminaDelay = StaminaDelay * 0.5f;
                if (Stamina <= 0f) sprintExhausted = true;
            }

            float speed = (IsSprinting ? SprintSpeed : LockTarget != null ? LockedMoveSpeed : MoveSpeed) * Stats.moveSpeed * MomentumSpeed;
            transform.position += dir * (speed * dt);

            // Sprinting breaks the lock-on facing, like souls games.
            if (LockTarget != null && !IsSprinting) FaceTowards(LockTarget.FlatPos - FlatPos, 900f);
            else if (dir.sqrMagnitude > 0.01f) FaceTowards(dir, IsSprinting ? 900f : 720f);

            // Chess-piece hop while walking, leaning into the movement (bigger, faster hops when sprinting).
            float run = IsSprinting ? 1f : 0f;
            sprintBlend = Mathf.Lerp(sprintBlend, run, 1f - Mathf.Exp(-8f * dt));
            prevMove = Mathf.Lerp(prevMove, m, 1f - Mathf.Exp(-10f * dt));
            bobPhase += dt * Mathf.Lerp(10f, 15f, sprintBlend) * m;
            // A footstep each time the hop touches down; a whoosh when a sprint starts.
            int step = Mathf.FloorToInt(bobPhase / Mathf.PI);
            if (m > 0.05f && step != lastStep)
            {
                Sfx.Play(IsSprinting ? "PL_Step_Sprint" : "PL_Step", FlatPos);
                // Sprinting: every footstep kicks up a puff of dust behind the King.
                if (IsSprinting) Vfx.StepDust(FlatPos, -dir);
            }
            lastStep = step;
            if (IsSprinting && !prevSprint) Sfx.Play("PL_Sprint_Start", FlatPos);
            prevSprint = IsSprinting;
            float hop = m > 0.05f ? Mathf.Abs(Mathf.Sin(bobPhase)) * Mathf.Lerp(0.12f, 0.22f, sprintBlend) : 0f;
            float heading = 0f;
            if (m > 0.05f) heading = Vector3.SignedAngle(transform.forward, dir, Vector3.up);
            ApplyPose(BodyPose.P(tilt: Mathf.Lerp(7f, 18f, sprintBlend) * prevMove, heading: heading, lift: hop));
            // Walking: a few grains of dust at the feet (sprinting adds the footstep puffs above).
            if (m > 0.05f && Random.value < 0.25f)
                Effects.Sparks(FlatPos + Vector3.up * 0.05f, new Color(0.6f, 0.55f, 0.5f), 1, 2f);
        }

        void StartAttack(bool heavy)
        {
            var style = Style ?? AttackStyles.Default;
            bool chain = state == State.Attack && !atk.heavy && !heavy;
            // King's Dance (special item): after the 4th light hit the combo goes on into the heavy, at half its stamina.
            bool danceFinisher = chain && Has(Special.KingsDance) && comboIndex == ComboLength(style) - 1 &&
                                 (InfiniteStamina || Stamina >= HeavyAttackCost * 0.5f);
            if (danceFinisher) heavy = true;
            EnemyBase gapTarget = null;
            atkIsGapCloser = false;
            if (heavy) atk = HeavyMotionTrial ?? style.heavy;
            else if (!chain && style.gapCloser != null && TryFindGapTarget(style, out gapTarget, out float gap))
            {
                // Mid range: dash in first; the combo continues with hit 1 (index -1 + 1 = 0).
                comboIndex = -1;
                atk = style.gapCloser(gap + GapOvershoot);
                atkIsGapCloser = true;
            }
            else
            {
                comboIndex = chain ? (comboIndex + 1) % ComboLength(style) : 0;
                atk = style.lights[comboIndex];
            }
            // Stagger this attack deals, spread over its hit windows.
            float stagger = heavy ? StaggerHeavy : atkIsGapCloser ? StaggerLightEarly : ComboStagger(comboIndex, ComboLength(style));
            atkStagger = stagger / Mathf.Max(1, atk.hits.Length);
            atkSwordWave = !atkIsGapCloser && Has(Special.SwordWave);
            atkWhirl = !heavy && !atkIsGapCloser && comboIndex == WhirlHit && Has(Special.Tempest) && style.lights.Length > WhirlHit;
            atkRangeMul = atkWhirl ? WhirlRange : 1f;
            atkDamageMul = atkWhirl ? WhirlDamage : 1f;
            atkComboHit = heavy ? -1 : comboIndex;
            Consume(heavy ? atk.cost * Mathf.Max(0f, 1f - Stats.attackCostCut) * (danceFinisher ? 0.5f : 1f)
                          : atk.cost * LightCostScale * Mathf.Max(0f, 1f - Stats.attackCostCut) * (atkWhirl ? WhirlCost : 1f));
            atkUsesSlashFx = FxLibrary.Instance != null && System.Array.Exists(atk.hits, h => h.fx != SlashFxKind.None);
            hitStopUsed = false;

            if (hitSets.Length < atk.hits.Length)
            {
                hitSets = new HashSet<EnemyBase>[atk.hits.Length];
                windowStarted = new bool[atk.hits.Length];
                sparksEmitted = new int[atk.hits.Length];
            }
            for (int i = 0; i < hitSets.Length; i++)
            {
                hitSets[i] ??= new HashSet<EnemyBase>();
                hitSets[i].Clear();
                windowStarted[i] = false;
                sparksEmitted[i] = 0;
            }
            Enter(State.Attack);
            Sfx.Play(atk.sfx, FlatPos);

            Vector3 dir = InputDirection();
            // Facing: the gap-closer's target, then the lock-on target, then the player's own input direction. Only with
            // no input does auto-aim pick the direction that hits the most nearby pieces (far away it does nothing).
            bool steering = dir.sqrMagnitude > 0.01f;
            if (gapTarget != null) dir = gapTarget.FlatPos - FlatPos;
            else if (LockTarget != null) dir = LockTarget.FlatPos - FlatPos;
            else if (!steering && TryFindAssistDirection(transform.forward, out var best)) dir = best;
            if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z));
        }

        /// <summary>
        /// A piece is in "mid range" when combo hit 1 can't reach anything from here, but one is within the style's
        /// gap-closer range (the lock-on target, or the closest one roughly in the aimed direction).
        /// </summary>
        bool TryFindGapTarget(AttackStyle style, out EnemyBase target, out float gap)
        {
            target = null;
            gap = 0f;
            var first = style.lights[0];
            Vector3 input = InputDirection();
            Vector3 preferred = input.sqrMagnitude > 0.01f ? input : transform.forward;
            float best = float.MaxValue;
            foreach (var e in Game.Enemies)
            {
                if (e == null || e.IsDead || !e.Targetable) continue;
                Vector3 to = e.FlatPos - FlatPos;
                float d = to.magnitude;
                if (d < 0.01f) return false;
                if (first.WouldHit(FlatPos, to, e.FlatPos, e.Radius, Stats.attackRange, Motion)) return false;  // hit 1 already reaches someone
                // The dash reach shrinks and grows with the attack range, like the hits themselves.
                if (d > style.gapCloserMaxRange * Stats.attackRange + e.Radius) continue;
                // With input, only dash at a piece roughly where the player is pointing.
                float limit = input.sqrMagnitude > 0.01f ? GapInputAngle : GapAngle;
                if (LockTarget != null ? e != LockTarget : Vector3.Angle(preferred, to) > limit) continue;
                if (d < best) { best = d; target = e; }
            }
            if (target == null) return false;
            gap = best - (Radius + target.Radius + GapStopDistance);
            if (gap > 0.5f) return true;
            target = null;
            return false;
        }

        /// <summary>
        /// Picks the facing that makes the current attack hit the most pieces in assist range.
        /// Candidates: toward each nearby piece and between every pair of them; the player's own aim
        /// only breaks ties. Returns false when nothing is close enough to assist.
        /// </summary>
        bool TryFindAssistDirection(Vector3 preferred, out Vector3 best)
        {
            best = Vector3.zero;
            float range = Mathf.Clamp(atk.Reach * Stats.attackRange + 0.6f, AssistMinRange, AssistMaxRange);
            assistNearby.Clear();
            foreach (var e in Game.Enemies)
                if (e != null && !e.IsDead && e.Targetable && (e.FlatPos - FlatPos).magnitude <= range + e.Radius)
                    assistNearby.Add(e);
            if (assistNearby.Count == 0) return false;

            assistCandidates.Clear();
            preferred.y = 0f;
            for (int i = 0; i < assistNearby.Count; i++)
            {
                Vector3 a = (assistNearby[i].FlatPos - FlatPos).normalized;
                assistCandidates.Add(a);
                for (int j = i + 1; j < assistNearby.Count; j++)
                {
                    Vector3 mid = a + (assistNearby[j].FlatPos - FlatPos).normalized;
                    if (mid.sqrMagnitude > 0.01f) assistCandidates.Add(mid.normalized);
                }
            }

            int bestHits = 0;
            float bestScore = float.MinValue;
            foreach (var c in assistCandidates)
            {
                if (c.sqrMagnitude < 0.01f) continue;
                int hits = 0;
                float off = 0f;
                foreach (var e in Game.Enemies)
                    if (e != null && !e.IsDead && e.Targetable && atk.WouldHit(FlatPos, c, e.FlatPos, e.Radius, Stats.attackRange, Motion))
                    {
                        hits++;
                        off += Vector3.Angle(c, e.FlatPos - FlatPos);
                    }
                // Most pieces first; then the facing most centred on the pieces it hits (so a lone piece is faced
                // squarely), with the player's own aim only as a faint tie-break.
                float score = hits * 1000f - (hits > 0 ? off / hits : 0f) - 0.02f * Vector3.Angle(c, preferred);
                if (score > bestScore) { bestScore = score; bestHits = hits; best = c; }
            }

            if (bestHits == 0)
            {
                // Close but out of this attack's reach: still turn to the nearest piece.
                EnemyBase nearest = null;
                float nd = float.MaxValue;
                foreach (var e in assistNearby)
                {
                    float d = (e.FlatPos - FlatPos).magnitude;
                    if (d < nd) { nd = d; nearest = e; }
                }
                best = (nearest.FlatPos - FlatPos).normalized;
            }
            return true;
        }

        /// <summary>A piece right in front of the King stops the attack's forward lunge.</summary>
        bool LungeBlocked()
        {
            foreach (var e in Game.Enemies)
            {
                if (e == null || e.IsDead || e.IgnoresCollision) continue;
                Vector3 to = e.FlatPos - FlatPos;
                if (to.magnitude < Radius + e.Radius + 0.3f && Vector3.Angle(transform.forward, to) < 50f) return true;
            }
            return false;
        }

        /// <summary>
        /// Attack motion multiplier: 1 = the motion as tuned (dynamism 120%). The endless King's dynamism runs 80–140% with
        /// his attack power, speed and range, i.e. ×0.67 … ×1.17 on the lean, hop, squash, lunge and camera kick.
        /// </summary>
        public float Motion => Stats.Dynamism / 1.2f;

        /// <summary>The pose with its lean, lift and squash scaled (full turns — spins, flips — stay whole).</summary>
        static BodyPose Dynamic(BodyPose p, float k)
        {
            p.tilt *= k;
            p.lift *= k;
            p.squash *= k;
            return p;
        }

        void UpdateAttack(float dt)
        {
            float t = stateTime, prevT = t - dt;

            // Keep tracking the best direction during the wind-up (enemies move).
            if (t < atk.FirstHit)
            {
                if (LockTarget != null) FaceTowards(LockTarget.FlatPos - FlatPos, 540f);
                // Leaping attacks (meteor, stomp) commit to the facing they started with: no auto-aim turning in the air.
                // Steering with the stick also keeps the facing the player chose.
                else if (!atk.lockFacing && InputDirection().sqrMagnitude <= 0.01f && TryFindAssistDirection(transform.forward, out var best)) FaceTowards(best, 720f);
            }

            // Dodge can cancel the attack at any time except during its first active hit frames.
            bool inActive = atk.hits.Length > 0 && t >= atk.FirstHit && t <= atk.hits[0].end;
            if (buffer == Buffered.Dodge && !inActive && TryAct()) return;

            // Root motion from move segments (stronger / weaker with the motion's dynamism).
            float dyn = Motion, reach = Stats.attackRange * atkRangeMul;
            float lunge = atkIsGapCloser ? 1f : dyn;
            bool blocked = LungeBlocked();
            if (!blocked)
            {
                foreach (var m in atk.moves)
                {
                    float a = PoseTrack.Curve(m.ease, (prevT - m.start) / (m.end - m.start));
                    float b = PoseTrack.Curve(m.ease, (t - m.start) / (m.end - m.start));
                    transform.position += transform.forward * (m.distance * lunge * (b - a));
                }
            }

            ApplyPose(Dynamic(atk.pose.Eval(t), dyn));

            // Authored effects, the ground range arc, dust and impacts all scale with the attack range.
            if (atk.cues != null)
                foreach (var cue in atk.cues)
                    if (prevT < cue.time && t >= cue.time)
                    {
                        Sfx.Play(cue.sfx, FlatPos);
                        Vfx.Slash(cue.kind, transform.position + Vector3.up * cue.height, transform.rotation * Quaternion.Euler(0f, cue.yaw, cue.roll),
                            cue.scale * reach, cue.gold ? Vfx.Accent : (Color?)null, cue.mirror);
                    }

            bool trail = t >= atk.FirstHit - 0.07f && t <= atk.LastHit + 0.03f;
            // An authored slash replaces the crown trail for this attack.
            crownTrail.emitting = trail && !atkUsesSlashFx;
            baseTrail.emitting = trail && atk.heavy;

            for (int i = 0; i < atk.hits.Length; i++)
            {
                var w = atk.hits[i];
                if (t < w.start || t > w.end + 0.02f) continue;
                if (!windowStarted[i])
                {
                    windowStarted[i] = true;
                    Sfx.Play(w.sfx, FlatPos);
                    if (w.fx != SlashFxKind.None)
                        Vfx.Slash(w.fx, transform.position + Vector3.up * w.fxHeight, transform.rotation * Quaternion.Euler(0f, w.fxYaw, 0f),
                            w.fxScale * reach, w.fxGold ? Vfx.Accent : (Color?)null, w.fxFlip);
                    // Ground range marker (translucent black/white arc) shows the attack's real reach: the same centre and
                    // radius DoHits uses, sweeping over the window's real duration (attack speed plays it faster).
                    Vector3 hitCenter = FlatPos + transform.forward * (w.forward * reach);
                    if (w.slash)
                        SlashFx.Spawn(hitCenter, transform.forward, w.range * reach * 0.82f,
                            w.sweepFrom, w.sweepTo, (w.end - w.start) / Mathf.Max(0.1f, Stats.attackSpeed), new Color(1f, 0.82f, 0.45f));
                    if (w.kick > 0f) Game.Rig.Kick(w.kick * dyn);
                    if (w.dust) Vfx.Dust(hitCenter, w.range * reach);
                    // White sparks thrown up all around the strike area.
                    for (int k = 0; k < w.sparkRing; k++)
                    {
                        Vector3 around = Quaternion.Euler(0f, Random.value * 360f, 0f) * Vector3.forward * (w.range * reach * Random.Range(0.2f, 0.95f));
                        Effects.Sparks(hitCenter + around + Vector3.up * 0.2f, Vfx.Accent, 1, 6f);
                    }
                    if (w.impact)
                    {
                        Vfx.GroundImpact(hitCenter, w.range * reach, atk.heavy ? 1.4f : 1f);
                        Game.Rig.Shake(w.shake * dyn);
                    }
                    // Sword Wave (special item): each attack's last window sends the blade light flying.
                    if (atkSwordWave && i == atk.hits.Length - 1) LaunchSwordWave();
                    // Tempest (special item): the 3rd hit's first window opens with a storm of NamuFX slashes over the whole reach.
                    if (atkWhirl) { if (i == 0) WhirlFx(w, reach); else WhirlGroundFx(w, reach); }
                }
                if (w.sparkTrail)
                {
                    // Spread evenly over the window: the same shower at any frame rate or attack speed.
                    float wp = Mathf.Clamp01((t - w.start) / Mathf.Max(0.001f, w.end - w.start));
                    int owed = Mathf.RoundToInt(wp * SparkTrailCount) - sparksEmitted[i];
                    if (owed > 0)
                    {
                        Effects.Sparks(crownTrail.transform.position, Vfx.Accent, owed, 6f);
                        sparksEmitted[i] += owed;
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
            int hitCount = 0;
            // Attack range scales the reach (and how far ahead the hit is centred); attack power the damage.
            float range = w.range * Stats.attackRange * atkRangeMul, damage = w.damage * DamageMult * atkDamageMul;
            Vector3 center = FlatPos + transform.forward * (w.forward * Stats.attackRange * atkRangeMul);
            scratch.Clear();
            scratch.AddRange(Game.Enemies);
            foreach (var e in scratch)
            {
                if (e == null || e.IsDead || already.Contains(e)) continue;
                Vector3 to = e.FlatPos - center;
                float d = to.magnitude;
                if (d > range + e.Radius) continue;
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
                bool crit = RollCrit(out float mult);
                // Execution (special item): a heavy on a stunned piece deals double.
                if (atk.heavy && Has(Special.Execution) && e.IsStunned) mult *= 2f;
                if (e.TakeHit(damage * mult, atkStagger * StaggerScale(crit), transform.position))
                {
                    landed = true;
                    Game.ReportDamage(e, damage * mult, crit);
                    Vector3 toE = (e.FlatPos - FlatPos).normalized;
                    // The hit flash/burst sits just inside the struck piece's near side (not out at its radius).
                    Vfx.Hit(e.Visual.position + Vector3.up * 1.0f - toE * (e.Radius * 0.3f), toE, atk.heavy, e.Visual);
                    Sfx.Play(e.IsBoss ? "HIT_Boss" : atk.heavy ? "HIT_Heavy" : "HIT_Light", e.transform.position + Vector3.up);
                    hitCount++;
                    if (w.launch) e.ForceLaunch(transform.position);
                    if (crit && Has(Special.ChainLightning)) ChainLightning(e, damage);
                }
            }
            if (landed)
            {
                if (hitCount >= 3) Sfx.Play("HIT_Multi", center);
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
            Vector3 dir = AutoDodgeDirection(InputDirection());
            bool backstep = dir.sqrMagnitude <= 0.01f;
            dodgeDir = backstep ? -transform.forward : dir.normalized;
            LastDodgeDirection = dodgeDir;
            Sfx.Play("PL_Dodge", FlatPos);
            if (!backstep) transform.rotation = Quaternion.LookRotation(dodgeDir);
            Enter(State.Dodge);
        }

        /// <summary>
        /// Steers the roll out of the most imminent attack aimed at the King:
        /// across a dash/lunge line (perpendicular to its path), or out of a leap/slam circle.
        /// Free input that already escapes is kept; no input rolls along the escape instead of backstepping.
        /// </summary>
        Vector3 AutoDodgeDirection(Vector3 input)
        {
            bool hasInput = input.sqrMagnitude > 0.01f;
            Vector3 escape = Vector3.zero;
            bool isLine = false;
            float soonest = float.MaxValue;
            foreach (var e in Game.Enemies)
            {
                if (e == null || !e.TryGetThreat(FlatPos, Radius, out var esc, out var line, out var tti)) continue;
                if (tti < soonest) { soonest = tti; escape = esc; isLine = line; }
            }
            if (escape == Vector3.zero) return input;

            if (isLine)
            {
                // Pick the side the player leans toward; otherwise stay on our side, unless that side is the board edge.
                float lean = hasInput ? Vector3.Dot(input.normalized, escape) : 0f;
                if (lean < -0.1f) escape = -escape;
                else if (Mathf.Abs(lean) <= 0.1f && !InsideBoard(FlatPos + escape * 2.5f)) escape = -escape;
                // Input clearly sideways already escapes the line; keep it.
                if (hasInput && Mathf.Abs(Vector3.Dot(input.normalized, escape)) >= 0.7f) return input;
                return escape;
            }

            // Circle: keep input unless it points back into the blast.
            if (hasInput && Vector3.Dot(input.normalized, escape) >= 0.2f) return input;
            return escape;
        }

        static bool InsideBoard(Vector3 p) =>
            Mathf.Abs(p.x) <= GameManager.PlayableHalf && Mathf.Abs(p.z) <= GameManager.PlayableHalf;

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
                if (!backstep) Sfx.Play("PL_Land", FlatPos);
                ApplyPose(default);
                Enter(State.Normal);
            }
        }

        void StartHeal()
        {
            buffer = Buffered.None;
            Flasks--;
            Game.ReportFlask();
            healed = false;
            Enter(State.Heal);
            // Tazo FX healing aura while drinking (falls back to the old gold sparks when the pack isn't there).
            healFx = Vfx.Attach(FxLibrary.Instance != null ? FxLibrary.Instance.recoverHold : null, transform, Vector3.up * 0.35f, 0.8f, 1.1f);   // played at 1.1× speed
            SetGlow((healFx != null ? HealGreen : new Color(0.9f, 0.6f, 0.15f)) * 0.6f);
        }

        static readonly Color HealGreen = new Color(0.45f, 1f, 0.35f);

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
                Sfx.Play("PL_Heal", FlatPos);
                if (healFx != null) Flash(HealGreen * 1.6f, 0.25f);
                else
                {
                    Flash(new Color(1f, 0.8f, 0.3f) * 2f, 0.25f);
                    Effects.Sparks(transform.position + Vector3.up * 1.2f, new Color(1f, 0.8f, 0.3f), 14, 3f);
                    Vfx.Slash(SlashFxKind.SlashD, transform.position + Vector3.up * 0.3f, transform.rotation, 0.9f, new Color(1f, 0.8f, 0.4f));
                }
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
            // Lean away from the blow (whichever way the King faces), then spring back.
            float heading = Vector3.SignedAngle(transform.forward, hitAway, Vector3.up);
            ApplyPose(BodyPose.P(tilt: 24f * Mathf.Sin(p * Mathf.PI), heading: heading));
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

        /// <summary>Lab: a perfect dodge as if <paramref name="attacker"/>'s blow had just been rolled through.</summary>
        public void DebugPerfectDodge(EnemyBase attacker)
        {
            justThisDodge = false;
            TriggerJustDodge(attacker);
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
            // Reward: half the stamina bar back.
            Stamina = Mathf.Min(MaxStamina, Stamina + MaxStamina * 0.5f);
            Sfx.Play("PL_PerfectDodge");
            DodgeCharges = Mathf.Min(MaxDodges, DodgeCharges + 1);
            // Invincible for the whole slow motion: nothing that follows the dodged blow can land in the meantime.
            perfectGuard = SlowDuration;
            Game.SlowMo(SlowScale, SlowDuration);
            Game.Rig.Kick(0.45f);
            // No burst effects (wind ring, shockwave, sparks): slow motion, the screen tint and a cool flash on the King
            // carry the moment.
            Flash(new Color(0.6f, 1.2f, 2.2f), 0.25f);
        }

        // ---------------------------------------------------------------- royal throw (counter)

        // Against the queen (the boss) there is no throw: a short leap at her and one strong blow. Invincible throughout,
        // like every counter.
        const float LeapCounterRise = 0.3f, LeapCounterEnd = 0.62f, LeapCounterDamage = 55f, StaggerLeapCounter = 60f;
        bool leapCounter, leapStruck;

        void StartCounter()
        {
            buffer = Buffered.None;
            counterTarget = justTarget;
            justTarget = null;
            leapCounter = counterTarget.IsBoss;
            if (leapCounter)
            {
                leapStruck = false;
                LockTarget = counterTarget;
                Vector3 toQ = counterTarget.FlatPos - FlatPos;
                if (toQ.sqrMagnitude < 0.01f) toQ = transform.forward;
                counterFrom = FlatPos;
                counterTo = counterTarget.FlatPos - toQ.normalized * (counterTarget.Radius + Radius + 0.35f);
                transform.rotation = Quaternion.LookRotation(toQ);
                Sfx.Play("PL_Dodge", FlatPos);
                Enter(State.Counter);
                return;
            }
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
            counterSpin = Random.value < 0.5f ? 1f : -1f;
            swingSfx = false;
            inkArcs = 0;
            Sfx.Play("THR_Grab", counterTarget.FlatPos);
            transform.rotation = Quaternion.LookRotation(to);
            Enter(State.Counter);
        }

        /// <summary>The leap at the queen: rise and come down just in front of her, strike on landing, recover.</summary>
        void UpdateLeapCounter()
        {
            float t = stateTime;
            if (t < LeapCounterRise)
            {
                float p = t / LeapCounterRise;
                transform.position = Vector3.Lerp(counterFrom, counterTo, PoseTrack.Curve(Ease.Out, p));
                if (counterTarget != null) FaceTowards(counterTarget.FlatPos - FlatPos, 900f);
                ApplyPose(BodyPose.P(tilt: Mathf.Lerp(-10f, 24f, p), lift: Mathf.Sin(p * Mathf.PI) * 1.3f, squash: -0.08f * (1f - p)));
                crownTrail.emitting = true;
                return;
            }
            if (!leapStruck)
            {
                leapStruck = true;
                Game.EndSlowMo();
                if (counterTarget != null && !counterTarget.IsDead)
                {
                    bool crit = RollCrit(out float mult);
                    float damage = LeapCounterDamage * DamageMult * mult;
                    Vector3 toQ = (counterTarget.FlatPos - FlatPos).normalized;
                    if (counterTarget.TakeHit(damage, StaggerLeapCounter * StaggerScale(crit), transform.position))
                    {
                        Game.ReportDamage(counterTarget, damage, crit);
                        Vfx.Hit(counterTarget.Visual.position + Vector3.up * 1.4f - toQ * (counterTarget.Radius * 0.3f), toQ, true, counterTarget.Visual);
                        Vfx.Slash(SlashFxKind.Ink, FlatPos + Vector3.up * 1.2f, Quaternion.LookRotation(toQ), 1.3f, null);
                        Sfx.Play("HIT_Boss", counterTarget.transform.position + Vector3.up);
                    }
                }
                Vfx.GroundImpact(FlatPos, 1.6f, 0.8f);
                Game.HitStop(0.1f);
                Game.Rig.Kick(0.8f);
                Game.Rig.Shake(0.4f);
                LockTarget = null;
            }
            float r = Mathf.Clamp01((t - LeapCounterRise) / (LeapCounterEnd - LeapCounterRise));
            ApplyPose(BodyPose.Lerp(BodyPose.P(tilt: 30f, squash: -0.15f), default, PoseTrack.Curve(Ease.OutBack, r)));
            crownTrail.emitting = false;
            if (t >= LeapCounterEnd)
            {
                counterTarget = null;
                leapCounter = false;
                ApplyPose(default);
                Enter(State.Normal);
            }
        }

        void UpdateCounter(float dt)
        {
            float t = stateTime;
            if (leapCounter) { UpdateLeapCounter(); return; }
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
                if (!swingSfx) { swingSfx = true; Sfx.Play("THR_Swing_Loop", FlatPos); }
                float p = (t - CounterApproach) / (CounterRelease - CounterApproach);
                float angle = counterAngle0 + counterSpin * 720f * Mathf.Pow(p, 1.7f);
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 piecePos = FlatPos + dir * SwingRadius;
                counterTarget.SetGrabbedPose(piecePos, dir, 75f, 0.7f + 0.3f * Mathf.Sin(p * Mathf.PI));
                transform.rotation = Quaternion.LookRotation(dir);
                ApplyPose(BodyPose.P(tilt: -18f, squash: -0.06f));
                crownTrail.emitting = true;

                // Ink arcs trace the swing: one per half turn, each sweeping the half the piece is about to cross
                // (a NamuFX slash sweeps right → front → left of its facing, i.e. anticlockwise; mirrored for clockwise).
                float turned = Mathf.Abs(angle - counterAngle0);
                while (inkArcs < 4 && turned >= inkArcs * 180f)
                {
                    float mid = counterAngle0 + counterSpin * (inkArcs * 180f + 90f);
                    Vfx.Slash(SlashFxKind.Ink, FlatPos + Vector3.up * 1.0f, Quaternion.Euler(0f, mid, 0f), 1.5f, null, mirror: counterSpin > 0f);
                    inkArcs++;
                }

                if (Random.value < 0.6f)
                    Effects.Sparks(piecePos + Vector3.up * 1.2f, Vfx.Accent, 2, 4f);

                foreach (var e in Game.Enemies)
                {
                    if (e == null || e.IsDead || counterHits.Contains(e)) continue;
                    if ((e.FlatPos - piecePos).magnitude > counterTarget.Radius + e.Radius + 0.5f) continue;
                    counterHits.Add(e);
                    bool crit = RollCrit(out float mult);
                    float swingDamage = SwingHitDamage * DamageMult * mult;
                    if (e.TakeHit(swingDamage, StaggerCounterSwing * StaggerScale(crit), piecePos))
                    {
                        Game.ReportDamage(e, swingDamage, crit);
                        Sfx.Play("THR_SwingHit", e.transform.position + Vector3.up);
                        Effects.Sparks(e.transform.position + Vector3.up, Vfx.Accent, 14, 6f);
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
                    Sfx.Play("THR_Release", FlatPos);
                    Sfx.Play("THR_Fly", counterTarget.transform.position + Vector3.up);
                    Vector3 dir = counterTarget.FlatPos - FlatPos;
                    Game.EndSlowMo();
                    // The landing damage can crit too (rolled now, shown when it lands).
                    bool crit = RollCrit(out float mult);
                    counterTarget.Launch(dir, 17f, 7f, ThrowLandingDamage * DamageMult * mult, crit, StaggerCounterThrow * StaggerScale(crit));
                    Game.HitStop(0.12f);
                    Game.Rig.Kick(1f);
                    Game.Rig.Shake(0.5f);
                    Effects.Shockwave(FlatPos, 2.2f, Vfx.Accent, 0.4f);
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

        // ---------------------------------------------------------------- death

        Vector3 hitAway = Vector3.back;   // direction of the last blow (the King leans and slides that way)
        const float CrackTime = 0.14f;
        Vector3 deathAway;
        float deathForce = 1f;
        bool crumbled;

        /// <summary>Like the black pieces: a brief shudder, then the King shatters (white inside) along the killing blow.</summary>
        void UpdateDeath()
        {
            if (crumbled) return;
            if (stateTime < CrackTime)
            {
                body.localPosition = new Vector3(Random.Range(-0.05f, 0.05f), 0f, Random.Range(-0.05f, 0.05f));
                return;
            }
            crumbled = true;
            body.localPosition = Vector3.zero;
            Crumble.Spawn(body.GetComponentsInChildren<MeshRenderer>(), deathAway, new Color(0.93f, 0.91f, 0.86f), deathForce);
            body.gameObject.SetActive(false);
            Game.Rig.Shake(0.5f);
        }

        // ---------------------------------------------------------------- damage

        public override bool TakeHit(float damage, float poiseDamage, Vector3 source)
        {
            if (IsDead || IsInvulnerable || GodMode || Game.State != GameManager.GameState.Playing) return false;

            float taken = Stats.DamageTaken(damage);
            // Martyr's Crown: at 30% health or less, blows hurt 20% less.
            if (Has(Special.MartyrCrown) && Health <= MaxHealth * 0.3f) taken *= 0.8f;
            if (Has(Special.Gambit) && !gambitUsed)
            {
                // Gambit: a blow given up to a pawn — no damage (once a minute).
                gambitReadyAt = Time.time + GambitRecharge;
                Flash(new Color(1.4f, 1.4f, 1.3f), 0.25f);
                Sfx.Play("HIT_Armor", transform.position + Vector3.up);
                Effects.Shockwave(FlatPos, 1.6f, Vfx.Accent, 0.35f);
                Effects.Sparks(transform.position + Vector3.up, Vfx.Accent, 14, 5f);
                Game.ShowBanner("갬빗");
                return true;
            }
            taken = Mathf.Min(Health, taken);
            if (taken >= Health && Has(Special.Stalemate) && !stalemateUsed)
            {
                TriggerStalemate();
                return true;
            }
            Health -= taken;
            Momentum = 0;   // Momentum (special item) is lost on a hit
            if (taken > 0f && Has(Special.Thorns) && Time.time >= thornReadyAt)
            {
                thornReadyAt = Time.time + ThornCooldown;
                ThornBurst();
            }
            Game.ReportPlayerDamage(taken);
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
                Sfx.Play("PL_Death");
                transform.rotation = Quaternion.LookRotation(-away);
                deathAway = away;
                deathForce = Mathf.Clamp(damage / 18f, 0.8f, 2.5f);
                crumbled = false;
                Enter(State.Dead);
                Game.OnPlayerDied();
            }
            else
            {
                hitAway = away;
                knockback = away * (5.5f + poiseDamage * 0.08f);   // shoved away from the blow
                Sfx.Play(damage >= 24f ? "PL_Hurt_Heavy" : "PL_Hurt", FlatPos);
                Enter(State.Stagger);
            }
            return true;
        }
    }
}

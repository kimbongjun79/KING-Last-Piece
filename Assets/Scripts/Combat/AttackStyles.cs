using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Procedural pose of the King's body. The piece leans about the rim of its base in the lean
    /// direction (so it never sinks into the board), can spin around its own axis, hop, flip and squash.
    /// </summary>
    public struct BodyPose
    {
        public float tilt;     // lean angle in degrees (negative = lean opposite to heading)
        public float heading;  // lean direction relative to facing, degrees (0 = forward, 90 = right)
        public float spin;     // yaw of the body around its own axis
        public float flip;     // forward somersault about the body center
        public float lift;     // vertical offset
        public float squash;   // vertical scale offset (0 = none, -0.2 = squashed)

        public static BodyPose Lerp(BodyPose a, BodyPose b, float t) => new BodyPose
        {
            tilt = Mathf.LerpUnclamped(a.tilt, b.tilt, t),
            heading = Mathf.LerpUnclamped(a.heading, b.heading, t),
            spin = Mathf.LerpUnclamped(a.spin, b.spin, t),
            flip = Mathf.LerpUnclamped(a.flip, b.flip, t),
            lift = Mathf.LerpUnclamped(a.lift, b.lift, t),
            squash = Mathf.LerpUnclamped(a.squash, b.squash, t),
        };

        public static BodyPose P(float tilt = 0f, float heading = 0f, float spin = 0f, float flip = 0f, float lift = 0f, float squash = 0f) =>
            new BodyPose { tilt = tilt, heading = heading, spin = spin, flip = flip, lift = lift, squash = squash };

        /// <summary>Applies the pose to a body transform whose rest position is (0, centerHeight, 0) under the root.</summary>
        public void Apply(Transform body, float centerHeight, float baseRadius)
        {
            float s = 1f + squash;
            Vector3 dir = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            Quaternion lean = Quaternion.AngleAxis(tilt, Vector3.Cross(Vector3.up, dir));
            Vector3 pivot = dir * (baseRadius * (tilt >= 0f ? 1f : -1f));
            Vector3 center = new Vector3(0f, centerHeight * s, 0f);

            body.localPosition = pivot + lean * (center - pivot) + Vector3.up * lift;
            body.localRotation = lean * Quaternion.AngleAxis(flip, Vector3.right) * Quaternion.Euler(0f, spin, 0f);
            body.localScale = new Vector3(1f / Mathf.Sqrt(s), s, 1f / Mathf.Sqrt(s));
        }
    }

    public enum Ease { Linear, In, Out, InOut, OutBack }

    public class PoseTrack
    {
        readonly List<float> times = new List<float>();
        readonly List<BodyPose> poses = new List<BodyPose>();
        readonly List<Ease> eases = new List<Ease>();

        /// <summary>Adds a key; the ease shapes the segment arriving at this key.</summary>
        public PoseTrack Key(float t, BodyPose p, Ease e = Ease.InOut)
        {
            times.Add(t);
            poses.Add(p);
            eases.Add(e);
            return this;
        }

        public BodyPose Eval(float t)
        {
            if (times.Count == 0) return default;
            if (t <= times[0]) return poses[0];
            for (int i = 1; i < times.Count; i++)
            {
                if (t <= times[i])
                {
                    float u = (t - times[i - 1]) / Mathf.Max(0.0001f, times[i] - times[i - 1]);
                    return BodyPose.Lerp(poses[i - 1], poses[i], Curve(eases[i], u));
                }
            }
            return poses[poses.Count - 1];
        }

        public static float Curve(Ease e, float u)
        {
            u = Mathf.Clamp01(u);
            switch (e)
            {
                case Ease.In: return u * u * u;
                case Ease.Out: return 1f - (1f - u) * (1f - u) * (1f - u);
                case Ease.InOut: return u * u * (3f - 2f * u);
                case Ease.OutBack:
                    const float c1 = 1.9f, c3 = c1 + 1f;
                    float v = u - 1f;
                    return 1f + c3 * v * v * v + c1 * v * v;
                default: return u;
            }
        }
    }

    public enum HitShape { Arc, Circle }

    public class HitWindow
    {
        public float start, end;
        public HitShape shape = HitShape.Arc;
        public float forward;          // shape center, ahead of the King
        public float range = 2.4f;
        public float arc = 360f;       // Arc only
        public float damage = 12f, poise = 12f;
        public float hitStop = 0.05f, shake = 0.12f;
        public bool impact;            // ground impact: shockwave + shake even on whiff
        public bool impactAtHead;       // project the King's tilted crown contact onto the board
        public float impactRadius;      // visual ripple/dust radius; zero uses the hit range
        public bool frontOnly;          // reject pieces behind the King, including those inside the close-range arc exception
        public bool sweep;             // Arc only: hits land progressively from sweepFrom to sweepTo (degrees, + = right)
        public float sweepFrom, sweepTo;
        public bool slash;             // draw a ground crescent along the sweep (shows the real reach)
        public bool launch;            // knock non-boss pieces airborne regardless of their stagger gauge (and armor)
        public float kick;             // camera zoom punch on window start
        public bool sparkTrail;        // spray sparks from the crown while the window is active
        public int sparkRing;          // white sparks thrown up all around the strike area when the window opens
        // Authored slash effect (NamuFX) played when the window starts, aligned with the King's facing.
        public SlashFxKind fx;
        public float fxScale = 1f, fxYaw, fxHeight = 0.9f;
        public bool fxFlip;            // mirrored (negative X scale) = the arc sweeps the other way
        public bool fxGold;            // recolor the prefab to the King's accent (white)
        public bool dust;              // low dust burst around the impact (NamuFX smoke layers)
        public string sfx;             // sound id played when the window starts (Docs/Sound/sounds.json)

        public bool Covers(Vector3 origin, Vector3 center, Vector3 facing, Vector3 target, float targetRadius, float reach, float progress)
        {
            Vector3 to = target - center;
            float d = to.magnitude;
            if (d > reach + targetRadius || (frontOnly && Vector3.Dot(facing, target - origin) <= 0f)) return false;
            if (shape != HitShape.Arc || d <= 0.4f) return true;
            if (Vector3.Angle(facing, to) > arc * 0.5f) return false;
            if (!sweep) return true;
            float ang = Vector3.SignedAngle(facing, to, Vector3.up);
            float cur = Mathf.Lerp(sweepFrom, sweepTo, progress);
            float lo = Mathf.Min(sweepFrom, cur) - 15f, hi = Mathf.Max(sweepFrom, cur) + 15f;
            return ang >= lo && ang <= hi;
        }
    }

    /// <summary>An authored effect played at a fixed time into an attack, aligned with the King's facing.</summary>
    public struct FxCue
    {
        public float time;
        public SlashFxKind kind;
        public float scale, height, yaw;
        public float roll;         // about the facing axis: 180 turns a bottom-to-top sweep into top-to-bottom
        public bool gold, mirror;
        public float lifeScale;     // zero uses the prefab's authored particle lifetime
        public string sfx;         // sound id played at this time (kind may be None for a sound-only cue)
    }

    public struct MoveSeg
    {
        public float start, end, distance;
        public Ease ease;
        public MoveSeg(float start, float end, float distance, Ease ease = Ease.Out)
        {
            this.start = start; this.end = end; this.distance = distance; this.ease = ease;
        }
    }

    public class AttackDef
    {
        public string name;
        public float duration, cancelAt, cost;
        /// <summary>Until this time after a hit, dodge cannot interrupt the attack; zero keeps ordinary dodge cancellation.</summary>
        public float dodgeCancelAt;
        public bool heavy;
        /// <summary>Leaping attacks keep the facing they started with (no auto-aim turning while airborne).</summary>
        public bool lockFacing;
        public PoseTrack pose;
        public HitWindow[] hits;
        public MoveSeg[] moves;
        /// <summary>Timed effects that don't belong to a hit window (e.g. a slash during a slam's descent).</summary>
        public FxCue[] cues;
        /// <summary>Sound id played when the attack starts.</summary>
        public string sfx;

        public float FirstHit => hits.Length > 0 ? hits[0].start : 0f;
        public float LastHit => hits.Length > 0 ? hits[hits.Length - 1].end : 0f;
        public bool CanDodgeCancel(float t) =>
            (hits.Length == 0 || t < FirstHit || t > hits[0].end) &&
            (t < FirstHit || t >= dodgeCancelAt);
        public float TotalDamage { get { float d = 0f; foreach (var h in hits) d += h.damage; return d; } }

        public float Reach
        {
            get
            {
                float best = 0f;
                foreach (var h in hits)
                {
                    float moved = 0f;
                    foreach (var m in moves) if (m.start < h.end) moved += m.distance * Mathf.Clamp01((h.end - m.start) / (m.end - m.start));
                    best = Mathf.Max(best, moved + h.forward + h.range);
                }
                return best;
            }
        }

        /// <summary>
        /// Would this attack, started at <paramref name="origin"/> facing <paramref name="dir"/>, reach a piece at
        /// <paramref name="target"/>? Uses every hit window's shape plus the root motion before it ends. <paramref name="reach"/>
        /// and <paramref name="lunge"/> scale the hit range and the root motion the way the King's stats do in play.
        /// </summary>
        public bool WouldHit(Vector3 origin, Vector3 dir, Vector3 target, float targetRadius, float reach = 1f, float lunge = 1f)
        {
            origin.y = 0f;
            target.y = 0f;
            dir.y = 0f;
            dir.Normalize();
            foreach (var h in hits)
            {
                float moved = 0f;
                foreach (var m in moves)
                    if (m.start < h.end) moved += m.distance * Mathf.Clamp01((h.end - m.start) / (m.end - m.start));
                Vector3 center = origin + dir * (moved * lunge + h.forward * reach);
                Vector3 to = target - center;
                float d = to.magnitude;
                if (d > h.range * reach + targetRadius) continue;
                if (h.shape == HitShape.Circle || d < 0.4f) return true;
                float half = h.sweep ? Mathf.Max(Mathf.Abs(h.sweepFrom), Mathf.Abs(h.sweepTo)) : h.arc * 0.5f;
                if (Vector3.Angle(dir, to) <= half) return true;
            }
            return false;
        }

        public float Coverage
        {
            get
            {
                float best = 0f;
                foreach (var h in hits) best = Mathf.Max(best, h.shape == HitShape.Circle ? 360f : h.arc);
                return best;
            }
        }
    }

    public class AttackStyle
    {
        public string name, koName, description;
        public AttackDef[] lights;
        public AttackDef heavy;

        /// <summary>
        /// Optional opener for mid-range targets: when the first combo hit can't reach but the target is within
        /// <see cref="gapCloserMaxRange"/>, this attack (built for the distance to cover) comes before hit 1.
        /// </summary>
        public System.Func<float, AttackDef> gapCloser;
        public float gapCloserMaxRange;

        /// <summary>Damage per second of chaining the full light combo at the earliest cancel points.</summary>
        public float ComboDps
        {
            get
            {
                float d = 0f, t = 0f;
                foreach (var a in lights) { d += a.TotalDamage; t += a.cancelAt; }
                return t > 0f ? d / t : 0f;
            }
        }
    }

    /// <summary>The candidate body-attack styles evaluated in the Attack Lab.</summary>
    public static class AttackStyles
    {
        /// <summary>Index of the style used by the main game (chosen after the Attack Lab evaluation).</summary>
        public const int DefaultIndex = 5;

        static BodyPose P(float tilt = 0f, float heading = 0f, float spin = 0f, float flip = 0f, float lift = 0f, float squash = 0f) =>
            BodyPose.P(tilt, heading, spin, flip, lift, squash);

        public static readonly AttackStyle[] All = Build();
        public static AttackStyle Default => All[DefaultIndex];

        static AttackStyle[] Build()
        {
            var spinLight = SpinLight(12f, 10f, 0.55f);
            var pendA = PendulumLight(1f);
            var pendB = PendulumLight(-1f);
            var toppleHeavy = ToppleHeavy();

            return new[]
            {
                new AttackStyle
                {
                    name = "Royal Spin", koName = "회전 팽이",
                    description = "Tilts and spins like a top. 360° hits, heavy is a rising double spin.",
                    lights = new[] { spinLight }, heavy = SpinHeavy(),
                },
                new AttackStyle
                {
                    name = "Crown Smash", koName = "왕관 내려찍기",
                    description = "Rocks back then topples forward crown-first. Heavy falls flat with a shockwave.",
                    lights = new[] { ToppleLight(0f), ToppleLight(12f) }, heavy = toppleHeavy,
                },
                new AttackStyle
                {
                    name = "Pendulum", koName = "몸통 휘두르기",
                    description = "Leans and whips the whole body sideways, alternating. Heavy is a wide sweep.",
                    lights = new[] { pendA, pendB }, heavy = PendulumHeavy(),
                },
                new AttackStyle
                {
                    name = "Royal Stomp", koName = "착수 내려찍기",
                    description = "Hops and slams the base down like placing a piece. Heavy is a flipping meteor.",
                    lights = new[] { StompLight() }, heavy = StompHeavy(),
                },
                new AttackStyle
                {
                    name = "Royal Charge", koName = "돌진 박치기",
                    description = "Leans in and rams forward. Heavy is a long rocket charge.",
                    lights = new[] { ChargeLight() }, heavy = ChargeHeavy(),
                },
                new AttackStyle
                {
                    name = "Royal Ensemble", koName = "왕의 연무",
                    description = "Ram in from mid range, whip R→L, whip L→R, 360° cyclone, air somersault slam; heavy is one charged diagonal cut.",
                    lights = new[] { EnsembleSwing(1f), EnsembleSwing(-1f), EnsembleCyclone(), EnsembleMeteor() }, heavy = HeavyCut(2),
                    gapCloser = EnsembleDash, gapCloserMaxRange = 8f,
                },
            };
        }

        // ---------------------------------------------------------------- royal ensemble (chosen style)
        // Tuned to be more dynamic than the candidates it's built from: deeper leans with a hop,
        // follow-through overshoot, ground slashes that show the wider reach, and leaping finishers.

        static AttackDef EnsembleSwing(float side) => new AttackDef
        {
            name = side > 0f ? "Royal Whip R→L" : "Royal Whip L→R", duration = 0.56f, cancelAt = 0.36f, cost = 13f,
            pose = new PoseTrack()
                .Key(0f, P(heading: 110f * side))
                .Key(0.11f, P(tilt: 24f, heading: 128f * side, spin: 45f * side, squash: -0.14f), Ease.Out)
                .Key(0.29f, P(tilt: 64f, heading: -122f * side, spin: -75f * side, lift: 0.28f, squash: 0.08f), Ease.InOut)
                .Key(0.37f, P(tilt: 52f, heading: -140f * side, spin: -90f * side, lift: 0.05f), Ease.Out)
                .Key(0.56f, P(heading: -140f * side), Ease.OutBack),
            hits = new[]
            {
                new HitWindow
                {
                    start = 0.13f, end = 0.29f, range = 2.8f, arc = 220f, damage = 14f, poise = 14f, shake = 0.16f,
                    sweep = true, sweepFrom = 110f * side, sweepTo = -110f * side, slash = true,
                    // Plays in place of the crown trail, at the height the crown sweeps through.
                    fx = SlashFxKind.SlashB, fxScale = 1.7f, fxHeight = 1.2f, fxFlip = side > 0f, fxGold = true,
                    fxYaw = side > 0f ? 180f : 0f,  // the mirrored 1st whip faces backwards otherwise
                    sfx = side > 0f ? "ATK_Whip_1" : "ATK_Whip_2",
                },
            },
            moves = new[] { new MoveSeg(0.04f, 0.3f, 1.0f) },
        };

        /// <summary>3rd hit: a violent three-turn spin that wobbles and bucks, hitting everything around twice.</summary>
        static AttackDef EnsembleCyclone() => new AttackDef
        {
            name = "Royal Cyclone", duration = 0.86f, cancelAt = 0.6f, cost = 15f,
            pose = new PoseTrack()
                .Key(0f, P())
                .Key(0.12f, P(tilt: 22f, heading: -110f, spin: -110f, squash: -0.26f), Ease.Out)
                // Alternating deep/shallow lean and hops make the spin wobble and buck like a thrown top.
                .Key(0.22f, P(tilt: 70f, heading: 160f, spin: 160f, lift: 0.5f, squash: 0.12f), Ease.Linear)
                .Key(0.32f, P(tilt: 50f, heading: 430f, spin: 430f, lift: 0.12f, squash: -0.08f), Ease.Linear)
                .Key(0.42f, P(tilt: 74f, heading: 700f, spin: 700f, lift: 0.6f, squash: 0.14f), Ease.Linear)
                .Key(0.52f, P(tilt: 56f, heading: 980f, spin: 980f, lift: 0.1f, squash: -0.1f), Ease.Linear)
                .Key(0.62f, P(tilt: 18f, heading: 1080f, spin: 1080f, squash: -0.24f), Ease.Out)
                .Key(0.86f, P(heading: 1080f, spin: 1080f), Ease.OutBack),
            hits = new[]
            {
                new HitWindow
                {
                    start = 0.14f, end = 0.34f, shape = HitShape.Circle, range = 3.4f, damage = 9f, poise = 12f, shake = 0.25f,
                    slash = true, sweepFrom = 0f, sweepTo = 360f, sparkTrail = true,
                    fx = SlashFxKind.AoeA, fxScale = 1.05f, fxHeight = 0.9f, fxGold = true,
                    sfx = "ATK_Cyclone",
                },
                new HitWindow
                {
                    start = 0.35f, end = 0.56f, shape = HitShape.Circle, range = 3.4f, damage = 9f, poise = 12f, shake = 0.3f,
                    sparkTrail = true, kick = 0.35f,
                },
            },
            moves = new[] { new MoveSeg(0.1f, 0.56f, 0.8f, Ease.InOut) },
        };

        /// <summary>4th hit: leaps, somersaults once in the air and slams down with a shockwave.</summary>
        static AttackDef EnsembleMeteor() => new AttackDef
        {
            name = "Royal Meteor", duration = 0.88f, cancelAt = 0.68f, cost = 17f, sfx = "ATK_Meteor_Jump", lockFacing = true,
            pose = new PoseTrack()
                .Key(0f, P())
                .Key(0.14f, P(squash: -0.26f), Ease.Out)
                .Key(0.38f, P(flip: 180f, lift: 2.1f, squash: 0.1f), Ease.Out)
                .Key(0.52f, P(flip: 360f), Ease.In)
                .Key(0.6f, P(flip: 360f, squash: -0.3f), Ease.Out)
                .Key(0.88f, P(flip: 360f), Ease.OutBack),
            hits = new[]
            {
                new HitWindow
                {
                    start = 0.52f, end = 0.57f, shape = HitShape.Circle, range = 3.2f, damage = 20f, poise = 32f,
                    hitStop = 0.09f, shake = 0.45f, impact = true, kick = 0.6f,
                    dust = true, sparkRing = 45, sfx = "ATK_Meteor_Slam",
                },
            },
            moves = new[] { new MoveSeg(0.14f, 0.52f, 1.6f, Ease.InOut) },
            // Top-to-bottom slash while the King somersaults down onto the board.
            cues = new[] { new FxCue { time = 0.4f, kind = SlashFxKind.SlashBVertical, scale = 2.6f, height = 1.8f, roll = 180f, gold = true } },
        };

        /// <summary>Opener: leans in and rams across <paramref name="distance"/> (Royal Charge's light, sized to the gap).</summary>
        static AttackDef EnsembleDash(float distance) => new AttackDef
        {
            name = "Royal Charge", duration = 0.52f, cancelAt = 0.34f, cost = 10f, sfx = "ATK_Charge",
            pose = new PoseTrack()
                .Key(0f, P())
                .Key(0.1f, P(tilt: -12f, squash: -0.12f), Ease.Out)
                .Key(0.17f, P(tilt: 38f), Ease.In)
                .Key(0.34f, P(tilt: 32f), Ease.Linear)
                .Key(0.52f, P(), Ease.OutBack),
            hits = new[] { new HitWindow { start = 0.14f, end = 0.34f, shape = HitShape.Circle, forward = 0.9f, range = 0.95f, damage = 10f, poise = 12f } },
            moves = new[] { new MoveSeg(0.12f, 0.34f, Mathf.Clamp(distance, 1.5f, 7.5f)) },
        };

        /// <summary>Three refinements of the chosen high leap: the paired slash precedes the single landing hit.</summary>
        public static AttackDef HeavyCut(int variant)
        {
            float crouch = variant == 0 ? 0.68f : variant == 2 ? 0.75f : 0.72f;
            float apex = variant == 0 ? 0.8f : variant == 2 ? 0.88f : 0.84f;
            float slashAt = variant == 0 ? 0.9f : variant == 2 ? 0.99f : 0.94f;
            float land = variant == 0 ? 0.96f : variant == 2 ? 1.05f : 1f;
            float height = variant == 0 ? 2.15f : variant == 2 ? 2.6f : 2.4f;
            float compression = variant == 0 ? -0.27f : variant == 2 ? -0.36f : -0.32f;
            float rebound = variant == 0 ? 0.09f : variant == 2 ? 0.15f : 0.12f;
            float duration = land + 0.36f;
            var pose = new PoseTrack()
                .Key(0f, P())
                .Key(crouch, P(tilt: -20f, squash: -0.32f), Ease.Out)
                .Key(apex, P(tilt: -12f, lift: height, squash: 0.08f), Ease.Out)
                .Key(land, P(tilt: 105f, squash: compression), Ease.InOut)
                .Key(land + 0.12f, P(tilt: 72f, lift: rebound, squash: 0.05f), Ease.Out)
                .Key(duration, P(), Ease.InOut);
            return new AttackDef
            {
                name = variant == 0 ? "Cross Cut - Snappy" : variant == 2 ? "Cross Cut - Weighty" : "Cross Cut - Balanced",
                duration = duration, cancelAt = duration, dodgeCancelAt = duration,
                cost = 48f, heavy = true, sfx = "ATK_Stomp_Charge", lockFacing = true,
                pose = pose,
                hits = new[]
                {
                    new HitWindow
                    {
                        start = land, end = land + 0.1f, range = 3.5f, arc = 90f, frontOnly = true,
                        damage = 46f, poise = 65f, hitStop = 0.12f, shake = 0.65f, kick = 1f,
                        impact = true, impactAtHead = true, impactRadius = 2.5f,
                        dust = true, launch = true, sparkRing = 70, sfx = "ATK_Stomp_Slam",
                    },
                },
                // Both authored slashes fire together just before the base lands; no second damage window.
                cues = new[]
                {
                    new FxCue { time = slashAt, kind = SlashFxKind.SlashBVertical, scale = 2.3f, height = 1.6f, roll = 225f, gold = true, lifeScale = 1.2f },
                    new FxCue { time = slashAt, kind = SlashFxKind.SlashBVertical, scale = 2.3f, height = 1.6f, roll = 135f, gold = true, lifeScale = 1.2f },
                },
                // A fast takeoff eases into the landing; by the early slashes only a few centimetres of travel remain.
                moves = new[] { new MoveSeg(crouch, land, 3f, Ease.Out) },
            };
        }

        // ---------------------------------------------------------------- spin

        static AttackDef SpinLight(float dmg, float poise, float duration) => new AttackDef
        {
            name = "Spin", duration = duration, cancelAt = duration * 0.72f, cost = 14f,
            pose = new PoseTrack()
                .Key(0f, P())
                .Key(0.12f, P(tilt: 8f, heading: -40f, spin: -40f, squash: -0.06f), Ease.Out)
                .Key(0.32f, P(tilt: 26f, heading: 340f, spin: 340f, lift: 0.1f), Ease.Linear)
                .Key(duration, P(heading: 360f, spin: 360f), Ease.Out),
            hits = new[] { new HitWindow { start = 0.14f, end = 0.3f, shape = HitShape.Circle, range = 2.2f, damage = dmg, poise = poise } },
            moves = new[] { new MoveSeg(0f, 0.3f, 0.6f) },
        };

        static AttackDef SpinHeavy() => new AttackDef
        {
            name = "Rising Spin", duration = 1.15f, cancelAt = 0.9f, cost = 30f, heavy = true,
            pose = new PoseTrack()
                .Key(0f, P())
                .Key(0.4f, P(tilt: 6f, heading: -90f, spin: -90f, squash: -0.15f), Ease.Out)
                .Key(0.85f, P(tilt: 28f, heading: 1080f, spin: 1080f, lift: 0.7f), Ease.Linear)
                .Key(1.15f, P(heading: 1080f, spin: 1080f), Ease.Out),
            hits = new[]
            {
                new HitWindow { start = 0.42f, end = 0.62f, shape = HitShape.Circle, range = 2.6f, damage = 16f, poise = 25f, hitStop = 0.06f, shake = 0.18f },
                new HitWindow { start = 0.63f, end = 0.85f, shape = HitShape.Circle, range = 2.6f, damage = 18f, poise = 25f, hitStop = 0.08f, shake = 0.25f },
            },
            moves = new[] { new MoveSeg(0.4f, 0.85f, 1.6f, Ease.InOut) },
        };

        // ---------------------------------------------------------------- topple

        static AttackDef ToppleLight(float heading) => new AttackDef
        {
            name = "Crown Smash", duration = 0.6f, cancelAt = 0.42f, cost = 14f,
            pose = new PoseTrack()
                .Key(0f, P(heading: heading))
                .Key(0.18f, P(tilt: -18f, heading: heading), Ease.Out)
                .Key(0.3f, P(tilt: 75f, heading: heading), Ease.In)
                .Key(0.4f, P(tilt: 62f, heading: heading), Ease.Out)
                .Key(0.6f, P(heading: heading), Ease.OutBack),
            hits = new[] { new HitWindow { start = 0.24f, end = 0.34f, range = 2.6f, arc = 70f, damage = 16f, poise = 15f, hitStop = 0.06f, shake = 0.15f } },
            moves = new[] { new MoveSeg(0.15f, 0.3f, 0.8f) },
        };

        static AttackDef ToppleHeavy() => new AttackDef
        {
            name = "Royal Topple", duration = 1.3f, cancelAt = 1.05f, cost = 30f, heavy = true,
            pose = new PoseTrack()
                .Key(0f, P())
                .Key(0.45f, P(tilt: -28f, lift: 0.1f), Ease.Out)
                .Key(0.62f, P(tilt: 90f), Ease.In)
                .Key(0.85f, P(tilt: 88f), Ease.Linear)
                .Key(1.08f, P(tilt: -12f), Ease.Out)
                .Key(1.3f, P(), Ease.OutBack),
            hits = new[]
            {
                new HitWindow { start = 0.6f, end = 0.68f, shape = HitShape.Circle, forward = 2.1f, range = 1.7f, damage = 38f, poise = 55f, hitStop = 0.11f, shake = 0.5f, impact = true },
            },
            moves = new[] { new MoveSeg(0.4f, 0.62f, 1.0f) },
        };

        // ---------------------------------------------------------------- pendulum

        static AttackDef PendulumLight(float side) => new AttackDef
        {
            name = side > 0f ? "Pendulum R→L" : "Pendulum L→R", duration = 0.55f, cancelAt = 0.38f, cost = 13f,
            pose = new PoseTrack()
                .Key(0f, P(heading: 100f * side))
                .Key(0.14f, P(tilt: 32f, heading: 115f * side, spin: 30f * side, squash: -0.05f), Ease.Out)
                .Key(0.32f, P(tilt: 40f, heading: -115f * side, spin: -45f * side), Ease.InOut)
                .Key(0.55f, P(heading: -115f * side), Ease.Out),
            hits = new[] { new HitWindow { start = 0.15f, end = 0.3f, range = 2.4f, arc = 180f, damage = 13f, poise = 12f, sweep = true, sweepFrom = 95f * side, sweepTo = -95f * side } },
            moves = new[] { new MoveSeg(0f, 0.3f, 0.6f) },
        };

        static AttackDef PendulumHeavy() => new AttackDef
        {
            name = "Grand Sweep", duration = 1.1f, cancelAt = 0.85f, cost = 30f, heavy = true,
            pose = new PoseTrack()
                .Key(0f, P(heading: 130f))
                .Key(0.45f, P(tilt: 42f, heading: 150f, spin: 60f, squash: -0.1f), Ease.Out)
                .Key(0.7f, P(tilt: 55f, heading: -170f, spin: -120f), Ease.InOut)
                .Key(1.1f, P(heading: -170f), Ease.Out),
            hits = new[] { new HitWindow { start = 0.48f, end = 0.68f, range = 2.9f, arc = 240f, sweep = true, sweepFrom = 125f, sweepTo = -125f, damage = 32f, poise = 45f, hitStop = 0.09f, shake = 0.3f } },
            moves = new[] { new MoveSeg(0.45f, 0.68f, 1.2f) },
        };

        // ---------------------------------------------------------------- stomp

        static AttackDef StompLight() => new AttackDef
        {
            name = "Stomp", duration = 0.65f, cancelAt = 0.48f, cost = 15f,
            pose = new PoseTrack()
                .Key(0f, P())
                .Key(0.12f, P(tilt: -6f, squash: -0.15f), Ease.Out)
                .Key(0.3f, P(tilt: 8f, lift: 1.1f, squash: 0.08f), Ease.Out)
                .Key(0.4f, P(), Ease.In)
                .Key(0.47f, P(squash: -0.2f), Ease.Out)
                .Key(0.65f, P(), Ease.OutBack),
            hits = new[] { new HitWindow { start = 0.4f, end = 0.46f, shape = HitShape.Circle, forward = 0.2f, range = 1.9f, damage = 14f, poise = 15f, shake = 0.2f, impact = true } },
            moves = new[] { new MoveSeg(0.12f, 0.4f, 1.4f, Ease.InOut) },
        };

        static AttackDef StompHeavy() => new AttackDef
        {
            name = "Meteor Flip", duration = 1.3f, cancelAt = 1.05f, cost = 32f, heavy = true,
            pose = new PoseTrack()
                .Key(0f, P())
                .Key(0.42f, P(squash: -0.25f), Ease.Out)
                .Key(0.62f, P(flip: 180f, lift: 2.8f, squash: 0.1f), Ease.Out)
                .Key(0.82f, P(flip: 360f), Ease.In)
                .Key(0.92f, P(flip: 360f, squash: -0.3f), Ease.Out)
                .Key(1.3f, P(flip: 360f), Ease.OutBack),
            hits = new[] { new HitWindow { start = 0.82f, end = 0.9f, shape = HitShape.Circle, range = 3.0f, damage = 40f, poise = 60f, hitStop = 0.12f, shake = 0.6f, impact = true } },
            moves = new[] { new MoveSeg(0.42f, 0.82f, 3.0f, Ease.InOut) },
        };

        // ---------------------------------------------------------------- charge

        static AttackDef ChargeLight() => new AttackDef
        {
            name = "Ram", duration = 0.6f, cancelAt = 0.45f, cost = 14f,
            pose = new PoseTrack()
                .Key(0f, P())
                .Key(0.14f, P(tilt: -12f, squash: -0.1f), Ease.Out)
                .Key(0.22f, P(tilt: 35f), Ease.In)
                .Key(0.4f, P(tilt: 30f), Ease.Linear)
                .Key(0.6f, P(), Ease.OutBack),
            hits = new[] { new HitWindow { start = 0.17f, end = 0.4f, shape = HitShape.Circle, forward = 0.9f, range = 0.9f, damage = 13f, poise = 15f } },
            moves = new[] { new MoveSeg(0.16f, 0.4f, 3.0f) },
        };

        static AttackDef ChargeHeavy() => new AttackDef
        {
            name = "Rocket Ram", duration = 1.2f, cancelAt = 1.0f, cost = 30f, heavy = true,
            pose = new PoseTrack()
                .Key(0f, P())
                .Key(0.5f, P(tilt: -20f, squash: -0.2f), Ease.Out)
                .Key(0.58f, P(tilt: 50f), Ease.In)
                .Key(0.9f, P(tilt: 45f), Ease.Linear)
                .Key(1.2f, P(), Ease.OutBack),
            hits = new[] { new HitWindow { start = 0.56f, end = 0.9f, shape = HitShape.Circle, forward = 1.0f, range = 1.2f, damage = 34f, poise = 50f, hitStop = 0.09f, shake = 0.3f } },
            moves = new[] { new MoveSeg(0.55f, 0.9f, 6.0f, Ease.Linear) },
        };
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>Selectable effect looks, compared side by side in Scenes/EffectsLab.unity.</summary>
    public enum VfxStyle { Classic, CrackedBoard, BoardRipple }

    /// <summary>Authored slash prefabs (NamuFX Simple Stylized Slash vol2) registered in <see cref="FxLibrary"/>.</summary>
    public enum SlashFxKind { None, SlashA, SlashB, SlashC, SlashD, Ink, Water, AoeA, SlashBVertical }

    /// <summary>
    /// Gameplay-facing effect events. Gameplay calls these (never raw sparks/shockwaves for hits and slams),
    /// so a style can be swapped globally — or picked per call, which is how the Effects Lab shows all styles at once.
    /// </summary>
    public static class Vfx
    {
        // Chosen in the Effects Lab: C. Board Ripple.
        public static VfxStyle Style = VfxStyle.BoardRipple;

        public static readonly string[] Names = { "A. Classic", "B. Cracked Board", "C. Board Ripple" };
        public static readonly string[] Descriptions =
        {
            "Current effects: gold cube sparks, flat glowing shockwave disc.",
            "Hits chip off black & ivory marble. Slams crack the board (decal cracks), with debris in the struck square's color and heaved shards.",
            "Hits burst into black & white square confetti. Slams make the real board squares ripple outward, with a square shockwave and a small center crack.",
        };

        /// <summary>The King's attack-effect color (white, slightly warm).</summary>
        public static readonly Color Accent = new Color(1f, 0.97f, 0.92f);
        public static readonly Color Ember = new Color(1f, 0.3f, 0.1f);
        public static readonly Color Ivory = new Color(0.92f, 0.89f, 0.8f);
        public static readonly Color Onyx = new Color(0.06f, 0.06f, 0.07f);

        // ---------------------------------------------------------------- hit (King strikes a piece)

        static Transform followTarget;

        /// <param name="follow">The struck piece's model: the flash, sparks and debris ride along with it (a launched piece
        /// carries its hit effects instead of leaving them hanging in the air).</param>
        public static void Hit(Vector3 pos, Vector3 dir, bool heavy, Transform follow = null)
        {
            followTarget = follow;
            try { Hit(Style, pos, dir, heavy); }
            finally { followTarget = null; }
        }

        /// <summary>Effects spawned during a <see cref="Hit(Vector3, Vector3, bool, Transform)"/> on a piece follow that piece.</summary>
        public static void Track(GameObject go)
        {
            if (followTarget != null) FollowTarget.Attach(go, followTarget);
        }

        public static void Hit(VfxStyle style, Vector3 pos, Vector3 dir, bool heavy)
        {
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;
            switch (style)
            {
                case VfxStyle.CrackedBoard:
                    HitFlashFx.Spawn(pos, Accent, heavy ? 0.9f : 0.6f);
                    Burst(SlashFxKind.SlashC, pos, Quaternion.LookRotation(dir), heavy ? 0.9f : 0.6f, Accent, "flare", "spark");
                    Chips(pos, dir, heavy ? 18 : 11, heavy ? 6.5f : 5f, Ivory, Onyx, heavy ? 0.16f : 0.13f);
                    Effects.Sparks(pos, Accent, heavy ? 8 : 4, heavy ? 6f : 4f);
                    break;
                case VfxStyle.BoardRipple:
                    HitFlashFx.Spawn(pos, Ivory, heavy ? 0.9f : 0.6f);
                    Burst(SlashFxKind.SlashC, pos, Quaternion.LookRotation(dir), heavy ? 0.9f : 0.6f, Ivory, "flare", "spark");
                    Confetti(pos, dir, heavy ? 22 : 14, heavy ? 6.5f : 5f);
                    Effects.Sparks(pos, Accent, heavy ? 6 : 3, 4f);
                    break;
                default:
                    Effects.Sparks(pos, Accent, heavy ? 16 : 10, heavy ? 7f : 5f);
                    break;
            }
        }

        // ---------------------------------------------------------------- authored slashes

        /// <summary>
        /// Plays a NamuFX slash prefab. Particle systems are switched to hierarchy scaling so <paramref name="scale"/>
        /// works; <paramref name="tint"/> recolors every system's start color (keeping alpha).
        /// Returns false when the asset isn't available (callers fall back to the primitive crescent).
        /// </summary>
        /// <summary>
        /// Starts a looping authored effect riding on <paramref name="parent"/> (particles simulate in its local space so
        /// they move with it). Null when the prefab isn't there. End it with <see cref="Release"/>.
        /// </summary>
        public static GameObject Attach(GameObject prefab, Transform parent, Vector3 localPos, float scale, float speed = 1f)
        {
            if (prefab == null || parent == null) return null;
            var fx = Object.Instantiate(prefab, parent);
            fx.transform.localPosition = localPos;
            fx.transform.localRotation = Quaternion.identity;
            fx.transform.localScale = Vector3.one * scale;
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>())
            {
                var m = ps.main;
                m.simulationSpace = ParticleSystemSimulationSpace.Local;
                m.scalingMode = ParticleSystemScalingMode.Hierarchy;
                m.simulationSpeed *= speed;
            }
            foreach (var a in fx.GetComponentsInChildren<AudioSource>()) a.enabled = false;   // sounds go through Sfx
            return fx;
        }

        /// <summary>Stops a looping effect from emitting; what's already out fades, then it's destroyed.</summary>
        public static void Release(GameObject fx, float linger = 1.2f)
        {
            if (fx == null) return;
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>())
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            Object.Destroy(fx, linger);
        }

        /// <param name="mirror">Mirror across the facing's left/right axis: the arc sweeps the other way.</param>
        public static bool Slash(SlashFxKind kind, Vector3 pos, Quaternion rot, float scale, Color? tint = null, bool mirror = false) =>
            Slash(kind, pos, rot, scale, tint, null, mirror);

        /// <summary>A slash that rides along with <paramref name="follow"/> (its particles move with it).</summary>
        public static bool SlashFollowing(SlashFxKind kind, Vector3 pos, Quaternion rot, float scale, Color? tint, Transform follow, bool mirror = false)
        {
            followTarget = follow;
            try { return Slash(kind, pos, rot, scale, tint, null, mirror); }
            finally { followTarget = null; }
        }

        /// <param name="keep">When set, only particle systems whose (lower-case) name passes keep emitting.</param>
        static bool Slash(SlashFxKind kind, Vector3 pos, Quaternion rot, float scale, Color? tint, System.Predicate<string> keep,
                          bool mirror = false, float dustLife = 1f, bool burst = false)
        {
            Vector3 facingRight = rot * Vector3.right;
            var lib = FxLibrary.Instance;
            if (lib == null) return false;
            GameObject prefab = kind switch
            {
                SlashFxKind.SlashA => lib.slashA,
                SlashFxKind.SlashB => lib.slashB,
                SlashFxKind.SlashC => lib.slashC,
                SlashFxKind.SlashD => lib.slashD,
                SlashFxKind.Ink => lib.slashInk,
                SlashFxKind.Water => lib.slashWater,
                SlashFxKind.AoeA => lib.slashAoeA,
                SlashFxKind.SlashBVertical => lib.slashB,
                _ => null,
            };
            if (prefab == null) return false;

            // NamuFX slashes animate a sweep that isn't centered on the prefab's +Z. Calibrated top-down
            // (Effects Lab E6): this turn about the vertical makes the sweep run right → front → left of the facing.
            if (kind != SlashFxKind.AoeA)
                rot = Quaternion.AngleAxis(SlashYawFix, Vector3.up) * rot;
            var fx = Object.Instantiate(prefab, pos, rot);
            if (followTarget != null)
            {
                // Riding along with a piece: simulate in local space so live particles move with it.
                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>())
                {
                    var m = ps.main;
                    m.simulationSpace = ParticleSystemSimulationSpace.Local;
                }
                Track(fx);
            }
            var s = Vector3.one * scale;
            if (mirror)
            {
                // The slash meshes sit under rotated children, so the sweep's left/right is the prefab-local axis that
                // lines up with the facing's *forward* (verified top-down in Effects Lab E6; the other axis flips it behind).
                Vector3 local = Quaternion.Inverse(rot) * facingRight;
                if (Mathf.Abs(local.x) >= Mathf.Abs(local.z)) s.z = -s.z;
                else s.x = -s.x;
            }
            fx.transform.localScale = s;
            if (kind == SlashFxKind.SlashBVertical)
            {
                // Same as NamuFX's "Slash_B_vertical Variant": the slash layers are stood up on their side.
                foreach (var t in fx.GetComponentsInChildren<Transform>())
                    if (t.name == "slash_add" || t.name == "slash_alp") t.localRotation = Quaternion.Euler(0f, 90f, -68.41f);
            }
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>())
            {
                string n = ps.name.ToLowerInvariant();
                if (keep != null && !keep(n))
                {
                    var em = ps.emission;
                    em.enabled = false;
                    ps.Clear(false);
                    continue;
                }
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                // Burst layers (flare, sparks) sit ~1.4 up in the slash prefab; pull them onto the hit point itself.
                if (burst) ps.transform.localPosition = Vector3.zero;
                bool dust = n.Contains("dust") || n.Contains("smoke");
                if (dust)
                {
                    Recolor(ps, DustColor, dustLife > 1f ? 1f : (float?)null);  // neutral dust, never "enemy red"
                    if (dustLife > 1f)
                    {
                        // Denser, longer-lived dust: bigger, opaque-ish particles that linger.
                        main.startLifetimeMultiplier *= dustLife;
                        main.startSizeMultiplier *= 1.35f;
                    }
                }
                else if (tint.HasValue) Recolor(ps, tint.Value);
                if (dust || tint.HasValue)
                {
                    var r = ps.GetComponent<ParticleSystemRenderer>();
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = VertexColored(mats[i], burst);
                    r.sharedMaterials = mats;
                    if (r.trailMaterial != null) r.trailMaterial = VertexColored(r.trailMaterial, burst);
                }
            }
            Object.Destroy(fx, 2.2f * Mathf.Max(1f, dustLife));
            return true;
        }

        /// <summary>
        /// Plays only some layers of a NamuFX prefab (e.g. just its flash and sparks), recolored.
        /// Layers match by substring of the particle system's name. Returns false when the pack is missing.
        /// </summary>
        public static bool Burst(SlashFxKind kind, Vector3 pos, Quaternion rot, float scale, Color tint, params string[] layers)
        {
            System.Predicate<string> keep = n =>
            {
                foreach (var l in layers) if (n.Contains(l)) return true;
                return false;
            };
            return Slash(kind, pos, rot, scale, tint, keep, false, 1f, true);
        }

        /// <summary>World-yaw correction applied to directional NamuFX slashes (calibrated in Effects Lab E6).</summary>
        public static float SlashYawFix = 180f;

        static readonly Color DustColor = new Color(0.5f, 0.46f, 0.41f);
        const float DustLife = 2.5f;

        /// <summary>
        /// Low dust burst around a landing: only the smoke / dust layers of the NamuFX prefabs (no slashes),
        /// spread in a ring so the cracks in the middle stay readable. Falls back to grey sparks.
        /// </summary>
        public static void Dust(Vector3 center, float radius)
        {
            Sfx.Play("IMP_Dust", center);
            center.y = 0.15f;
            bool any = false;
            System.Predicate<string> dustOnly = n => n.Contains("smoke") || n.Contains("dust");
            int puffs = Mathf.Clamp(Mathf.RoundToInt(radius * 2.4f), 5, 11);
            float offset = Random.value * 360f;
            for (int i = 0; i < puffs; i++)
            {
                Vector3 d = Quaternion.Euler(0f, offset + i * 360f / puffs, 0f) * Vector3.forward;
                var kind = i % 2 == 0 ? SlashFxKind.AoeA : SlashFxKind.SlashA;
                any |= Slash(kind, center + d * radius * Random.Range(0.45f, 0.75f), Quaternion.LookRotation(d),
                    Mathf.Lerp(0.9f, 1.5f, radius / 4f), null, dustOnly, false, DustLife);
            }
            if (!any) Effects.Shockwave(center, radius * 0.7f, new Color(0.22f, 0.2f, 0.18f), 0.45f);
            Effects.Sparks(center, DustColor, Mathf.RoundToInt(6 + radius * 3f), 3.5f);
        }

        /// <summary>
        /// A sprinting footstep: a small puff of the NamuFX dust kicked up behind the King (no sound — the step plays its
        /// own), plus a few grains. Falls back to grains only.
        /// </summary>
        public static void StepDust(Vector3 foot, Vector3 back)
        {
            foot.y = 0.1f;
            back.y = 0f;
            if (back.sqrMagnitude < 0.01f) back = Vector3.back;
            System.Predicate<string> dustOnly = n => n.Contains("smoke") || n.Contains("dust");
            Slash(SlashFxKind.SlashA, foot + back.normalized * 0.25f, Quaternion.LookRotation(back), 0.55f, null, dustOnly, false, 1.4f);
            Effects.Sparks(foot, DustColor, 4, 2.5f);
        }

        static readonly Dictionary<Material, Material> vertexColored = new Dictionary<Material, Material>();
        static readonly Dictionary<Material, Material> flatColored = new Dictionary<Material, Material>();
        static readonly int GradientMapId = Shader.PropertyToID("_GradientMap");
        static readonly int UseCustomColorId = Shader.PropertyToID("_UseCustomdata_Color");

        /// <summary>
        /// Copy of a NamuFX material that takes its color from particle color instead of custom data. <paramref name="flat"/>
        /// also swaps the material's color ramp (_GradientMap, e.g. the flare's purple→gold) for plain white so the tint
        /// is the only color.
        /// </summary>
        public static Material VertexColored(Material src, bool flat = false)
        {
            if (src == null || !src.HasProperty(UseCustomColorId)) return src;
            var cache = flat ? flatColored : vertexColored;
            if (cache.TryGetValue(src, out var m) && m != null) return m;
            m = new Material(src) { name = src.name + (flat ? "_Flat" : "_Tinted") };
            m.SetFloat(UseCustomColorId, 0f);
            if (flat && m.HasProperty(GradientMapId) && m.GetTexture(GradientMapId) != null) m.SetTexture(GradientMapId, Texture2D.whiteTexture);
            cache[src] = m;
            return m;
        }

        /// <summary>Recolors a particle system: start color and the color-over-lifetime gradient (alpha kept).</summary>
        /// <param name="alpha">When set: start alpha override, and the lifetime alpha curve is strengthened (denser).</param>
        static void Recolor(ParticleSystem ps, Color c, float? alpha = null)
        {
            var main = ps.main;
            float a = alpha ?? (main.startColor.mode == ParticleSystemGradientMode.Color ? main.startColor.color.a : 1f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(c.r, c.g, c.b, a));
            var trails = ps.trails;
            if (trails.enabled)
            {
                trails.colorOverLifetime = new ParticleSystem.MinMaxGradient(new Color(c.r, c.g, c.b, 1f));
                trails.colorOverTrail = new ParticleSystem.MinMaxGradient(Color.white);
                trails.inheritParticleColor = false;
            }
            var bySpeed = ps.colorBySpeed;
            if (bySpeed.enabled) bySpeed.enabled = false;
            var col = ps.colorOverLifetime;
            if (!col.enabled) return;
            var src = col.color.mode == ParticleSystemGradientMode.TwoGradients ? col.color.gradientMax : col.color.gradient;
            if (src == null) return;
            var alphaKeys = src.alphaKeys;
            if (alpha.HasValue)
                for (int i = 0; i < alphaKeys.Length; i++)
                    alphaKeys[i].alpha = Mathf.Clamp01(alphaKeys[i].alpha * 1.6f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.Lerp(c, Color.white, 0.55f), 0f), new GradientColorKey(c, 0.5f), new GradientColorKey(c * 0.8f, 1f) },
                alphaKeys);
            col.color = new ParticleSystem.MinMaxGradient(g);
        }

        // ---------------------------------------------------------------- ground impact (slams / landings)

        /// <param name="strength">~0.6 small landing, 1 normal slam, 1.3+ big finisher.</param>
        public static void GroundImpact(Vector3 center, float radius, float strength, bool hostile = false) =>
            GroundImpact(Style, center, radius, strength, hostile);

        /// <param name="hostile">Enemy slams use an ember-red accent instead of the King's gold.</param>
        public static void GroundImpact(VfxStyle style, Vector3 center, float radius, float strength, bool hostile = false)
        {
            center.y = 0f;
            Sfx.Play("IMP_GroundCrack", center);
            if (style == VfxStyle.BoardRipple) Sfx.Play("IMP_BoardRipple", center);
            Color accent = hostile ? Ember : Accent;
            switch (style)
            {
                case VfxStyle.CrackedBoard:
                    CrackDecalFx.Spawn(center, radius * 4.75f, strength);
                    Chips(center + Vector3.up * 0.05f, Vector3.zero, Mathf.RoundToInt(10 * strength), 4.5f * strength,
                        SquareColor(center), SquareColor(center) * 0.7f, 0.13f);
                    Effects.Sparks(center + Vector3.up * 0.1f, accent, Mathf.RoundToInt(6 * strength), 4f);
                    break;
                case VfxStyle.BoardRipple:
                    BoardFx.Ripple(center, radius, strength);
                    SquareRingFx.Spawn(center, radius, accent, 0.5f, 0f);   // square (the diamond marks a launched piece)
                    // Deep projector: the squares lift up to ~0.4 while rippling and must keep showing the cracks.
                    CrackDecalFx.Spawn(center, Mathf.Min(6f, radius * 2f), strength * 0.8f, 1.1f);
                    Confetti(center + Vector3.up * 0.1f, Vector3.zero, Mathf.RoundToInt(10 * strength), 4f * strength);
                    break;
                default:
                    Effects.Shockwave(center, radius, accent, 0.45f);
                    Effects.Sparks(center + Vector3.up * 0.1f, new Color(0.75f, 0.7f, 0.6f), 14, 5f);
                    break;
            }
        }

        /// <summary>Color of the board square under a point (light / dark), used for debris.</summary>
        public static Color SquareColor(Vector3 p)
        {
            // World-grid parity (a1 of the board at the origin is dark; the stage's floors share the grid).
            int f = Mathf.FloorToInt(p.x / GameManager.TileSize);
            int r = Mathf.FloorToInt(p.z / GameManager.TileSize);
            return ((f + r) % 2 + 2) % 2 == 0 ? GameManager.DarkSquare : GameManager.LightSquare;
        }

        // ---------------------------------------------------------------- particles

        /// <summary>Flat stone chips in two alternating colors (marble / board debris).</summary>
        static void Chips(Vector3 pos, Vector3 dir, int count, float speed, Color a, Color b, float size)
        {
            var ma = MaterialLib.Get(a, 0.85f, 0.1f);
            var mb = MaterialLib.Get(b, 0.85f, 0.1f);
            for (int i = 0; i < count; i++)
            {
                float s = size * Random.Range(0.6f, 1.3f);
                var t = PieceFactory.Part(PrimitiveType.Cube, null, pos + Random.insideUnitSphere * 0.1f,
                    new Vector3(s, s * 0.3f, s * Random.Range(0.6f, 1.2f)), i % 2 == 0 ? ma : mb, Random.rotation.eulerAngles);
                Track(t.gameObject);
                var p = t.gameObject.AddComponent<SparkParticle>();
                Vector3 v = Random.onUnitSphere;
                v.y = Mathf.Abs(v.y) + 0.5f;
                p.velocity = (v.normalized + dir * 0.8f).normalized * speed * Random.Range(0.5f, 1.1f);
                p.life = p.maxLife = Random.Range(0.3f, 0.55f);   // chips vanish quickly
            }
        }

        /// <summary>Thin black & white squares that flutter down like confetti.</summary>
        static void Confetti(Vector3 pos, Vector3 dir, int count, float speed)
        {
            var white = MaterialLib.Get(Ivory, 0.6f, 0f, Ivory * 0.25f);
            var black = MaterialLib.Get(Onyx, 0.9f, 0.1f);
            for (int i = 0; i < count; i++)
            {
                float s = Random.Range(0.08f, 0.14f);
                var t = PieceFactory.Part(PrimitiveType.Cube, null, pos + Random.insideUnitSphere * 0.1f,
                    new Vector3(s, 0.01f, s), i % 2 == 0 ? white : black, Random.rotation.eulerAngles);
                Track(t.gameObject);
                var p = t.gameObject.AddComponent<FlutterParticle>();
                Vector3 v = Random.onUnitSphere;
                v.y = Mathf.Abs(v.y) + 0.6f;
                p.velocity = (v.normalized + dir * 0.7f).normalized * speed * Random.Range(0.5f, 1.1f);
                p.spin = Random.onUnitSphere * Random.Range(300f, 900f);
                // Played a bit faster and gone quickly: a snappy burst rather than litter on the board.
                p.rate = 1.35f;
                p.life = p.maxLife = Random.Range(0.45f, 0.7f);
            }
        }
    }

    /// <summary>Light, air-braked particle that tumbles (confetti).</summary>
    public class FlutterParticle : MonoBehaviour
    {
        public Vector3 velocity, spin;
        public float life, maxLife;
        /// <summary>Playback speed (1 = real time).</summary>
        public float rate = 1f;
        Vector3 startScale;

        void Start() => startScale = transform.localScale;

        void Update()
        {
            float dt = Time.deltaTime * rate;
            velocity.y -= 7f * dt;
            velocity *= Mathf.Exp(-2.2f * dt);
            transform.position += velocity * dt;
            transform.Rotate(spin * dt, Space.World);
            if (transform.position.y < 0.02f)
            {
                var p = transform.position;
                p.y = 0.02f;
                transform.position = p;
                velocity = Vector3.zero;
                spin *= 0.9f;
            }
            life -= dt;
            // Shrinks away over the second half of its life.
            transform.localScale = startScale * Mathf.Clamp01(life / (maxLife * 0.5f));
            if (life <= 0f) Destroy(gameObject);
        }
    }

    /// <summary>
    /// Branching cracks on the board. With a glow color, each crack has a glowing core (gold kintsugi) that cools down,
    /// leaving dark lines that then shrink away.
    /// </summary>
    public class CrackFx : MonoBehaviour
    {
        const float Hold = 1.6f, Fade = 0.7f, GlowTime = 0.55f;
        readonly List<Transform> lines = new List<Transform>();
        readonly List<Transform> glow = new List<Transform>();
        readonly List<Vector3> lineScale = new List<Vector3>();
        readonly List<Vector3> glowScale = new List<Vector3>();
        float t;

        /// <param name="glow">Color of the glowing crack core (null: plain dark cracks).</param>
        public static void Spawn(Vector3 center, float length, float strength, Color? glow)
        {
            var go = new GameObject("Cracks");
            var fx = go.AddComponent<CrackFx>();
            var dark = MaterialLib.Get(new Color(0.03f, 0.025f, 0.02f), 0.2f);
            var gold = glow.HasValue ? MaterialLib.Emissive(glow.Value, 5f) : null;
            float w0 = Mathf.Lerp(0.09f, 0.16f, Mathf.Clamp01(strength - 0.5f));
            int count = Random.Range(7, 11);
            float offset = Random.value * 360f;
            for (int i = 0; i < count; i++)
            {
                float angle = offset + i * 360f / count + Random.Range(-14f, 14f);
                float len = length * Random.Range(0.6f, 1.1f);
                fx.Branch(center, angle, 0.12f, len, w0, Random.Range(4, 7), dark, gold, true);
            }
            // Crushed crater: overlapping dark slabs at random angles make an irregular hole.
            float crater = 0.32f * strength;
            for (int i = 0; i < 4; i++)
                fx.Add(PieceFactory.Part(PrimitiveType.Cube, go.transform, center + Vector3.up * 0.005f + Vector3.Scale(Random.insideUnitSphere, new Vector3(1f, 0f, 1f)) * crater * 0.3f,
                    new Vector3(crater * Random.Range(1.2f, 1.8f), 0.003f, crater * Random.Range(0.8f, 1.3f)), dark, new Vector3(0f, Random.value * 180f, 0f)), false);
            // Strong slams heave broken pieces of the square up out of the board.
            if (strength >= 1f)
            {
                var tileMat = MaterialLib.Get(Vfx.SquareColor(center), 0.4f);
                int shards = Mathf.RoundToInt(5 * strength);
                for (int i = 0; i < shards; i++)
                {
                    float a = i * 360f / shards + Random.Range(-20f, 20f);
                    Vector3 d = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                    var shard = PieceFactory.Part(PrimitiveType.Cube, go.transform, center + d * Random.Range(0.35f, 0.75f) * strength,
                        new Vector3(Random.Range(0.22f, 0.4f), 0.07f, Random.Range(0.18f, 0.3f)), tileMat,
                        new Vector3(Random.Range(-35f, -18f), a, Random.Range(-12f, 12f)));
                    fx.Add(shard, false);
                }
            }
        }

        void Branch(Vector3 center, float angle, float startR, float length, float width, int segments,
                    Material dark, Material gold, bool canFork)
        {
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Vector3 p = center + dir * startR;
            float seg = length / segments;
            for (int s = 0; s < segments; s++)
            {
                float k = (float)s / segments;
                angle += Random.Range(-28f, 28f);
                dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 q = p + dir * seg;
                Vector3 mid = (p + q) * 0.5f;
                float w = Mathf.Lerp(width, width * 0.25f, k);
                var rot = new Vector3(0f, angle, 0f);
                Add(PieceFactory.Part(PrimitiveType.Cube, transform, mid + Vector3.up * 0.004f, new Vector3(w, 0.004f, seg * 1.08f), dark, rot), false);
                if (gold != null)
                    Add(PieceFactory.Part(PrimitiveType.Cube, transform, mid + Vector3.up * 0.0065f, new Vector3(w * 0.4f, 0.003f, seg * 1.02f), gold, rot), true);
                if (canFork && s >= 1 && Random.value < 0.35f)
                    Branch(q, angle + (Random.value < 0.5f ? -1f : 1f) * Random.Range(25f, 45f), 0f,
                        length * 0.4f, w * 0.7f, Random.Range(2, 4), dark, gold, false);
                p = q;
            }
        }

        void Add(Transform part, bool isGlow)
        {
            (isGlow ? glow : lines).Add(part);
            (isGlow ? glowScale : lineScale).Add(part.localScale);
        }

        void Update()
        {
            t += Time.deltaTime;
            // Gold core cools off first.
            float g = 1f - Mathf.Clamp01((t - 0.15f) / GlowTime);
            for (int i = 0; i < glow.Count; i++)
                glow[i].localScale = new Vector3(glowScale[i].x * g, glowScale[i].y, glowScale[i].z);
            // Then the dark cracks close up.
            float f = 1f - Mathf.Clamp01((t - Hold) / Fade);
            for (int i = 0; i < lines.Count; i++)
                lines[i].localScale = new Vector3(lineScale[i].x * f, lineScale[i].y, lineScale[i].z);
            if (t > Hold + Fade) Destroy(gameObject);
        }
    }

    /// <summary>Expanding square outline (a chess square) that sinks into the board.</summary>
    public class SquareRingFx : MonoBehaviour
    {
        float radius, duration, t;
        Transform[] sides;

        /// <param name="yaw">Square orientation (45 = a diamond); random 0/45 when omitted.</param>
        public static void Spawn(Vector3 center, float radius, Color color, float duration, float? yaw = null)
        {
            var go = new GameObject("SquareRing");
            go.transform.position = new Vector3(center.x, 0.03f, center.z);
            go.transform.rotation = Quaternion.Euler(0f, yaw ?? (Random.value < 0.5f ? 0f : 45f), 0f);
            var fx = go.AddComponent<SquareRingFx>();
            fx.radius = radius;
            fx.duration = duration;
            var mat = MaterialLib.Emissive(color, 3f);
            fx.sides = new Transform[4];
            for (int i = 0; i < 4; i++)
                fx.sides[i] = PieceFactory.Part(PrimitiveType.Cube, go.transform, Vector3.zero, Vector3.one, mat);
        }

        void Update()
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            float r = Mathf.Lerp(0.3f, radius, 1f - (1f - p) * (1f - p));
            float w = Mathf.Lerp(0.18f, 0.03f, p);
            for (int i = 0; i < 4; i++)
            {
                Vector3 n = Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward;
                sides[i].localPosition = n * r;
                sides[i].localRotation = Quaternion.Euler(0f, i * 90f + 90f, 0f);
                sides[i].localScale = new Vector3(w, 0.02f, r * 2f + w);
            }
            if (p >= 1f) Destroy(gameObject);
        }
    }

    /// <summary>
    /// Animates the real board squares: a ripple lifts and tilts each square outward, delayed by its distance,
    /// so a slam sends a wave through the chessboard.
    /// </summary>
    public class BoardFx : MonoBehaviour
    {
        struct RippleWave { public Vector3 center; public float radius, strength, age; }

        const float WaveSpeed = 9f, BumpTime = 0.32f, MaxLift = 0.32f, MaxTilt = 14f;
        static BoardFx instance;
        Transform[,] tiles;
        Vector3[,] basePos;
        readonly List<RippleWave> waves = new List<RippleWave>();
        bool settled = true;

        public static void Register(Transform[,] squares)
        {
            if (instance == null) instance = new GameObject("BoardFx").AddComponent<BoardFx>();
            instance.tiles = squares;
            instance.basePos = new Vector3[squares.GetLength(0), squares.GetLength(1)];
            for (int f = 0; f < squares.GetLength(0); f++)
            for (int r = 0; r < squares.GetLength(1); r++)
                if (squares[f, r] != null) instance.basePos[f, r] = squares[f, r].localPosition;
        }

        public static void Ripple(Vector3 center, float radius, float strength)
        {
            if (instance == null || instance.tiles == null) return;
            instance.waves.Add(new RippleWave { center = new Vector3(center.x, 0f, center.z), radius = radius + 1f, strength = strength });
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void Update()
        {
            if (tiles == null || (waves.Count == 0 && settled)) return;
            settled = waves.Count == 0;  // one last pass puts every square back
            float dt = Time.deltaTime;
            for (int i = waves.Count - 1; i >= 0; i--)
            {
                var w = waves[i];
                w.age += dt;
                waves[i] = w;
                if (w.age > w.radius / WaveSpeed + BumpTime) waves.RemoveAt(i);
            }

            for (int f = 0; f < tiles.GetLength(0); f++)
            for (int r = 0; r < tiles.GetLength(1); r++)
            {
                var tile = tiles[f, r];
                if (tile == null) continue;   // a gap in the grid (the stage's notched corners)
                Vector3 bp = basePos[f, r];
                float lift = 0f;
                Vector3 tiltAxis = Vector3.zero;
                foreach (var w in waves)
                {
                    Vector3 to = new Vector3(bp.x - w.center.x, 0f, bp.z - w.center.z);
                    float d = to.magnitude;
                    if (d > w.radius) continue;
                    float local = w.age - d / WaveSpeed;
                    if (local < 0f || local > BumpTime) continue;
                    float h = Mathf.Sin(local / BumpTime * Mathf.PI) * (1f - d / w.radius) * w.strength;
                    if (h > lift)
                    {
                        lift = h;
                        tiltAxis = d > 0.01f ? Vector3.Cross(Vector3.up, to / d) : Vector3.zero;
                    }
                }
                tile.localPosition = bp + Vector3.up * (lift * MaxLift);
                tile.localRotation = tiltAxis == Vector3.zero ? Quaternion.identity : Quaternion.AngleAxis(lift * MaxTilt, tiltAxis);
            }
        }
    }
}

namespace KingLastPiece
{
    /// <summary>A brief bright burst at the point of impact (pops out, then collapses).</summary>
    /// <summary>Moves an effect by however much its target moved since last frame, so hit effects stay on the piece.</summary>
    public class FollowTarget : MonoBehaviour
    {
        Transform target;
        Vector3 last;

        public static void Attach(GameObject go, Transform t)
        {
            var f = go.AddComponent<FollowTarget>();
            f.target = t;
            f.last = t.position;
        }

        void LateUpdate()
        {
            if (target == null || !target.gameObject.activeInHierarchy) { enabled = false; return; }
            Vector3 p = target.position;
            transform.position += p - last;
            last = p;
        }
    }

    public class HitFlashFx : MonoBehaviour
    {
        const float Life = 0.13f;
        float size, t;

        public static void Spawn(Vector3 pos, Color color, float size)
        {
            var tr = PieceFactory.Part(PrimitiveType.Sphere, null, pos, Vector3.one * 0.05f, MaterialLib.Emissive(color, 6f));
            tr.name = "HitFlash";
            tr.gameObject.AddComponent<HitFlashFx>().size = size;
            Vfx.Track(tr.gameObject);
        }

        void Update()
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / Life);
            float s = size * (p < 0.35f ? p / 0.35f : 1f - (p - 0.35f) / 0.65f);
            transform.localScale = new Vector3(s, s * 0.6f, s);
            if (p >= 1f) Destroy(gameObject);
        }
    }
}

namespace KingLastPiece
{
    /// <summary>
    /// Board cracks as a URP decal (textures from Resources/Textures/Decals, baked by KING ▸ Setup ▸ Effects Assets).
    /// Random crack image and rotation, sized to the slam, then fades out. Falls back to primitive cracks when the
    /// decal assets are missing.
    /// </summary>
    public class CrackDecalFx : MonoBehaviour
    {
        const float Hold = 2.2f, Fade = 0.9f, DefaultDepth = 0.24f;
        static readonly Dictionary<Texture2D, Material> materials = new Dictionary<Texture2D, Material>();
        static readonly int BaseMapId = Shader.PropertyToID("Base_Map");  // URP Decal shader graph property

        UnityEngine.Rendering.Universal.DecalProjector projector;
        float t;

        /// <param name="size">Width of the crack image on the board.</param>
        /// <param name="depth">Vertical reach of the projector (raise it when the board squares move).</param>
        public static void Spawn(Vector3 center, float size, float strength, float depth = DefaultDepth)
        {
            var lib = FxLibrary.Instance;
            if (lib == null || lib.crackDecal == null || lib.crackTextures == null || lib.crackTextures.Length == 0)
            {
                CrackFx.Spawn(center, size * 0.45f, strength, null);
                return;
            }
            var tex = lib.crackTextures[Random.Range(0, lib.crackTextures.Length)];
            if (!materials.TryGetValue(tex, out var mat) || mat == null)
            {
                mat = new Material(lib.crackDecal) { name = "KLP_Crack_" + tex.name };
                mat.SetTexture(BaseMapId, tex);
                materials[tex] = mat;
            }

            var go = new GameObject("CrackDecal");
            go.transform.SetPositionAndRotation(new Vector3(center.x, 0f, center.z), Quaternion.Euler(90f, Random.value * 360f, 0f));
            var fx = go.AddComponent<CrackDecalFx>();
            var dp = go.AddComponent<UnityEngine.Rendering.Universal.DecalProjector>();
            dp.material = mat;
            float s = size * Mathf.Lerp(0.85f, 1.15f, Random.value);
            dp.size = new Vector3(s, s, depth);
            dp.pivot = Vector3.zero;
            // Decal layers: the cracks land on the board squares only, never on the King or the pieces.
            dp.renderingLayerMask = WorldBuilder.BoardDecalLayer;
            // Only the board's flat top: steep surfaces (piece sides) are skipped.
            dp.startAngleFade = 25f;
            dp.endAngleFade = 45f;
            dp.fadeFactor = 1f;
            fx.projector = dp;
        }

        void Update()
        {
            t += Time.deltaTime;
            if (projector != null) projector.fadeFactor = 1f - Mathf.Clamp01((t - Hold) / Fade);
            if (t > Hold + Fade) Destroy(gameObject);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>Selectable effect looks, compared side by side in Scenes/EffectsLab.unity.</summary>
    public enum VfxStyle { Classic, KintsugiCrack, BoardRipple }

    /// <summary>
    /// Gameplay-facing effect events. Gameplay calls these (never raw sparks/shockwaves for hits and slams),
    /// so a style can be swapped globally — or picked per call, which is how the Effects Lab shows all styles at once.
    /// </summary>
    public static class Vfx
    {
        public static VfxStyle Style = VfxStyle.Classic;

        public static readonly string[] Names = { "A. Classic", "B. Kintsugi Crack", "C. Board Ripple" };
        public static readonly string[] Descriptions =
        {
            "Current effects: gold cube sparks, flat glowing shockwave disc.",
            "Hits chip off black & ivory marble. Slams crack the board; the cracks glow gold (kintsugi), then cool to dark lines; chips in the struck square's color.",
            "Hits burst into black & white square confetti. Slams make the real board squares ripple outward, with a square shockwave and a small center crack.",
        };

        public static readonly Color Gold = new Color(1f, 0.8f, 0.4f);
        public static readonly Color Ember = new Color(1f, 0.3f, 0.1f);
        public static readonly Color Ivory = new Color(0.92f, 0.89f, 0.8f);
        public static readonly Color Onyx = new Color(0.06f, 0.06f, 0.07f);

        // ---------------------------------------------------------------- hit (King strikes a piece)

        public static void Hit(Vector3 pos, Vector3 dir, bool heavy) => Hit(Style, pos, dir, heavy);

        public static void Hit(VfxStyle style, Vector3 pos, Vector3 dir, bool heavy)
        {
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;
            switch (style)
            {
                case VfxStyle.KintsugiCrack:
                    HitFlashFx.Spawn(pos, Gold, heavy ? 0.9f : 0.6f);
                    Chips(pos, dir, heavy ? 18 : 11, heavy ? 6.5f : 5f, Ivory, Onyx, heavy ? 0.16f : 0.13f);
                    Effects.Sparks(pos, Gold, heavy ? 8 : 4, heavy ? 6f : 4f);
                    break;
                case VfxStyle.BoardRipple:
                    HitFlashFx.Spawn(pos, Ivory, heavy ? 0.9f : 0.6f);
                    Confetti(pos, dir, heavy ? 22 : 14, heavy ? 6.5f : 5f);
                    Effects.Sparks(pos, Gold, heavy ? 6 : 3, 4f);
                    break;
                default:
                    Effects.Sparks(pos, new Color(1f, 0.85f, 0.5f), heavy ? 16 : 10, heavy ? 7f : 5f);
                    break;
            }
        }

        // ---------------------------------------------------------------- ground impact (slams / landings)

        /// <param name="strength">~0.6 small landing, 1 normal slam, 1.3+ big finisher.</param>
        public static void GroundImpact(Vector3 center, float radius, float strength, bool hostile = false) =>
            GroundImpact(Style, center, radius, strength, hostile);

        /// <param name="hostile">Enemy slams use an ember-red accent instead of the King's gold.</param>
        public static void GroundImpact(VfxStyle style, Vector3 center, float radius, float strength, bool hostile = false)
        {
            center.y = 0f;
            Color accent = hostile ? Ember : Gold;
            switch (style)
            {
                case VfxStyle.KintsugiCrack:
                    CrackFx.Spawn(center, radius * 0.85f, strength, accent);
                    Chips(center + Vector3.up * 0.05f, Vector3.zero, Mathf.RoundToInt(10 * strength), 4.5f * strength,
                        SquareColor(center), SquareColor(center) * 0.7f, 0.13f);
                    Effects.Sparks(center + Vector3.up * 0.1f, accent, Mathf.RoundToInt(6 * strength), 4f);
                    break;
                case VfxStyle.BoardRipple:
                    BoardFx.Ripple(center, radius, strength);
                    SquareRingFx.Spawn(center, radius, accent, 0.5f);
                    CrackFx.Spawn(center, Mathf.Min(1.2f, radius * 0.35f), strength * 0.8f, null);
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
            int f = Mathf.FloorToInt(p.x / GameManager.TileSize + 4f);
            int r = Mathf.FloorToInt(p.z / GameManager.TileSize + 4f);
            return (f + r) % 2 == 0 ? GameManager.DarkSquare : GameManager.LightSquare;
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
                var p = t.gameObject.AddComponent<SparkParticle>();
                Vector3 v = Random.onUnitSphere;
                v.y = Mathf.Abs(v.y) + 0.5f;
                p.velocity = (v.normalized + dir * 0.8f).normalized * speed * Random.Range(0.5f, 1.1f);
                p.life = p.maxLife = Random.Range(0.5f, 0.9f);
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
                var p = t.gameObject.AddComponent<FlutterParticle>();
                Vector3 v = Random.onUnitSphere;
                v.y = Mathf.Abs(v.y) + 0.6f;
                p.velocity = (v.normalized + dir * 0.7f).normalized * speed * Random.Range(0.5f, 1.1f);
                p.spin = Random.onUnitSphere * Random.Range(300f, 900f);
                p.life = p.maxLife = Random.Range(0.8f, 1.3f);
            }
        }
    }

    /// <summary>Light, air-braked particle that tumbles (confetti).</summary>
    public class FlutterParticle : MonoBehaviour
    {
        public Vector3 velocity, spin;
        public float life, maxLife;
        Vector3 startScale;

        void Start() => startScale = transform.localScale;

        void Update()
        {
            float dt = Time.deltaTime;
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
            transform.localScale = startScale * Mathf.Clamp01(life / (maxLife * 0.4f));
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

        public static void Spawn(Vector3 center, float radius, Color color, float duration)
        {
            var go = new GameObject("SquareRing");
            go.transform.position = new Vector3(center.x, 0.03f, center.z);
            go.transform.rotation = Quaternion.Euler(0f, Random.value < 0.5f ? 0f : 45f, 0f);
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
                instance.basePos[f, r] = squares[f, r].localPosition;
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
    public class HitFlashFx : MonoBehaviour
    {
        const float Life = 0.13f;
        float size, t;

        public static void Spawn(Vector3 pos, Color color, float size)
        {
            var tr = PieceFactory.Part(PrimitiveType.Sphere, null, pos, Vector3.one * 0.05f, MaterialLib.Emissive(color, 6f));
            tr.name = "HitFlash";
            tr.gameObject.AddComponent<HitFlashFx>().size = size;
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

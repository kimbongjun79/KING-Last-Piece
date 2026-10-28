using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Materials for the ranged special items' effects, built from Tazo FX images: unlit additive copies of the Dark
    /// aura's URP particle material (black in the image adds nothing, so the pack's black-backed flares work as they are).
    /// </summary>
    public static class RangedFx
    {
        static Material flash, streak, white;

        static Material Template()
        {
            var lib = FxLibrary.Instance;
            return lib != null && lib.eliteAuraBlack != null && lib.eliteAuraBlack.Length > 0 ? lib.eliteAuraBlack[0] : null;
        }

        static Texture2D Tex(int i)
        {
            var lib = FxLibrary.Instance;
            return lib != null && lib.rangedTextures != null && i < lib.rangedTextures.Length ? lib.rangedTextures[i] : null;
        }

        static Material Additive(string name, Texture2D tex)
        {
            var template = Template();
            if (template == null) return MaterialLib.Emissive(Color.white, 3f);
            var m = new Material(template) { name = name };
            m.SetTexture("_BaseMap", tex != null ? tex : Texture2D.whiteTexture);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Blend", 2f);   // additive
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            return m;
        }

        /// <summary>A star flare (Center_x_shock), additive.</summary>
        public static Material Flash => flash ??= Additive("Ranged Flash", Tex(0));
        /// <summary>A soft horizontal energy streak (Energy_Line_03), additive.</summary>
        public static Material Streak => streak ??= Additive("Ranged Streak", Tex(1));

        /// <summary>Plain white, alpha-blended and unlit (pure white on the board whatever the light).</summary>
        public static Material White
        {
            get
            {
                if (white != null) return white;
                var template = Template();
                if (template == null) return white = MaterialLib.Emissive(Color.white, 2f);
                white = new Material(template) { name = "Ranged White" };
                white.SetTexture("_BaseMap", Texture2D.whiteTexture);
                white.SetColor("_BaseColor", Color.white);
                return white;
            }
        }

        static Mesh quad;
        /// <summary>A unit quad facing -Z with white vertex colours (the particle shader multiplies by them).</summary>
        public static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "Ranged Quad" };
                quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
                quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
                quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                quad.RecalculateNormals();
                quad.RecalculateBounds();
                return quad;
            }
        }

        /// <summary>A flare that pops and fades at <paramref name="pos"/>, facing the camera.</summary>
        public static void Burst(Vector3 pos, float size, Color color, float life = 0.22f)
        {
            var go = new GameObject("Flare");
            go.transform.position = pos;
            var f = go.AddComponent<FlareFx>();
            f.size = size;
            f.color = color;
            f.life = life;
        }

        /// <summary>A lightning bolt flickering from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static void Bolt(Vector3 from, Vector3 to, float life = 0.28f)
        {
            var go = new GameObject("Bolt");
            var b = go.AddComponent<BoltFx>();
            b.from = from;
            b.to = to;
            b.life = life;
        }
    }

    /// <summary>A camera-facing flare that pops up to size and fades.</summary>
    public class FlareFx : MonoBehaviour
    {
        public float size = 1f, life = 0.22f;
        public Color color = Color.white;
        Material mat;
        float t;

        void Start()
        {
            gameObject.AddComponent<MeshFilter>().sharedMesh = RangedFx.Quad;
            var r = gameObject.AddComponent<MeshRenderer>();
            mat = new Material(RangedFx.Flash);
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            transform.Rotate(0f, 0f, Random.value * 360f, Space.Self);
            LateUpdate();
        }

        void LateUpdate()
        {
            if (mat == null) return;
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / life);
            var cam = Camera.main;
            if (cam != null)
            {
                float roll = transform.localEulerAngles.z;
                transform.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up) * Quaternion.Euler(0f, 0f, roll);
            }
            transform.localScale = Vector3.one * size * Mathf.Lerp(0.4f, 1f, PoseTrack.Curve(Ease.Out, Mathf.Clamp01(p * 3f)));
            mat.SetColor("_BaseColor", new Color(color.r, color.g, color.b, 1f - p));
            if (p >= 1f) Destroy(gameObject);
        }

        void OnDestroy() { if (mat != null) Destroy(mat); }
    }

    /// <summary>A jagged bolt (a glowing streak around a thin white core) re-drawn every few frames so it crackles.</summary>
    public class BoltFx : MonoBehaviour
    {
        const int Segments = 9;
        const float Jitter = 0.38f, Redraw = 0.04f;
        static readonly Color Glow = new Color(0.62f, 0.8f, 1f);
        public Vector3 from, to;
        public float life = 0.28f;
        LineRenderer glow, core;
        Material glowMat;
        float t, redraw;

        void Start()
        {
            glowMat = new Material(RangedFx.Streak);
            glow = Line("Glow", glowMat, 0.45f);
            core = Line("Core", RangedFx.White, 0.07f);
            Shape();
        }

        LineRenderer Line(string name, Material m, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<LineRenderer>();
            l.sharedMaterial = m;
            l.positionCount = Segments + 1;
            l.widthMultiplier = width;
            l.textureMode = LineTextureMode.Stretch;
            l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            l.receiveShadows = false;
            l.numCapVertices = 2;
            return l;
        }

        void Shape()
        {
            Vector3 along = to - from;
            Vector3 side = Vector3.Cross(along.normalized, Vector3.up);
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            side.Normalize();
            for (int i = 0; i <= Segments; i++)
            {
                float k = i / (float)Segments;
                Vector3 p = from + along * k;
                if (i > 0 && i < Segments)
                {
                    float j = Jitter * Mathf.Sin(k * Mathf.PI);
                    p += side * Random.Range(-j, j) + Vector3.up * Random.Range(-j, j) * 0.6f;
                }
                glow.SetPosition(i, p);
                core.SetPosition(i, p);
            }
        }

        void Update()
        {
            t += Time.deltaTime;
            redraw -= Time.deltaTime;
            if (redraw <= 0f) { redraw = Redraw; Shape(); }
            float a = 1f - Mathf.Clamp01(t / life);
            // A flicker: the bolt blinks a little as it fades.
            a *= Random.value < 0.2f ? 0.4f : 1f;
            glow.startColor = glow.endColor = new Color(Glow.r, Glow.g, Glow.b, a);
            core.startColor = core.endColor = new Color(1f, 1f, 1f, a);
            if (t >= life) Destroy(gameObject);
        }

        void OnDestroy() { if (glowMat != null) Destroy(glowMat); }
    }

    /// <summary>
    /// Sword Wave (special item): a NamuFX slash flying straight ahead from the 4th light hit (fresh slashes keep riding
    /// along with it while it flies, white sparks trailing); it passes through pieces, striking each once. Without the
    /// NamuFX pack it falls back to a white crescent of blade light with a glowing streak.
    /// </summary>
    public class SwordWaveFx : MonoBehaviour
    {
        const float Speed = 20f, BossCounterSpeed = 13f, HalfWidth = 1.3f, SlashEvery = 0.11f, SlashScale = 1.5f;
        static Mesh crescent;

        PlayerController owner;
        EnemyBase counterTarget;
        Vector3 dir;
        float reach, size = 1f, travelled, sparkTimer, slashTimer = SlashEvery;
        bool namu, bossCounterWave;
        Material coreMat, glowMat;
        readonly HashSet<EnemyBase> struck = new HashSet<EnemyBase>();

        /// <param name="size">Scales the wave (its width and slashes) — the attack range.</param>
        public static void Spawn(PlayerController owner, Vector3 from, Vector3 dir, float reach, float size = 1f) =>
            Create(owner, from, dir, reach, size);

        /// <summary>A single homing sword wave for a boss counter; only the dodged boss can be struck.</summary>
        public static void SpawnBossCounter(PlayerController owner, EnemyBase target, Vector3 from)
        {
            if (owner == null || target == null || target.IsDead) return;
            Vector3 dir = target.FlatPos - from;
            if (dir.sqrMagnitude < 0.01f) return;
            var wave = Create(owner, from, dir, GameManager.BoardHalf * 2.7f, 1.25f);
            wave.counterTarget = target;
            wave.bossCounterWave = true;
            wave.name = "Boss Counter Wave";
        }

        static SwordWaveFx Create(PlayerController owner, Vector3 from, Vector3 dir, float reach, float size)
        {
            var go = new GameObject("Sword Wave");
            go.transform.position = new Vector3(from.x, 0.9f, from.z);
            dir.y = 0f;
            go.transform.rotation = Quaternion.LookRotation(dir.normalized);
            var w = go.AddComponent<SwordWaveFx>();
            w.owner = owner;
            w.dir = dir.normalized;
            w.reach = reach;
            w.size = size;
            w.Build();
            return w;
        }

        void Build()
        {
            namu = Slash();
            if (namu) return;
            crescent ??= BuildCrescent(24);
            coreMat = new Material(RangedFx.White);
            glowMat = new Material(RangedFx.Streak);
            Layer("Core", coreMat, new Vector3(1f, 1f, 1f), 0.02f);
            Layer("Glow", glowMat, new Vector3(1.25f, 1f, 1.6f), 0f);
            transform.localScale = Vector3.one * HalfWidth * size;
        }

        /// <summary>
        /// White NamuFX slashes riding along with the wave: the slash and its mirror image laid over each other, so the
        /// lopsided arc reads as a symmetric crescent.
        /// </summary>
        bool Slash()
        {
            bool ok = Vfx.SlashFollowing(SlashFxKind.SlashB, transform.position, transform.rotation, SlashScale * size, Vfx.Accent, transform);
            if (ok) Vfx.SlashFollowing(SlashFxKind.SlashB, transform.position, transform.rotation, SlashScale * size, Vfx.Accent, transform, true);
            return ok;
        }

        void Layer(string name, Material m, Vector3 scale, float lift)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            t.localScale = scale;
            t.localPosition = Vector3.up * lift;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = crescent;
            var r = t.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void Update()
        {
            if (owner == null) { Destroy(gameObject); return; }
            if (bossCounterWave)
            {
                if (counterTarget == null || counterTarget.IsDead || !counterTarget.Targetable) { Destroy(gameObject); return; }
                Vector3 toward = counterTarget.FlatPos - new Vector3(transform.position.x, 0f, transform.position.z);
                if (toward.sqrMagnitude > 0.01f)
                {
                    dir = toward.normalized;
                    transform.rotation = Quaternion.LookRotation(dir);
                }
            }
            float step = (bossCounterWave ? BossCounterSpeed : Speed) * Time.deltaTime;
            travelled += step;
            transform.position += dir * step;
            float p = Mathf.Clamp01(travelled / reach);
            // Grows a little as it flies; fades over the last third.
            transform.localScale = Vector3.one * HalfWidth * size * Mathf.Lerp(0.8f, 1.2f, p);
            float a = p < 0.66f ? 1f : 1f - (p - 0.66f) / 0.34f;
            if (namu)
            {
                slashTimer -= Time.deltaTime;
                if (slashTimer <= 0f && p < 0.7f) { slashTimer = SlashEvery; Slash(); }
            }
            else
            {
                coreMat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.9f * a));
                glowMat.SetColor("_BaseColor", new Color(1f, 0.9f, 0.65f, a));
            }

            sparkTimer -= Time.deltaTime;
            if (sparkTimer <= 0f)
            {
                sparkTimer = 0.035f;
                Vector3 side = Vector3.Cross(Vector3.up, dir) * Random.Range(-HalfWidth, HalfWidth) * size;
                Effects.Sparks(transform.position + side, Vfx.Accent, 1, 4f);
            }

            float width = HalfWidth * size * Mathf.Lerp(0.8f, 1.2f, p);
            Vector3 flat = new Vector3(transform.position.x, 0f, transform.position.z);
            if (bossCounterWave)
            {
                if ((counterTarget.FlatPos - flat).magnitude <= counterTarget.Radius + 0.6f)
                {
                    owner.BossCounterStrike(counterTarget, transform.position);
                    Destroy(gameObject);
                    return;
                }
            }
            else
            {
                foreach (var e in GameManager.Instance.Enemies.ToArray())
                {
                    if (e == null || e.IsDead || !e.Targetable || struck.Contains(e)) continue;
                    Vector3 to = e.FlatPos - flat;
                    float ahead = Vector3.Dot(to, dir);
                    if (ahead < -0.6f - e.Radius || ahead > 0.6f + e.Radius) continue;
                    if (Mathf.Abs(Vector3.Dot(to, Vector3.Cross(Vector3.up, dir))) > width + e.Radius) continue;
                    struck.Add(e);
                    owner.SwordWaveStrike(e, transform.position);
                }
            }
            if (travelled >= reach) Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (coreMat != null) Destroy(coreMat);
            if (glowMat != null) Destroy(glowMat);
        }

        /// <summary>
        /// A flat crescent facing up, bulging forward (+Z): radius 1 at the middle, thick there and tapering to points at
        /// both horns. U runs along the arc (the streak texture lies along it), V across.
        /// </summary>
        static Mesh BuildCrescent(int segments)
        {
            const float Arc = 140f, Thick = 0.32f;
            var verts = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[verts.Length];
            var colors = new Color[verts.Length];
            var tris = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float k = i / (float)segments;
                float a = Mathf.Deg2Rad * (-Arc * 0.5f + Arc * k);
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a) - 0.7f);   // centred near the blade
                var n = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                float th = Thick * Mathf.Sin(k * Mathf.PI);
                verts[i * 2] = d;
                verts[i * 2 + 1] = d - n * th;
                uv[i * 2] = new Vector2(k, 1f);
                uv[i * 2 + 1] = new Vector2(k, 0f);
                colors[i * 2] = colors[i * 2 + 1] = Color.white;
            }
            for (int i = 0; i < segments; i++)
            {
                int o = i * 2;
                // Seen from above (clockwise = front).
                tris[i * 6 + 0] = o; tris[i * 6 + 1] = o + 2; tris[i * 6 + 2] = o + 1;
                tris[i * 6 + 3] = o + 2; tris[i * 6 + 4] = o + 3; tris[i * 6 + 5] = o + 1;
            }
            var m = new Mesh { name = "Sword Wave", vertices = verts, uv = uv, colors = colors, triangles = tris };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Black Aurora's ground field, built from Tazo FX images (no particle prefab): flat layers lying on the board around
    /// the King and following him — a dark pool, two drifts of black smoke turning opposite ways, faint purple-black rays
    /// slowly wheeling, and dark waves that keep spreading out to the field's edge and fading.
    /// </summary>
    public class AuroraFieldFx : MonoBehaviour
    {
        const float WaveEvery = 0.9f, WaveLife = 1.8f;

        Transform follow;
        float radius, waveTimer;
        Transform pool, smokeA, smokeB, rays;
        readonly List<(Transform t, Material m, float age)> waves = new List<(Transform, Material, float)>();
        readonly List<Material> owned = new List<Material>();
        Material template;
        Texture2D waveTex;

        static readonly Color Ink = new Color(0.04f, 0.02f, 0.06f);
        static readonly Color Violet = new Color(0.22f, 0.06f, 0.3f);

        public static AuroraFieldFx Spawn(Transform follow, float radius)
        {
            var go = new GameObject("Black Aurora");
            var fx = go.AddComponent<AuroraFieldFx>();
            fx.follow = follow;
            fx.radius = radius;
            fx.Build();
            return fx;
        }

        void Build()
        {
            var lib = FxLibrary.Instance;
            var tex = lib != null ? lib.auroraTextures : null;
            // Alpha-blended unlit material (the Dark aura's), so black reads on the bright board.
            template = lib != null && lib.eliteAuraBlack != null && lib.eliteAuraBlack.Length > 0 ? lib.eliteAuraBlack[0] : null;
            Texture2D Tex(int i) => tex != null && i < tex.Length ? tex[i] : null;
            float d = radius * 2f;
            pool = Layer("Pool", Tex(0), Ink, 0.55f, d * 1.15f, 0.03f);
            smokeA = Layer("Smoke A", Tex(1), Ink, 0.6f, d * 1.05f, 0.04f);
            smokeB = Layer("Smoke B", Tex(1), Ink, 0.45f, d * 0.8f, 0.05f);
            rays = Layer("Rays", Tex(2), Violet, 0.35f, d * 1.1f, 0.06f);
            waveTex = Tex(3);
        }

        /// <summary>A flat quad on the board with the image, tinted, at a little height above the squares.</summary>
        Transform Layer(string name, Texture2D tex, Color color, float alpha, float size, float height)
        {
            var mat = new Material(template != null ? template : MaterialLib.Transparent(color, Color.black));
            if (template != null && tex != null) mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", new Color(color.r, color.g, color.b, alpha));
            mat.name = "Aurora " + name;
            owned.Add(mat);
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            t.localPosition = Vector3.up * height;
            t.localRotation = Quaternion.Euler(90f, 0f, 0f);
            t.localScale = Vector3.one * size;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = Quad;
            var r = t.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return t;
        }

        static Mesh quad;
        /// <summary>A unit quad facing -Z with white vertex colours (the particle shader multiplies by them).</summary>
        static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "Aurora Quad" };
                quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
                quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
                quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                quad.RecalculateNormals();
                quad.RecalculateBounds();
                return quad;
            }
        }

        void LateUpdate()
        {
            if (follow == null) { Destroy(gameObject); return; }
            transform.position = new Vector3(follow.position.x, 0f, follow.position.z);
            float dt = Time.deltaTime, time = Time.time;

            // The smoke drifts turn opposite ways; the rays wheel slowly; the pool breathes.
            smokeA.localRotation = Quaternion.Euler(90f, time * 20f, 0f);
            smokeB.localRotation = Quaternion.Euler(90f, -time * 34f, 0f);
            rays.localRotation = Quaternion.Euler(90f, time * 8f, 0f);
            float breathe = 1f + 0.04f * Mathf.Sin(time * 2.2f);
            pool.localScale = Vector3.one * radius * 2.3f * breathe;

            // Dark waves spreading out from the King to the field's edge, fading as they go.
            waveTimer -= dt;
            if (waveTimer <= 0f)
            {
                waveTimer = WaveEvery;
                var w = Layer("Wave", waveTex, Violet, 0f, 0.1f, 0.055f);
                waves.Add((w, owned[owned.Count - 1], 0f));
            }
            for (int i = waves.Count - 1; i >= 0; i--)
            {
                var (t, m, age) = waves[i];
                age += dt;
                float p = age / WaveLife;
                if (p >= 1f)
                {
                    owned.Remove(m);
                    Destroy(m);
                    Destroy(t.gameObject);
                    waves.RemoveAt(i);
                    continue;
                }
                t.localScale = Vector3.one * Mathf.Lerp(0.3f, radius * 2.2f, PoseTrack.Curve(Ease.Out, p));
                t.localRotation = Quaternion.Euler(90f, age * 40f, 0f);
                float a = Mathf.Sin(p * Mathf.PI) * 0.45f;
                m.SetColor("_BaseColor", new Color(Violet.r, Violet.g, Violet.b, a));
                waves[i] = (t, m, age);
            }
        }

        void OnDestroy()
        {
            foreach (var m in owned) if (m != null) Destroy(m);
        }
    }
}

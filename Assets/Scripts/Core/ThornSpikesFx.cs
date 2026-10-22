using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Thorn Armor's burst, riding along with the King (a knockback carries it with him): a ring of steel cone spikes
    /// shoots out in every direction (a long outer ring and a shorter one between them, tilted a little up), holds a
    /// beat and draws back in; a thin white ring spreads out to the burst's reach and fades, and white sparks fly.
    /// </summary>
    public class ThornSpikesFx : MonoBehaviour
    {
        const float Out = 0.07f, Hold = 0.1f, Back = 0.16f, RingTime = 0.32f, RingWidth = 0.03f;
        static Mesh cone, ring;
        static Material steel, ringBase;

        Transform follow, ringT;
        Material ringMat;
        Transform[] spikes;
        float[] lengths;
        float t, radius;

        /// <param name="radius">How far the long spikes and the ring reach.</param>
        public static void Spawn(Transform follow, float radius)
        {
            var go = new GameObject("Thorn Spikes");
            var fx = go.AddComponent<ThornSpikesFx>();
            fx.follow = follow;
            fx.radius = radius;
            fx.Place();
            fx.Build(radius);
            for (int k = 0; k < 16; k++)
            {
                Vector3 d = Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
                Effects.Sparks(go.transform.position + d * 0.6f, Vfx.Accent, 1, 9f);
            }
        }

        /// <summary>The King's body material (the first mesh renderer under him; trails and particles skipped).</summary>
        static Material KingMaterial(Transform king)
        {
            if (king == null) return null;
            foreach (var r in king.GetComponentsInChildren<Renderer>())
            {
                if (r is TrailRenderer || r is ParticleSystemRenderer || r is LineRenderer) continue;
                if (r.sharedMaterial != null && r.sharedMaterial.GetTexture("_BaseMap") != null) return r.sharedMaterial;
            }
            return null;
        }

        void Place()
        {
            if (follow != null) transform.position = new Vector3(follow.position.x, 0.9f, follow.position.z);
        }

        void Build(float radius)
        {
            cone ??= BuildCone(10);
            ring ??= BuildRing(64);
            // The spikes are cut from the King's own marble (his body material).
            if (steel == null) steel = KingMaterial(follow) ?? MaterialLib.Get(new Color(0.78f, 0.79f, 0.82f), 0.85f, 0.9f, new Color(0.35f, 0.35f, 0.38f));
            ringBase ??= RingMaterial();
            // The ring: an outline (not a filled disc) lying on the board under the King.
            ringT = new GameObject("Ring").transform;
            ringT.SetParent(transform, false);
            ringT.localPosition = Vector3.down * 0.86f;
            ringT.gameObject.AddComponent<MeshFilter>().sharedMesh = ring;
            var rr = ringT.gameObject.AddComponent<MeshRenderer>();
            ringMat = new Material(ringBase);
            rr.sharedMaterial = ringMat;
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            const int count = 24;
            spikes = new Transform[count];
            lengths = new float[count];
            for (int i = 0; i < count; i++)
            {
                bool outer = i % 2 == 0;
                float yaw = i * 360f / count + Random.Range(-4f, 4f);
                var s = new GameObject("Spike").transform;
                s.SetParent(transform, false);
                // The cone points along its local +Y: lay it outward (tilted a little up for the long ones).
                s.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(outer ? 80f : 88f, 0f, 0f);
                s.localPosition = s.localRotation * Vector3.up * 0.3f;
                var mf = s.gameObject.AddComponent<MeshFilter>();
                mf.sharedMesh = cone;
                var mr = s.gameObject.AddComponent<MeshRenderer>();
                mr.sharedMaterial = steel;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                spikes[i] = s;
                lengths[i] = (outer ? radius * 0.85f : radius * 0.55f) * Random.Range(0.9f, 1.05f);
                s.localScale = Vector3.zero;
            }
        }

        void LateUpdate()
        {
            t += Time.deltaTime;
            Place();
            // The ring spreads out to the reach and fades.
            float rp = Mathf.Clamp01(t / RingTime);
            float rr = Mathf.Lerp(0.6f, radius, PoseTrack.Curve(Ease.Out, rp));
            ringT.localScale = new Vector3(rr, 1f, rr);
            var rc = ringMat.GetColor("_BaseColor");
            rc.a = 1f - rp;
            ringMat.SetColor("_BaseColor", rc);
            float k = t < Out ? PoseTrack.Curve(Ease.Out, t / Out)
                    : t < Out + Hold ? 1f
                    : 1f - PoseTrack.Curve(Ease.In, Mathf.Clamp01((t - Out - Hold) / Back));
            for (int i = 0; i < spikes.Length; i++)
            {
                float len = lengths[i] * k, w = Mathf.Lerp(0.12f, 0.26f, lengths[i] / 4f) * Mathf.Clamp01(k * 3f);
                spikes[i].localScale = new Vector3(w, len, w);
            }
            if (t >= Mathf.Max(Out + Hold + Back, RingTime)) Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (ringMat != null) Destroy(ringMat);
        }

        /// <summary>
        /// Pure white whatever the lighting: an unlit alpha-blended copy of the Dark aura's particle material with a plain
        /// white texture (a lit material greys out on the board).
        /// </summary>
        static Material RingMaterial()
        {
            var lib = FxLibrary.Instance;
            var template = lib != null && lib.eliteAuraBlack != null && lib.eliteAuraBlack.Length > 0 ? lib.eliteAuraBlack[0] : null;
            if (template == null) return MaterialLib.Emissive(Color.white, 2f);
            var m = new Material(template) { name = "Thorn Ring" };
            m.SetTexture("_BaseMap", Texture2D.whiteTexture);
            m.SetColor("_BaseColor", Color.white);
            return m;
        }

        /// <summary>A flat ring (annulus) of outer radius 1 and width <see cref="RingWidth"/>, facing up.</summary>
        static Mesh BuildRing(int segments)
        {
            var verts = new Vector3[segments * 2];
            var tris = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts[i * 2] = dir;
                verts[i * 2 + 1] = dir * (1f - RingWidth);
                int n = (i + 1) % segments;
                // Seen from above (clockwise = front).
                tris[i * 6 + 0] = i * 2; tris[i * 6 + 1] = i * 2 + 1; tris[i * 6 + 2] = n * 2;
                tris[i * 6 + 3] = n * 2; tris[i * 6 + 4] = i * 2 + 1; tris[i * 6 + 5] = n * 2 + 1;
            }
            var colors = new Color[verts.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;   // the particle shader multiplies by them
            var m = new Mesh { name = "Thorn Ring", vertices = verts, triangles = tris, colors = colors };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A cone along +Y: base circle of radius 0.5 at y = 0, tip at y = 1.</summary>
        static Mesh BuildCone(int sides)
        {
            var verts = new Vector3[sides * 2 + 2];
            var tris = new int[sides * 6];
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                var p = new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f);
                verts[i] = p;              // side ring
                verts[sides + i] = p;      // base ring
            }
            int tip = sides * 2, bottom = tip + 1;
            verts[tip] = Vector3.up;
            verts[bottom] = Vector3.zero;
            for (int i = 0; i < sides; i++)
            {
                int n = (i + 1) % sides;
                tris[i * 6 + 0] = i; tris[i * 6 + 1] = tip; tris[i * 6 + 2] = n;
                tris[i * 6 + 3] = sides + n; tris[i * 6 + 4] = bottom; tris[i * 6 + 5] = sides + i;
            }
            // Clockwise seen from outside (Unity's front faces).
            // Planar UVs from the vertex position, so the King's marble texture reads on the spikes.
            var uv = new Vector2[verts.Length];
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(verts[i].x * 0.5f + 0.5f, verts[i].y * 0.5f + verts[i].z * 0.25f + 0.25f);
            var m = new Mesh { name = "Thorn Cone", vertices = verts, triangles = tris, uv = uv };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}

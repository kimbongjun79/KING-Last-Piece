using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Thorn Armor's burst: a ring of steel cone spikes shoots out of the King in every direction (a long outer ring and
    /// a shorter one between them, tilted a little up), holds a beat, then draws back in.
    /// </summary>
    public class ThornSpikesFx : MonoBehaviour
    {
        const float Out = 0.07f, Hold = 0.1f, Back = 0.16f;
        static Mesh cone;
        static Material steel;

        Transform[] spikes;
        float[] lengths;
        float t;

        /// <param name="radius">How far the long spikes reach.</param>
        public static void Spawn(Vector3 center, float radius)
        {
            var go = new GameObject("Thorn Spikes");
            go.transform.position = new Vector3(center.x, 0.9f, center.z);
            var fx = go.AddComponent<ThornSpikesFx>();
            fx.Build(radius);
        }

        void Build(float radius)
        {
            cone ??= BuildCone(10);
            steel ??= MaterialLib.Get(new Color(0.78f, 0.79f, 0.82f), 0.85f, 0.9f, new Color(0.35f, 0.35f, 0.38f));
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

        void Update()
        {
            t += Time.deltaTime;
            float k = t < Out ? PoseTrack.Curve(Ease.Out, t / Out)
                    : t < Out + Hold ? 1f
                    : 1f - PoseTrack.Curve(Ease.In, Mathf.Clamp01((t - Out - Hold) / Back));
            for (int i = 0; i < spikes.Length; i++)
            {
                float len = lengths[i] * k, w = Mathf.Lerp(0.12f, 0.26f, lengths[i] / 4f) * Mathf.Clamp01(k * 3f);
                spikes[i].localScale = new Vector3(w, len, w);
            }
            if (t >= Out + Hold + Back) Destroy(gameObject);
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
            var m = new Mesh { name = "Thorn Cone", vertices = verts, triangles = tris };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}

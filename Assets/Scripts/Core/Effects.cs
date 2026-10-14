using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>Cheap primitive-based VFX: hit sparks and ground shockwaves.</summary>
    public static class Effects
    {
        public static void Sparks(Vector3 pos, Color color, int count = 10, float speed = 5f)
        {
            var mat = MaterialLib.Emissive(color, 4f);
            for (int i = 0; i < count; i++)
            {
                var t = PieceFactory.Part(PrimitiveType.Cube, null, pos, Vector3.one * Random.Range(0.06f, 0.13f), mat,
                    Random.rotation.eulerAngles);
                var p = t.gameObject.AddComponent<SparkParticle>();
                Vector3 dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) + 0.3f;
                p.velocity = dir.normalized * speed * Random.Range(0.5f, 1.2f);
                p.life = p.maxLife = Random.Range(0.25f, 0.5f);
            }
        }

        public static void Shockwave(Vector3 center, float radius, Color color, float duration = 0.4f)
        {
            center.y = 0.03f;
            var t = PieceFactory.Part(PrimitiveType.Cylinder, null, center, new Vector3(0.1f, 0.01f, 0.1f),
                MaterialLib.Emissive(color, 2.5f));
            var s = t.gameObject.AddComponent<ShockwaveFx>();
            s.radius = radius;
            s.duration = duration;
        }
    }

    public class SparkParticle : MonoBehaviour
    {
        public Vector3 velocity;
        public float life, maxLife;
        Vector3 startScale;

        void Start() => startScale = transform.localScale;

        void Update()
        {
            float dt = Time.deltaTime;
            velocity.y -= 16f * dt;
            transform.position += velocity * dt;
            if (transform.position.y < 0.05f)
            {
                transform.position = new Vector3(transform.position.x, 0.05f, transform.position.z);
                velocity *= 0.5f;
                velocity.y = Mathf.Abs(velocity.y) * 0.3f;
            }
            life -= dt;
            transform.localScale = startScale * Mathf.Clamp01(life / maxLife);
            if (life <= 0f) Destroy(gameObject);
        }
    }

    public class ShockwaveFx : MonoBehaviour
    {
        public float radius = 2f, duration = 0.4f;
        float t;

        void Update()
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            float r = Mathf.Lerp(0.2f, radius * 2f, 1f - (1f - p) * (1f - p));
            transform.localScale = new Vector3(r, 0.01f, r);
            // Sink below the board to "fade" (materials are opaque).
            var pos = transform.position;
            pos.y = p < 0.5f ? 0.03f : Mathf.Lerp(0.03f, -0.1f, (p - 0.5f) * 2f);
            transform.position = pos;
            if (p >= 1f) Destroy(gameObject);
        }
    }
}

namespace KingLastPiece
{
    /// <summary>
    /// Glowing crescent drawn on the ground that follows a body sweep, so the full hit range reads visually.
    /// Segments appear progressively from <see cref="from"/> to <see cref="to"/> (degrees relative to forward, + = right).
    /// </summary>
    public class SlashFx : MonoBehaviour
    {
        public Vector3 center, forward;
        public float radius, from, to, sweepTime, life = 0.28f;
        public Color color;
        const int Segments = 18;
        float t;
        int spawned;
        Material mat;

        public static void Spawn(Vector3 center, Vector3 forward, float radius, float from, float to, float sweepTime, Color color)
        {
            var fx = new GameObject("SlashFx").AddComponent<SlashFx>();
            fx.center = new Vector3(center.x, 0f, center.z);
            fx.forward = new Vector3(forward.x, 0f, forward.z).normalized;
            fx.radius = radius;
            fx.from = from;
            fx.to = to;
            fx.sweepTime = Mathf.Max(0.01f, sweepTime);
            fx.color = color;
        }

        void Start() => mat = MaterialLib.Emissive(color, 3.5f);

        void Update()
        {
            t += Time.deltaTime;
            int target = Mathf.Min(Segments, Mathf.CeilToInt(t / sweepTime * Segments));
            float step = (to - from) / Segments;
            float chord = Mathf.Abs(step) * Mathf.Deg2Rad * radius * 1.15f;
            for (; spawned < target; spawned++)
            {
                float a = from + step * (spawned + 0.5f);
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * forward;
                // Thicker in the middle of the arc, tapered at both ends; long axis along the tangent.
                float k = Mathf.Sin((spawned + 0.5f) / Segments * Mathf.PI);
                PieceFactory.Part(PrimitiveType.Cube, transform, center + dir * radius + Vector3.up * 0.04f,
                        new Vector3(0.12f + 0.22f * k, 0.03f, chord), mat,
                        (Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 90f, 0f)).eulerAngles)
                    .gameObject.AddComponent<FadeSink>().life = life;
            }
            if (spawned >= Segments && transform.childCount == 0) Destroy(gameObject);
        }
    }

    /// <summary>Shrinks a piece of opaque VFX to nothing, then destroys it.</summary>
    public class FadeSink : MonoBehaviour
    {
        public float life = 0.3f;
        float t;
        Vector3 start;

        void Start() => start = transform.localScale;

        void Update()
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / life);
            transform.localScale = new Vector3(start.x * (1f - p), start.y, start.z * (1f - p * 0.5f));
            if (p >= 1f) Destroy(gameObject);
        }
    }
}

namespace KingLastPiece
{
    /// <summary>
    /// Shatters a piece into fragments of its real mesh (shell shards with its own material) that collapse like a
    /// crumbling castle wall: the base gives way first, upper shards follow, everything tumbles, bounces, settles
    /// and sinks into the board. Fragment count scales with the piece's height, so the rubble matches its size.
    /// Models whose meshes can't be read (or primitive-built pieces) fall back to a few small stone blocks.
    /// </summary>
    public static class Crumble
    {
        static readonly int CullId = Shader.PropertyToID("_Cull");
        static readonly Dictionary<Material, Material> twoSided = new Dictionary<Material, Material>();

        public static void Spawn(Renderer[] renderers, Vector3 away)
        {
            if (renderers == null || renderers.Length == 0) return;
            away.y = 0f;
            if (away.sqrMagnitude > 0.001f) away.Normalize();

            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);

            if (!SpawnShards(renderers, b, away)) SpawnBlocks(b, renderers[0].sharedMaterial, away);

            var dust = new Color(0.22f, 0.2f, 0.18f);
            float baseR = Mathf.Max(0.15f, Mathf.Min(b.extents.x, b.extents.z));
            Effects.Shockwave(new Vector3(b.center.x, 0f, b.center.z), baseR * 1.6f, dust, 0.45f);
            Effects.Sparks(new Vector3(b.center.x, b.min.y + 0.2f, b.center.z), dust, 18, 4f);
        }

        /// <summary>Splits the surface into Voronoi-like shards around seed points stacked in height layers.</summary>
        static bool SpawnShards(Renderer[] renderers, Bounds b, Vector3 away)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            Material mat = null;

            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                var mesh = mf.sharedMesh;
                var m = r.transform.localToWorldMatrix;
                int baseIndex = verts.Count;
                var mv = mesh.vertices;
                var mn = mesh.normals;
                var mu = mesh.uv;
                for (int i = 0; i < mv.Length; i++)
                {
                    verts.Add(m.MultiplyPoint3x4(mv[i]));
                    norms.Add(mn.Length == mv.Length ? m.MultiplyVector(mn[i]).normalized : Vector3.up);
                    uvs.Add(mu.Length == mv.Length ? mu[i] : Vector2.zero);
                }
                for (int s = 0; s < mesh.subMeshCount; s++)
                    foreach (int idx in mesh.GetTriangles(s)) tris.Add(baseIndex + idx);
                if (mat == null) mat = r.sharedMaterial;
            }
            if (tris.Count < 30) return false;

            // Seeds: a few per height layer, slightly inside the silhouette.
            int layers = Mathf.Clamp(Mathf.RoundToInt(b.size.y / 0.45f), 3, 6);
            int perLayer = b.extents.x > 0.6f ? 4 : 3;
            var seeds = new List<Vector3>();
            float r0 = Mathf.Min(b.extents.x, b.extents.z);
            for (int l = 0; l < layers; l++)
            {
                float y = b.min.y + b.size.y * (l + Random.Range(0.25f, 0.75f)) / layers;
                float offset = Random.value * 360f;
                for (int k = 0; k < perLayer; k++)
                {
                    Vector3 dir = Quaternion.Euler(0f, offset + k * 360f / perLayer, 0f) * Vector3.forward;
                    seeds.Add(new Vector3(b.center.x, y, b.center.z) + dir * r0 * Random.Range(0.3f, 0.8f));
                }
            }

            // Assign each triangle to its nearest seed.
            var buckets = new List<int>[seeds.Count];
            for (int i = 0; i < buckets.Length; i++) buckets[i] = new List<int>();
            for (int t = 0; t < tris.Count; t += 3)
            {
                Vector3 c = (verts[tris[t]] + verts[tris[t + 1]] + verts[tris[t + 2]]) / 3f;
                int best = 0;
                float bd = float.MaxValue;
                for (int s = 0; s < seeds.Count; s++)
                {
                    float d = (c - seeds[s]).sqrMagnitude;
                    if (d < bd) { bd = d; best = s; }
                }
                buckets[best].Add(t);
            }

            var shardMat = TwoSided(mat);
            var remap = new Dictionary<int, int>();
            for (int s = 0; s < seeds.Count; s++)
            {
                var bucket = buckets[s];
                if (bucket.Count == 0) continue;
                remap.Clear();
                var sv = new List<Vector3>();
                var sn = new List<Vector3>();
                var su = new List<Vector2>();
                var st = new List<int>(bucket.Count * 3);
                Vector3 centroid = Vector3.zero;
                foreach (int t in bucket)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int vi = tris[t + k];
                        if (!remap.TryGetValue(vi, out int ni))
                        {
                            ni = sv.Count;
                            remap[vi] = ni;
                            sv.Add(verts[vi]);
                            sn.Add(norms[vi]);
                            su.Add(uvs[vi]);
                            centroid += verts[vi];
                        }
                        st.Add(ni);
                    }
                }
                centroid /= sv.Count;
                for (int i = 0; i < sv.Count; i++) sv[i] -= centroid;

                var shardMesh = new Mesh { name = "Shard" };
                if (sv.Count > 65000) shardMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                shardMesh.SetVertices(sv);
                shardMesh.SetNormals(sn);
                shardMesh.SetUVs(0, su);
                shardMesh.SetTriangles(st, 0);
                shardMesh.RecalculateBounds();
                shardMesh.RecalculateTangents();

                var go = new GameObject("Shard");
                go.transform.position = centroid;
                go.AddComponent<MeshFilter>().sharedMesh = shardMesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = shardMat;

                float h = Mathf.Clamp01((centroid.y - b.min.y) / Mathf.Max(0.01f, b.size.y));
                Vector3 outward = centroid - new Vector3(b.center.x, centroid.y, b.center.z);
                outward.y = 0f;
                outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : Vector3.forward;

                var chunk = go.AddComponent<CrumbleChunk>();
                chunk.ownedMesh = shardMesh;
                chunk.groundOffset = shardMesh.bounds.extents.magnitude * 0.35f;
                // Lower shards give way first; the top follows as the support disappears.
                chunk.delay = h * 0.12f + Random.Range(0f, 0.04f);
                chunk.velocity = outward * Random.Range(0.5f, 2f) + away * Random.Range(0.2f, 1.4f) +
                                 Vector3.up * Random.Range(-0.4f, 1.3f) * (1f - h * 0.5f);
                chunk.spin = Random.onUnitSphere * Random.Range(100f, 420f);
                chunk.settleTime = Random.Range(1.4f, 2.2f);
            }
            return true;
        }

        /// <summary>Shards are open shells, so draw both faces.</summary>
        static Material TwoSided(Material src)
        {
            if (src == null) return MaterialLib.Get(PieceFactory.BlackPiece, 0.8f);
            if (twoSided.TryGetValue(src, out var m) && m != null) return m;
            m = new Material(src) { name = src.name + "_TwoSided" };
            if (m.HasProperty(CullId)) m.SetFloat(CullId, 0f);
            m.doubleSidedGI = true;
            twoSided[src] = m;
            return m;
        }

        /// <summary>Fallback for pieces without a readable mesh: a modest pile of small blocks.</summary>
        static void SpawnBlocks(Bounds b, Material mat, Vector3 away)
        {
            Vector3 c = b.center;
            float baseR = Mathf.Max(0.15f, Mathf.Min(b.extents.x, b.extents.z));
            int layers = Mathf.Clamp(Mathf.RoundToInt(b.size.y / 0.3f), 3, 10);
            for (int l = 0; l < layers; l++)
            {
                float h = (l + 0.5f) / layers;
                float y = b.min.y + b.size.y * h;
                float r = baseR * Mathf.Lerp(0.7f, 0.45f, h);
                for (int k = 0; k < 3; k++)
                {
                    Vector3 dir = Quaternion.Euler(0f, Random.value * 360f, 0f) * Vector3.forward;
                    float s = Random.Range(0.1f, 0.18f);
                    var t = PieceFactory.Part(PrimitiveType.Cube, null, new Vector3(c.x, y, c.z) + dir * r * 0.6f,
                        new Vector3(s, s * 0.8f, s), mat, Random.rotation.eulerAngles);
                    var chunk = t.gameObject.AddComponent<CrumbleChunk>();
                    chunk.groundOffset = s * 0.4f;
                    chunk.delay = h * 0.12f;
                    chunk.velocity = dir * Random.Range(0.5f, 2f) + away * Random.Range(0.2f, 1.4f) + Vector3.up * Random.Range(-0.4f, 1.2f);
                    chunk.spin = Random.onUnitSphere * Random.Range(120f, 480f);
                    chunk.settleTime = Random.Range(1.4f, 2.2f);
                }
            }
        }
    }

    /// <summary>One piece of rubble: waits, falls with gravity, bounces on the board, settles and sinks away.</summary>
    public class CrumbleChunk : MonoBehaviour
    {
        public float delay, settleTime = 1.8f, groundOffset = 0.1f;
        public Vector3 velocity, spin;
        public Mesh ownedMesh;  // runtime shard mesh, freed with the chunk
        float age;

        void OnDestroy()
        {
            if (ownedMesh != null) Destroy(ownedMesh);
        }
        const float Gravity = 18f, SinkTime = 0.6f;

        void Update()
        {
            float dt = Time.deltaTime;
            if (delay > 0f) { delay -= dt; return; }
            age += dt;

            if (age > settleTime)
            {
                // Sink into the board and disappear.
                float p = (age - settleTime) / SinkTime;
                transform.position += Vector3.down * (0.5f * dt / SinkTime);
                if (p >= 1f) Destroy(gameObject);
                return;
            }

            velocity.y -= Gravity * dt;
            transform.position += velocity * dt;
            transform.Rotate(spin * dt, Space.World);

            float ground = groundOffset;
            if (transform.position.y < ground)
            {
                var p = transform.position;
                p.y = ground;
                transform.position = p;
                if (velocity.y < -1.2f)
                {
                    velocity.y = -velocity.y * 0.3f;
                    velocity.x *= 0.55f;
                    velocity.z *= 0.55f;
                    spin *= 0.5f;
                }
                else
                {
                    velocity = Vector3.Lerp(velocity, Vector3.zero, 1f - Mathf.Exp(-10f * dt));
                    velocity.y = 0f;
                    spin = Vector3.Lerp(spin, Vector3.zero, 1f - Mathf.Exp(-8f * dt));
                }
            }
        }
    }
}

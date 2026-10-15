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
                Vfx.Track(t.gameObject);
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

        /// <summary>
        /// Dark ripple: translucent black rings that spread out across the board and fade (a piece being set down).
        /// <paramref name="rings"/> waves follow each other a moment apart.
        /// </summary>
        public static void DarkWave(Vector3 center, float radius, float duration = 0.6f, int rings = 2)
        {
            for (int i = 0; i < rings; i++)
            {
                var go = new GameObject("DarkWave");
                go.transform.position = new Vector3(center.x, 0.035f, center.z);
                var w = go.AddComponent<DarkWaveFx>();
                w.radius = radius * (1f - 0.25f * i);
                w.duration = duration;
                w.delay = i * 0.12f;
                w.thickness = i == 0 ? 0.22f : 0.12f;
            }
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

    /// <summary>An expanding, fading, smooth ring of translucent onyx (see <see cref="Effects.DarkWave"/>).</summary>
    public class DarkWaveFx : MonoBehaviour
    {
        public float radius = 1.5f, duration = 0.6f, delay, thickness = 0.2f;
        const int Segments = 72;
        float t;
        Mesh mesh;
        Vector3[] verts;
        MeshRenderer mr;
        Material mat;

        void Start()
        {
            // A flat annulus mesh rebuilt each frame (inner/outer radius), so the ring stays perfectly round.
            verts = new Vector3[Segments * 2];
            var tris = new int[Segments * 6];
            for (int i = 0; i < Segments; i++)
            {
                int a = i * 2, b = ((i + 1) % Segments) * 2;
                tris[i * 6 + 0] = a; tris[i * 6 + 1] = b; tris[i * 6 + 2] = a + 1;
                tris[i * 6 + 3] = a + 1; tris[i * 6 + 4] = b; tris[i * 6 + 5] = b + 1;
            }
            mesh = new Mesh { name = "DarkWave" };
            mesh.vertices = verts;
            mesh.triangles = tris;
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = gameObject.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Own material instance so the alpha can fade without touching the shared cache.
            mat = new Material(MaterialLib.Transparent(new Color(0.02f, 0.02f, 0.03f, 0.75f)));
            mr.sharedMaterial = mat;
            mr.enabled = false;
        }

        void Update()
        {
            t += Time.deltaTime;
            if (t < delay) return;
            float p = Mathf.Clamp01((t - delay) / duration);
            float ease = 1f - (1f - p) * (1f - p) * (1f - p);
            float outer = Mathf.Lerp(0.25f, radius, ease);
            float inner = Mathf.Max(0f, outer - thickness * Mathf.Lerp(1f, 0.35f, p));
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts[i * 2] = d * inner;
                verts[i * 2 + 1] = d * outer;
            }
            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mr.enabled = true;
            var c = mat.GetColor("_BaseColor");
            c.a = 0.75f * (1f - p * p);
            mat.SetColor("_BaseColor", c);
            if (p >= 1f)
            {
                Destroy(mat);
                Destroy(mesh);
                Destroy(gameObject);
            }
        }
    }
}

namespace KingLastPiece
{
    /// <summary>
    /// Ground range marker: a clean, tapered arc laid along the attack's sweep so the real reach reads on the board.
    /// Two translucent lanes — an ivory outer edge and an onyx inner edge — grow from <see cref="from"/> to
    /// <see cref="to"/> (degrees relative to forward, + = right), then shrink away.
    /// </summary>
    public class SlashFx : MonoBehaviour
    {
        public Vector3 center, forward;
        public float radius, from, to, sweepTime, life = 0.3f;
        public Color color;
        const int Segments = 18;
        const float Alpha = 0.55f;
        float t;
        int segments, spawned;
        Material ivoryMat, onyxMat;

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

        void Start()
        {
            ivoryMat = MaterialLib.Transparent(new Color(0.97f, 0.96f, 0.93f, Alpha), new Color(0.35f, 0.35f, 0.33f));
            onyxMat = MaterialLib.Transparent(new Color(0.03f, 0.03f, 0.04f, Alpha * 0.9f));
            // Full rings get more segments so the circle stays smooth.
            segments = Mathf.Max(Segments, Mathf.CeilToInt(Mathf.Abs(to - from) / 10f));
        }

        void Update()
        {
            t += Time.deltaTime;
            int target = Mathf.Min(segments, Mathf.CeilToInt(t / sweepTime * segments));
            float step = (to - from) / segments;
            bool ring = Mathf.Abs(to - from) >= 359f;
            for (; spawned < target; spawned++)
            {
                float a = from + step * (spawned + 0.5f);
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * forward;
                // Thicker in the middle of the arc, tapered at both ends (a full ring stays even).
                float k = ring ? 0.8f : Mathf.Sin((spawned + 0.5f) / segments * Mathf.PI);
                float w = 0.08f + 0.14f * k;
                var rot = (Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 90f, 0f)).eulerAngles;
                for (int lane = 0; lane < 2; lane++)
                {
                    // Outer lane ivory, inner lane onyx; each lane's chord matches its own radius so they stay seamless.
                    float r = radius + (lane == 0 ? 0.5f : -0.5f) * w;
                    float chord = Mathf.Abs(step) * Mathf.Deg2Rad * r;
                    PieceFactory.Part(PrimitiveType.Cube, transform, center + dir * r + Vector3.up * 0.04f,
                            new Vector3(w, 0.01f, chord), lane == 0 ? ivoryMat : onyxMat, rot)
                        .gameObject.AddComponent<FadeSink>().life = life;
                }
            }
            if (spawned >= segments && transform.childCount == 0) Destroy(gameObject);
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
    /// Shatters a piece into fragments of its real mesh and throws them with physics: each shard is a rigid body
    /// that flies off along the direction of the last blow (harder blows send it farther), tumbles, bounces on the
    /// board, settles and sinks away. Shards are open shells, so their inside faces get a solid interior colour
    /// (black for the black pieces, white for the King). Models whose meshes can't be read fall back to blocks.
    /// </summary>
    public static class Crumble
    {
        static PhysicsMaterial rubble;
        static readonly List<Collider> batch = new List<Collider>();

        static PhysicsMaterial Rubble => rubble != null ? rubble : rubble = new PhysicsMaterial("Rubble")
        {
            bounciness = 0.25f, dynamicFriction = 0.6f, staticFriction = 0.7f,
            bounceCombine = PhysicsMaterialCombine.Average, frictionCombine = PhysicsMaterialCombine.Average,
        };

        /// <param name="away">Direction of the killing blow (from the attacker through the piece).</param>
        /// <param name="interior">Colour of the shards' inside faces.</param>
        /// <param name="force">~1 for a normal hit, up to ~2.5 for a big finisher.</param>
        public static void Spawn(Renderer[] renderers, Vector3 away, Color interior, float force = 1f)
        {
            if (renderers == null || renderers.Length == 0) return;
            away.y = 0f;
            away = away.sqrMagnitude > 0.001f ? away.normalized : Random.insideUnitSphere.normalized;
            away.y = 0f;

            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            var inside = MaterialLib.Get(interior, 0.15f);

            batch.Clear();
            if (!SpawnShards(renderers, b, away, inside, force)) SpawnBlocks(b, renderers[0].sharedMaterial, away, force);
            // Shards start interpenetrating: they only collide with the world, not with each other.
            for (int i = 0; i < batch.Count; i++)
                for (int j = i + 1; j < batch.Count; j++) Physics.IgnoreCollision(batch[i], batch[j]);
            batch.Clear();

            var dust = new Color(0.22f, 0.2f, 0.18f);
            float baseR = Mathf.Max(0.15f, Mathf.Min(b.extents.x, b.extents.z));
            Effects.Shockwave(new Vector3(b.center.x, 0f, b.center.z), baseR * 1.6f, dust, 0.45f);
            Effects.Sparks(new Vector3(b.center.x, b.min.y + 0.2f, b.center.z), dust, 18, 4f);
        }

        /// <summary>Splits the surface into Voronoi-like shards around seed points stacked in height layers.</summary>
        static bool SpawnShards(Renderer[] renderers, Bounds b, Vector3 away, Material inside, float force)
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

                var outer = BuildMesh("Shard", sv, sn, su, st);
                // Inside faces: same shell, reversed winding and normals, in the interior colour.
                var inNormals = new List<Vector3>(sn.Count);
                foreach (var n in sn) inNormals.Add(-n);
                var inTris = new List<int>(st.Count);
                for (int i = 0; i < st.Count; i += 3) { inTris.Add(st[i]); inTris.Add(st[i + 2]); inTris.Add(st[i + 1]); }
                var inner = BuildMesh("ShardInside", sv, inNormals, su, inTris);

                var go = new GameObject("Shard");
                go.transform.position = centroid;
                go.AddComponent<MeshFilter>().sharedMesh = outer;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                var back = new GameObject("Inside");
                back.transform.SetParent(go.transform, false);
                back.AddComponent<MeshFilter>().sharedMesh = inner;
                back.AddComponent<MeshRenderer>().sharedMaterial = inside;

                float h = Mathf.Clamp01((centroid.y - b.min.y) / Mathf.Max(0.01f, b.size.y));
                Vector3 outward = centroid - new Vector3(b.center.x, centroid.y, b.center.z);
                outward.y = 0f;
                outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : away;

                var box = go.AddComponent<BoxCollider>();
                box.center = outer.bounds.center;
                box.size = Vector3.Max(outer.bounds.size, Vector3.one * 0.06f);
                // Higher shards catch more of the blow; everything bursts a little outward too.
                Vector3 velocity = away * (Random.Range(3.5f, 6.5f) * force * Mathf.Lerp(0.6f, 1.15f, h)) +
                                   outward * Random.Range(0.5f, 2f) + Vector3.up * Random.Range(1.5f, 4f) * Mathf.Lerp(0.7f, 1.2f, h);
                Launch(go, box, velocity, outer.bounds.size.magnitude, outer, inner);
            }
            return true;
        }

        static Mesh BuildMesh(string name, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t)
        {
            var mesh = new Mesh { name = name };
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Gives a piece of rubble a rigid body, sends it flying and schedules its clean-up.</summary>
        static void Launch(GameObject go, Collider col, Vector3 velocity, float size, Mesh outer = null, Mesh inner = null)
        {
            col.sharedMaterial = Rubble;
            batch.Add(col);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Clamp(size * 0.6f, 0.1f, 1.5f);
            rb.linearDamping = 0.1f;
            rb.angularDamping = 0.4f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = velocity;
            rb.angularVelocity = Random.onUnitSphere * Random.Range(6f, 14f);
            var chunk = go.AddComponent<CrumbleChunk>();
            chunk.ownedMeshes = new[] { outer, inner };
            chunk.settleTime = Random.Range(2.4f, 3.2f);
        }

        /// <summary>Fallback for pieces without a readable mesh: a modest burst of small blocks.</summary>
        static void SpawnBlocks(Bounds b, Material mat, Vector3 away, float force)
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
                    var box = t.gameObject.AddComponent<BoxCollider>();
                    Launch(t.gameObject, box, away * Random.Range(3f, 6f) * force + dir * Random.Range(0.5f, 2f) + Vector3.up * Random.Range(1.5f, 4f), s);
                }
            }
        }
    }

    /// <summary>One piece of physics rubble: tumbles freely, then turns kinematic, sinks into the board and goes.</summary>
    public class CrumbleChunk : MonoBehaviour
    {
        public float settleTime = 2.8f;
        public Mesh[] ownedMeshes;  // runtime shard meshes, freed with the chunk
        const float SinkTime = 0.6f;
        float age;
        bool sinking;

        void OnDestroy()
        {
            if (ownedMeshes == null) return;
            foreach (var m in ownedMeshes) if (m != null) Destroy(m);
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age < settleTime) return;
            if (!sinking)
            {
                sinking = true;
                var rb = GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = true;
                foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            }
            transform.position += Vector3.down * (0.5f * Time.deltaTime / SinkTime);
            if (age >= settleTime + SinkTime) Destroy(gameObject);
        }
    }
}

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
    /// Breaks a piece into stone chunks that collapse like a crumbling castle wall: the base gives way first,
    /// upper chunks drop after it, everything tumbles outward, bounces, settles and sinks into the board.
    /// </summary>
    public static class Crumble
    {
        public static void Spawn(Bounds b, Material mat, Vector3 away)
        {
            away.y = 0f;
            if (away.sqrMagnitude > 0.001f) away.Normalize();
            Vector3 c = b.center;
            float baseR = Mathf.Max(0.15f, Mathf.Min(b.extents.x, b.extents.z));
            float sizeScale = Mathf.Clamp(baseR / 0.45f, 0.7f, 1.8f);
            int layers = Mathf.Clamp(Mathf.RoundToInt(b.size.y / 0.2f), 5, 16);

            for (int l = 0; l < layers; l++)
            {
                float h = (l + 0.5f) / layers;
                float y = b.min.y + b.size.y * h;
                float r = baseR * Mathf.Lerp(0.85f, 0.55f, h);
                int around = r > 0.35f ? 5 : 4;
                float offset = Random.value * 360f;
                for (int k = 0; k <= around; k++)
                {
                    // k == around: a core chunk in the middle of the layer.
                    Vector3 dir = Quaternion.Euler(0f, offset + k * 360f / around + Random.Range(-15f, 15f), 0f) * Vector3.forward;
                    Vector3 pos = new Vector3(c.x, y, c.z) + (k == around ? Vector3.zero : dir * r * Random.Range(0.45f, 0.8f));
                    float s = Random.Range(0.13f, 0.27f) * sizeScale;
                    var scale = new Vector3(s * Random.Range(0.8f, 1.5f), s * Random.Range(0.6f, 1.1f), s * Random.Range(0.8f, 1.5f));
                    var t = PieceFactory.Part(PrimitiveType.Cube, null, pos, scale, mat, Random.rotation.eulerAngles);
                    t.name = "Rubble";
                    var chunk = t.gameObject.AddComponent<CrumbleChunk>();
                    Vector3 outward = k == around ? Random.insideUnitSphere * 0.3f : dir;
                    outward.y = 0f;
                    // Lower layers give way first; the top follows as the support disappears.
                    chunk.delay = h * 0.12f + Random.Range(0f, 0.04f);
                    chunk.velocity = outward * Random.Range(0.6f, 2.4f) + away * Random.Range(0.3f, 1.8f) +
                                     Vector3.up * Random.Range(-0.5f, 1.6f) * (1f - h * 0.5f);
                    chunk.spin = Random.onUnitSphere * Random.Range(120f, 520f);
                    chunk.settleTime = Random.Range(1.4f, 2.2f);
                }
            }

            var dust = new Color(0.22f, 0.2f, 0.18f);
            Effects.Shockwave(new Vector3(c.x, 0f, c.z), baseR * 1.6f, dust, 0.45f);
            Effects.Sparks(new Vector3(c.x, b.min.y + 0.2f, c.z), dust, 22, 4f);
            Effects.Sparks(new Vector3(c.x, b.center.y, c.z), dust, 14, 3f);
        }
    }

    /// <summary>One piece of rubble: waits, falls with gravity, bounces on the board, settles and sinks away.</summary>
    public class CrumbleChunk : MonoBehaviour
    {
        public float delay, settleTime = 1.8f;
        public Vector3 velocity, spin;
        float age;
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

            float ground = transform.localScale.y * 0.45f;
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

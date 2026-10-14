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

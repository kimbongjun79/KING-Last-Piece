using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// The Boomerang Crown (special item): a spinning golden crown thrown straight ahead. It flies out to its range
    /// (turning early at the board's edge), then homes back to the King, striking every piece it passes through —
    /// once on the way out and once more on the way back. The King catches it to throw it again.
    /// </summary>
    public class CrownBoomerang : MonoBehaviour
    {
        const float OutTime = 0.42f, ReturnSpeed = 20f, Height = 1.3f, HitRadius = 0.9f, SpinSpeed = 1080f;

        PlayerController owner;
        Vector3 start, dir;
        float range, t;
        bool returning;
        Transform body;
        readonly HashSet<EnemyBase> hitOut = new HashSet<EnemyBase>(), hitBack = new HashSet<EnemyBase>();

        static readonly Color Gold = new Color(1f, 0.8f, 0.3f);

        public static CrownBoomerang Spawn(PlayerController owner, Vector3 dir, float range)
        {
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : owner.transform.forward;
            var go = new GameObject("Boomerang Crown");
            var c = go.AddComponent<CrownBoomerang>();
            c.owner = owner;
            c.dir = dir;
            c.range = range;
            c.start = owner.FlatPos + dir * 0.6f;
            go.transform.position = c.start + Vector3.up * Height;
            c.Build();
            return c;
        }

        /// <summary>A small crown: a gold band with five points and a red jewel, lying flat so it spins like a disc.</summary>
        void Build()
        {
            body = new GameObject("Crown").transform;
            body.SetParent(transform, false);
            var gold = MaterialLib.Get(Gold, 0.85f, 0.9f, Gold * 0.6f);
            var jewel = MaterialLib.Emissive(new Color(1f, 0.15f, 0.1f), 2f);
            PieceFactory.Part(PrimitiveType.Cylinder, body, Vector3.zero, new Vector3(0.75f, 0.09f, 0.75f), gold);
            for (int i = 0; i < 5; i++)
            {
                Vector3 d = Quaternion.Euler(0f, i * 72f, 0f) * Vector3.forward;
                PieceFactory.Part(PrimitiveType.Cube, body, d * 0.33f + Vector3.up * 0.15f, new Vector3(0.12f, 0.26f, 0.12f), gold,
                    new Vector3(0f, i * 72f, 45f));
            }
            PieceFactory.Part(PrimitiveType.Sphere, body, Vector3.up * 0.1f, Vector3.one * 0.16f, jewel);
            foreach (var r in body.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Update()
        {
            if (owner == null || owner.IsDead || GameManager.Instance == null) { Destroy(gameObject); return; }
            float dt = Time.deltaTime;
            body.Rotate(0f, SpinSpeed * dt, 0f, Space.Self);
            Vector3 pos = transform.position;

            if (!returning)
            {
                t += dt / OutTime;
                float p = PoseTrack.Curve(Ease.Out, Mathf.Clamp01(t));
                pos = start + dir * (range * p) + Vector3.up * Height;
                // Turn back at the end of the throw, or early at the board's edge.
                if (t >= 1f || !BoardSlots.Inside(pos, GameManager.EdgeInset - 0.5f)) returning = true;
                Strike(pos, hitOut);
            }
            else
            {
                Vector3 target = owner.FlatPos + Vector3.up * Height;
                Vector3 to = target - pos;
                float step = ReturnSpeed * dt;
                if (to.magnitude <= Mathf.Max(0.6f, step))
                {
                    owner.CatchCrown();
                    Destroy(gameObject);
                    return;
                }
                pos += to.normalized * step;
                Strike(pos, hitBack);
            }
            transform.position = pos;
            if (Random.value < 0.7f) Effects.Sparks(pos, Vfx.Accent, 1, 2f);
        }

        void Strike(Vector3 pos, HashSet<EnemyBase> already)
        {
            Vector3 flat = new Vector3(pos.x, 0f, pos.z);
            foreach (var e in GameManager.Instance.Enemies)
            {
                if (e == null || e.IsDead || !e.Targetable || already.Contains(e)) continue;
                if ((e.FlatPos - flat).magnitude > HitRadius + e.Radius) continue;
                already.Add(e);
                owner.CrownStrike(e, pos);
            }
        }
    }
}

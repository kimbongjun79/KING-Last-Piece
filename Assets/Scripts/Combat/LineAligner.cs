using UnityEngine;

namespace KingLastPiece
{
    /// <summary>Helpers for rook/bishop style line alignment.</summary>
    public static class LineAligner
    {
        static readonly Vector3[] Ortho = { Vector3.right, Vector3.forward };
        static readonly Vector3[] Diag = { new Vector3(1, 0, 1).normalized, new Vector3(1, 0, -1).normalized };

        /// <summary>Closest point on any line through the target (ranks/files or diagonals), kept at a striking distance.</summary>
        public static Vector3 Nearest(Vector3 self, Vector3 target, bool diagonal)
        {
            Vector3 best = self;
            float bestDist = float.MaxValue;
            foreach (var dir in diagonal ? Diag : Ortho)
            {
                float s = Vector3.Dot(self - target, dir);
                if (Mathf.Abs(s) < 3.5f) s = (s >= 0f ? 1f : -1f) * 3.5f;
                Vector3 q = target + dir * s;
                float h = GameManager.PlayableHalf;
                if (Mathf.Abs(q.x) > h || Mathf.Abs(q.z) > h)
                {
                    s = -s; // try the other side of the target
                    q = target + dir * s;
                }
                q = new Vector3(Mathf.Clamp(q.x, -h, h), 0f, Mathf.Clamp(q.z, -h, h));
                float d = (q - self).magnitude;
                if (d < bestDist) { bestDist = d; best = q; }
            }
            return best;
        }

        public static Vector3 Snap(Vector3 v, bool diagonal)
        {
            if (diagonal) return new Vector3(Mathf.Sign(v.x), 0f, Mathf.Sign(v.z)).normalized;
            return Mathf.Abs(v.x) > Mathf.Abs(v.z) ? new Vector3(Mathf.Sign(v.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(v.z));
        }
    }
}

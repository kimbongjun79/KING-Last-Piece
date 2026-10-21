using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// The chess table under the board (placed in the EndlessMode scene by KING ▸ Setup ▸ Bake Classroom): widens the
    /// table (the desk model, scaled across only) whenever the endless board would outgrow it, keeps the top's
    /// collider on its surface and the chess clock beside the board.
    /// </summary>
    public class ChessTableFit : MonoBehaviour
    {
        [Tooltip("The desk model used as the table (scaled across, not up).")]
        public Transform model;
        [Tooltip("Box table top, when there is no model.")]
        public Transform top;
        public BoxCollider topCollider;
        public Transform clock;
        [Tooltip("Table depth (Z) at its baked size, in game metres.")]
        public float baseDepth = 43.6f;
        [Tooltip("Half of the table's depth at its baked size.")]
        public float baseHalf = 21.8f;
        [Tooltip("Table space kept around the board's edge.")]
        public float margin = 6f;
        [Tooltip("Height of the table top (the board's underside).")]
        public float topY = -0.4f;
        [Tooltip("Board half-width the clock's placement is for (it follows the board's edge from there).")]
        public float clockBoardHalf = 8f;
        [Tooltip("How much further out than its placement the clock stands from the board's edge.")]
        public float clockMargin = 2.5f;

        Vector3 modelScale;
        bool measured;

        void Start() => Fit();

        public void Fit()
        {
            float board = GameManager.BoardHalf;
            float k = Mathf.Max(1f, (board + margin) / Mathf.Max(0.01f, baseHalf));
            float width = baseHalf * 2f, depth = baseHalf * 2f;
            if (model != null)
            {
                if (!measured) { modelScale = model.localScale; measured = true; }
                model.localScale = new Vector3(modelScale.x * k, modelScale.y, modelScale.z * k);
                var rs = model.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    width = b.size.x;
                    depth = b.size.z;
                }
            }
            else if (top != null)
            {
                top.localScale = new Vector3(baseHalf * 2f * k, top.localScale.y, baseHalf * 2f * k);
                width = depth = baseHalf * 2f * k;
            }
            if (topCollider != null)
            {
                topCollider.center = new Vector3(0f, topY - 0.5f, 0f);
                topCollider.size = new Vector3(width, 1f, depth);
            }
            // The clock stays where it was placed (by hand in the scene); it only moves out with the board's edge when
            // the endless board grows, and back when it shrinks.
            // clockMargin keeps it a little further out than where it was first placed.
            float clockAt = board + clockMargin;
            // A static clock is batched into the scene's geometry: moving it in play would only move its digits (built at
            // run time), leaving them behind the body. It is placed in the scene for the 무쌍 board instead.
            bool frozen = Application.isPlaying && clock != null && clock.gameObject.isStatic;
            if (clock != null && !frozen && !Mathf.Approximately(clockAt, clockBoardHalf))
            {
                var p = clock.localPosition;
                var away = new Vector3(p.x, 0f, p.z);
                away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.right;
                clock.localPosition = p + away * (clockAt - clockBoardHalf);
                clockBoardHalf = clockAt;
            }
            // Other things lying on the table beside the board (the score sheet) move out with its edge the same way.
            for (int i = 0; i < followers.Count && i < followerHalf.Count; i++)
            {
                var t = followers[i];
                if (t == null || Mathf.Approximately(board, followerHalf[i])) continue;
                var p = t.localPosition;
                var away = new Vector3(p.x, 0f, p.z);
                away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.right;
                t.localPosition = p + away * (board - followerHalf[i]);
                followerHalf[i] = board;
            }
        }

        [Tooltip("Things on the table that keep beside the board's edge as it grows (with the board half they were placed for).")]
        public System.Collections.Generic.List<Transform> followers = new System.Collections.Generic.List<Transform>();
        public System.Collections.Generic.List<float> followerHalf = new System.Collections.Generic.List<float>();

        /// <summary><paramref name="t"/> (a child of the table) now keeps beside the board's edge.</summary>
        public void Follow(Transform t)
        {
            for (int i = followers.Count - 1; i >= 0; i--)
                if (followers[i] == null) { followers.RemoveAt(i); if (i < followerHalf.Count) followerHalf.RemoveAt(i); }
            followers.Add(t);
            followerHalf.Add(GameManager.BoardHalf);
        }
    }
}

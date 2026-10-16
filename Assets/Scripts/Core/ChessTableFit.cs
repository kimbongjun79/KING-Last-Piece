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
            if (clock != null)
            {
                var p = clock.localPosition;
                clock.localPosition = new Vector3(board + 6.5f, p.y, p.z);
            }
        }
    }
}

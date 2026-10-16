using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// The chess table under the board (placed in the EndlessMode scene by KING ▸ Setup ▸ Bake Classroom): keeps its
    /// top at least a margin wider than the board, so the endless board's growth stays on the table, and keeps the
    /// legs at the corners and the chess clock beside the board.
    /// </summary>
    public class ChessTableFit : MonoBehaviour
    {
        public Transform top;
        public Transform[] legs = new Transform[0];
        public Transform clock;
        [Tooltip("Half width of the table at its real size (game metres).")]
        public float baseHalf = 22f;
        [Tooltip("Table space kept around the board's edge.")]
        public float margin = 6f;
        public float legSize = 1.8f;

        void Start() => Fit();

        public void Fit()
        {
            float board = GameManager.BoardHalf;
            float half = Mathf.Max(baseHalf, board + margin);
            if (top != null) top.localScale = new Vector3(half * 2f, top.localScale.y, half * 2f);
            foreach (var leg in legs)
            {
                if (leg == null) continue;
                var p = leg.localPosition;
                float inset = half - legSize;
                leg.localPosition = new Vector3(Mathf.Sign(p.x) * inset, p.y, Mathf.Sign(p.z) * inset);
            }
            if (clock != null)
            {
                var p = clock.localPosition;
                clock.localPosition = new Vector3(board + (half - board) * 0.5f + 0.6f, p.y, p.z);
            }
        }
    }
}

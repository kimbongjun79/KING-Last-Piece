using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Marks the baked board in a scene and keeps its 64 squares (file-major: index = file * 8 + rank) so the
    /// board-ripple effect can animate the real tiles.
    /// </summary>
    public class ArenaBoard : MonoBehaviour
    {
        public Transform[] squares = new Transform[64];

        public Transform[,] Grid
        {
            get
            {
                var g = new Transform[8, 8];
                for (int f = 0; f < 8; f++)
                for (int r = 0; r < 8; r++)
                    g[f, r] = squares[f * 8 + r];
                return g;
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Marks the baked board in a scene and keeps its 64 squares (file-major: index = file * 8 + rank) so the
    /// board-ripple effect can animate the real tiles. The endless mode grows the board a ring of squares at a time
    /// (<see cref="Resize"/>): the baked 8×8 stays in the middle, the frame, collider and pillars move out with the edge.
    /// </summary>
    public class ArenaBoard : MonoBehaviour
    {
        public Transform[] squares = new Transform[64];

        // Squares added around the baked 8×8, keyed by their file/rank relative to a1 (negative or ≥ 8).
        readonly Dictionary<Vector2Int, Transform> extra = new Dictionary<Vector2Int, Transform>();
        Vector3[] pillarDirs;
        float pillarRadius;

        /// <summary>The live squares, [file, rank] from the board's corner (BoardSlots.Size per side).</summary>
        public Transform[,] Grid
        {
            get
            {
                int n = BoardSlots.Size, o = (n - 8) / 2;
                var g = new Transform[n, n];
                for (int f = 0; f < n; f++)
                for (int r = 0; r < n; r++)
                {
                    int bf = f - o, br = r - o;
                    g[f, r] = bf >= 0 && br >= 0 && bf < 8 && br < 8 ? squares[bf * 8 + br] : extra[new Vector2Int(bf, br)];
                }
                return g;
            }
        }

        /// <summary>Grows (or shrinks back) to <paramref name="size"/> squares per side and moves the edge with it.</summary>
        public void Resize(int size)
        {
            size = Mathf.Max(8, size);
            BoardSlots.SetSize(size);
            int o = (size - 8) / 2;
            float tile = GameManager.TileSize;
            Transform holder = squares[0].parent;
            Material dark = squares[0].GetComponent<Renderer>().sharedMaterial;   // a1 is a dark square
            Material light = squares[1].GetComponent<Renderer>().sharedMaterial;

            foreach (var kv in extra) kv.Value.gameObject.SetActive(false);
            for (int f = -o; f < 8 + o; f++)
            for (int r = -o; r < 8 + o; r++)
            {
                if (f >= 0 && r >= 0 && f < 8 && r < 8) continue;
                var key = new Vector2Int(f, r);
                if (!extra.TryGetValue(key, out var sq))
                {
                    var pos = new Vector3((f - 3.5f) * tile, -0.2f, (r - 3.5f) * tile);
                    sq = PieceFactory.Part(PrimitiveType.Cube, holder, pos, new Vector3(tile, 0.4f, tile), ((f + r) % 2 + 2) % 2 == 0 ? dark : light);
                    sq.name = $"ring {f},{r}";
                    sq.GetComponent<Renderer>().renderingLayerMask |= WorldBuilder.BoardDecalLayer;
                    extra[key] = sq;
                }
                sq.gameObject.SetActive(true);
            }

            // Frame rails hug the new edge; the physics top and the pillars ring move out with it.
            float half = GameManager.BoardHalf, edge = half + 0.35f, len = half * 2f + 1.4f;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "Frame N": t.localPosition = new Vector3(0f, -0.15f, edge); t.localScale = new Vector3(len, 0.5f, 0.7f); break;
                    case "Frame S": t.localPosition = new Vector3(0f, -0.15f, -edge); t.localScale = new Vector3(len, 0.5f, 0.7f); break;
                    case "Frame E": t.localPosition = new Vector3(edge, -0.15f, 0f); t.localScale = new Vector3(0.7f, 0.5f, len); break;
                    case "Frame W": t.localPosition = new Vector3(-edge, -0.15f, 0f); t.localScale = new Vector3(0.7f, 0.5f, len); break;
                    case "Board Collider":
                        var box = t.GetComponent<BoxCollider>();
                        if (box != null) box.size = new Vector3(half * 2f, 1f, half * 2f);
                        break;
                }
            }
            MovePillars(Mathf.Max(15f, half + 7f));
            BoardFx.Register(Grid);
        }

        void MovePillars(float radius)
        {
            var pillars = transform.Find("Pillars");
            if (pillars == null) return;
            if (pillarDirs == null)
            {
                // Each pillar's parts sit around the same ground point (15 m out); remember the direction once.
                pillarDirs = new Vector3[pillars.childCount];
                for (int i = 0; i < pillars.childCount; i++)
                {
                    var p = pillars.GetChild(i);
                    Vector3 c = p.childCount > 0 ? p.GetChild(0).position : p.position;
                    c.y = 0f;
                    pillarDirs[i] = c.sqrMagnitude > 0.01f ? c.normalized : Vector3.forward;
                }
                pillarRadius = 15f;
            }
            // Pillars are grouped objects whose parts were placed at the 15 m ring: shift each group outward.
            for (int i = 0; i < pillars.childCount && i < pillarDirs.Length; i++)
                pillars.GetChild(i).localPosition = pillarDirs[i] * (radius - pillarRadius);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Enemy attack warning, drawn on the chessboard itself while a piece winds up:
    /// <list type="bullet">
    /// <item>Lit squares — the attacker's own square and every board square the attack will reach glow red and fills from its centre outward as the
    /// wind-up runs; a fully red square means "now". Charges fill square by square from the attacker to the end of
    /// the charge.</item>
    /// <item>Timing — for the last <see cref="FlashTime"/> seconds the square outlines and the route blink white: the
    /// cue for a perfect dodge.</item>
    /// <item>Route — a dotted path that lights up in sequence: along the line for charges and thrusts, the L for a
    /// knight's leap.</item>
    /// <item>Knight leaps — the landing square (and the cross it strikes) lit, plus a translucent ghost of the knight
    /// standing where it will land.</item>
    /// </list>
    /// </summary>
    public class Telegraph : MonoBehaviour
    {
        public const float FlashTime = 0.2f;

        enum Kind { Line, Area, Leap }

        class Cell
        {
            public Vector2Int square;
            public Transform fill;
            public Transform[] edges;
        }

        Kind kind;
        float windup, progress;
        Vector3 start, dir, center;
        float length;
        readonly List<Cell> cells = new List<Cell>();
        readonly List<Vector2Int> squares = new List<Vector2Int>();
        readonly List<Vector2Int> scratch = new List<Vector2Int>();
        Transform cellRoot, routeRoot, ghost;
        readonly List<Transform> dots = new List<Transform>();
        Vector3 routeStart, routeDir;
        float routeLength = -1f;
        Material baseMat, fillMat, ghostMat;

        static float Tile => GameManager.TileSize;
        static readonly Color Red = new Color(1f, 0.12f, 0.05f);
        static Material EdgeMat => MaterialLib.Emissive(Red, 2.6f);
        static Material DotMat => MaterialLib.Emissive(new Color(1f, 0.32f, 0.08f), 3f);
        static Material FlashMat => MaterialLib.Emissive(new Color(1f, 0.97f, 0.92f), 4f);

        // ---------------------------------------------------------------- factories

        /// <summary>A charge or thrust along a line: path squares lit in order, dotted route along the line.</summary>
        public static Telegraph Line(Vector3 start, Vector3 dir, float length, float width, float windup)
        {
            var t = Create("Telegraph_Line", Kind.Line, windup);
            t.SetLine(start, dir, length, width);
            return t;
        }

        /// <summary>A blast around a point (slams; the queen's leap), plus the attacker's own square at <paramref name="origin"/>.</summary>
        public static Telegraph Area(Vector3 center, float radius, float windup, Vector3 origin)
        {
            var t = Create("Telegraph_Area", Kind.Area, windup);
            t.center = Flat(center);
            t.RebuildCells(WithSquare(AreaSquares(t.center, radius, t.scratch), BoardSlots.ToSquare(origin)));
            return t;
        }

        /// <summary>The knight's leap: the cross it strikes lit, dotted L route, and a ghost on the landing square.</summary>
        public static Telegraph Leap(Vector3 from, Vector3 target, float windup, Transform ghostModel)
        {
            var t = Create("Telegraph_Leap", Kind.Leap, windup);
            t.start = Flat(from);
            t.center = Flat(target);
            t.RebuildCells(WithSquare(CrossSquares(BoardSlots.ToSquare(t.center), t.scratch), BoardSlots.ToSquare(t.start)));
            t.BuildKnightRoute();
            if (ghostModel != null) t.BuildGhost(ghostModel);
            return t;
        }

        static Telegraph Create(string name, Kind kind, float windup)
        {
            var t = new GameObject(name).AddComponent<Telegraph>();
            t.kind = kind;
            t.windup = Mathf.Max(0.05f, windup);
            t.baseMat = new Material(MaterialLib.Transparent(new Color(0.85f, 0.06f, 0.03f, 0.2f), new Color(0.25f, 0.02f, 0f)));
            t.fillMat = new Material(MaterialLib.Transparent(new Color(1f, 0.1f, 0.04f, 0.5f), new Color(0.9f, 0.08f, 0.02f)));
            t.cellRoot = new GameObject("Squares").transform;
            t.cellRoot.SetParent(t.transform, false);
            t.routeRoot = new GameObject("Route").transform;
            t.routeRoot.SetParent(t.transform, false);
            return t;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void OnDestroy()
        {
            if (baseMat != null) Destroy(baseMat);
            if (fillMat != null) Destroy(fillMat);
            if (ghostMat != null) Destroy(ghostMat);
        }

        // ---------------------------------------------------------------- updates from the attacker

        /// <summary>Re-aims a line (a tracking thrust); squares and dots are only rebuilt when they change.</summary>
        public void SetLine(Vector3 s, Vector3 d, float len, float w)
        {
            start = Flat(s);
            dir = d.sqrMagnitude > 0.0001f ? Flat(d).normalized : Vector3.forward;
            length = Mathf.Max(0.1f, len);
            RebuildCells(LineSquares(start, dir, length, scratch));
            if (Vector3.Angle(dir, routeDir) > 1f || Mathf.Abs(length - routeLength) > 0.05f || (start - routeStart).sqrMagnitude > 0.0025f)
            {
                routeStart = start;
                routeDir = dir;
                routeLength = length;
                ClearDots();
                AddDots(start + dir * 0.5f, start + dir * Mathf.Max(0.6f, length - 0.2f));
            }
        }

        public void SetProgress(float p) => progress = Mathf.Clamp01(p);

        // ---------------------------------------------------------------- squares

        /// <summary>Squares along a line, nearest first: square by square for the 8 chess directions, sampled otherwise.</summary>
        static List<Vector2Int> LineSquares(Vector3 start, Vector3 dir, float length, List<Vector2Int> list)
        {
            list.Clear();
            var from = BoardSlots.ToSquare(start);
            list.Add(from);   // the attacker's own square lights first
            Vector3 snapped = BoardSlots.Snap8(dir);
            if (Vector3.Angle(snapped, dir) < 3f)
            {
                var step = new Vector2Int(Mathf.RoundToInt(snapped.x), Mathf.RoundToInt(snapped.z));
                float stepLen = Tile * (step.x != 0 && step.y != 0 ? 1.4142f : 1f);
                for (int k = 1; k * stepLen <= length + 0.05f; k++)
                {
                    var s = from + step * k;
                    if (!BoardSlots.OnBoard(s)) break;
                    list.Add(s);
                }
                if (list.Count == 1 && BoardSlots.OnBoard(from + step)) list.Add(from + step);
                return list;
            }
            for (float t = 0.4f; t <= length; t += 0.25f)
            {
                Vector3 p = start + dir * t;
                if (Mathf.Abs(p.x) >= GameManager.BoardHalf || Mathf.Abs(p.z) >= GameManager.BoardHalf) break;
                var s = BoardSlots.ToSquare(p);
                if (s != from && !list.Contains(s)) list.Add(s);
            }
            return list;
        }

        /// <summary>Squares a circular blast really covers (the square's nearest point well inside the radius).</summary>
        static List<Vector2Int> AreaSquares(Vector3 center, float radius, List<Vector2Int> list)
        {
            list.Clear();
            float h = Tile * 0.5f, reach = radius * 0.85f;
            for (int x = 0; x < BoardSlots.Size; x++)
            for (int y = 0; y < BoardSlots.Size; y++)
            {
                var s = new Vector2Int(x, y);
                Vector3 c = BoardSlots.Center(s);
                float dx = Mathf.Max(0f, Mathf.Abs(center.x - c.x) - h), dz = Mathf.Max(0f, Mathf.Abs(center.z - c.z) - h);
                if (dx * dx + dz * dz <= reach * reach) list.Add(s);
            }
            return list;
        }

        static List<Vector2Int> WithSquare(List<Vector2Int> list, Vector2Int s)
        {
            if (!list.Contains(s)) list.Add(s);
            return list;
        }

        /// <summary>The landing square and its four straight neighbours (the knight's strike).</summary>
        static List<Vector2Int> CrossSquares(Vector2Int c, List<Vector2Int> list)
        {
            list.Clear();
            list.Add(c);
            foreach (var o in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                if (BoardSlots.OnBoard(c + o)) list.Add(c + o);
            return list;
        }

        void RebuildCells(List<Vector2Int> wanted)
        {
            bool same = wanted.Count == squares.Count;
            for (int i = 0; same && i < wanted.Count; i++) same = squares[i] == wanted[i];
            if (same && cells.Count > 0) return;

            squares.Clear();
            squares.AddRange(wanted);
            foreach (Transform c in cellRoot) Destroy(c.gameObject);
            cells.Clear();
            foreach (var s in squares) cells.Add(BuildCell(s));
        }

        Cell BuildCell(Vector2Int s)
        {
            // The cell root stays at the origin: its parts are placed in world coordinates.
            var root = new GameObject(BoardSlots.Name(s)).transform;
            root.SetParent(cellRoot, false);
            Vector3 c0 = BoardSlots.Center(s);
            float size = Tile * 0.96f;
            PieceFactory.Part(PrimitiveType.Cube, root, c0 + Vector3.up * 0.022f, new Vector3(size, 0.01f, size), baseMat);
            var cell = new Cell
            {
                square = s,
                fill = PieceFactory.Part(PrimitiveType.Cube, root, c0 + Vector3.up * 0.028f, new Vector3(0.001f, 0.01f, 0.001f), fillMat),
                edges = new Transform[4],
            };
            float h = size * 0.5f, w = 0.06f;
            Vector3 c = c0 + Vector3.up * 0.036f;
            cell.edges[0] = PieceFactory.Part(PrimitiveType.Cube, root, c + new Vector3(0f, 0f, h), new Vector3(size + w, 0.012f, w), EdgeMat);
            cell.edges[1] = PieceFactory.Part(PrimitiveType.Cube, root, c - new Vector3(0f, 0f, h), new Vector3(size + w, 0.012f, w), EdgeMat);
            cell.edges[2] = PieceFactory.Part(PrimitiveType.Cube, root, c + new Vector3(h, 0f, 0f), new Vector3(w, 0.012f, size + w), EdgeMat);
            cell.edges[3] = PieceFactory.Part(PrimitiveType.Cube, root, c - new Vector3(h, 0f, 0f), new Vector3(w, 0.012f, size + w), EdgeMat);
            foreach (var e in cell.edges) e.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return cell;
        }

        // ---------------------------------------------------------------- dotted route

        void ClearDots()
        {
            foreach (var d in dots) if (d != null) Destroy(d.gameObject);
            dots.Clear();
        }

        /// <summary>Small diamond dots every 0.4 m from <paramref name="a"/> to <paramref name="b"/>.</summary>
        void AddDots(Vector3 a, Vector3 b)
        {
            float len = (b - a).magnitude;
            Vector3 step = len > 0.001f ? (b - a) / len : Vector3.forward;
            for (float s = 0f; s <= len + 0.001f; s += 0.4f)
                dots.Add(PieceFactory.Part(PrimitiveType.Cube, routeRoot, a + step * s + Vector3.up * 0.05f,
                    new Vector3(0.13f, 0.012f, 0.13f), DotMat, new Vector3(0f, 45f, 0f)));
        }

        /// <summary>The knight's L: the long leg first, then the short one.</summary>
        void BuildKnightRoute()
        {
            var from = BoardSlots.ToSquare(start);
            var to = BoardSlots.ToSquare(center);
            Vector2Int d = to - from;
            var corner = Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? new Vector2Int(to.x, from.y) : new Vector2Int(from.x, to.y);
            Vector3 a = BoardSlots.Center(from), b = BoardSlots.Center(corner), c = BoardSlots.Center(to);
            Vector3 ab = (b - a).normalized, bc = (c - b).normalized;
            AddDots(a + ab * 0.5f, b);
            AddDots(b + bc * 0.4f, c - bc * 0.6f);
        }

        void BuildGhost(Transform model)
        {
            ghostMat = new Material(MaterialLib.Transparent(new Color(1f, 0.15f, 0.08f, 0.3f), new Color(1.2f, 0.12f, 0.04f)));
            var clone = Instantiate(model.gameObject, transform);
            clone.name = "Ghost";
            foreach (var b in clone.GetComponentsInChildren<MonoBehaviour>()) Destroy(b);
            foreach (var r in clone.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = ghostMat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.SetPropertyBlock(null);
            }
            ghost = clone.transform;
            Vector3 face = center - start;
            ghost.SetPositionAndRotation(center, face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : model.rotation);
        }

        // ---------------------------------------------------------------- animation

        void Update()
        {
            float p = progress;
            float timeLeft = windup * (1f - p);
            // Blink fast in the final moment: white on/off.
            bool white = timeLeft <= FlashTime && Mathf.Repeat(timeLeft * 30f, 2f) < 1.2f;

            float size = Tile * 0.96f;
            int n = cells.Count;
            for (int i = 0; i < n; i++)
            {
                var c = cells[i];
                // Centre-out fill. A charge fills its squares one after another, from the attacker to the end.
                float f = kind == Kind.Line ? Mathf.Clamp01(p * n - i) : p;
                float s = Mathf.Max(0.001f, size * PoseTrack.Curve(Ease.Out, f));
                c.fill.localScale = new Vector3(s, 0.01f, s);
                foreach (var e in c.edges)
                {
                    var r = e.GetComponent<Renderer>();
                    var m = white ? FlashMat : EdgeMat;
                    if (r.sharedMaterial != m) r.sharedMaterial = m;
                }
            }

            var col = fillMat.GetColor("_BaseColor");
            col.a = Mathf.Lerp(0.35f, 0.65f, p);
            fillMat.SetColor("_BaseColor", col);

            // Route dots light up in sequence, toward the target, quicker as the wind-up runs out.
            float speed = Mathf.Lerp(2f, 4.5f, p);
            for (int i = 0; i < dots.Count; i++)
            {
                if (dots[i] == null) continue;
                float wave = Mathf.Repeat(Time.time * speed - i * 0.12f, 1f);
                dots[i].localScale = new Vector3(0.13f, 0.012f, 0.13f) * Mathf.Lerp(0.6f, 1.3f, wave < 0.3f ? 1f - wave / 0.3f : 0f);
                var r = dots[i].GetComponent<Renderer>();
                var m = white ? FlashMat : DotMat;
                if (r.sharedMaterial != m) r.sharedMaterial = m;
            }

            if (ghost != null)
            {
                var gc = ghostMat.GetColor("_BaseColor");
                gc.a = Mathf.Lerp(0.3f, 0.65f, p) * (white ? 1.4f : 1f);
                ghostMat.SetColor("_BaseColor", gc);
                ghost.position = center + Vector3.up * (0.08f + Mathf.Sin(Time.time * 5f) * 0.05f);
            }
        }
    }
}

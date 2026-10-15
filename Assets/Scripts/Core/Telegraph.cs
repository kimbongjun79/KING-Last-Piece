using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Enemy attack warning, drawn on the chessboard itself while a piece winds up:
    /// <list type="bullet">
    /// <item>Lit squares — every board square the attack will reach glows red; each one fills from its edges inward as
    /// the wind-up runs, so a fully red square means "now".</item>
    /// <item>Timing — for the last <see cref="FlashTime"/> seconds the square outlines (and arrow) blink white: the cue
    /// for a perfect dodge.</item>
    /// <item>Charges (lines) — a move arrow from the attacker's square to the end of its path, with chevrons streaming
    /// along it faster and faster.</item>
    /// <item>Knight leaps — a dotted L route from its square to the landing square, the landing square outlined, and a
    /// translucent ghost of the knight standing where it will land.</item>
    /// </list>
    /// </summary>
    public class Telegraph : MonoBehaviour
    {
        public const float FlashTime = 0.2f;

        enum Kind { Line, Area, Leap }

        class Cell
        {
            public Vector2Int square;
            public bool strong;
            public Transform fillN, fillS, fillE, fillW;
            public Transform[] edges;
        }

        Kind kind;
        float windup, progress;
        Vector3 start, dir, center;
        float length, width, radius;
        readonly List<Cell> cells = new List<Cell>();
        readonly List<Vector2Int> squares = new List<Vector2Int>();
        readonly List<Vector2Int> scratch = new List<Vector2Int>();
        Transform cellRoot, arrowRoot, shaft, headL, headR;
        readonly List<Transform[]> chevrons = new List<Transform[]>();
        readonly List<Transform> dots = new List<Transform>();
        Transform ghost;
        Material baseMat, fillMat, ghostMat;

        static float Tile => GameManager.TileSize;
        static readonly Color Red = new Color(1f, 0.12f, 0.05f);
        static Material EdgeMat => MaterialLib.Emissive(Red, 2.6f);
        static Material ArrowMat => MaterialLib.Emissive(new Color(1f, 0.32f, 0.08f), 3f);
        static Material FlashMat => MaterialLib.Emissive(new Color(1f, 0.97f, 0.92f), 4f);

        // ---------------------------------------------------------------- factories

        /// <summary>A charge or thrust along a line (lit path squares + move arrow).</summary>
        public static Telegraph Line(Vector3 start, Vector3 dir, float length, float width, float windup)
        {
            var t = Create("Telegraph_Line", Kind.Line, windup);
            t.SetLine(start, dir, length, width);
            t.BuildArrow();
            return t;
        }

        /// <summary>A blast around a point (slams; the queen's leap).</summary>
        public static Telegraph Area(Vector3 center, float radius, float windup)
        {
            var t = Create("Telegraph_Area", Kind.Area, windup);
            t.center = Flat(center);
            t.radius = radius;
            t.RebuildCells(AreaSquares(t.center, radius, t.scratch), BoardSlots.ToSquare(t.center));
            return t;
        }

        /// <summary>The knight's leap: lit landing area, dotted L route and a ghost of the knight on the landing square.</summary>
        public static Telegraph Leap(Vector3 from, Vector3 target, float radius, float windup, Transform ghostModel)
        {
            var t = Create("Telegraph_Leap", Kind.Leap, windup);
            t.start = Flat(from);
            t.center = Flat(target);
            t.radius = radius;
            t.RebuildCells(AreaSquares(t.center, radius, t.scratch), BoardSlots.ToSquare(t.center));
            t.BuildRoute();
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

        /// <summary>Re-aims a line (a tracking thrust); squares are only rebuilt when the path's squares change.</summary>
        public void SetLine(Vector3 s, Vector3 d, float len, float w)
        {
            start = Flat(s);
            dir = d.sqrMagnitude > 0.0001f ? Flat(d).normalized : Vector3.forward;
            length = Mathf.Max(0.1f, len);
            width = w;
            RebuildCells(LineSquares(start, dir, length, scratch), new Vector2Int(-1, -1));
        }

        public void SetProgress(float p) => progress = Mathf.Clamp01(p);

        // ---------------------------------------------------------------- squares

        /// <summary>Squares along a line: square by square for the 8 chess directions, sampled otherwise.</summary>
        static List<Vector2Int> LineSquares(Vector3 start, Vector3 dir, float length, List<Vector2Int> list)
        {
            list.Clear();
            var from = BoardSlots.ToSquare(start);
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
                if (list.Count == 0 && BoardSlots.OnBoard(from + step)) list.Add(from + step);
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

        void RebuildCells(List<Vector2Int> wanted, Vector2Int strong)
        {
            bool same = wanted.Count == squares.Count;
            for (int i = 0; same && i < wanted.Count; i++) same = squares.Contains(wanted[i]);
            if (same && cells.Count > 0) return;

            squares.Clear();
            squares.AddRange(wanted);
            foreach (Transform c in cellRoot) Destroy(c.gameObject);
            cells.Clear();
            foreach (var s in squares) cells.Add(BuildCell(s, s == strong));
        }

        Cell BuildCell(Vector2Int s, bool strong)
        {
            // The cell root stays at the origin: its parts are placed in world coordinates.
            var root = new GameObject(BoardSlots.Name(s)).transform;
            root.SetParent(cellRoot, false);
            Vector3 center = BoardSlots.Center(s);
            float size = Tile * 0.96f;
            PieceFactory.Part(PrimitiveType.Cube, root, center + Vector3.up * 0.022f, new Vector3(size, 0.01f, size), baseMat);
            var cell = new Cell
            {
                square = s,
                strong = strong,
                fillN = PieceFactory.Part(PrimitiveType.Cube, root, center, Vector3.one, fillMat),
                fillS = PieceFactory.Part(PrimitiveType.Cube, root, center, Vector3.one, fillMat),
                fillE = PieceFactory.Part(PrimitiveType.Cube, root, center, Vector3.one, fillMat),
                fillW = PieceFactory.Part(PrimitiveType.Cube, root, center, Vector3.one, fillMat),
                edges = new Transform[4],
            };
            float h = size * 0.5f, w = strong ? 0.12f : 0.06f;
            Vector3 c = center + Vector3.up * 0.036f;
            cell.edges[0] = PieceFactory.Part(PrimitiveType.Cube, root, c + new Vector3(0f, 0f, h), new Vector3(size + w, 0.012f, w), EdgeMat);
            cell.edges[1] = PieceFactory.Part(PrimitiveType.Cube, root, c - new Vector3(0f, 0f, h), new Vector3(size + w, 0.012f, w), EdgeMat);
            cell.edges[2] = PieceFactory.Part(PrimitiveType.Cube, root, c + new Vector3(h, 0f, 0f), new Vector3(w, 0.012f, size + w), EdgeMat);
            cell.edges[3] = PieceFactory.Part(PrimitiveType.Cube, root, c - new Vector3(h, 0f, 0f), new Vector3(w, 0.012f, size + w), EdgeMat);
            foreach (var e in cell.edges) e.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return cell;
        }

        // ---------------------------------------------------------------- charge arrow

        void BuildArrow()
        {
            arrowRoot = new GameObject("Arrow").transform;
            arrowRoot.SetParent(transform, false);
            shaft = PieceFactory.Part(PrimitiveType.Cube, arrowRoot, Vector3.zero, Vector3.one, ArrowMat);
            headL = PieceFactory.Part(PrimitiveType.Cube, arrowRoot, Vector3.zero, Vector3.one, ArrowMat);
            headR = PieceFactory.Part(PrimitiveType.Cube, arrowRoot, Vector3.zero, Vector3.one, ArrowMat);
        }

        void LayoutArrow(Material mat)
        {
            Vector3 a = start + dir * 0.55f, b = start + dir * Mathf.Max(0.8f, length - 0.25f);
            float y = 0.05f;
            float len = (b - a).magnitude;
            var rot = Quaternion.LookRotation(dir);
            Place(shaft, (a + b) * 0.5f + Vector3.up * y, rot, new Vector3(0.1f, 0.012f, len), mat);
            const float headLen = 0.75f;
            for (int i = 0; i < 2; i++)
            {
                var hRot = rot * Quaternion.Euler(0f, i == 0 ? 145f : -145f, 0f);
                Vector3 hDir = hRot * Vector3.forward;
                Place(i == 0 ? headL : headR, b + hDir * (headLen * 0.5f) + Vector3.up * y, hRot, new Vector3(0.12f, 0.012f, headLen), mat);
            }

            // Chevrons stream from the attacker toward the tip, faster as the wind-up runs out.
            int want = Mathf.Max(2, Mathf.FloorToInt(len / 1.3f));
            while (chevrons.Count < want)
                chevrons.Add(new[]
                {
                    PieceFactory.Part(PrimitiveType.Cube, arrowRoot, Vector3.zero, Vector3.one, ArrowMat),
                    PieceFactory.Part(PrimitiveType.Cube, arrowRoot, Vector3.zero, Vector3.one, ArrowMat),
                });
            float speed = Mathf.Lerp(2.2f, 8f, progress);
            float spacing = len / want;
            for (int i = 0; i < chevrons.Count; i++)
            {
                bool on = i < want;
                foreach (var part in chevrons[i]) part.gameObject.SetActive(on);
                if (!on) continue;
                float s = Mathf.Repeat(Time.time * speed + i * spacing, len);
                Vector3 p = a + dir * s + Vector3.up * (y + 0.004f);
                float fade = Mathf.Clamp01(Mathf.Min(s, len - s) / 0.6f);
                for (int k = 0; k < 2; k++)
                {
                    var cRot = rot * Quaternion.Euler(0f, k == 0 ? 135f : -135f, 0f);
                    Place(chevrons[i][k], p + cRot * Vector3.forward * 0.22f, cRot, new Vector3(0.09f, 0.012f, 0.44f * fade + 0.01f), mat);
                }
            }
        }

        static void Place(Transform t, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
        {
            t.SetPositionAndRotation(pos, rot);
            t.localScale = scale;
            var r = t.GetComponent<Renderer>();
            if (r.sharedMaterial != mat) r.sharedMaterial = mat;
        }

        // ---------------------------------------------------------------- knight route + ghost

        void BuildRoute()
        {
            var from = BoardSlots.ToSquare(start);
            var to = BoardSlots.ToSquare(center);
            Vector2Int d = to - from;
            // The L: the long leg first, then the short one.
            var corner = Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? new Vector2Int(to.x, from.y) : new Vector2Int(from.x, to.y);
            Vector3[] pts = { BoardSlots.Center(from), BoardSlots.Center(corner), BoardSlots.Center(to) };
            var root = new GameObject("Route").transform;
            root.SetParent(transform, false);
            for (int leg = 0; leg < 2; leg++)
            {
                Vector3 a = pts[leg], b = pts[leg + 1];
                float len = (b - a).magnitude;
                for (float s = leg == 0 ? 0.5f : 0f; s < len - (leg == 1 ? 0.6f : 0f); s += 0.4f)
                    dots.Add(PieceFactory.Part(PrimitiveType.Cube, root, a + (b - a) / Mathf.Max(0.001f, len) * s + Vector3.up * 0.05f,
                        new Vector3(0.13f, 0.012f, 0.13f), ArrowMat, new Vector3(0f, 45f, 0f)));
            }
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

            float size = Tile * 0.96f, h = size * 0.5f;
            float th = Mathf.Lerp(0.04f, h, p);   // fill grows from the edges inward
            foreach (var c in cells)
            {
                Vector3 o = BoardSlots.Center(c.square) + Vector3.up * 0.028f;
                c.fillN.SetPositionAndRotation(o + new Vector3(0f, 0f, h - th * 0.5f), Quaternion.identity);
                c.fillN.localScale = new Vector3(size, 0.01f, th);
                c.fillS.SetPositionAndRotation(o - new Vector3(0f, 0f, h - th * 0.5f), Quaternion.identity);
                c.fillS.localScale = new Vector3(size, 0.01f, th);
                float side = Mathf.Max(0.001f, size - 2f * th);
                c.fillE.SetPositionAndRotation(o + new Vector3(h - th * 0.5f, 0f, 0f), Quaternion.identity);
                c.fillE.localScale = new Vector3(th, 0.01f, side);
                c.fillW.SetPositionAndRotation(o - new Vector3(h - th * 0.5f, 0f, 0f), Quaternion.identity);
                c.fillW.localScale = new Vector3(th, 0.01f, side);
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

            if (kind == Kind.Line && arrowRoot != null) LayoutArrow(white ? FlashMat : ArrowMat);

            if (kind == Kind.Leap)
            {
                // Dots light up in sequence along the route.
                for (int i = 0; i < dots.Count; i++)
                {
                    float wave = Mathf.Repeat(Time.time * 2.5f - i * 0.12f, 1f);
                    dots[i].localScale = new Vector3(0.13f, 0.012f, 0.13f) * Mathf.Lerp(0.6f, 1.3f, wave < 0.3f ? 1f - wave / 0.3f : 0f);
                    var r = dots[i].GetComponent<Renderer>();
                    var m = white ? FlashMat : ArrowMat;
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
}

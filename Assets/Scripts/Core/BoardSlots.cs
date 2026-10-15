using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Chessboard slot system. Instead of swarming the King, every piece reserves a square to attack from — the
    /// squares its own move pattern reaches the King's square from (pawn: forward diagonals, knight: the L squares,
    /// bishop/rook/queen: their lines) — walks there in its own gait, and strikes along that move. One piece per
    /// square. When every attack square of its pattern is taken, a piece waits on an outer square (3–4 squares out)
    /// and takes over a slot as soon as one frees up. Squares off the board simply don't exist, so a King with his
    /// back to the edge can only be surrounded from the open side.
    /// </summary>
    public static class BoardSlots
    {
        public const int Size = 8;
        static float Tile => GameManager.TileSize;

        static readonly Dictionary<Vector2Int, EnemyBase> owners = new Dictionary<Vector2Int, EnemyBase>();
        static readonly Dictionary<EnemyBase, Vector2Int> byPiece = new Dictionary<EnemyBase, Vector2Int>();
        static bool hasKing;

        /// <summary>The King's square (with a little hysteresis so it doesn't flicker on square borders).</summary>
        public static Vector2Int KingSquare { get; private set; }

        /// <summary>Bumped whenever the King changes square or a reservation is released, so pieces re-plan.</summary>
        public static int Version { get; private set; }

        public static IEnumerable<KeyValuePair<Vector2Int, EnemyBase>> Reservations => owners;

        // ---------------------------------------------------------------- squares

        public static Vector2Int ToSquare(Vector3 p) => new Vector2Int(
            Mathf.Clamp(Mathf.FloorToInt(p.x / Tile + Size * 0.5f), 0, Size - 1),
            Mathf.Clamp(Mathf.FloorToInt(p.z / Tile + Size * 0.5f), 0, Size - 1));

        public static Vector3 Center(Vector2Int s) => new Vector3((s.x - 3.5f) * Tile, 0f, (s.y - 3.5f) * Tile);

        public static bool OnBoard(Vector2Int s) => s.x >= 0 && s.y >= 0 && s.x < Size && s.y < Size;

        public static int Chebyshev(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

        public static string Name(Vector2Int s) => $"{(char)('a' + s.x)}{s.y + 1}";

        // ---------------------------------------------------------------- king

        public static void UpdateKing(Vector3 kingPos)
        {
            var sq = ToSquare(kingPos);
            if (!hasKing)
            {
                hasKing = true;
                KingSquare = sq;
                Version++;
                return;
            }
            if (sq == KingSquare) return;
            // Only switch once the King is clearly inside the new square.
            Vector3 d = kingPos - Center(KingSquare);
            if (Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.z)) < Tile * 0.5f + 0.25f) return;
            KingSquare = sq;
            Version++;
        }

        // ---------------------------------------------------------------- reservations

        public static EnemyBase Owner(Vector2Int s) => owners.TryGetValue(s, out var e) && e != null ? e : null;

        public static bool IsFreeFor(Vector2Int s, EnemyBase e)
        {
            var o = Owner(s);
            return o == null || o == e;
        }

        public static bool TryGet(EnemyBase e, out Vector2Int square) => byPiece.TryGetValue(e, out square);

        public static void Reserve(EnemyBase e, Vector2Int s)
        {
            Release(e, false);
            owners[s] = e;
            byPiece[e] = s;
        }

        public static void Release(EnemyBase e) => Release(e, true);

        static void Release(EnemyBase e, bool bump)
        {
            if (e == null || !byPiece.TryGetValue(e, out var s)) return;
            byPiece.Remove(e);
            if (owners.TryGetValue(s, out var o) && o == e) owners.Remove(s);
            if (bump) Version++;
        }

        public static void Clear()
        {
            owners.Clear();
            byPiece.Clear();
            hasKing = false;
            Version++;
        }

        // ---------------------------------------------------------------- move patterns (offsets from the King's square)

        /// <summary>Black pawns capture diagonally toward rank 1, so the squares above the King come first.</summary>
        public static readonly Vector2Int[] PawnSlots =
        {
            new Vector2Int(-1, 1), new Vector2Int(1, 1), new Vector2Int(-1, -1), new Vector2Int(1, -1),
        };

        public static readonly Vector2Int[] KnightSlots =
        {
            new Vector2Int(1, 2), new Vector2Int(2, 1), new Vector2Int(2, -1), new Vector2Int(1, -2),
            new Vector2Int(-1, -2), new Vector2Int(-2, -1), new Vector2Int(-2, 1), new Vector2Int(-1, 2),
        };

        public static readonly Vector2Int[] BishopSlots = Lines(diagonal: true, orthogonal: false);
        public static readonly Vector2Int[] RookSlots = Lines(diagonal: false, orthogonal: true);
        public static readonly Vector2Int[] QueenSlots = Lines(diagonal: true, orthogonal: true);

        /// <summary>Line pieces strike from 2–3 squares away (room to build up the charge).</summary>
        static Vector2Int[] Lines(bool diagonal, bool orthogonal)
        {
            var list = new List<Vector2Int>();
            for (int k = 2; k <= 3; k++)
            {
                if (orthogonal)
                {
                    list.Add(new Vector2Int(k, 0)); list.Add(new Vector2Int(-k, 0));
                    list.Add(new Vector2Int(0, k)); list.Add(new Vector2Int(0, -k));
                }
                if (diagonal)
                {
                    list.Add(new Vector2Int(k, k)); list.Add(new Vector2Int(-k, k));
                    list.Add(new Vector2Int(k, -k)); list.Add(new Vector2Int(-k, -k));
                }
            }
            return list.ToArray();
        }

        static readonly Vector2Int[] knightPrev = new Vector2Int[Size * Size];
        static readonly bool[] knightSeen = new bool[Size * Size];
        static readonly Queue<Vector2Int> knightQueue = new Queue<Vector2Int>();

        /// <summary>
        /// First hop of a shortest knight route from <paramref name="from"/> to <paramref name="to"/> that never lands
        /// on the King's square or on a square another piece holds. False when there is no route (or already there).
        /// </summary>
        public static bool KnightStep(Vector2Int from, Vector2Int to, EnemyBase self, out Vector2Int step)
        {
            step = from;
            if (from == to) return false;
            System.Array.Clear(knightSeen, 0, knightSeen.Length);
            knightQueue.Clear();
            knightQueue.Enqueue(from);
            knightSeen[from.x * Size + from.y] = true;
            while (knightQueue.Count > 0)
            {
                var c = knightQueue.Dequeue();
                foreach (var o in KnightSlots)
                {
                    var n = c + o;
                    if (!OnBoard(n) || knightSeen[n.x * Size + n.y] || n == KingSquare) continue;
                    if (n != to && !IsFreeFor(n, self)) continue;
                    knightSeen[n.x * Size + n.y] = true;
                    knightPrev[n.x * Size + n.y] = c;
                    if (n == to)
                    {
                        // Walk back to the hop right after the start.
                        var s = n;
                        while (knightPrev[s.x * Size + s.y] != from) s = knightPrev[s.x * Size + s.y];
                        step = s;
                        return true;
                    }
                    knightQueue.Enqueue(n);
                }
            }
            return false;
        }

        /// <summary>Unit step (one of the 8 directions) from one square toward another along their line.</summary>
        public static Vector3 LineDirection(Vector2Int from, Vector2Int to)
        {
            var d = new Vector3(Mathf.Sign(to.x - from.x) * (to.x != from.x ? 1f : 0f), 0f,
                                Mathf.Sign(to.y - from.y) * (to.y != from.y ? 1f : 0f));
            return d.sqrMagnitude > 0f ? d.normalized : Vector3.forward;
        }

        /// <summary>Snaps a direction to the nearest of the 8 chess directions.</summary>
        public static Vector3 Snap8(Vector3 v)
        {
            v.y = 0f;
            if (v.sqrMagnitude < 0.0001f) return Vector3.forward;
            float a = Mathf.Round(Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg / 45f) * 45f;
            return Quaternion.Euler(0f, a, 0f) * Vector3.forward;
        }
    }
}

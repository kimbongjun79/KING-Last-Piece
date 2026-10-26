using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Chessboard slot system. Instead of swarming the King, every piece reserves a square to attack from — the
    /// squares its own move pattern reaches the King's square from (knight: the L squares, bishop/rook/queen: their
    /// lines) — walks there in its own gait, and strikes along that move. One piece per square (queen > rook > bishop
    /// > knight when they compete). Pawns are foot soldiers outside the system. When every attack square of its
    /// pattern is taken, a piece waits on a square 2–3 squares out and takes over a slot as soon as one frees up.
    /// Squares off the board simply don't exist, so a King with his back to the edge can only be surrounded from the
    /// open side.
    /// </summary>
    public static class BoardSlots
    {
        // The grid of the fight in progress: a rectangle of Width × Height squares centred on Origin. The modes on one
        // board use a square at the world origin (SetSize); the stage mode moves it to each combat area (SetArea).

        /// <summary>Squares across (x) and deep (z) of the current fight's area.</summary>
        public static int Width { get; private set; } = 8;
        public static int Height { get; private set; } = 8;
        /// <summary>World centre of the area (y = 0).</summary>
        public static Vector3 Origin { get; private set; }

        /// <summary>Squares per side of a square board: 8, plus one ring (2 squares) per expansion of the endless board.</summary>
        public static int Size => Mathf.Max(Width, Height);

        /// <summary>Resizes the square board at the origin (the original 8×8 stays centred, so its square names keep their places).</summary>
        public static void SetSize(int size) => SetArea(Vector3.zero, size, size);

        /// <summary>The fight moves to an area of <paramref name="width"/> × <paramref name="height"/> squares centred on <paramref name="origin"/>.</summary>
        public static void SetArea(Vector3 origin, int width, int height)
        {
            origin.y = 0f;
            if (width == Width && height == Height && origin == Origin) return;
            Width = width;
            Height = height;
            Origin = origin;
            Clear();
        }

        /// <summary>Half the area's extent in world units, along x and z.</summary>
        public static float HalfX => Width * Tile * 0.5f;
        public static float HalfZ => Height * Tile * 0.5f;

        /// <summary><paramref name="p"/> kept inside the area, <paramref name="inset"/> in from its edges (y untouched).</summary>
        public static Vector3 ClampInside(Vector3 p, float inset)
        {
            p.x = Mathf.Clamp(p.x, Origin.x - HalfX + inset, Origin.x + HalfX - inset);
            p.z = Mathf.Clamp(p.z, Origin.z - HalfZ + inset, Origin.z + HalfZ - inset);
            return p;
        }

        /// <summary>Whether <paramref name="p"/> is inside the area, <paramref name="inset"/> in from its edges.</summary>
        public static bool Inside(Vector3 p, float inset) =>
            Mathf.Abs(p.x - Origin.x) <= HalfX - inset && Mathf.Abs(p.z - Origin.z) <= HalfZ - inset;

        /// <summary>Distance from <paramref name="from"/> along <paramref name="dir"/> (flat, normalised) to the area's edge, <paramref name="inset"/> in.</summary>
        public static float DistanceToEdge(Vector3 from, Vector3 dir, float inset)
        {
            float hx = HalfX - inset, hz = HalfZ - inset, t = float.MaxValue;
            float x = from.x - Origin.x, z = from.z - Origin.z;
            if (dir.x > 0.0001f) t = Mathf.Min(t, (hx - x) / dir.x);
            else if (dir.x < -0.0001f) t = Mathf.Min(t, (-hx - x) / dir.x);
            if (dir.z > 0.0001f) t = Mathf.Min(t, (hz - z) / dir.z);
            else if (dir.z < -0.0001f) t = Mathf.Min(t, (-hz - z) / dir.z);
            return Mathf.Max(0f, t);
        }

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
            Mathf.Clamp(Mathf.FloorToInt((p.x - Origin.x) / Tile + Width * 0.5f), 0, Width - 1),
            Mathf.Clamp(Mathf.FloorToInt((p.z - Origin.z) / Tile + Height * 0.5f), 0, Height - 1));

        public static Vector3 Center(Vector2Int s) =>
            Origin + new Vector3((s.x - (Width - 1) * 0.5f) * Tile, 0f, (s.y - (Height - 1) * 0.5f) * Tile);

        public static bool OnBoard(Vector2Int s) => s.x >= 0 && s.y >= 0 && s.x < Width && s.y < Height;

        /// <summary>The area's middle square (the nearest to its centre; ties go toward the start).</summary>
        public static Vector2Int Middle => new Vector2Int((Width - 1) / 2, (Height - 1) / 2);

        public static int Chebyshev(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

        public static string Name(Vector2Int s)
        {
            int o = (Size - 8) / 2;   // the original a1 square sits o squares in from the corner of a grown board
            return $"{(char)('a' + s.x - o)}{s.y - o + 1}";
        }

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

        /// <summary>
        /// Slot priority when two kinds of piece want the same square: queen > rook > bishop > knight > pawn.
        /// A higher-priority piece may take a square from a lower one that is not mid-attack.
        /// </summary>
        public static int Priority(PieceType t) => t switch
        {
            PieceType.Queen => 5, PieceType.Rook => 4, PieceType.Bishop => 3, PieceType.Knight => 2, _ => 1,
        };

        /// <summary>Free, or held by a lower-priority piece that can be bumped (<paramref name="bumps"/>).</summary>
        public static bool CanTake(Vector2Int s, EnemyBase e, out bool bumps)
        {
            var o = Owner(s);
            bumps = false;
            if (o == null || o == e) return true;
            if (o.IsAttacking || Priority(o.Type) >= Priority(e.Type)) return false;
            bumps = true;
            return true;
        }

        public static bool TryGet(EnemyBase e, out Vector2Int square) => byPiece.TryGetValue(e, out square);

        /// <summary>Reserves a square, bumping a lower-priority holder (which then re-plans).</summary>
        public static void Reserve(EnemyBase e, Vector2Int s)
        {
            Release(e, false);
            if (owners.TryGetValue(s, out var prev) && prev != null && prev != e)
            {
                byPiece.Remove(prev);
                Version++;
            }
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

        public static readonly Vector2Int[] KnightSlots =
        {
            new Vector2Int(1, 2), new Vector2Int(2, 1), new Vector2Int(2, -1), new Vector2Int(1, -2),
            new Vector2Int(-1, -2), new Vector2Int(-2, -1), new Vector2Int(-2, 1), new Vector2Int(-1, 2),
        };

        // Rooks and bishops strike from anywhere on their lines (2 squares out up to the far edge, however big the board
        // grows): a piece already on the King's rank/file or diagonal charges from where it is. The queen keeps to 2–3 squares.
        public static readonly Vector2Int[] BishopSlots = Lines(diagonal: true, orthogonal: false, 2, 31);
        public static readonly Vector2Int[] RookSlots = Lines(diagonal: false, orthogonal: true, 2, 31);
        public static readonly Vector2Int[] QueenSlots = Lines(diagonal: true, orthogonal: true, 2, 3);

        /// <summary>Squares on the lines through the King, <paramref name="minK"/>..<paramref name="maxK"/> squares out (at least 2: room to charge).</summary>
        static Vector2Int[] Lines(bool diagonal, bool orthogonal, int minK, int maxK)
        {
            var list = new List<Vector2Int>();
            for (int k = minK; k <= maxK; k++)
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

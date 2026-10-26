using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// The normal mode as a stage to explore (soulslike): small combat areas joined by short passages, through a black
    /// castle whose floors keep the chessboard grid. Stepping into a combat area shuts its gates and sets its pieces
    /// down; taking them all opens the way on (a flask back, and the area becomes the checkpoint). A death sends the King
    /// back to the checkpoint before the area he fell in, fully restored, with that area reset; areas already cleared
    /// stay clear. The final area's boss taken: the stage is won.
    /// <para>The layout is built at start from plain boxes (floors, walls, pillars, gates) on the global 2 m grid —
    /// placeholders for the castle's assets to come. The King keeps to the floors (walls push him back); in a fight the
    /// pieces' grid (<see cref="BoardSlots"/>) is the locked area's.</para>
    /// </summary>
    public class StageRun : MonoBehaviour
    {
        public static StageRun Instance { get; private set; }

        public const float WallHeight = 6f, KingRadius = 0.45f;
        const float WallThick = 0.8f;

        public class Area
        {
            public string name;
            public RectInt rect;                 // in tiles of the global grid (tile (i, j) spans x 2i…2i+2, z 2j…2j+2)
            public bool combat, boss;
            public (PieceType type, Vector2Int at, int tier)[] pieces;   // squares relative to the area's corner
            public RectInt[] notches;            // tiles left out of the floor (corners the towers stand in)
            public bool cleared;
            public GameObject gates;
            public readonly List<EnemyBase> spawned = new List<EnemyBase>();
            public Vector3 Center => new Vector3((rect.x + rect.width * 0.5f) * Tile, 0f, (rect.y + rect.height * 0.5f) * Tile);
        }

        static float Tile => GameManager.TileSize;
        const PieceType P = PieceType.Pawn, N = PieceType.Knight, R = PieceType.Rook, M = PieceType.Megatron;

        /// <summary>The sample stage: four areas, three passages between them, the altar of the colossus at the end.</summary>
        static List<Area> Layout() => new List<Area>
        {
            new Area
            {
                name = "폐허가 된 체스 궁정", rect = new RectInt(-5, 0, 10, 10), combat = true,
                pieces = new[] { (P, new Vector2Int(3, 7), 0), (P, new Vector2Int(6, 7), 0), (P, new Vector2Int(2, 8), 0), (P, new Vector2Int(7, 8), 0) },
            },
            new Area { name = "좁은 통로", rect = new RectInt(-1, 10, 2, 5) },
            new Area
            {
                name = "룩의 회랑", rect = new RectInt(-3, 15, 6, 18), combat = true,
                pieces = new[]
                {
                    (R, new Vector2Int(1, 16), 0), (R, new Vector2Int(4, 16), 1),
                    (P, new Vector2Int(0, 10), 0), (P, new Vector2Int(2, 10), 0), (P, new Vector2Int(3, 10), 0), (P, new Vector2Int(5, 10), 0),
                    (P, new Vector2Int(1, 13), 1),
                },
            },
            new Area { name = "어두운 통로", rect = new RectInt(-1, 33, 2, 4) },
            new Area { name = "어두운 통로", rect = new RectInt(-1, 37, 8, 2) },
            new Area { name = "어두운 통로", rect = new RectInt(5, 39, 2, 4) },
            new Area
            {
                name = "어둠의 기사", rect = new RectInt(2, 43, 8, 8), combat = true,
                pieces = new[] { (N, new Vector2Int(4, 5), 2) },
            },
            new Area { name = "제단으로 가는 길", rect = new RectInt(4, 51, 4, 5) },
            new Area
            {
                name = "거신병의 제단", rect = new RectInt(-1, 56, 14, 14), combat = true, boss = true,
                pieces = new[] { (M, new Vector2Int(7, 10), 0) },
                notches = new[] { new RectInt(-1, 56, 1, 1), new RectInt(12, 56, 1, 1), new RectInt(-1, 69, 1, 1), new RectInt(12, 69, 1, 1) },
            },
        };

        GameManager game;
        List<Area> areas;
        readonly HashSet<Vector2Int> floor = new HashSet<Vector2Int>();
        readonly Dictionary<Vector2Int, Transform> tiles = new Dictionary<Vector2Int, Transform>();
        HashSet<Vector2Int> allowed;
        readonly List<(Vector2 a, Vector2 b, Vector2 inward)> edges = new List<(Vector2, Vector2, Vector2)>();
        Vector3 lastValid;
        Transform root;

        public bool Built { get; private set; }
        /// <summary>The King walks the stage (so its floors, not one board, hold him).</summary>
        public bool Running => Built && game != null && game.CurrentMode == GameManager.Mode.Game;
        public Area Current { get; private set; }
        public bool Won { get; private set; }
        int checkpoint;   // index of the area the King comes back before after a death
        float spawnAt = -1f;

        public string CurrentName => (Current ?? AreaAt(game.Player.FlatPos))?.name ?? "";
        public Vector3 StartPosition => new Vector3(0f, 0f, 3f);

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        // ---------------------------------------------------------------- building

        /// <summary>Hides the baked hall and builds the stage from boxes.</summary>
        public void Build(GameManager game, ArenaBoard board)
        {
            this.game = game;
            areas = Layout();
            foreach (var name in new[] { "Squares", "Frame", "Hall Floor", "Pillars" })
            {
                var t = board.transform.Find(name);
                if (t != null) t.gameObject.SetActive(false);
            }
            root = new GameObject("Stage").transform;

            foreach (var a in areas)
                for (int x = a.rect.xMin; x < a.rect.xMax; x++)
                for (int y = a.rect.yMin; y < a.rect.yMax; y++)
                    floor.Add(new Vector2Int(x, y));
            foreach (var a in areas)
                if (a.notches != null)
                    foreach (var n in a.notches)
                        for (int x = n.xMin; x < n.xMax; x++)
                        for (int y = n.yMin; y < n.yMax; y++)
                            floor.Remove(new Vector2Int(x, y));

            BuildFloor(board);
            BuildWalls();
            foreach (var a in areas) if (a.combat) a.gates = BuildGates(a);
            Dress();
            Allow(floor);
            Built = true;
        }

        void BuildFloor(ArenaBoard board)
        {
            // The board's own tile materials (one per marble tile), by colour; a1 (dark) is squares[0].
            var darks = new List<Material>();
            var lights = new List<Material>();
            for (int i = 0; i < board.squares.Length; i++)
            {
                var r = board.squares[i] != null ? board.squares[i].GetComponent<Renderer>() : null;
                if (r == null) continue;
                var list = (i / 8 + i % 8) % 2 == 0 ? darks : lights;
                if (!list.Contains(r.sharedMaterial)) list.Add(r.sharedMaterial);
            }
            if (darks.Count == 0) darks.Add(MaterialLib.Get(GameManager.DarkSquare, 0.45f));
            if (lights.Count == 0) lights.Add(MaterialLib.Get(GameManager.LightSquare, 0.35f));
            var group = Group("Floor");
            foreach (var s in floor)
            {
                bool dark = ((s.x + s.y) % 2 + 2) % 2 == 0;
                var pool = dark ? darks : lights;
                var t = PieceFactory.Part(PrimitiveType.Cube, group, TileCenter(s) + Vector3.down * 0.2f, new Vector3(Tile, 0.4f, Tile),
                    pool[Mathf.Abs(s.x * 7 + s.y * 13) % pool.Count]);
                t.localRotation = Quaternion.Euler(0f, 90f * (Mathf.Abs(s.x * 3 + s.y * 5) % 4), 0f);
                t.name = $"Tile {s.x},{s.y}";
                t.GetComponent<Renderer>().renderingLayerMask |= WorldBuilder.BoardDecalLayer;
                t.gameObject.AddComponent<BoxCollider>();   // physics rubble lands on it
                tiles[s] = t;
            }
        }

        static readonly Color Stone = new Color(0.07f, 0.065f, 0.075f), DarkStone = new Color(0.035f, 0.033f, 0.04f);

        void BuildWalls()
        {
            var group = Group("Walls");
            var wall = MaterialLib.Get(Stone, 0.18f);
            var pillar = MaterialLib.Get(DarkStone, 0.25f);
            int n = 0;
            foreach (var (a, b, inward) in Edges(floor))
            {
                Vector3 mid = new Vector3((a.x + b.x) * 0.5f, WallHeight * 0.5f, (a.y + b.y) * 0.5f) - new Vector3(inward.x, 0f, inward.y) * (WallThick * 0.5f);
                bool alongX = Mathf.Abs(a.y - b.y) < 0.01f;
                var size = alongX ? new Vector3(Tile + WallThick, WallHeight, WallThick) : new Vector3(WallThick, WallHeight, Tile + WallThick);
                PieceFactory.Part(PrimitiveType.Cube, group, mid, size, wall).name = "Wall";
                // Buttress pillars along the walls, every third tile.
                if (n++ % 3 == 0)
                {
                    Vector3 p = new Vector3(a.x, WallHeight * 0.5f + 0.4f, a.y) - new Vector3(inward.x, 0f, inward.y) * (WallThick * 0.5f);
                    PieceFactory.Part(PrimitiveType.Cube, group, p, new Vector3(1.3f, WallHeight + 0.8f, 1.3f), pillar).name = "Pillar";
                }
            }
        }

        /// <summary>Gates across every opening of a combat area (shut while it is fought in).</summary>
        GameObject BuildGates(Area area)
        {
            var g = Group($"Gates — {area.name}").gameObject;
            var iron = MaterialLib.Get(new Color(0.05f, 0.05f, 0.055f), 0.6f, 0.7f);
            var glow = MaterialLib.Emissive(new Color(0.9f, 0.12f, 0.06f), 2.5f);
            var inside = AreaTiles(area);
            foreach (var (a, b, inward) in Edges(inside))
            {
                // Only openings: the tile beyond the edge is floor (another area or a passage).
                var beyond = new Vector2Int(Mathf.FloorToInt((a.x + b.x) * 0.5f / Tile - inward.x * 0.5f), Mathf.FloorToInt((a.y + b.y) * 0.5f / Tile - inward.y * 0.5f));
                if (!floor.Contains(beyond)) continue;
                bool alongX = Mathf.Abs(a.y - b.y) < 0.01f;
                Vector3 mid = new Vector3((a.x + b.x) * 0.5f, WallHeight * 0.5f, (a.y + b.y) * 0.5f);
                var size = alongX ? new Vector3(Tile, WallHeight, 0.35f) : new Vector3(0.35f, WallHeight, Tile);
                PieceFactory.Part(PrimitiveType.Cube, g.transform, mid, size, iron).name = "Gate";
                var bar = alongX ? new Vector3(Tile, 0.12f, 0.4f) : new Vector3(0.4f, 0.12f, Tile);
                PieceFactory.Part(PrimitiveType.Cube, g.transform, new Vector3(mid.x, 0.6f, mid.z), bar, glow).name = "Gate Glow";
            }
            g.SetActive(false);
            return g;
        }

        /// <summary>A few touches of the ruined castle: rubble in the first court, gem towers in the altar's corners, torches in the passages.</summary>
        void Dress()
        {
            var group = Group("Dressing");
            var rubble = MaterialLib.Get(Stone * 1.4f, 0.15f);
            var court = areas[0];
            var rng = new System.Random(7);
            for (int i = 0; i < 9; i++)
            {
                // Along the court's side walls.
                float x = (rng.Next(2) == 0 ? court.rect.xMin + 0.4f : court.rect.xMax - 0.4f) * Tile;
                float z = (court.rect.yMin + 1.5f + (float)rng.NextDouble() * (court.rect.height - 3f)) * Tile;
                float s = 0.5f + (float)rng.NextDouble() * 0.9f;
                PieceFactory.Part(PrimitiveType.Cube, group, new Vector3(x, s * 0.4f, z), new Vector3(s, s * 0.8f, s * 1.3f), rubble,
                    new Vector3(rng.Next(-20, 20), rng.Next(0, 360), rng.Next(-25, 25))).name = "Rubble";
            }
            foreach (var a in areas)
            {
                if (a.notches != null)
                    foreach (var n in a.notches)
                    {
                        var c = TileCenter(new Vector2Int(n.x, n.y));
                        var tower = WorldBuilder.GemTower(group, c, a.Center);
                        if (tower != null) tower.name = "Gem Tower";
                    }
                if (a.combat) continue;
                // Torches: a warm light in each passage.
                var light = new GameObject("Torch").AddComponent<Light>();
                light.transform.SetParent(group, false);
                light.transform.position = a.Center + Vector3.up * 3.2f;
                light.type = LightType.Point;
                light.color = new Color(1f, 0.6f, 0.3f);
                light.intensity = 9f;
                light.range = 10f;
                light.shadows = LightShadows.None;
            }
        }

        Transform Group(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            return t;
        }

        public static Vector3 TileCenter(Vector2Int s) => new Vector3((s.x + 0.5f) * Tile, 0f, (s.y + 0.5f) * Tile);
        static Vector2Int TileOf(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / Tile), Mathf.FloorToInt(p.z / Tile));

        HashSet<Vector2Int> AreaTiles(Area a)
        {
            var set = new HashSet<Vector2Int>();
            for (int x = a.rect.xMin; x < a.rect.xMax; x++)
            for (int y = a.rect.yMin; y < a.rect.yMax; y++)
            {
                var s = new Vector2Int(x, y);
                if (floor.Contains(s)) set.Add(s);
            }
            return set;
        }

        /// <summary>The border edges of a set of tiles (world xz), each with its normal pointing back into the set.</summary>
        static IEnumerable<(Vector2 a, Vector2 b, Vector2 inward)> Edges(HashSet<Vector2Int> set)
        {
            float t = GameManager.TileSize;
            foreach (var s in set)
            {
                float x0 = s.x * t, x1 = x0 + t, z0 = s.y * t, z1 = z0 + t;
                if (!set.Contains(s + Vector2Int.right)) yield return (new Vector2(x1, z0), new Vector2(x1, z1), Vector2.left);
                if (!set.Contains(s + Vector2Int.left)) yield return (new Vector2(x0, z0), new Vector2(x0, z1), Vector2.right);
                if (!set.Contains(s + Vector2Int.up)) yield return (new Vector2(x0, z1), new Vector2(x1, z1), Vector2.down);
                if (!set.Contains(s + Vector2Int.down)) yield return (new Vector2(x0, z0), new Vector2(x1, z0), Vector2.up);
            }
        }

        /// <summary>The King may walk these tiles (all floors, or a locked area's).</summary>
        void Allow(HashSet<Vector2Int> set)
        {
            allowed = set;
            edges.Clear();
            edges.AddRange(Edges(set));
        }

        // ---------------------------------------------------------------- the King among the walls

        public bool IsWalkable(Vector3 p) => allowed != null && allowed.Contains(TileOf(p));

        /// <summary><paramref name="p"/> pushed out of the walls (the King's radius), never through one.</summary>
        public Vector3 ClampWalkable(Vector3 p)
        {
            if (allowed == null) return p;
            if (!allowed.Contains(TileOf(p))) p = new Vector3(lastValid.x, p.y, lastValid.z);
            for (int pass = 0; pass < 2; pass++)
                foreach (var (a, b, inward) in edges)
                {
                    Vector2 q = new Vector2(p.x, p.z);
                    if (q.x < Mathf.Min(a.x, b.x) - 1f || q.x > Mathf.Max(a.x, b.x) + 1f || q.y < Mathf.Min(a.y, b.y) - 1f || q.y > Mathf.Max(a.y, b.y) + 1f) continue;
                    Vector2 ab = b - a;
                    Vector2 c = a + ab * Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
                    Vector2 v = q - c;
                    float d = v.magnitude;
                    if (d >= KingRadius) continue;
                    Vector2 push = d > 1e-4f && Vector2.Dot(v, inward) > -1e-4f ? v / d : inward;
                    q = c + push * KingRadius;
                    p.x = q.x;
                    p.z = q.y;
                }
            if (!allowed.Contains(TileOf(p))) p = new Vector3(lastValid.x, p.y, lastValid.z);
            lastValid = p;
            return p;
        }

        /// <summary>The camera kept on this side of the walls (unless it is above them): drawn in toward the King.</summary>
        public Vector3 CameraClamp(Vector3 focus, Vector3 cam)
        {
            const int steps = 24;
            float keep = 1f;
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector3 q = Vector3.Lerp(focus, cam, t);
                if (q.y > WallHeight + 0.3f) break;
                if (floor.Contains(TileOf(q)) && NearWallDistance(q) > 0.35f) continue;
                keep = (i - 1) / (float)steps;
                break;
            }
            return Vector3.Lerp(focus, cam, Mathf.Max(0.18f, keep));
        }

        float NearWallDistance(Vector3 p)
        {
            var s = TileOf(p);
            float lx = p.x - s.x * Tile, lz = p.z - s.y * Tile, best = 99f;
            if (!floor.Contains(s + Vector2Int.left)) best = Mathf.Min(best, lx);
            if (!floor.Contains(s + Vector2Int.right)) best = Mathf.Min(best, Tile - lx);
            if (!floor.Contains(s + Vector2Int.down)) best = Mathf.Min(best, lz);
            if (!floor.Contains(s + Vector2Int.up)) best = Mathf.Min(best, Tile - lz);
            return best;
        }

        // ---------------------------------------------------------------- flow

        Area AreaAt(Vector3 p)
        {
            var s = TileOf(p);
            foreach (var a in areas) if (a.rect.Contains(s)) return a;
            return null;
        }

        /// <summary>A new run: every area fresh, the King at the court's south end.</summary>
        public void Begin()
        {
            foreach (var a in areas) a.cleared = false;
            checkpoint = 0;
            Won = false;
            Unlock();
            game.Player.ResetForWave(StartPosition, 0f);
            lastValid = StartPosition;
            game.Rig.Snap();
            Focus(areas[0]);
        }

        /// <summary>After a death: back before the area he fell in, restored, that area reset.</summary>
        public void Respawn()
        {
            if (Current != null)
            {
                foreach (var e in Current.spawned) if (e != null) Destroy(e.gameObject);
                Current.spawned.Clear();
            }
            game.ClearEnemies();
            Unlock();
            Vector3 at = Checkpoint();
            game.Player.ResetForWave(at, 0f);
            lastValid = at;
            game.Rig.Snap();
            game.ShowBanner(areas[checkpoint].name);
        }

        /// <summary>Where the King comes back: the court's start, or the middle of the passage before the area.</summary>
        Vector3 Checkpoint()
        {
            if (checkpoint <= 0) return StartPosition;
            return areas[checkpoint - 1].Center;
        }

        void Unlock()
        {
            if (Current != null && Current.gates != null) Current.gates.SetActive(false);
            Current = null;
            spawnAt = -1f;
            Allow(floor);
        }

        public void Tick()
        {
            var king = game.Player;
            if (king == null || Won) return;
            if (Current == null)
            {
                // Step well into an uncleared combat area: it closes around the King.
                var a = AreaAt(king.FlatPos);
                if (a != null && a.combat && !a.cleared && Inset(a, 1.6f, king.FlatPos)) Lock(a);
                return;
            }
            if (spawnAt >= 0f && Time.time >= spawnAt)
            {
                spawnAt = -1f;
                foreach (var (type, at, tier) in Current.pieces)
                    Current.spawned.Add(game.SpawnEmpowered(type, tier, BoardSlots.Center(at)));
                return;
            }
            if (spawnAt >= 0f) return;
            foreach (var e in game.Enemies) if (e != null && !e.IsDead) return;
            Clear(Current);
        }

        bool Inset(Area a, float inset, Vector3 p) =>
            p.x > a.rect.xMin * Tile + inset && p.x < a.rect.xMax * Tile - inset &&
            p.z > a.rect.yMin * Tile + inset && p.z < a.rect.yMax * Tile - inset;

        void Lock(Area a)
        {
            Current = a;
            checkpoint = areas.IndexOf(a);
            a.spawned.Clear();
            if (a.gates != null) a.gates.SetActive(true);
            Allow(AreaTiles(a));
            BoardSlots.SetArea(a.Center, a.rect.width, a.rect.height);
            RegisterRipples(a);
            Focus(a);
            spawnAt = Time.time + 0.9f;
            game.ShowBanner(a.name);
            Sfx.Play("GAME_RoundStart");
            if (a.boss) Sfx.Play("GAME_FinalRound");
            Sfx.Play(a.boss ? "BGM_Boss" : "BGM_Battle");
            Effects.DarkWave(game.Player.FlatPos, 4f);
        }

        void Clear(Area a)
        {
            a.cleared = true;
            if (a.boss)
            {
                Won = true;
                return;
            }
            Unlock();
            checkpoint = Mathf.Min(areas.Count - 1, areas.IndexOf(a) + 1);
            // The next combat area is the one to come back before.
            while (checkpoint < areas.Count - 1 && !areas[checkpoint].combat) checkpoint++;
            game.Player.Rest();
            game.ShowBanner("구역 정화  ·  길이 열렸다");
            Sfx.Play("GAME_RoundClear");
        }

        /// <summary>The area's tiles ripple when struck (the board's ripple effect, on this area's grid).</summary>
        void RegisterRipples(Area a)
        {
            var grid = new Transform[a.rect.width, a.rect.height];
            for (int x = 0; x < a.rect.width; x++)
            for (int y = 0; y < a.rect.height; y++)
                tiles.TryGetValue(new Vector2Int(a.rect.x + x, a.rect.y + y), out grid[x, y]);
            BoardFx.Register(grid);
        }

        /// <summary>The hall's stage light and rim light over the area being fought in (or about to be).</summary>
        void Focus(Area a)
        {
            var hall = FindFirstObjectByType<HallAmbience>();
            if (hall != null) hall.Focus(a.Center, a.rect.width * Tile, a.rect.height * Tile);
        }

        /// <summary>Lab: the King set down inside combat area <paramref name="index"/> (it locks on the next tick).</summary>
        public void DebugEnter(int index)
        {
            DebugJump(index);
            var a = areas[checkpoint];
            Vector3 at = a.Center + Vector3.back * (a.rect.height * Tile * 0.3f);
            game.Player.ResetForWave(at, 0f);
            lastValid = at;
            game.Rig.Snap();
        }

        /// <summary>Lab: the whole stage's middle and extent (for an overview capture).</summary>
        public Bounds DebugBounds()
        {
            var b = new Bounds(areas[0].Center, Vector3.zero);
            foreach (var a in areas) b.Encapsulate(new Bounds(a.Center, new Vector3(a.rect.width * Tile, 0f, a.rect.height * Tile)));
            return b;
        }

        /// <summary>Lab: the King set down before area <paramref name="index"/> (counting combat areas only).</summary>
        public void DebugJump(int index)
        {
            int n = -1;
            for (int i = 0; i < areas.Count; i++)
                if (areas[i].combat && ++n == index) { checkpoint = i; break; }
            Respawn();
        }
    }
}

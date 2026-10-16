using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Grey-box layout of the planned "white chess classroom" (to be replaced by the real models), at true chess scale:
    /// the battlefield is a real chessboard lying on a table. A board square is 2 m in the game and about 5.5 cm in real
    /// life, so the world around is <see cref="Scale"/> ≈ 36× real size — the table top is a 44 m plateau 27 m above the
    /// floor, the room (10 × 10 m, 2.8 m high in real life) is ~360 m across and ~100 m tall, and the trees outside the
    /// windows are giants. The table grows with the endless board; the room stays.
    /// It replaces the dark hall (pillars, braziers, black floor) and switches the lighting to daylight.
    /// </summary>
    public static class ClassroomBlockout
    {
        const string RootName = "Classroom (blockout)";

        /// <summary>Game metres per real metre: a 2 m square stands for a 5.5 cm one.</summary>
        public const float Scale = 2f / 0.055f;

        /// <summary>Top of the table (the board's underside rests on it).</summary>
        const float TableTop = -0.4f;

        static readonly Color WallGrey = new Color(0.86f, 0.86f, 0.84f);
        static readonly Color FloorGrey = new Color(0.66f, 0.64f, 0.6f);
        static readonly Color PropGrey = new Color(0.58f, 0.57f, 0.55f);
        static readonly Color TableGrey = new Color(0.62f, 0.6f, 0.56f);
        static readonly Color DarkGrey = new Color(0.28f, 0.29f, 0.3f);
        static readonly Color CeilingGrey = new Color(0.9f, 0.9f, 0.89f);
        static readonly Color GroundGrey = new Color(0.5f, 0.55f, 0.48f);
        static readonly Color TreeGrey = new Color(0.38f, 0.45f, 0.36f);
        static readonly Color TrunkGrey = new Color(0.4f, 0.36f, 0.32f);

        static float M(float realMetres) => realMetres * Scale;

        // Real-life room (metres): the chess table stands in the middle of a 10 × 10 m classroom.
        const float RoomHalf = 5f, RoomHeight = 2.8f, WallThick = 0.15f;
        const float TableHeight = 0.75f, TableThick = 0.03f, TableLeg = 0.05f;

        static float Floor => TableTop - M(TableHeight);

        /// <summary>Builds (or rebuilds, for the current board size) the room under the arena.</summary>
        public static void Build(ArenaBoard board)
        {
            var arena = board.transform;
            var old = arena.Find(RootName);
            if (old != null) Object.Destroy(old.gameObject);

            // The dark hall goes away.
            foreach (var name in new[] { "Pillars", "Hall Floor" })
            {
                var t = arena.Find(name);
                if (t != null) t.gameObject.SetActive(false);
            }

            var root = new GameObject(RootName).transform;
            root.SetParent(arena, false);
            var wall = MaterialLib.Get(WallGrey, 0.2f);
            var prop = MaterialLib.Get(PropGrey, 0.3f);
            var dark = MaterialLib.Get(DarkGrey, 0.4f);

            ChessTable(root, MaterialLib.Get(TableGrey, 0.35f));
            Room(root, wall, prop, dark);
            Furniture(root, prop, dark);
            Trees(root);
            Daylight();
        }

        /// <summary>The table the board lies on: 1.2 m square in real life (≈ 44 m), wider if the endless board outgrows it.</summary>
        static void ChessTable(Transform root, Material wood)
        {
            float half = Mathf.Max(M(0.6f), GameManager.BoardHalf + 6f);
            float thick = M(TableThick);
            var top = Box(root, "Chess Table Top", new Vector3(0f, TableTop - thick / 2f, 0f), new Vector3(half * 2f, thick, half * 2f), wood);
            top.gameObject.AddComponent<BoxCollider>();   // rubble that slides off the board lands on the table
            float leg = M(TableLeg), legH = M(TableHeight) - thick, inset = half - leg;
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1 : 1) * inset, z = (i < 2 ? -1 : 1) * inset;
                Box(root, "Chess Table Leg", new Vector3(x, Floor + legH / 2f, z), new Vector3(leg, legH, leg), wood);
            }
        }

        static void Room(Transform root, Material wall, Material prop, Material dark)
        {
            float half = M(RoomHalf), height = M(RoomHeight), thick = M(WallThick), floorY = Floor;
            var floor = Box(root, "Floor", new Vector3(0f, floorY - 0.5f, 0f), new Vector3(half * 2f, 1f, half * 2f), MaterialLib.Get(FloorGrey, 0.3f));
            floor.gameObject.AddComponent<BoxCollider>();
            Box(root, "Outside Ground", new Vector3(0f, floorY - 3f, 0f), new Vector3(half * 12f, 1f, half * 12f), MaterialLib.Get(GroundGrey, 0.1f));

            // Ceiling with grey light panels.
            // (No shadow from the ceiling: the sun comes in at an angle the windows alone couldn't light the table at.)
            Box(root, "Ceiling", new Vector3(0f, floorY + height + thick / 2f, 0f), new Vector3(half * 2f + thick, thick, half * 2f + thick), MaterialLib.Get(CeilingGrey, 0.1f), shadows: false);
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j += 2)
                    Box(root, "Ceiling Light", new Vector3(i * M(3f), floorY + height - M(0.02f), j * M(1.8f)), new Vector3(M(1.2f), M(0.04f), M(0.3f)),
                        MaterialLib.Get(new Color(0.97f, 0.97f, 0.95f), 0.1f, 0f, new Color(0.6f, 0.6f, 0.58f)));

            // North: chalkboard (3.6 × 1.2 m) and the teacher's desk.
            Box(root, "Wall N", new Vector3(0f, floorY + height / 2f, half), new Vector3(half * 2f + thick, height, thick), wall);
            Box(root, "Chalkboard", new Vector3(0f, floorY + M(1.5f), half - thick / 2f - M(0.02f)), new Vector3(M(3.6f), M(1.2f), M(0.03f)), dark);
            Box(root, "Chalk Tray", new Vector3(0f, floorY + M(0.88f), half - thick / 2f - M(0.05f)), new Vector3(M(3.6f), M(0.03f), M(0.08f)), prop);

            // South: door (0.9 × 2.1 m) and bookshelves.
            Box(root, "Wall S", new Vector3(0f, floorY + height / 2f, -half), new Vector3(half * 2f + thick, height, thick), wall);
            Box(root, "Door", new Vector3(M(3f), floorY + M(1.05f), -half + thick / 2f + M(0.02f)), new Vector3(M(0.9f), M(2.1f), M(0.04f)), dark);
            for (int i = 0; i < 3; i++)
                Box(root, "Bookshelf", new Vector3(M(-3.6f + i * 1f), floorY + M(1f), -half + thick / 2f + M(0.18f)), new Vector3(M(0.9f), M(2f), M(0.35f)), prop);

            // East and west: three windows each (1.5 × 1.4 m, sill at 0.9 m), the trees beyond.
            for (int side = -1; side <= 1; side += 2)
                WindowWall(root, side, half, height, thick, floorY, wall, dark);
        }

        static void WindowWall(Transform root, int side, float half, float height, float thick, float floorY, Material wall, Material dark)
        {
            float x = side * half, len = half * 2f + thick;
            float sill = M(0.9f), top = M(2.3f), window = M(1.5f);
            string n = side > 0 ? "E" : "W";
            Box(root, $"Wall {n} Low", new Vector3(x, floorY + sill / 2f, 0f), new Vector3(thick, sill, len), wall);
            Box(root, $"Wall {n} High", new Vector3(x, floorY + (top + height) / 2f, 0f), new Vector3(thick, height - top, len), wall);
            const int windows = 3;
            float pier = (len - windows * window) / (windows + 1);
            var glass = MaterialLib.Transparent(new Color(0.75f, 0.88f, 1f, 0.12f));
            for (int i = 0; i <= windows; i++)
            {
                float z = -len / 2f + pier / 2f + i * (pier + window);
                Box(root, $"Wall {n} Pier", new Vector3(x, floorY + (sill + top) / 2f, z), new Vector3(thick, top - sill, pier), wall);
                if (i == windows) break;
                float wz = z + pier / 2f + window / 2f;
                Box(root, "Window Glass", new Vector3(x, floorY + (sill + top) / 2f, wz), new Vector3(M(0.01f), top - sill, window), glass, shadows: false);
                Box(root, "Window Mullion", new Vector3(x, floorY + (sill + top) / 2f, wz), new Vector3(M(0.05f), top - sill, M(0.04f)), dark);
                Box(root, "Window Transom", new Vector3(x, floorY + sill + (top - sill) * 0.66f, wz), new Vector3(M(0.05f), M(0.04f), window), dark);
            }
        }

        /// <summary>Teacher's desk, rows of student desks and chairs (some with chessboards set up), a chess clock.</summary>
        static void Furniture(Transform root, Material prop, Material dark)
        {
            float floorY = Floor, half = M(RoomHalf);
            // Teacher's desk in front of the chalkboard.
            Desk(root, new Vector3(0f, 0f, half - M(1.2f)), 1.4f, 0.7f, prop);
            // Student desks in two blocks to either side of the chess table, facing the chalkboard.
            for (int side = -1; side <= 1; side += 2)
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 2; col++)
                    {
                        var c = new Vector3(side * M(2.4f + col * 1.3f), 0f, M(-2.6f + row * 1.6f));
                        Desk(root, c, 1.1f, 0.6f, prop);
                        // Chair behind the desk (seat 0.45 m, back 0.85 m).
                        Box(root, "Chair Seat", new Vector3(c.x, floorY + M(0.44f), c.z - M(0.55f)), new Vector3(M(0.42f), M(0.04f), M(0.4f)), prop);
                        Box(root, "Chair Back", new Vector3(c.x, floorY + M(0.65f), c.z - M(0.74f)), new Vector3(M(0.42f), M(0.4f), M(0.03f)), prop);
                        Box(root, "Chair Legs", new Vector3(c.x, floorY + M(0.21f), c.z - M(0.55f)), new Vector3(M(0.36f), M(0.42f), M(0.34f)), prop);
                        // Every other desk has a little game set up (board 0.45 m, a few pieces).
                        if ((row + col) % 2 == 0)
                        {
                            float deskTop = floorY + M(0.75f);
                            Box(root, "Desk Chessboard", new Vector3(c.x, deskTop + M(0.01f), c.z), new Vector3(M(0.45f), M(0.02f), M(0.45f)), dark);
                            for (int k = 0; k < 4; k++)
                                Box(root, "Desk Piece", new Vector3(c.x + M(-0.15f + k * 0.1f), deskTop + M(0.06f), c.z + M(k % 2 == 0 ? 0.15f : -0.15f)),
                                    new Vector3(M(0.03f), M(0.08f), M(0.03f)), k % 2 == 0 ? prop : dark);
                        }
                    }
            // A chess clock on the chess table, beside the board.
            float tableHalf = Mathf.Max(M(0.6f), GameManager.BoardHalf + 6f);
            Box(root, "Chess Clock", new Vector3(GameManager.BoardHalf + (tableHalf - GameManager.BoardHalf) / 2f, TableTop + M(0.03f), 0f),
                new Vector3(M(0.06f), M(0.06f), M(0.18f)), dark);
        }

        static void Desk(Transform root, Vector3 c, float width, float depth, Material mat)
        {
            float floorY = Floor, h = M(0.75f), top = M(0.03f), leg = M(0.04f);
            Box(root, "Desk Top", new Vector3(c.x, floorY + h - top / 2f, c.z), new Vector3(M(width), top, M(depth)), mat);
            for (int i = 0; i < 4; i++)
            {
                float x = c.x + (i % 2 == 0 ? -1 : 1) * (M(width) / 2f - leg);
                float z = c.z + (i < 2 ? -1 : 1) * (M(depth) / 2f - leg);
                Box(root, "Desk Leg", new Vector3(x, floorY + (h - top) / 2f, z), new Vector3(leg, h - top, leg), mat);
            }
        }

        /// <summary>Grey-box trees outside the window walls (6–10 m tall in real life), and a few beyond the others.</summary>
        static void Trees(Transform root)
        {
            var trunk = MaterialLib.Get(TrunkGrey, 0.1f);
            var crown = MaterialLib.Get(TreeGrey, 0.1f);
            var rng = new System.Random(7);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float half = M(RoomHalf);
            for (int i = 0; i < 40; i++)
            {
                int sideKind = i % 4;   // 0, 1: east / west (window views), 2: north, 3: south
                float along = M(R(-12f, 12f)), out_ = half + M(R(3f, 16f));
                Vector3 p = sideKind < 2
                    ? new Vector3((sideKind == 0 ? 1 : -1) * out_, 0f, along)
                    : new Vector3(along, 0f, (sideKind == 2 ? 1 : -1) * out_);
                float h = M(R(6f, 10f)), w = M(R(3f, 5f));
                var tree = new GameObject("Tree").transform;
                tree.SetParent(root, false);
                tree.localPosition = new Vector3(p.x, Floor - 2.5f, p.z);
                PieceFactory.Part(PrimitiveType.Cylinder, tree, new Vector3(0f, h * 0.3f, 0f), new Vector3(M(0.35f), h * 0.3f, M(0.35f)), trunk).name = "Trunk";
                PieceFactory.Part(PrimitiveType.Sphere, tree, new Vector3(0f, h * 0.72f, 0f), new Vector3(w, w * 0.85f, w), crown).name = "Crown";
                PieceFactory.Part(PrimitiveType.Sphere, tree, new Vector3(w * 0.25f, h * 0.72f + w * 0.35f, 0f), Vector3.one * w * 0.65f, crown).name = "Crown";
            }
        }

        /// <summary>Bright daylight through the windows, a pale sky and a light haze over the giant room.</summary>
        static void Daylight()
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional)
                {
                    l.transform.rotation = Quaternion.Euler(48f, -62f, 0f);
                    l.color = new Color(1f, 0.96f, 0.88f);
                    l.intensity = 1.25f;
                }
                else if (l.name == "Brazier Light") l.enabled = false;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.58f, 0.6f, 0.64f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.82f, 0.88f, 0.94f);
            RenderSettings.fogStartDistance = 250f;
            RenderSettings.fogEndDistance = 1600f;
            var cam = Camera.main;
            if (cam != null)
            {
                cam.backgroundColor = new Color(0.72f, 0.83f, 0.95f);
                cam.farClipPlane = 2500f;   // the room is ~360 m across, the trees further out
            }
        }

        static Transform Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, bool shadows = true)
        {
            var t = PieceFactory.Part(PrimitiveType.Cube, parent, pos, size, mat);
            t.name = name;
            if (!shadows) t.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return t;
        }
    }
}

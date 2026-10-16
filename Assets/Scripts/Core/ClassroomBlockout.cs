using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Grey-box layout of the planned "white chess classroom" around the board (to be replaced by the real models):
    /// a bright room with big windows on the east and west walls looking out on trees, a chalkboard and teacher's desk
    /// on the north wall, the door and bookshelves on the south wall, and student desks with little chessboards along
    /// the sides. The room keeps a fixed margin around the board and is rebuilt when the endless board grows.
    /// It replaces the dark hall (pillars, braziers, black floor) and switches the lighting to daylight.
    /// </summary>
    public static class ClassroomBlockout
    {
        const string RootName = "Classroom (blockout)";
        /// <summary>Floor space between the board's edge and the walls.</summary>
        const float Margin = 9f;
        const float WallHeight = 6f, WallThick = 0.4f, FloorY = -0.4f;

        static readonly Color WallGrey = new Color(0.86f, 0.86f, 0.84f);
        static readonly Color FloorGrey = new Color(0.66f, 0.64f, 0.6f);
        static readonly Color PropGrey = new Color(0.55f, 0.55f, 0.55f);
        static readonly Color DarkGrey = new Color(0.28f, 0.29f, 0.3f);
        static readonly Color GroundGrey = new Color(0.5f, 0.55f, 0.48f);
        static readonly Color TreeGrey = new Color(0.38f, 0.45f, 0.36f);
        static readonly Color TrunkGrey = new Color(0.4f, 0.36f, 0.32f);

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
            float half = GameManager.BoardHalf + Margin;
            var wall = MaterialLib.Get(WallGrey, 0.2f);
            var floor = MaterialLib.Get(FloorGrey, 0.3f);
            var prop = MaterialLib.Get(PropGrey, 0.3f);
            var dark = MaterialLib.Get(DarkGrey, 0.4f);

            // Floor of the room, and the lawn outside.
            Box(root, "Floor", new Vector3(0f, FloorY - 0.05f, 0f), new Vector3(half * 2f, 0.1f, half * 2f), floor);
            Box(root, "Outside Ground", new Vector3(0f, FloorY - 0.15f, 0f), new Vector3(220f, 0.1f, 220f), MaterialLib.Get(GroundGrey, 0.1f));

            // North: solid wall, chalkboard, teacher's desk.
            Box(root, "Wall N", new Vector3(0f, FloorY + WallHeight / 2f, half), new Vector3(half * 2f + WallThick, WallHeight, WallThick), wall);
            Box(root, "Chalkboard", new Vector3(0f, FloorY + 2.6f, half - WallThick / 2f - 0.06f), new Vector3(9f, 3f, 0.1f), dark);
            Box(root, "Chalk Tray", new Vector3(0f, FloorY + 1.05f, half - WallThick / 2f - 0.15f), new Vector3(9f, 0.08f, 0.2f), prop);
            Box(root, "Teacher Desk", new Vector3(0f, FloorY + 0.4f, half - 2.4f), new Vector3(3f, 0.8f, 1.2f), prop);

            // South: wall with the door and bookshelves.
            Box(root, "Wall S", new Vector3(0f, FloorY + WallHeight / 2f, -half), new Vector3(half * 2f + WallThick, WallHeight, WallThick), wall);
            Box(root, "Door", new Vector3(half * 0.55f, FloorY + 1.3f, -half + WallThick / 2f + 0.05f), new Vector3(1.4f, 2.6f, 0.1f), dark);
            for (int i = 0; i < 2; i++)
                Box(root, "Bookshelf", new Vector3(-half * 0.55f + i * 3.4f, FloorY + 1.1f, -half + 0.6f), new Vector3(3f, 2.2f, 0.7f), prop);

            // East and west: walls with three big windows each, looking out on the trees.
            for (int side = -1; side <= 1; side += 2)
                WindowWall(root, side, half, wall, dark);

            // Student desks with little chessboards along the window walls.
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 4; i++)
                {
                    float z = Mathf.Lerp(-half * 0.6f, half * 0.6f, i / 3f);
                    float x = side * (half - 3.2f);
                    Box(root, "Student Desk", new Vector3(x, FloorY + 0.37f, z), new Vector3(1.4f, 0.75f, 0.8f), prop);
                    Box(root, "Desk Chessboard", new Vector3(x, FloorY + 0.76f, z), new Vector3(0.55f, 0.03f, 0.55f), dark);
                    Box(root, "Chair", new Vector3(x - side * 1.0f, FloorY + 0.23f, z), new Vector3(0.5f, 0.45f, 0.5f), prop);
                }

            Trees(root, half);
            Daylight();
        }

        static void WindowWall(Transform root, int side, float half, Material wall, Material dark)
        {
            float x = side * half, len = half * 2f + WallThick;
            const float sill = 1f, top = 4.4f;
            string n = side > 0 ? "E" : "W";
            // Below the sills and above the windows: full-length strips.
            Box(root, $"Wall {n} Low", new Vector3(x, FloorY + sill / 2f, 0f), new Vector3(WallThick, sill, len), wall);
            Box(root, $"Wall {n} High", new Vector3(x, FloorY + (top + WallHeight) / 2f, 0f), new Vector3(WallThick, WallHeight - top, len), wall);
            // Piers between three equal windows.
            const int windows = 3;
            float window = Mathf.Min(6f, len / (windows * 1.6f));
            float pier = (len - windows * window) / (windows + 1);
            var glass = MaterialLib.Transparent(new Color(0.75f, 0.88f, 1f, 0.12f));
            for (int i = 0; i <= windows; i++)
            {
                float z = -len / 2f + pier / 2f + i * (pier + window);
                Box(root, $"Wall {n} Pier", new Vector3(x, FloorY + (sill + top) / 2f, z), new Vector3(WallThick, top - sill, pier), wall);
                if (i == windows) break;
                float wz = z + pier / 2f + window / 2f;
                Box(root, "Window Glass", new Vector3(x, FloorY + (sill + top) / 2f, wz), new Vector3(0.04f, top - sill, window), glass, shadows: false);
                Box(root, "Window Mullion", new Vector3(x, FloorY + (sill + top) / 2f, wz), new Vector3(0.12f, top - sill, 0.1f), dark);
                Box(root, "Window Transom", new Vector3(x, FloorY + sill + (top - sill) * 0.66f, wz), new Vector3(0.12f, 0.1f, window), dark);
            }
        }

        /// <summary>Grey-box trees outside: trunks with stacked crowns, scattered behind every wall (most behind the windows).</summary>
        static void Trees(Transform root, float half)
        {
            var trunk = MaterialLib.Get(TrunkGrey, 0.1f);
            var crown = MaterialLib.Get(TreeGrey, 0.1f);
            var rng = new System.Random(7);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            for (int i = 0; i < 46; i++)
            {
                // Two thirds east/west (in view of the windows), the rest north/south.
                int sideKind = i % 3;
                float along = R(-half - 14f, half + 14f), out_ = half + R(4f, 22f);
                Vector3 p = sideKind == 2
                    ? new Vector3(along, 0f, (i % 2 == 0 ? 1 : -1) * out_)
                    : new Vector3((i % 2 == 0 ? 1 : -1) * out_, 0f, along);
                float h = R(3.5f, 7f), w = R(2.6f, 4.6f);
                var tree = new GameObject("Tree").transform;
                tree.SetParent(root, false);
                tree.localPosition = new Vector3(p.x, FloorY - 0.1f, p.z);
                PieceFactory.Part(PrimitiveType.Cylinder, tree, new Vector3(0f, h * 0.35f, 0f), new Vector3(0.45f, h * 0.35f, 0.45f), trunk).name = "Trunk";
                PieceFactory.Part(PrimitiveType.Sphere, tree, new Vector3(0f, h * 0.8f, 0f), new Vector3(w, w * 0.9f, w), crown).name = "Crown";
                PieceFactory.Part(PrimitiveType.Sphere, tree, new Vector3(w * 0.25f, h * 0.8f + w * 0.35f, 0f), Vector3.one * w * 0.65f, crown).name = "Crown";
            }
        }

        /// <summary>Bright daylight through the windows, a pale sky and a light haze instead of the dark hall.</summary>
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
            RenderSettings.fogStartDistance = 35f;
            RenderSettings.fogEndDistance = 110f;
            var cam = Camera.main;
            if (cam != null) cam.backgroundColor = new Color(0.72f, 0.83f, 0.95f);
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

using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Builds the "white chess classroom" of the endless mode at true chess scale — run in edit mode by
    /// KING ▸ Setup ▸ Bake Classroom, which saves it into the EndlessMode scene (so everything is a real, editable scene
    /// object). The battlefield is a real chessboard lying on a table: a board square is 2 m in the game and about
    /// 5.5 cm in real life, so the world is <see cref="Scale"/> ≈ 36× real size — the table top is a 44 m plateau 27 m
    /// above the floor and the room (10 × 10 m, 2.8 m high in real life) is ~360 m across and ~100 m tall.
    /// Furniture uses the provided models (desk, chair, door) and the game's own chess piece models; the desks show
    /// famous games. Walls, windows, ceiling and trees are still grey boxes.
    /// </summary>
    public static class ClassroomBlockout
    {
        public const string RootName = "Classroom";

        /// <summary>Game metres per real metre: a 2 m square stands for a 5.5 cm one.</summary>
        public const float Scale = 2f / 0.055f;

        /// <summary>Top of the table (the board's underside rests on it).</summary>
        public const float TableTop = -0.4f;

        const string DeskModel = "Models/table_v01_with VARCO";
        const string ChairModel = "Models/chair_v01_with VARCO";
        const string DoorModel = "Models/door_v01_with VARCO";
        const string PlantModel = "Models/plant_v01_with VARCO";
        const string WindowModel = "Models/window_v01_with VARCO";
        const string TreeModel = "Models/Green Leafy Tree_with VARCO";

        static readonly Color WallGrey = new Color(0.86f, 0.86f, 0.84f);
        static readonly Color FloorGrey = new Color(0.66f, 0.64f, 0.6f);
        static readonly Color PropGrey = new Color(0.58f, 0.57f, 0.55f);
        static readonly Color TableWood = new Color(0.55f, 0.42f, 0.3f);
        static readonly Color DarkGrey = new Color(0.28f, 0.29f, 0.3f);
        static readonly Color CeilingGrey = new Color(0.9f, 0.9f, 0.89f);

        public static float M(float realMetres) => realMetres * Scale;

        // Real-life room (metres): the chess table stands in the middle of a 10 × 10 m classroom.
        const float RoomHalf = 5f, RoomHeight = 2.8f, WallThick = 0.15f;
        const float TableHeight = 0.75f, TableThick = 0.03f, TableLeg = 0.05f;

        public static float Floor => TableTop - M(TableHeight);

        /// <summary>
        /// Famous positions set up on the student desks (FEN piece placement, rank 8 first).
        /// </summary>
        public static readonly (string name, string fen)[] Games =
        {
            ("Opera Game — Morphy, Paris 1858 (17.Rd8#)", "1n1Rkb1r/p4ppp/4q3/4p1B1/4P3/8/PPP2PPP/2K5"),
            ("Immortal Game — Anderssen v Kieseritzky, London 1851 (23.Be7#)", "r1bk3r/p2pBpNp/n4n2/1p1NP2P/6P1/3P4/P1P1K3/q5b1"),
            ("Fool's Mate (2...Qh4#)", "rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR"),
            ("Scholar's Mate (4.Qxf7#)", "r1bqkb1r/pppp1Qpp/2n2n2/4p3/2B1P3/8/PPPP1PPP/RNB1K1NR"),
            ("Ruy Lopez (1.e4 e5 2.Nf3 Nc6 3.Bb5)", "r1bqkbnr/pppp1ppp/2n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R"),
            ("Starting position", "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR"),
        };

        /// <summary>Builds the classroom under the arena and returns its root.</summary>
        public static Transform Build(Transform arena)
        {
            var root = new GameObject(RootName).transform;
            root.SetParent(arena, false);
            var wall = Resources.Load<Material>(WallpaperMaterial) ?? MaterialLib.Get(WallGrey, 0.2f);
            var prop = MaterialLib.Get(PropGrey, 0.3f);
            var dark = MaterialLib.Get(DarkGrey, 0.4f);

            var table = ChessTable(Group(root, "Chess Table"), MaterialLib.Get(TableWood, 0.4f));
            table.clock = ChessClock(table.transform, dark);
            table.Fit();
            // The room (walls, chalkboard, door, teacher's desk, plants, trees) is laid out with the chalkboard at its
            // north, then turned so a window wall faces the battlefield's front (+Z): the fight looks out on the
            // trees. The board, the chess table and the student desks keep facing +Z — toward the windows.
            var room = Group(root, "Room");
            Room(room, wall, prop, dark);
            TreesOutside(Group(room, "Trees Outside"));
            room.localRotation = Quaternion.Euler(0f, RoomYaw, 0f);
            Furniture(Group(root, "Furniture"), prop, dark);
            var ambience = root.gameObject.AddComponent<ClassroomAmbience>();
            // The shadow light: a directional light from high above (the main light, the only one URP shadows from).
            var shadowLight = new GameObject("Shadow Light (overhead)").AddComponent<Light>();
            shadowLight.transform.SetParent(root, false);
            shadowLight.type = LightType.Directional;
            ambience.shadowLight = shadowLight;
            return root;
        }

        static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        // ---------------------------------------------------------------- the chess table and its clock

        /// <summary>The table the board lies on: 1.2 m square in real life (≈ 44 m); ChessTableFit widens it for a bigger board.</summary>
        /// <summary>Yaw of the room around the table: −90° puts the east window wall in front of the battlefield.</summary>
        const float RoomYaw = -90f;

        /// <summary>
        /// The table the board lies on: the provided desk model at real height (75 cm) with its top widened to hold the
        /// board — about 2.4 × 1.2 m in real life (≈ 87 × 44 m). ChessTableFit widens it further for a bigger board.
        /// Falls back to a box table when the model is missing.
        /// </summary>
        static ChessTableFit ChessTable(Transform root, Material wood)
        {
            var fit = root.gameObject.AddComponent<ChessTableFit>();
            float depth = M(1.2f);
            var model = Model(root, DeskModel, TableHeight, AlignWidthAlongX);
            if (model != null)
            {
                model.localPosition = new Vector3(0f, Floor, 0f);
                // Same height, a footprint deep enough for the board: scale only across.
                var b = Bounds(model);
                float k = depth / Mathf.Max(0.01f, b.size.z);
                model.localScale = new Vector3(model.localScale.x * k, model.localScale.y, model.localScale.z * k);
                fit.model = model;
                fit.baseDepth = depth;
            }
            else
            {
                float thick = M(TableThick);
                fit.top = Box(root, "Table Top", new Vector3(0f, TableTop - thick / 2f, 0f), new Vector3(depth, thick, depth), wood);
            }
            fit.baseHalf = depth / 2f;
            // Rubble that slides off the board lands on the table top.
            var top = new GameObject("Table Top Collider").AddComponent<BoxCollider>();
            top.transform.SetParent(root, false);
            fit.topCollider = top;
            fit.Fit();
            return fit;
        }

        /// <summary>
        /// A digital chess clock (real 18 × 6 × 9 cm) beside the board, its sloped face toward the player: a black body
        /// with chamfered ends, two LED windows (play time and round, see ChessClockDisplay), two rocker buttons
        /// (one pressed), a centre ridge and feet.
        /// </summary>
        static Transform ChessClock(Transform table, Material dark)
        {
            var clock = Group(table, "Chess Clock");
            float b = GameManager.BoardHalf;
            clock.localPosition = new Vector3(b + (M(0.6f) - b) * 0.5f + 0.6f, TableTop, 2f);
            clock.localRotation = Quaternion.Euler(0f, -18f, 0f);   // turned a little toward the board's middle

            var body = MaterialLib.Get(new Color(0.07f, 0.07f, 0.08f), 0.65f, 0.1f);
            var trim = MaterialLib.Get(new Color(0.72f, 0.62f, 0.38f), 0.7f, 0.8f);   // brass
            var glass = MaterialLib.Get(new Color(0.02f, 0.02f, 0.025f), 0.95f);
            var cream = MaterialLib.Get(new Color(0.88f, 0.84f, 0.74f), 0.5f);
            float w = M(0.18f), h = M(0.065f), d = M(0.09f);

            // Feet, body, a chamfered top edge at both ends.
            for (int i = 0; i < 4; i++)
                Box(clock, "Foot", new Vector3((i % 2 == 0 ? -1 : 1) * w * 0.42f, M(0.003f), (i < 2 ? -1 : 1) * d * 0.38f),
                    new Vector3(M(0.012f), M(0.006f), M(0.012f)), dark);
            Box(clock, "Body", new Vector3(0f, M(0.006f) + h * 0.45f, 0f), new Vector3(w, h * 0.9f, d), body);
            // Sloped front panel holding the two displays (faces -Z, toward the camera).
            var face = Group(clock, "Face");
            face.localPosition = new Vector3(0f, M(0.006f) + h * 0.55f, -d * 0.5f);
            face.localRotation = Quaternion.Euler(-22f, 0f, 0f);
            Box(face, "Face Panel", new Vector3(0f, 0f, -M(0.002f)), new Vector3(w * 0.98f, h * 0.75f, M(0.006f)), body);
            Box(face, "Face Trim", new Vector3(0f, h * 0.38f, -M(0.004f)), new Vector3(w * 0.98f, M(0.002f), M(0.004f)), trim);
            var display = clock.gameObject.AddComponent<ChessClockDisplay>();
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * w * 0.24f;
                Box(face, "Display Bezel", new Vector3(x, -h * 0.02f, -M(0.006f)), new Vector3(w * 0.4f, h * 0.46f, M(0.003f)), trim);
                Box(face, "Display Glass", new Vector3(x, -h * 0.02f, -M(0.0075f)), new Vector3(w * 0.37f, h * 0.4f, M(0.002f)), glass);
                var anchor = Group(face, side < 0 ? "Left Digits" : "Right Digits");
                anchor.localPosition = new Vector3(x, -h * 0.02f, -M(0.009f));
                if (side < 0) display.leftFace = anchor; else display.rightFace = anchor;
            }
            display.digitHeight = h * 0.26f;
            // Top: centre ridge and the two rocker buttons (the left one pressed: it's the player's move).
            float top = M(0.006f) + h * 0.9f;
            Box(clock, "Centre Ridge", new Vector3(0f, top + M(0.004f), d * 0.05f), new Vector3(M(0.012f), M(0.008f), d * 0.7f), trim);
            for (int side = -1; side <= 1; side += 2)
            {
                bool pressed = side < 0;
                var btn = PieceFactory.Part(PrimitiveType.Cylinder, clock, new Vector3(side * w * 0.27f, top + (pressed ? M(0.004f) : M(0.012f)), d * 0.05f),
                    new Vector3(M(0.035f), pressed ? M(0.004f) : M(0.012f), M(0.035f)), side < 0 ? cream : dark);
                btn.name = pressed ? "Button (pressed)" : "Button";
                PieceFactory.Part(PrimitiveType.Cylinder, clock, new Vector3(side * w * 0.27f, top + M(0.001f), d * 0.05f),
                    new Vector3(M(0.042f), M(0.001f), M(0.042f)), trim).name = "Button Ring";
            }
            Box(clock, "Name Plate", new Vector3(0f, top - h * 0.05f, d * 0.5f + M(0.001f)), new Vector3(w * 0.3f, h * 0.15f, M(0.002f)), trim);
            return clock;
        }

        // ---------------------------------------------------------------- room

        static void Room(Transform root, Material wall, Material prop, Material dark)
        {
            float half = M(RoomHalf), height = M(RoomHeight), thick = M(WallThick), floorY = Floor;
            var floor = Box(root, "Floor", new Vector3(0f, floorY - 0.5f, 0f), new Vector3(half * 2f, 1f, half * 2f), MaterialLib.Get(FloorGrey, 0.3f));
            floor.gameObject.AddComponent<BoxCollider>();

            // Ceiling: no lamps — the room is lit by the sun through the windows. It casts no shadow: the overhead shadow
            // light (see ClassroomAmbience) shines down through it to give the pieces their shadows.
            Box(root, "Ceiling", new Vector3(0f, floorY + height + thick / 2f, 0f), new Vector3(half * 2f + thick, thick, half * 2f + thick),
                MaterialLib.Get(CeilingGrey, 0.1f), shadows: false);

            // North: the world champions 8th–12th in frames.
            Box(root, "Wall N", new Vector3(0f, floorY + height / 2f, half), new Vector3(half * 2f + thick, height, thick), wall);
            Portraits(root, side: 1, half, thick, floorY, 0);

            // South: the door (provided model) and the champions 13th–17th.
            Box(root, "Wall S", new Vector3(0f, floorY + height / 2f, -half), new Vector3(half * 2f + thick, height, thick), wall);
            var door = Model(root, DoorModel, 2.1f, AlignWidthAlongX);
            if (door != null) door.localPosition = new Vector3(M(3f), floorY, -half + thick / 2f + M(0.03f));
            // The teacher's desk under the portraits, and potted plants in the corners.
            Desk(root, new Vector3(0f, 0f, half - M(0.7f)), withChair: false, game: -1, dark);
            foreach (var p in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f) })
                Plant(root, new Vector3(p.x * (half - M(0.5f)), 0f, p.y * (half - M(0.5f))));
            Portraits(root, side: -1, half, thick, floorY, 5);

            // East and west: three windows each (the provided window model, 1.39 × 1.5 m, sill at 0.85 m).
            for (int side = -1; side <= 1; side += 2)
                WindowWall(root, side, half, height, thick, floorY, wall, dark);
        }

        static void WindowWall(Transform root, int side, float half, float height, float thick, float floorY, Material wall, Material dark)
        {
            float x = side * half, len = half * 2f + thick;
            // Openings sized to the provided window model (1.39 m wide, 1.5 m tall).
            float sill = M(0.85f), top = M(2.35f), window = M(1.39f);
            string n = side > 0 ? "E" : "W";
            Box(root, $"Wall {n} Low", new Vector3(x, floorY + sill / 2f, 0f), new Vector3(thick, sill, len), wall);
            Box(root, $"Wall {n} High", new Vector3(x, floorY + (top + height) / 2f, 0f), new Vector3(thick, height - top, len), wall);
            const int windows = 3;
            float pier = (len - windows * window) / (windows + 1);
            for (int i = 0; i <= windows; i++)
            {
                float z = -len / 2f + pier / 2f + i * (pier + window);
                Box(root, $"Wall {n} Pier", new Vector3(x, floorY + (sill + top) / 2f, z), new Vector3(thick, top - sill, pier), wall);
                if (i == windows) break;
                float wz = z + pier / 2f + window / 2f;
                // The window frame model in the opening, its thin side across the wall.
                var frame = Model(root, WindowModel, top / Scale - sill / Scale, m =>
                {
                    var b = Bounds(m);
                    if (b.size.x > b.size.z) m.localRotation = Quaternion.Euler(0f, 90f, 0f) * m.localRotation;
                });
                if (frame != null)
                {
                    frame.localPosition = new Vector3(x, floorY + sill, wz);
                    // Centred in the opening and in the wall's thickness (the model's pivot is at its base centre).
                    var b = Bounds(frame);
                    frame.position += new Vector3(x - b.center.x, 0f, wz - b.center.z);
                    ArchSpandrels(root, x, wz, thick, floorY + top, window / 2f, wall);
                }
                else Box(root, "Window Glass", new Vector3(x, floorY + (sill + top) / 2f, wz), new Vector3(M(0.01f), top - sill, window),
                    MaterialLib.Transparent(new Color(0.75f, 0.88f, 1f, 0.12f)), shadows: false);
            }
        }

        /// <summary>
        /// The window model's top is a round arch, the wall opening a rectangle: fill the two upper corners beside the
        /// arch with wall, in thin horizontal strips that follow the curve (radius = half the window's width, the arch
        /// rising from <paramref name="topY"/> − radius to <paramref name="topY"/>).
        /// </summary>
        static void ArchSpandrels(Transform root, float x, float centreZ, float thick, float topY, float radius, Material wall)
        {
            const int Strips = 10;
            float h = radius / Strips;
            for (int k = 0; k < Strips; k++)
            {
                float y = (k + 0.5f) * h;                                   // above the arch's spring line
                float halfWidth = Mathf.Sqrt(Mathf.Max(0f, radius * radius - y * y));
                float fill = radius - halfWidth;
                if (fill < 0.02f) continue;
                float cy = topY - radius + y;
                for (int s = -1; s <= 1; s += 2)
                    Box(root, "Arch Spandrel", new Vector3(x, cy, centreZ + s * (halfWidth + fill / 2f)), new Vector3(thick, h * 1.02f, fill), wall);
            }
        }

        /// <summary>
        /// Trees (the provided model, 7–10 m tall in real life) on the lawns outside both window walls, a few rows deep,
        /// so the windows look out on them over the fields of the sky.
        /// </summary>
        static void TreesOutside(Transform root)
        {
            float half = M(RoomHalf);
            var rng = new System.Random(11);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 9; i++)
                {
                    float out_ = half + M(R(3.5f, 16f)), along = M(-8f + i * 2f + R(-0.6f, 0.6f));
                    var tree = Model(root, TreeModel, R(7f, 10f), _ => { });
                    if (tree == null) return;
                    tree.localPosition = new Vector3(side * out_, Floor, along);
                    tree.localRotation = Quaternion.Euler(0f, R(0f, 360f), 0f) * tree.localRotation;
                    foreach (var r in tree.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
        }

        // ---------------------------------------------------------------- portraits

        /// <summary>World chess champions with a portrait in Resources/Textures/Chess_Champion_Photo (8th–17th).</summary>
        static readonly (int n, string name, string years)[] Champions =
        {
            (8, "Mikhail Tal", "1960-1961"),
            (9, "Tigran Petrosian", "1963-1969"),
            (10, "Boris Spassky", "1969-1972"),
            (11, "Bobby Fischer", "1972-1975"),
            (12, "Anatoly Karpov", "1975-1985"),
            (13, "Garry Kasparov", "1985-2000"),
            (14, "Vladimir Kramnik", "2000-2007"),
            (15, "Viswanathan Anand", "2007-2013"),
            (16, "Magnus Carlsen", "2013-2023"),
            (17, "Ding Liren", "2023-2024"),
        };

        static string Ordinal(int n) => n + (n % 10 == 1 && n != 11 ? "st" : n % 10 == 2 && n != 12 ? "nd" : n % 10 == 3 && n != 13 ? "rd" : "th");

        /// <summary>
        /// Five framed portraits along a wall (north: side 1, south: side −1), starting at champion <paramref name="first"/>:
        /// an 80 × 100 cm photo in a dark wooden frame with a gilt inner edge, hung with its centre at 1.6 m, and a brass
        /// name plate below. The south row stays clear of the door.
        /// </summary>
        static void Portraits(Transform root, int side, float half, float thick, float floorY, int first)
        {
            float[] xs = side > 0 ? new[] { -3.4f, -1.7f, 0f, 1.7f, 3.4f } : new[] { -3.4f, -2.1f, -0.8f, 0.5f, 1.8f };
            float wallFace = side * (half - thick / 2f);
            var wood = MaterialLib.Get(new Color(0.16f, 0.1f, 0.06f), 0.45f);
            var gilt = MaterialLib.Get(new Color(0.78f, 0.62f, 0.32f), 0.7f, 0.8f);
            var plate = MaterialLib.Get(new Color(0.7f, 0.58f, 0.32f), 0.65f, 0.85f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int i = 0; i < 5 && first + i < Champions.Length; i++)
            {
                var c = Champions[first + i];
                var tex = Resources.Load<Texture2D>($"Textures/Chess_Champion_Photo/{Ordinal(c.n)}_Chess_Champion_Photo");
                var p = Group(root, $"Portrait {Ordinal(c.n)} — {c.name}");
                p.localPosition = new Vector3(M(xs[i]), floorY + M(1.6f), wallFace);
                p.localRotation = Quaternion.Euler(0f, side > 0 ? 180f : 0f, 0f);   // facing into the room
                float w = M(0.8f), h = M(1f), border = M(0.09f), depth = M(0.04f);
                // Frame: four wooden bars, a gilt lip, the photo.
                Box(p, "Frame Top", new Vector3(0f, h / 2f + border / 2f, depth / 2f), new Vector3(w + border * 2f, border, depth), wood);
                Box(p, "Frame Bottom", new Vector3(0f, -h / 2f - border / 2f, depth / 2f), new Vector3(w + border * 2f, border, depth), wood);
                Box(p, "Frame Left", new Vector3(-w / 2f - border / 2f, 0f, depth / 2f), new Vector3(border, h, depth), wood);
                Box(p, "Frame Right", new Vector3(w / 2f + border / 2f, 0f, depth / 2f), new Vector3(border, h, depth), wood);
                Box(p, "Gilt Lip", new Vector3(0f, 0f, depth * 0.35f), new Vector3(w + M(0.012f), h + M(0.012f), depth * 0.3f), gilt);
                var photo = Box(p, "Photo", new Vector3(0f, 0f, depth * 0.55f), new Vector3(w, h, M(0.002f)), tex != null ? PhotoMaterial(tex) : wood, shadows: false);
                photo.localRotation = Quaternion.Euler(0f, 180f, 180f);   // the cube's −Z face toward the room, upright
                // Name plate and its engraving.
                var plateT = Box(p, "Name Plate", new Vector3(0f, -h / 2f - border - M(0.1f), M(0.008f)), new Vector3(M(0.68f), M(0.09f), M(0.012f)), plate);
                Engrave(p, font, $"{Ordinal(c.n).ToUpperInvariant()} WORLD CHAMPION", new Vector3(0f, plateT.localPosition.y + M(0.022f), M(0.015f)), M(0.015f));
                Engrave(p, font, $"{c.name}   {c.years}", new Vector3(0f, plateT.localPosition.y - M(0.016f), M(0.015f)), M(0.021f));
            }
        }

        static Material PhotoMaterial(Texture2D tex)
        {
            var m = new Material(MaterialLib.Get(Color.white, 0.25f));   // its own copy: the white base is shared
            m.SetTexture("_BaseMap", tex);
            m.SetTexture("_MainTex", tex);
            m.name = "Portrait " + tex.name;
            return m;
        }

        /// <summary>Dark text on a plate (legacy text mesh, facing the room like the frame).</summary>
        static void Engrave(Transform parent, Font font, string text, Vector3 pos, float height)
        {
            var go = new GameObject("Engraving");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.text = text;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            tm.characterSize = height / 2.8f;   // sized so the longest line fits the plate
            tm.color = new Color(0.12f, 0.08f, 0.04f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = font.material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ---------------------------------------------------------------- furniture

        /// <summary>
        /// Two blocks of student desks either side of the chess table, facing the windows like the board, each with its
        /// chair; half of them have a famous game set up. (The teacher's desk belongs to the room, by the chalkboard.)
        /// </summary>
        static void Furniture(Transform root, Material prop, Material dark)
        {
            int game = 0;
            for (int side = -1; side <= 1; side += 2)
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 2; col++)
                    {
                        var c = new Vector3(side * M(2.2f + col * 1.15f), 0f, M(-2.6f + row * 1.6f));
                        bool hasGame = (row + col) % 2 == 0 && game < Games.Length;
                        Desk(root, c, withChair: true, game: hasGame ? game++ : -1, dark);
                    }
        }

        /// <summary>A potted floor plant (the provided model, about 1.4 m tall), turned a little differently each time.</summary>
        static void Plant(Transform root, Vector3 at)
        {
            var plant = Model(root, PlantModel, 1.4f, _ => { });
            if (plant == null) return;
            plant.localPosition = new Vector3(at.x, Floor, at.z);
            plant.localRotation = Quaternion.Euler(0f, (at.x * 7.3f + at.z * 3.1f) % 360f, 0f) * plant.localRotation;
        }

        static void Desk(Transform root, Vector3 at, bool withChair, int game, Material dark)
        {
            var desk = Model(root, DeskModel, 0.75f, AlignWidthAlongX);
            float deskTop = Floor + M(0.75f);
            float depth = M(0.6f);
            if (desk != null)
            {
                desk.localPosition = new Vector3(at.x, Floor, at.z);
                var b = Bounds(desk);
                deskTop = b.max.y;
                depth = b.size.z;
            }
            else Box(root, "Desk (box)", new Vector3(at.x, Floor + M(0.75f) / 2f, at.z), new Vector3(M(1.1f), M(0.75f), M(0.6f)), dark);

            if (withChair)
            {
                // Chair behind the desk (south of it), its back away from the desk.
                var chair = Model(root, ChairModel, 0.85f, BackTowardMinusZ);
                if (chair != null) chair.localPosition = new Vector3(at.x, Floor, at.z - depth * 0.5f - M(0.3f));
            }
            if (game >= 0) SetUpGame(root, new Vector3(at.x, deskTop, at.z), Games[game]);
        }

        /// <summary>A 45 cm board on the desk with the position laid out (white at the south edge, facing the chalkboard).</summary>
        static void SetUpGame(Transform root, Vector3 top, (string name, string fen) game)
        {
            var set = Group(root, "Desk Game — " + game.name);
            set.localPosition = top;
            float sq = M(0.05f), edge = M(0.025f);
            var light = MaterialLib.Get(new Color(0.85f, 0.78f, 0.62f), 0.4f);
            var darkSq = MaterialLib.Get(new Color(0.42f, 0.26f, 0.16f), 0.4f);
            Box(set, "Board", new Vector3(0f, M(0.006f), 0f), new Vector3(sq * 8f + edge * 2f, M(0.012f), sq * 8f + edge * 2f), darkSq);
            for (int f = 0; f < 8; f++)
                for (int r = 0; r < 8; r++)
                    if ((f + r) % 2 == 1)
                        Box(set, "Light Square", new Vector3((f - 3.5f) * sq, M(0.0125f), (r - 3.5f) * sq), new Vector3(sq, M(0.001f), sq), light, shadows: false);

            var white = MaterialLib.Get(PieceFactory.WhitePiece, 0.55f);
            var black = MaterialLib.Get(PieceFactory.BlackPiece, 0.75f, 0.1f);
            string[] ranks = game.fen.Split('/');
            for (int ri = 0; ri < 8 && ri < ranks.Length; ri++)
            {
                int rank = 7 - ri, file = 0;
                foreach (char ch in ranks[ri])
                {
                    if (char.IsDigit(ch)) { file += ch - '0'; continue; }
                    if (file > 7) break;
                    bool isWhite = char.IsUpper(ch);
                    var (path, height) = PieceModel(char.ToLower(ch));
                    var piece = PieceFactory.LoadModel(path, set, M(height), isWhite ? 0f : 180f);
                    if (piece != null)
                    {
                        piece.name = $"{(isWhite ? "White" : "Black")} {char.ToUpper(ch)} {(char)('a' + file)}{rank + 1}";
                        piece.localPosition = new Vector3((file - 3.5f) * sq, M(0.013f), (rank - 3.5f) * sq);
                        foreach (var r in piece.GetComponentsInChildren<Renderer>())
                        {
                            var mats = r.sharedMaterials;
                            for (int i = 0; i < mats.Length; i++) mats[i] = isWhite ? white : black;
                            r.sharedMaterials = mats;
                        }
                    }
                    file++;
                }
            }
        }

        /// <summary>The game's piece models (the white king's for both kings), at real Staunton heights.</summary>
        static (string path, float height) PieceModel(char p) => p switch
        {
            'k' => ("Models/White_Chess_King_v02", 0.095f),
            'q' => ("Models/Black_Chess_Queen_v03", 0.085f),
            'b' => ("Models/Black_Chess_Bishop_v03", 0.07f),
            'n' => ("Models/Black_Chess_Knight_v03", 0.06f),
            'r' => ("Models/Black_Chess_Rook_v03", 0.055f),
            _ => ("Models/Black_Chess_Pawn_v03", 0.05f),
        };

        // ---------------------------------------------------------------- wallpaper and props

        const string WallpaperMaterial = "Materials/Wallpaper", ScoreSheetMaterial = "Materials/Score Sheet";
        const string PenModel = "Models/Silver_Ballpoint_Pen_v01_with VARCO";
        const string CabinetModel = "Models/Metal_Filing_Cabinet_v01_with VARCO";
        const string LightPanelModel = "Models/Rectangular_Light_Panel_v01_with VARCO";
        /// <summary>Real size of one wallpaper repeat.</summary>
        const float WallpaperRepeat = 0.8f;
        /// <summary>The baked plain wall material (named by its colour), swapped for the wallpaper at runtime.</summary>
        const string PlainWallName = "KLP_DBDBD6";

        /// <summary>
        /// Dresses the (baked) classroom at runtime: papers the walls (each wall box gets a mesh whose UVs repeat the
        /// wallpaper every <see cref="WallpaperRepeat"/> m instead of stretching it), and — once — lays a score sheet
        /// and a pen on the chess table beside the board, stands a filing cabinet against the south wall and hangs LED
        /// light panels under the ceiling.
        /// </summary>
        public static void Dress(Transform classroom)
        {
            var paper = Resources.Load<Material>(WallpaperMaterial);
            if (paper != null)
                foreach (var r in classroom.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var m = r.sharedMaterial;
                    if (m == null || (m != paper && !m.name.StartsWith(PlainWallName))) continue;
                    r.sharedMaterial = paper;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && (mf.sharedMesh == null || !mf.sharedMesh.name.StartsWith("Wallpapered Box")))
                        mf.sharedMesh = RepeatingBox(r.transform.localScale, M(WallpaperRepeat));
                }
            if (classroom.Find("Props") != null) return;
            var props = Group(classroom, "Props");
            ScoreSheet(classroom);
            var room = classroom.Find("Room");
            if (room == null) return;
            Cabinet(room);
            LightPanels(room);
            props.SetAsLastSibling();
        }

        static readonly Dictionary<Vector3, Mesh> wallMeshes = new Dictionary<Vector3, Mesh>();
        /// <summary>The wallpapered box meshes made so far (one per wall size) — the editor saves them as assets.</summary>
        public static IEnumerable<Mesh> WallpaperMeshes => wallMeshes.Values;

        /// <summary>A unit cube whose UVs count repeats of <paramref name="repeat"/> world units across each face of a box this size.</summary>
        static Mesh RepeatingBox(Vector3 size, float repeat)
        {
            var key = new Vector3(Mathf.Round(size.x * 100f), Mathf.Round(size.y * 100f), Mathf.Round(size.z * 100f));
            if (wallMeshes.TryGetValue(key, out var made) && made != null) return made;
            var src = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var v = src.vertices;
            var n = src.normals;
            var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 a = new Vector3(Mathf.Abs(n[i].x), Mathf.Abs(n[i].y), Mathf.Abs(n[i].z));
                Vector3 p = Vector3.Scale(v[i], size) / repeat;
                uv[i] = a.x > 0.5f ? new Vector2(p.z, p.y) : a.y > 0.5f ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y);
            }
            var mesh = Object.Instantiate(src);
            mesh.name = "Wallpapered Box";
            mesh.uv = uv;
            wallMeshes[key] = mesh;
            return mesh;
        }

        /// <summary>The score sheet (with a pen on it) lying on the table beside the board, on the side away from the clock.</summary>
        static void ScoreSheet(Transform classroom)
        {
            var mat = Resources.Load<Material>(ScoreSheetMaterial);
            var fit = classroom.GetComponentInChildren<ChessTableFit>(true);
            if (mat == null || fit == null) return;
            // Keep table room for the sheet beside the board as the endless board grows (the table widens to suit).
            fit.margin = Mathf.Max(fit.margin, M(0.21f) + 4f);
            fit.Fit();
            float board = GameManager.BoardHalf;
            float side = fit.clock != null && fit.clock.position.x > 0f ? -1f : 1f;
            // The picture is square with the A4 sheet in its middle (≈ 55% of its width): 0.385 m across in all.
            float size = M(0.385f);
            var sheet = new GameObject("Score Sheet").transform;
            sheet.SetParent(fit.transform, false);
            sheet.position = new Vector3(side * (board + 1.2f + M(0.21f) * 0.5f + 0.8f), TableTop + 0.02f, -1.5f);
            sheet.rotation = Quaternion.Euler(0f, side * -7f, 0f);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.name = "Sheet";
            quad.SetParent(sheet, false);
            quad.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.localScale = Vector3.one * size;
            quad.GetComponent<Renderer>().sharedMaterial = mat;
            fit.Follow(sheet);

            // The pen across the sheet's lower half.
            var pen = PieceFactory.LoadModel(PenModel, sheet, 1f, 0f, standUp: false);
            if (pen == null) return;
            pen.name = "Pen";
            LayFlat(pen);
            FitLongest(pen, M(0.145f));
            pen.rotation = Quaternion.Euler(0f, 35f, 0f) * pen.rotation;
            Settle(pen, sheet.position + sheet.rotation * new Vector3(0.8f, 0f, -2.2f), sheet.position.y + 0.01f);
        }

        /// <summary>A metal filing cabinet against the south wall, between the corner plant and the door.</summary>
        static void Cabinet(Transform room)
        {
            var cabinet = Model(room, CabinetModel, 1.32f, AlignWidthAlongX);
            if (cabinet == null) return;
            float half = M(RoomHalf), thick = M(WallThick);
            var b = Bounds(cabinet);
            float depth = Mathf.Min(b.size.x, b.size.z);   // its width runs along the wall
            cabinet.localPosition = new Vector3(-M(3.2f), Floor, -half + thick / 2f + depth / 2f + M(0.02f));
        }

        /// <summary>LED light panels (2 × 3) just under the ceiling, glowing.</summary>
        static void LightPanels(Transform room)
        {
            float top = Floor + M(RoomHeight);
            var group = Group(room, "LED Panels");
            foreach (var x in new[] { -M(2f), M(2f) })
            foreach (var z in new[] { -M(2.6f), 0f, M(2.6f) })
            {
                var panel = PieceFactory.LoadModel(LightPanelModel, group, 1f, 0f, standUp: false);
                if (panel == null) return;
                panel.name = "LED Panel";
                LayFlat(panel);
                FitLongest(panel, M(1.2f));
                foreach (var r in panel.GetComponentsInChildren<Renderer>())
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null) continue;
                        mats[i] = new Material(mats[i]);
                        mats[i].EnableKeyword("_EMISSION");
                        mats[i].SetColor("_EmissionColor", new Color(1f, 0.97f, 0.9f) * 1.6f);
                    }
                    r.sharedMaterials = mats;
                }
                var b = Bounds(panel);
                Vector3 at = room.TransformPoint(new Vector3(x, 0f, z));
                panel.position += new Vector3(at.x - b.center.x, top - 0.5f - b.max.y, at.z - b.center.z);
            }
        }

        /// <summary>Turns a model so its thinnest side is vertical (a pen or a panel lying flat).</summary>
        static void LayFlat(Transform m)
        {
            var s = Bounds(m).size;
            if (s.y <= s.x && s.y <= s.z) return;
            m.rotation = (s.x <= s.z ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f)) * m.rotation;
        }

        /// <summary>Scales a model so its longest side is <paramref name="length"/>.</summary>
        static void FitLongest(Transform m, float length)
        {
            var s = Bounds(m).size;
            m.localScale *= length / Mathf.Max(0.001f, Mathf.Max(s.x, Mathf.Max(s.y, s.z)));
        }

        /// <summary>Moves a model so its footprint is centred on <paramref name="at"/> with its underside at <paramref name="y"/>.</summary>
        static void Settle(Transform m, Vector3 at, float y)
        {
            var b = Bounds(m);
            m.position += new Vector3(at.x - b.center.x, y - b.min.y, at.z - b.center.z);
        }

        // ---------------------------------------------------------------- provided models

        /// <summary>A provided model scaled to a real height, run through <paramref name="orient"/>, base at its origin.</summary>
        static Transform Model(Transform parent, string path, float realHeight, System.Action<Transform> orient)
        {
            var m = PieceFactory.LoadModel(path, parent, M(realHeight), 0f, standUp: false);
            if (m == null) return null;
            m.name = System.IO.Path.GetFileName(path).Replace("_with VARCO", "");
            orient(m);
            return m;
        }

        static Bounds Bounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>Turns the model so its widest horizontal side runs along X (desks, the door in a south wall).</summary>
        static void AlignWidthAlongX(Transform m)
        {
            var b = Bounds(m);
            if (b.size.z > b.size.x) m.localRotation = Quaternion.Euler(0f, 90f, 0f) * m.localRotation;
        }

        /// <summary>Turns a chair so its back (the tall part) is toward −Z: the sitter faces +Z, the chalkboard.</summary>
        static void BackTowardMinusZ(Transform m)
        {
            var b = Bounds(m);
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var mf in m.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                var v = mesh.vertices;
                for (int i = 0; i < v.Length; i += 7)
                {
                    Vector3 w = mf.transform.TransformPoint(v[i]);
                    if (w.y > b.min.y + b.size.y * 0.75f) { sum += w; n++; }
                }
            }
            if (n == 0) return;
            Vector3 back = sum / n - b.center;
            back.y = 0f;
            if (back.sqrMagnitude < 1e-4f) return;
            float yaw = Vector3.SignedAngle(back.normalized, Vector3.back, Vector3.up);
            m.localRotation = Quaternion.Euler(0f, yaw, 0f) * m.localRotation;
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

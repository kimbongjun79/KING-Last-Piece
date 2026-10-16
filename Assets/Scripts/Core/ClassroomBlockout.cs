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

        static readonly Color WallGrey = new Color(0.86f, 0.86f, 0.84f);
        static readonly Color FloorGrey = new Color(0.66f, 0.64f, 0.6f);
        static readonly Color PropGrey = new Color(0.58f, 0.57f, 0.55f);
        static readonly Color TableWood = new Color(0.55f, 0.42f, 0.3f);
        static readonly Color DarkGrey = new Color(0.28f, 0.29f, 0.3f);
        static readonly Color CeilingGrey = new Color(0.9f, 0.9f, 0.89f);
        static readonly Color GroundGrey = new Color(0.5f, 0.55f, 0.48f);
        static readonly Color TreeGrey = new Color(0.38f, 0.45f, 0.36f);
        static readonly Color TrunkGrey = new Color(0.4f, 0.36f, 0.32f);

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
            var wall = MaterialLib.Get(WallGrey, 0.2f);
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
            Trees(Group(room, "Trees Outside"));
            room.localRotation = Quaternion.Euler(0f, RoomYaw, 0f);
            Furniture(Group(root, "Furniture"), prop, dark);
            root.gameObject.AddComponent<ClassroomAmbience>();
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
            Box(root, "Outside Ground", new Vector3(0f, floorY - 3f, 0f), new Vector3(half * 12f, 1f, half * 12f), MaterialLib.Get(GroundGrey, 0.1f));

            // Ceiling: no lamps — the room is lit only by the sun through the windows, so the ceiling and walls cast shadows.
            Box(root, "Ceiling", new Vector3(0f, floorY + height + thick / 2f, 0f), new Vector3(half * 2f + thick, thick, half * 2f + thick),
                MaterialLib.Get(CeilingGrey, 0.1f));

            // North: chalkboard (3.6 × 1.2 m).
            Box(root, "Wall N", new Vector3(0f, floorY + height / 2f, half), new Vector3(half * 2f + thick, height, thick), wall);
            Box(root, "Chalkboard", new Vector3(0f, floorY + M(1.5f), half - thick / 2f - M(0.02f)), new Vector3(M(3.6f), M(1.2f), M(0.03f)), dark);
            Box(root, "Chalk Tray", new Vector3(0f, floorY + M(0.88f), half - thick / 2f - M(0.05f)), new Vector3(M(3.6f), M(0.03f), M(0.08f)), prop);

            // South: the door (provided model) and bookshelves.
            Box(root, "Wall S", new Vector3(0f, floorY + height / 2f, -half), new Vector3(half * 2f + thick, height, thick), wall);
            var door = Model(root, DoorModel, 2.1f, AlignWidthAlongX);
            if (door != null) door.localPosition = new Vector3(M(3f), floorY, -half + thick / 2f + M(0.03f));
            // The teacher's desk in front of the chalkboard, and potted plants in the corners and by the chalkboard.
            Desk(root, new Vector3(0f, 0f, half - M(0.7f)), withChair: false, game: -1, dark);
            foreach (var p in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f) })
                Plant(root, new Vector3(p.x * (half - M(0.5f)), 0f, p.y * (half - M(0.5f))));
            Plant(root, new Vector3(-M(2.3f), 0f, half - M(0.45f)));
            Plant(root, new Vector3(M(2.3f), 0f, half - M(0.45f)));
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

        // ---------------------------------------------------------------- outside

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
                // Trees don't shade the windows: the low sun has to reach the battlefield.
                foreach (var r in tree.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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

using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Builds the static world: the board and its frame, pillars with braziers, lighting, the camera setup and the
    /// White King. Runs in edit mode (KING ▸ Setup ▸ Bake World Into Scenes saves the result into the scenes) and,
    /// as a fallback, at runtime for any scene that has nothing baked.
    /// </summary>
    public static class WorldBuilder
    {
        /// <summary>Rendering layer the board squares add for decals (crack projectors target only this layer).</summary>
        public const uint BoardDecalLayer = 1u << 1;

        public static ArenaBoard BuildArena()
        {
            var root = new GameObject("Arena").transform;
            var board = root.gameObject.AddComponent<ArenaBoard>();
            var light = MaterialLib.Get(GameManager.LightSquare, 0.35f);
            var dark = MaterialLib.Get(GameManager.DarkSquare, 0.45f);
            var wood = MaterialLib.Get(new Color(0.16f, 0.09f, 0.05f), 0.5f);
            var stone = MaterialLib.Get(new Color(0.16f, 0.16f, 0.18f), 0.15f);
            var floor = MaterialLib.Get(new Color(0.04f, 0.04f, 0.05f), 0.1f);
            float tile = GameManager.TileSize;

            var squares = new GameObject("Squares").transform;
            squares.SetParent(root, false);
            for (int f = 0; f < 8; f++)
            for (int r = 0; r < 8; r++)
            {
                var pos = new Vector3((f - 3.5f) * tile, -0.2f, (r - 3.5f) * tile);
                var sq = PieceFactory.Part(PrimitiveType.Cube, squares, pos, new Vector3(tile, 0.4f, tile), (f + r) % 2 == 0 ? dark : light);
                sq.name = $"{(char)('a' + f)}{r + 1}";
                board.squares[f * 8 + r] = sq;
            }

            var frame = new GameObject("Frame").transform;
            frame.SetParent(root, false);
            float edge = GameManager.BoardHalf + 0.35f;
            float len = GameManager.BoardHalf * 2f + 1.4f;
            PieceFactory.Part(PrimitiveType.Cube, frame, new Vector3(0, -0.15f, edge), new Vector3(len, 0.5f, 0.7f), wood).name = "Frame N";
            PieceFactory.Part(PrimitiveType.Cube, frame, new Vector3(0, -0.15f, -edge), new Vector3(len, 0.5f, 0.7f), wood).name = "Frame S";
            PieceFactory.Part(PrimitiveType.Cube, frame, new Vector3(edge, -0.15f, 0), new Vector3(0.7f, 0.5f, len), wood).name = "Frame E";
            PieceFactory.Part(PrimitiveType.Cube, frame, new Vector3(-edge, -0.15f, 0), new Vector3(0.7f, 0.5f, len), wood).name = "Frame W";
            PieceFactory.Part(PrimitiveType.Cube, root, new Vector3(0, -1.2f, 0), new Vector3(160f, 1f, 160f), floor).name = "Hall Floor";

            // Ring of pillars with braziers around the board.
            var fire = MaterialLib.Emissive(new Color(1f, 0.45f, 0.12f), 4f);
            var pillars = new GameObject("Pillars").transform;
            pillars.SetParent(root, false);
            for (int i = 0; i < 8; i++)
            {
                float a = (i * 45f + 22.5f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 15f;
                var pillar = new GameObject($"Pillar {i + 1}").transform;
                pillar.SetParent(pillars, false);
                PieceFactory.Part(PrimitiveType.Cylinder, pillar, p + Vector3.up * 2.3f, new Vector3(1.3f, 3f, 1.3f), stone).name = "Column";
                PieceFactory.Part(PrimitiveType.Cube, pillar, p + Vector3.up * 5.5f, new Vector3(1.8f, 0.4f, 1.8f), stone).name = "Capital";
                if (i % 2 == 0)
                {
                    PieceFactory.Part(PrimitiveType.Sphere, pillar, p + Vector3.up * 6f, new Vector3(0.8f, 0.6f, 0.8f), fire).name = "Brazier Fire";
                    var lg = new GameObject("Brazier Light").AddComponent<Light>();
                    lg.transform.SetParent(pillar, false);
                    lg.transform.position = p + Vector3.up * 6.5f;
                    lg.type = LightType.Point;
                    lg.color = new Color(1f, 0.55f, 0.25f);
                    lg.range = 18f;
                    lg.intensity = 4f;
                }
            }
            EnsureColliders(board);
            return board;
        }

        /// <summary>
        /// Runtime board setup: static colliders for physics rubble (board top at y = 0, frame rails, hall floor — pieces
        /// and the King never use colliders) and the decal layer on the squares. Idempotent, so older baked scenes get it.
        /// </summary>
        public static void EnsureColliders(ArenaBoard board)
        {
            var root = board.transform;
            foreach (var sq in board.squares)
            {
                var r = sq != null ? sq.GetComponent<Renderer>() : null;
                if (r != null) r.renderingLayerMask |= BoardDecalLayer;
            }
            if (root.Find("Board Collider") != null) return;
            var top = new GameObject("Board Collider").AddComponent<BoxCollider>();
            top.transform.SetParent(root, false);
            float size = GameManager.BoardHalf * 2f;
            top.center = new Vector3(0f, -0.5f, 0f);
            top.size = new Vector3(size, 1f, size);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if ((t.name.StartsWith("Frame ") || t.name == "Hall Floor") && t.GetComponent<Collider>() == null)
                    t.gameObject.AddComponent<BoxCollider>();
        }

        /// <summary>Moonlit key light, flat purple ambient and the dark hall fog.</summary>
        public static void ApplyLighting()
        {
            Light sun = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
            if (sun == null)
            {
                sun = new GameObject("Directional Light").AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            sun.color = new Color(0.75f, 0.78f, 0.95f);
            sun.intensity = 0.9f;
            sun.shadows = LightShadows.Soft;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.2f, 0.19f, 0.24f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.025f, 0.025f, 0.04f);
            RenderSettings.fogStartDistance = 20f;
            RenderSettings.fogEndDistance = 55f;
        }

        /// <summary>The scene's main camera (created if missing), set up for the hall, with the orbit rig attached.</summary>
        public static Camera SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.025f, 0.025f, 0.04f);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            if (cam.GetComponent<CameraRig>() == null) cam.gameObject.AddComponent<CameraRig>();
            return cam;
        }

        /// <summary>The White King on e1, model and trails built.</summary>
        public static PlayerController BuildPlayer()
        {
            var go = new GameObject("WhiteKing");
            go.transform.position = GameManager.SquareToWorld("e1");
            var player = go.AddComponent<PlayerController>();
            player.Build();
            return player;
        }
    }
}

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KingLastPiece.EditorTools
{
    /// <summary>
    /// KING ▸ Setup ▸ Apply Look Pass To Scenes: writes the look pass into the scenes themselves, so the scene view
    /// shows what the game shows —
    /// · the board squares each take a different tile of the Tiles074 marble texture (18 light and 18 dark tile
    ///   materials, each square turned a random quarter as well), in every scene;
    /// · Normal mode: the gem towers in place of the stone pillars;
    /// · Endless mode: the papered walls, the score sheet and pen, the filing cabinet and the LED panels.
    /// Runs once by itself after it is first compiled (then only from the menu). Generated materials and meshes are
    /// saved under Assets/Materials/Baked/Look.
    /// </summary>
    [InitializeOnLoad]
    public static class LookPass
    {
        const int Version = 1;
        const string PrefKey = "KLP.LookPass.Version";
        const string Dir = "Assets/Materials/Baked/Look";
        const string TileDir = "Assets/Materials/Baked/Tiles";
        const string LightBase = "Assets/Materials/Baked/KLP_BDB29E.mat", DarkBase = "Assets/Materials/Baked/KLP_421F19.mat";
        const int Tiles = 6;   // the texture is 6 × 6 marble tiles, its top-left one light

        static readonly string[] Scenes =
        {
            "Assets/Scenes/NormalMode.unity", "Assets/Scenes/EndlessMode.unity",
            "Assets/Scenes/Practice.unity", "Assets/Scenes/EffectsLab.unity",
        };

        static LookPass()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (EditorPrefs.GetInt(PrefKey, 0) >= Version) return;
                EditorPrefs.SetInt(PrefKey, Version);
                Apply();
            };
        }

        [MenuItem("KING/Setup/Apply Look Pass To Scenes")]
        public static void Apply()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string back = EditorSceneManager.GetActiveScene().path;
            AssetDatabase.Refresh();
            Marble("Assets/Materials/Baked/KLP_material_0.mat", "Marble014");                    // the White King
            foreach (var guid in AssetDatabase.FindAssets("KLP_Black_Plastic t:Material", new[] { "Assets/Materials/Baked" }))
                Marble(AssetDatabase.GUIDToAssetPath(guid), "Marble006");                         // the black pieces
            var (lights, darks) = TileMaterials();
            foreach (var path in Scenes)
            {
                if (!File.Exists(path)) continue;
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var board = Object.FindFirstObjectByType<ArenaBoard>();
                if (board == null) continue;
                VaryTiles(board, lights, darks);
                if (path.EndsWith("NormalMode.unity")) WorldBuilder.GemTowers(board.transform);
                var classroom = board.transform.Find(ClassroomBlockout.RootName);
                if (classroom != null) ClassroomBlockout.Dress(classroom);
                SaveMeshes();
                WorldBake.SaveMaterials(board.gameObject, Dir);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            if (!string.IsNullOrEmpty(back) && File.Exists(back)) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
            Debug.Log("[LookPass] Board tiles, gem towers and the classroom dressing written into the scenes.");
        }

        /// <summary>Puts a marble set (Assets/Textures/&lt;name&gt;_2K-JPG: colour and normal map) on a piece material.</summary>
        static void Marble(string path, string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var color = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Textures/{name}_2K-JPG/{name}_2K-JPG_Color.jpg");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Textures/{name}_2K-JPG/{name}_2K-JPG_NormalDX.jpg");
            if (m == null || color == null) return;
            m.SetTexture("_BaseMap", color);
            m.SetTexture("_MainTex", color);
            m.SetColor("_BaseColor", Color.white);
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.EnableKeyword("_NORMALMAP");
            }
            EditorUtility.SetDirty(m);
        }

        /// <summary>One material per tile of the texture, by colour (the light / dark square materials as templates).</summary>
        static (List<Material> lights, List<Material> darks) TileMaterials()
        {
            var lights = new List<Material>();
            var darks = new List<Material>();
            var lightBase = AssetDatabase.LoadAssetAtPath<Material>(LightBase);
            var darkBase = AssetDatabase.LoadAssetAtPath<Material>(DarkBase);
            if (lightBase == null || darkBase == null) return (lights, darks);
            Directory.CreateDirectory(TileDir);
            float s = 1f / Tiles;
            for (int c = 0; c < Tiles; c++)
            for (int r = 0; r < Tiles; r++)
            {
                // UV rows count from the bottom; the top-left tile (column 0, row 5) is light.
                bool light = (c + r) % 2 == 1;
                string path = $"{TileDir}/Tile {(light ? "Light" : "Dark")} {c}{r}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(light ? lightBase : darkBase);
                    AssetDatabase.CreateAsset(m, path);
                }
                else m.CopyPropertiesFromMaterial(light ? lightBase : darkBase);
                foreach (var prop in new[] { "_BaseMap", "_MainTex" })
                {
                    m.SetTextureScale(prop, new Vector2(s, s));
                    m.SetTextureOffset(prop, new Vector2(c * s, r * s));
                }
                EditorUtility.SetDirty(m);
                (light ? lights : darks).Add(m);
            }
            return (lights, darks);
        }

        /// <summary>Every square shows its own tile (no two neighbours alike where avoidable), turned a random quarter.</summary>
        static void VaryTiles(ArenaBoard board, List<Material> lights, List<Material> darks)
        {
            if (lights.Count == 0 || darks.Count == 0) return;
            var rng = new System.Random(64);
            var holder = board.squares[0] != null ? board.squares[0].parent : null;
            if (holder == null) return;
            var used = new Dictionary<Vector2Int, Material>();
            foreach (Transform sq in holder)
            {
                var r = sq.GetComponent<Renderer>();
                if (r == null) continue;
                var cell = new Vector2Int(Mathf.RoundToInt(sq.localPosition.x / GameManager.TileSize), Mathf.RoundToInt(sq.localPosition.z / GameManager.TileSize));
                bool light = r.sharedMaterial != null && (lights.Contains(r.sharedMaterial) || r.sharedMaterial.name.Contains("BDB29E"));
                var pool = light ? lights : darks;
                Material pick = null;
                for (int tries = 0; tries < 8; tries++)
                {
                    pick = pool[rng.Next(pool.Count)];
                    bool clash = false;
                    foreach (var d in new[] { new Vector2Int(1, 1), new Vector2Int(-1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1) })
                        if (used.TryGetValue(cell + d, out var other) && other == pick) clash = true;
                    if (!clash) break;
                }
                used[cell] = pick;
                r.sharedMaterial = pick;
                sq.localRotation = Quaternion.Euler(0f, 90f * rng.Next(4), 0f);
            }
        }

        /// <summary>The wallpapered wall meshes become assets (the scene can't keep meshes made in memory).</summary>
        static void SaveMeshes()
        {
            Directory.CreateDirectory(Dir);
            int i = 0;
            foreach (var mesh in ClassroomBlockout.WallpaperMeshes)
            {
                if (mesh == null || AssetDatabase.Contains(mesh)) continue;
                AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath($"{Dir}/Wallpapered Box {i++}.asset"));
            }
        }
    }
}

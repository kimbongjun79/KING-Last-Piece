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
        // 2: the new models' embedded textures are extracted first, and their objects rebuilt with them.
        // 3: marble is not metal — the piece materials lose their metallic sheen (the King looked black).
        // 4: the chess clock moves a little further out from the board.
        // 5: placed against the 8×8 board (a play session can leave the static board size at 12), props rebuilt there.
        // 6: the dark hall's light in NormalMode (HallAmbience + its post-processing profile).
        // 7: the gem towers rebuilt with glowing gems (emission map), no crown lights.
        const int Version = 7;
        static readonly string[] NewModels =
        {
            "Black_Gothic_Gem_Tower_v01_with VARCO", "Metal_Filing_Cabinet_v01_with VARCO",
            "Rectangular_Light_Panel_v01_with VARCO", "Silver_Ballpoint_Pen_v01_with VARCO",
        };
        /// <summary>Objects made from the new models (rebuilt on every pass so they pick up the textures).</summary>
        static readonly string[] Rebuilt = { "Gem Tower", "Props", "Score Sheet", "Metal_Filing_Cabinet_v01", "LED Panels" };
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
            BoardSlots.SetSize(8);   // the scenes hold the 8×8 board
            AssetDatabase.Refresh();
            ClassroomBake.EnsureTextures(NewModels);
            // The previous pass's model materials (saved without textures) go; the objects are rebuilt below.
            if (AssetDatabase.IsValidFolder(Dir))
                foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Dir }))
                    AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
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
                var stale = new List<GameObject>();
                foreach (var t in board.GetComponentsInChildren<Transform>(true))
                    if (System.Array.IndexOf(Rebuilt, t.name) >= 0) stale.Add(t.gameObject);
                foreach (var go in stale) if (go != null) Object.DestroyImmediate(go);
                if (path.EndsWith("NormalMode.unity"))
                {
                    WorldBuilder.GemTowers(board.transform);
                    HallLook(board);
                }
                var classroom = board.transform.Find(ClassroomBlockout.RootName);
                if (classroom != null)
                {
                    ClassroomBlockout.Dress(classroom);
                    var fit = classroom.GetComponentInChildren<ChessTableFit>(true);
                    if (fit != null)
                    {
                        // The clock (static: it cannot move in play) and the score sheet stand beside the 무쌍 board.
                        BoardSlots.SetSize(MusouDirector.BoardSize);
                        fit.Fit();
                        BoardSlots.SetSize(8);
                        EditorUtility.SetDirty(fit);
                    }
                }
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
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.55f);
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

        const string HallProfilePath = "Assets/Settings/HallLook.asset";

        /// <summary>The dark hall's light on the arena, with its post-processing saved as a profile asset.</summary>
        static void HallLook(ArenaBoard board)
        {
            var profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(HallProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
                AssetDatabase.CreateAsset(profile, HallProfilePath);
            }
            profile.components.RemoveAll(c => c == null);
            HallAmbience.Fill(profile);
            // The overrides live inside the profile asset (otherwise they are lost on save).
            foreach (var c in profile.components)
                if (!AssetDatabase.Contains(c))
                {
                    c.name = c.GetType().Name;
                    AssetDatabase.AddObjectToAsset(c, profile);
                }
            EditorUtility.SetDirty(profile);
            var hall = board.GetComponent<HallAmbience>();
            if (hall == null) hall = board.gameObject.AddComponent<HallAmbience>();
            hall.profile = profile;
            hall.Apply();
            EditorUtility.SetDirty(hall);
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

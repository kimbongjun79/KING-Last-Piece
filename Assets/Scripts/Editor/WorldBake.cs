using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KingLastPiece.EditorTools
{
    /// <summary>
    /// KING ▸ Setup ▸ Bake World Into Scenes: puts the whole static world into the scenes as real objects — board,
    /// frame, pillars and braziers, lighting, camera rig, the White King and the GameManager (with the HUD) — and
    /// saves every piece as a prefab (enemy component + model) that the GameManager spawns for the rounds.
    /// Generated materials are saved under Assets/Materials/Baked. Re-running replaces the previous bake.
    /// CLI: -executeMethod KingLastPiece.EditorTools.WorldBake.BakeCli
    /// </summary>
    public static class WorldBake
    {
        const string MaterialDir = "Assets/Materials/Baked";
        const string PrefabDir = "Assets/Prefabs/Pieces";

        static readonly (string scene, GameManager.Mode mode)[] Scenes =
        {
            ("Assets/Scenes/teat.unity", GameManager.Mode.Endless),
            ("Assets/Scenes/Practice.unity", GameManager.Mode.Practice),
            ("Assets/Scenes/EffectsLab.unity", GameManager.Mode.Effects),
        };

        // Baked root objects (replaced on every bake).
        static readonly string[] BakedRoots = { "Arena", "WhiteKing", "GameManager" };

        [MenuItem("KING/Setup/Bake World Into Scenes")]
        public static void BakeAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ClearBakedMaterials();
            var prefabs = BakePrefabs();
            foreach (var (scene, mode) in Scenes) BakeScene(scene, mode, prefabs);
            AssetDatabase.SaveAssets();
            Debug.Log($"[WorldBake] Baked {Scenes.Length} scenes and {prefabs.Length} piece prefabs.");
        }

        public static void BakeCli()
        {
            ClearBakedMaterials();
            var prefabs = BakePrefabs();
            foreach (var (scene, mode) in Scenes) BakeScene(scene, mode, prefabs);
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }

        // ---------------------------------------------------------------- pieces

        static GameObject[] BakePrefabs()
        {
            Directory.CreateDirectory(PrefabDir);
            var list = new List<GameObject>
            {
                BakePiece<PawnEnemy>("Black Pawn", PieceType.Pawn),
                BakePiece<KnightEnemy>("Black Knight", PieceType.Knight),
                BakePiece<BishopEnemy>("Black Bishop", PieceType.Bishop),
                BakePiece<RookEnemy>("Black Rook", PieceType.Rook),
                BakePiece<QueenEnemy>("Black Queen", PieceType.Queen),
                BakePiece<TrainingDummy>("Training Dummy", PieceType.Pawn),
            };
            return list.ToArray();
        }

        static GameObject BakePiece<T>(string name, PieceType type) where T : EnemyBase
        {
            var go = new GameObject(name);
            go.AddComponent<T>();
            var model = PieceFactory.BuildEnemy(type, go.transform);
            model.name = "Model";
            SaveMaterials(go);
            string path = $"{PrefabDir}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ---------------------------------------------------------------- scenes

        static void BakeScene(string path, GameManager.Mode mode, GameObject[] prefabs)
        {
            var scene = File.Exists(path)
                ? EditorSceneManager.OpenScene(path, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                if (System.Array.IndexOf(BakedRoots, root.name) >= 0) Object.DestroyImmediate(root);

            WorldBuilder.BuildArena();
            WorldBuilder.ApplyLighting();
            WorldBuilder.SetupCamera();
            WorldBuilder.BuildPlayer();

            var gmGo = new GameObject("GameManager");
            var gm = gmGo.AddComponent<GameManager>();
            gmGo.AddComponent<Hud>();
            var so = new SerializedObject(gm);
            so.FindProperty("sceneMode").enumValueIndex = (int)mode;
            var list = so.FindProperty("piecePrefabs");
            list.arraySize = prefabs.Length;
            for (int i = 0; i < prefabs.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            foreach (var root in scene.GetRootGameObjects()) SaveMaterials(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
        }

        /// <summary>Readable, stable names: imported model materials keep theirs, generated ones are named by color.</summary>
        static string MaterialName(Material m)
        {
            if (!string.IsNullOrEmpty(m.name) && !System.Text.RegularExpressions.Regex.IsMatch(m.name, @"^KLP_\d+$")) return m.name;
            string n = "KLP_" + ColorUtility.ToHtmlStringRGB(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color);
            if (m.HasProperty("_EmissionColor"))
            {
                var e = m.GetColor("_EmissionColor");
                if (e.maxColorComponent > 0.01f) n += "_Glow";
            }
            return n;
        }

        /// <summary>A fresh bake rewrites every scene and prefab, so the previous materials can go.</summary>
        static void ClearBakedMaterials()
        {
            if (AssetDatabase.IsValidFolder(MaterialDir)) AssetDatabase.DeleteAsset(MaterialDir);
        }

        /// <summary>Runtime-made materials (MaterialLib) become assets so the scene/prefab can reference them.</summary>
        static void SaveMaterials(GameObject root)
        {
            Directory.CreateDirectory(MaterialDir);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || AssetDatabase.Contains(m)) continue;
                    string name = MaterialName(m);
                    foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                    AssetDatabase.CreateAsset(m, AssetDatabase.GenerateUniqueAssetPath($"{MaterialDir}/{name}.mat"));
                }
            }
        }
    }
}

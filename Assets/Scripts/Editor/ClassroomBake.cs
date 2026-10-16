using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KingLastPiece.EditorTools
{
    /// <summary>
    /// KING ▸ Setup ▸ Bake Classroom: builds the true-scale chess classroom (ClassroomBlockout) into the EndlessMode
    /// scene as real scene objects — the chess table and its digital clock, the room, the provided desk / chair / door
    /// models, famous games on the desks, the trees outside — removes the dark hall (pillars, hall floor) there, sets
    /// the daylight, and adds a depth-of-field volume that blurs everything far from the battlefield.
    /// Re-running replaces the previous classroom. Run it again after KING ▸ Setup ▸ Bake World Into Scenes.
    /// CLI: -executeMethod KingLastPiece.EditorTools.ClassroomBake.BakeCli
    /// </summary>
    public static class ClassroomBake
    {
        const string ScenePath = "Assets/Scenes/EndlessMode.unity";
        const string ProfilePath = "Assets/Settings/ClassroomFocusBlur.asset";
        const string MaterialDir = "Assets/Materials/Classroom";

        [MenuItem("KING/Setup/Bake Classroom (EndlessMode)")]
        public static void BakeMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Bake();
        }

        public static void BakeCli()
        {
            Bake();
            EditorApplication.Exit(0);
        }

        static void Bake()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var board = Object.FindFirstObjectByType<ArenaBoard>();
            if (board == null) { Debug.LogError("[ClassroomBake] No ArenaBoard in the scene — bake the world first."); return; }
            var arena = board.transform;
            foreach (var name in new[] { ClassroomBlockout.RootName, "Classroom (blockout)", "Pillars", "Hall Floor" })
            {
                var t = arena.Find(name);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }

            if (AssetDatabase.IsValidFolder(MaterialDir)) AssetDatabase.DeleteAsset(MaterialDir);
            var root = ClassroomBlockout.Build(arena);
            root.GetComponent<ClassroomAmbience>().Apply();   // so the scene view shows the daylight too
            AddFocusBlur(root);
            // Its own material folder, cleared on every bake (no duplicates piling up).
            WorldBake.SaveMaterials(root.gameObject, MaterialDir);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ClassroomBake] Baked the classroom into {ScenePath} ({root.GetComponentsInChildren<Renderer>().Length} renderers).");
        }

        /// <summary>
        /// Gaussian depth of field: sharp around the battlefield (the camera sits ~15 m from the King), soft beyond the
        /// table and fully blurred across the room, so the giant classroom frames the fight without pulling the eye.
        /// </summary>
        static void AddFocusBlur(Transform root)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            profile.components.RemoveAll(c => c == null);
            if (!profile.TryGet(out DepthOfField dof))
            {
                dof = profile.Add<DepthOfField>(true);
                // The override lives inside the profile asset (otherwise it's lost on save).
                dof.name = "DepthOfField";
                AssetDatabase.AddObjectToAsset(dof, profile);
            }
            dof.active = true;
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(42f);
            dof.gaussianEnd.Override(170f);
            dof.gaussianMaxRadius.Override(1.5f);
            dof.highQualitySampling.Override(true);
            EditorUtility.SetDirty(profile);

            var go = new GameObject("Focus Blur (depth of field)");
            go.transform.SetParent(root, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;
        }
    }
}

using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace KingLastPiece.EditorTools
{
    /// <summary>Builds standalone players for the game, the practice arena and the effects lab.</summary>
    public static class LabBuild
    {
        const string NormalScene = "Assets/Scenes/NormalMode.unity";     // the classic ten rounds
        const string EndlessScene = "Assets/Scenes/EndlessMode.unity";   // endless rounds with upgrades
        const string PracticeScene = "Assets/Scenes/Practice.unity";
        const string EffectsScene = "Assets/Scenes/EffectsLab.unity";

        [MenuItem("KING/Build Practice (Windows)")]
        public static void BuildPracticeMenu() => Build(new[] { PracticeScene }, "Builds/Practice/Practice.exe");

        [MenuItem("KING/Build Effects Lab (Windows)")]
        public static void BuildEffectsMenu() => Build(new[] { EffectsScene }, "Builds/EffectsLab/EffectsLab.exe");

        [MenuItem("KING/Build Game (Windows)")]
        public static void BuildGameMenu() => Build(new[] { NormalScene, EndlessScene }, "Builds/Game/KING.exe");

        /// <summary>
        /// CLI: -executeMethod KingLastPiece.EditorTools.LabBuild.BuildLabCli -scene &lt;scene path[,scene path…]&gt; -buildPath &lt;exe&gt;
        /// (defaults to the practice arena).
        /// </summary>
        public static void BuildLabCli()
        {
            string scene = PracticeScene, path = "Builds/Practice/Practice.exe";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-buildPath") path = args[i + 1];
                if (args[i] == "-scene") scene = args[i + 1];
            }
            // Several scenes: comma-separated (the first one boots).
            bool ok = Build(scene.Split(','), path);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Build(string[] scenes, string path)
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Debug.Log($"[LabBuild] {report.summary.result} → {path} ({report.summary.totalErrors} errors)");
            return report.summary.result == BuildResult.Succeeded;
        }
    }
}

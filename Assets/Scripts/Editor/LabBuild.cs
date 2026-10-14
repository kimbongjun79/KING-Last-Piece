using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace KingLastPiece.EditorTools
{
    /// <summary>Builds standalone players for the game and the Attack Lab test scene.</summary>
    public static class LabBuild
    {
        const string GameScene = "Assets/Scenes/SampleScene.unity";
        const string LabScene = "Assets/Scenes/AttackLab.unity";

        [MenuItem("KING/Build Attack Lab (Windows)")]
        public static void BuildLabMenu() => Build(new[] { LabScene }, "Builds/AttackLab/AttackLab.exe");

        [MenuItem("KING/Build Game (Windows)")]
        public static void BuildGameMenu() => Build(new[] { GameScene }, "Builds/Game/KING.exe");

        /// <summary>CLI: -executeMethod KingLastPiece.EditorTools.LabBuild.BuildLabCli -buildPath &lt;exe&gt;</summary>
        public static void BuildLabCli()
        {
            string path = "Builds/AttackLab/AttackLab.exe";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-buildPath") path = args[i + 1];
            bool ok = Build(new[] { LabScene }, path);
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

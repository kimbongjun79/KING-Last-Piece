using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Normal-mode (stage) check: a player build of the NormalMode scene run with "-autoshot &lt;dir&gt;" captures the
    /// title, an overview of the castle from above, and each combat area once it has locked and its pieces landed;
    /// logs the areas and pieces (stage.tsv), then quits.
    /// </summary>
    public class HallShots : MonoBehaviour
    {
        string dir;
        readonly StringBuilder log = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            string d = SoundRefCapture.GetArg("-autoshot");
            var g = GameManager.Instance;
            if (d == null || g == null || g.CurrentMode != GameManager.Mode.Game) return;
            Directory.CreateDirectory(d);
            new GameObject("HallShots").AddComponent<HallShots>().dir = d;
        }

        IEnumerator Start()
        {
            var game = GameManager.Instance;
            yield return Frames(90);
            yield return Shot("70_Stage_Title.png");
            game.DebugStartGame();
            game.Player.GodMode = true;
            yield return Frames(30);
            var stage = game.Stage;
            if (stage == null) { log.Append("NoStage\n"); Finish(); yield break; }

            // From above: the whole castle.
            var b = stage.DebugBounds();
            game.Rig.UseOverride = true;
            game.Rig.OverridePosition = b.center + new Vector3(-30f, 85f, -25f);
            game.Rig.OverrideLookAt = b.center;
            yield return Frames(5);
            yield return Shot("71_Stage_Overview.png");
            game.Rig.UseOverride = false;

            string[] files = { "72_Area1_Court.png", "73_Area2_Rook_Gallery.png", "74_Area3_Dark_Knight.png", "75_Area4_Altar.png" };
            for (int i = 0; i < files.Length; i++)
            {
                stage.DebugEnter(i);
                yield return Frames(150);
                log.Append($"Area\t{i}\t{stage.CurrentName}\tlocked={stage.Current != null}\tenemies={game.Enemies.Count}\t" +
                           $"grid={BoardSlots.Width}x{BoardSlots.Height}\torigin={BoardSlots.Origin}\tking={game.Player.FlatPos}\n");
                foreach (var e in game.Enemies)
                    if (e != null) log.Append($"Piece\t{e.Type}\ttier={e.EliteTier}\tat={e.FlatPos}\tsquare={BoardSlots.ToSquare(e.FlatPos)}\n");
                yield return Shot(files[i]);
            }
            Finish();
        }

        void Finish()
        {
            File.WriteAllText(Path.Combine(dir, "stage.tsv"), log.ToString());
            Application.Quit();
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        IEnumerator Shot(string file)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, file));
            yield return null;
        }
    }
}

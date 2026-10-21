using System.Collections;
using System.IO;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Normal-mode look check: a player build of the NormalMode scene run with "-autoshot &lt;dir&gt;" captures the title
    /// and the first round's play view (the dark hall's light), then quits.
    /// </summary>
    public class HallShots : MonoBehaviour
    {
        string dir;

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
            for (int i = 0; i < 90; i++) yield return null;
            yield return Shot("70_Hall_Title.png");
            game.DebugStartGame();
            game.Player.GodMode = true;
            for (int i = 0; i < 150; i++) yield return null;
            yield return Shot("71_Hall_Play.png");
            game.DebugStartWave(9);
            for (int i = 0; i < 150; i++) yield return null;
            yield return Shot("72_Hall_Final_Round.png");
            Application.Quit();
        }

        IEnumerator Shot(string file)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, file));
            yield return null;
        }
    }
}

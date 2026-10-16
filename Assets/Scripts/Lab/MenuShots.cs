using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Main-menu check: a game build (NormalMode + EndlessMode) run with "-menushot &lt;dir&gt;" captures the title, moves
    /// the selection to the other mode, starts it (a scene switch), and records that the other mode's first round began
    /// without a second title. Then it picks the first mode again from there, and quits.
    /// </summary>
    public class MenuShots : MonoBehaviour
    {
        string dir;
        readonly StringBuilder log = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            string d = SoundRefCapture.GetArg("-menushot");
            if (d == null || FindFirstObjectByType<MenuShots>() != null) return;
            Directory.CreateDirectory(d);
            var go = new GameObject("MenuShots");
            DontDestroyOnLoad(go);
            go.AddComponent<MenuShots>().dir = d;
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

        IEnumerator Start()
        {
            Application.targetFrameRate = 60;
            yield return Frames(90);
            var g = GameManager.Instance;
            log.Append($"Boot\tmode={g.CurrentMode}\tstate={g.State}\tchoice={g.TitleChoice}\n");
            yield return Shot("40_Menu_Normal_Selected.png");

            g.TitleChoice = 1;   // as if → was pressed
            yield return Frames(10);
            yield return Shot("41_Menu_Endless_Selected.png");
            g.ChooseMode(1);
            yield return Frames(120);
            g = GameManager.Instance;
            log.Append($"AfterEndless\tmode={g.CurrentMode}\tstate={g.State}\tround={g.Round}\tenemies={g.Enemies.Count}\tstats_power={g.Player.Stats.attackPower}\n");
            yield return Shot("42_Endless_Started.png");

            // Back to the normal mode: through the title of the endless scene.
            UnityEngine.SceneManagement.SceneManager.LoadScene(GameManager.EndlessScene);
            yield return Frames(60);
            g = GameManager.Instance;
            log.Append($"EndlessTitle\tmode={g.CurrentMode}\tstate={g.State}\tchoice={g.TitleChoice}\n");
            g.ChooseMode(0);
            yield return Frames(120);
            g = GameManager.Instance;
            log.Append($"AfterNormal\tmode={g.CurrentMode}\tstate={g.State}\twave={g.WaveIndex + 1}/{g.WaveCount}\tenemies={g.Enemies.Count}\tstats_power={g.Player.Stats.attackPower}\n");
            yield return Shot("43_Normal_Started.png");

            File.WriteAllText(Path.Combine(dir, "menu.tsv"), log.ToString());
            Application.Quit();
        }
    }
}

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
            if (SoundRefCapture.GetArg("-boardcheck") != null)
            {
                // Read-only check of the online ranking: load it on the title, log it, nothing is submitted.
                for (int f = 0; f < 900 && (OnlineBoard.State == OnlineBoard.Status.Idle || OnlineBoard.State == OnlineBoard.Status.Loading); f++)
                    yield return null;
                yield return Frames(30);
                log.Append($"Board\tstate={OnlineBoard.State}\terror={OnlineBoard.Error}\tentries={OnlineBoard.Top.Count}\tmine={(OnlineBoard.Mine.HasValue ? OnlineBoard.Mine.Value.rank.ToString() : "-")}\n");
                foreach (var e in OnlineBoard.Top) log.Append($"Entry\t{e.rank}\t{e.name}\tLv{e.level}\t{BestRecord.FormatTime(e.time)}\n");
                yield return Shot("46_Title_Online_Board.png");
                File.WriteAllText(Path.Combine(dir, "board.tsv"), log.ToString());
                Application.Quit();
                yield break;
            }
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

            // Endless death: some fighting, then the King falls → results screen → confirm → this scene's title.
            g.Player.Press(PlayerController.Buffered.Light);
            yield return Frames(240);
            g.Player.GodMode = false;
            g.Player.TakeHit(9999f, 0f, g.Player.transform.position + Vector3.forward);
            yield return Frames(150);
            var rec = g.Record;
            log.Append($"Dead\tstate={g.State}\tround={g.Round}\tdealt={rec.damageDealt:0}\ttaken={rec.damageTaken:0}\tkills={rec.kills}\ttime={g.RunTime:0.0}\n");
            yield return Shot("44_Endless_Results.png");
            // Nickname + confirm: the run goes to the online ranking (offline / unlinked: skipped after a moment).
            g.SubmitRun("테스트킹");
            for (int f = 0; f < 600 && GameManager.Instance != null && GameManager.Instance.State == GameManager.GameState.Dead; f++) yield return null;
            yield return Frames(90);
            log.Append($"Online\tstate={OnlineBoard.State}\terror={OnlineBoard.Error}\tsaved_name={OnlineBoard.SavedName}\n");
            g = GameManager.Instance;
            log.Append($"AfterResults\tmode={g.CurrentMode}\tstate={g.State}\n");
            yield return Shot("45_Back_To_Title.png");

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

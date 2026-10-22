using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Normal-mode check: a game build run with "-roundshot &lt;dir&gt;" starts every one of the ten rounds in turn and
    /// captures it from above, logging each round's pieces (type, Dark tier, square, boss) to rounds.tsv; then, in the
    /// final round, a perfect dodge — the King must stay invincible through its slow motion and only then be hurt again.
    /// </summary>
    public class RoundShots : MonoBehaviour
    {
        string dir;
        GameManager game;
        readonly StringBuilder log = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            string d = SoundRefCapture.GetArg("-roundshot");
            var g = GameManager.Instance;
            if (d == null || g == null || g.CurrentMode != GameManager.Mode.Game) return;
            Directory.CreateDirectory(d);
            new GameObject("RoundShots").AddComponent<RoundShots>().dir = d;
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

        static string Label(EnemyBase e) =>
            $"{e.GetType().Name.Replace("Enemy", "")}{(e.EliteTier > 0 ? "/D" + e.EliteTier : "")}@{BoardSlots.Name(BoardSlots.ToSquare(e.FlatPos))}";

        IEnumerator Start()
        {
            game = GameManager.Instance;
            Application.targetFrameRate = 60;
            yield return Frames(60);
            // The title with the normal mode selected: its best clear and its online ranking.
            for (int f = 0; f < 600 && (OnlineBoard.Normal.State == OnlineBoard.Status.Idle || OnlineBoard.Normal.State == OnlineBoard.Status.Loading); f++)
                yield return null;
            yield return Shot("R00_Title.png");
            if (SoundRefCapture.GetArg("-boardonly") != null)
            {
                // Only the title's ranking (nothing played, nothing recorded or sent).
                log.Append($"Board\tnormal={OnlineBoard.Normal.State}\terror={OnlineBoard.Normal.Error}\tentries={OnlineBoard.Normal.Top.Count}" +
                           $"\tmine={(OnlineBoard.Normal.Mine.HasValue ? OnlineBoard.Normal.Mine.Value.rank.ToString() : "-")}\n");
                File.WriteAllText(Path.Combine(dir, "rounds.tsv"), log.ToString());
                Application.Quit();
                yield break;
            }
            log.Append($"Title\tchoice={game.TitleChoice}\tnormal_board={OnlineBoard.Normal.State}\terror={OnlineBoard.Normal.Error}\n");
            game.DebugStartGame();
            var player = game.Player;
            player.GodMode = true;
            yield return Frames(10);

            log.Append("round\tname\tboss_music\tpieces\n");
            for (int r = 0; r < game.WaveCount; r++)
            {
                game.DebugStartWave(r);
                player.GodMode = true;
                game.Rig.UseOverride = true;
                game.Rig.OverridePosition = new Vector3(0f, 19f, -13f);
                game.Rig.OverrideLookAt = new Vector3(0f, 0f, 1.5f);
                yield return Frames(75);   // the pieces have dropped in
                yield return Shot($"R{r + 1:00}.png");
                var sb = new StringBuilder();
                foreach (var e in game.Enemies)
                {
                    if (e == null) continue;
                    sb.Append($"{e.GetType().Name}{(e.EliteTier > 0 ? "/D" + e.EliteTier : "")}@{BoardSlots.Name(BoardSlots.ToSquare(e.FlatPos))}{(e.IsBoss ? $"(boss hp {e.MaxHealth:0})" : "")} ");
                }
                log.Append($"{r + 1}\t{game.WaveName}\t{game.Boss != null}\t{sb.ToString().TrimEnd()}\n");

                // Capture the pieces one by one (oldest first) and log every piece that comes in afterwards: the
                // round's reinforcements must arrive exactly as designed, and the round must end only after them.
                var seen = new System.Collections.Generic.HashSet<EnemyBase>(game.Enemies);
                var order = new StringBuilder();
                for (int kills = 0; kills < 16 && game.State == GameManager.GameState.Playing; kills++)
                {
                    EnemyBase target = null;
                    foreach (var e in game.Enemies) if (e != null && !e.IsDead && e.Targetable) { target = e; break; }
                    if (target == null) { yield return Frames(30); continue; }
                    order.Append($"x{Label(target)} ");
                    target.TakeHit(99999f, 0f, player.transform.position);
                    yield return Frames(45);   // the reinforcement drops in
                    foreach (var e in game.Enemies)
                        if (e != null && !e.IsDead && seen.Add(e)) order.Append($"+{Label(e)} ");
                }
                log.Append($"\tcaptures\t{order.ToString().TrimEnd()}\tthen={game.State}\n");
            }
            // The last capture won the run: the victory screen with the clear and the nickname for the ranking.
            yield return Frames(150);
            yield return Shot("R11_Victory.png");
            log.Append($"Victory\tstate={game.State}\tdeaths={game.Deaths}\ttime={game.RunTime:0.0}\tnew_best={game.NewBest}\tcan_submit={game.CanSubmitRun}\n");
            game.DebugStartWave(game.WaveCount - 1);   // back to the final round for the perfect-dodge check
            yield return Frames(60);

            // Perfect dodge in the final round: invincible through the slow motion, hurt again after it.
            game.Rig.UseOverride = false;
            player.GodMode = false;
            var boss = game.Boss;
            float hp = player.Health;
            player.DebugPerfectDodge(boss);
            bool guard0 = player.IsInvulnerable;
            bool hitDuring = player.TakeHit(30f, 10f, boss != null ? boss.transform.position : Vector3.zero);
            float realStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - realStart < 1.0f) yield return null;
            bool guard1 = player.IsInvulnerable;
            bool slowAt1 = game.SlowMoActive;
            while (Time.realtimeSinceStartup - realStart < 2.2f) yield return null;
            bool guard2 = player.IsInvulnerable;
            bool hitAfter = player.TakeHit(5f, 0f, boss != null ? boss.transform.position : Vector3.zero);
            log.Append($"PerfectDodge\tinvincible_at_0s={guard0}\thit_during={hitDuring}\tinvincible_at_1s={guard1}\tslowmo_at_1s={slowAt1}" +
                       $"\tinvincible_at_2.2s={guard2}\thit_after={hitAfter}\thp {hp:0}->{player.Health:0}\n");

            File.WriteAllText(Path.Combine(dir, "rounds.tsv"), log.ToString());
            yield return Frames(5);
            Application.Quit();
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Endless-mode check: a player build of the "teat" scene run with "-autoshot &lt;dir&gt;" logs the generated armies of
    /// rounds 1–60 (endless_rounds.tsv) and captures the empowered tiers, a dark round, a boss round, the grown board,
    /// the upgrade cards and the stats tab, then quits.
    /// </summary>
    public class EndlessShots : MonoBehaviour
    {
        string dir;
        GameManager game;
        readonly StringBuilder stats = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            string d = SoundRefCapture.GetArg("-autoshot");
            var g = GameManager.Instance;
            if (d == null || g == null || g.CurrentMode != GameManager.Mode.Endless) return;
            Directory.CreateDirectory(d);
            new GameObject("EndlessShots").AddComponent<EndlessShots>().dir = d;
        }

        IEnumerator Start()
        {
            game = GameManager.Instance;
            Application.targetFrameRate = 60;
            LogArmies();
            yield return null;
            game.DebugStartGame();
            game.Player.GodMode = true;
            yield return Frames(60);

            yield return EliteTiers();
            yield return RoundShot(11, "21_Round11_First_Dark_Pawn.png", closeOnElite: true);
            yield return RoundShot(29, "22_Round29_Half_Dark.png", closeOnElite: false);
            yield return RoundShot(20, "23_Round20_Dark_Queen.png", closeOnElite: true);
            yield return RoundShot(21, "24_Round21_Board_10x10.png", closeOnElite: false, top: true);
            yield return UpgradeShots();

            File.WriteAllText(Path.Combine(dir, "endless_stats.tsv"), stats.ToString());
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

        void LogArmies()
        {
            var sb = new StringBuilder("round\tboard\thp\tpieces\tdark\tshare\tT1\tT2\tT3\tpawn\tknight\tbishop\trook\tqueen\n");
            for (int r = 1; r <= 60; r++)
            {
                var army = EndlessRounds.Army(r);
                int[] tiers = new int[4];
                int[] types = new int[6];
                foreach (var s in army) { tiers[s.tier]++; types[(int)s.type]++; }
                int dark = army.Count - tiers[0];
                sb.Append($"{r}\t{EndlessRounds.BoardSize(r)}\t{EndlessRounds.HealthMultiplier(r):0.0}\t{army.Count}\t{dark}\t{(army.Count > 0 ? dark * 100 / army.Count : 0)}%\t")
                  .Append($"{tiers[1]}\t{tiers[2]}\t{tiers[3]}\t{types[0]}\t{types[1]}\t{types[2]}\t{types[3]}\t{types[4]}\n");
            }
            File.WriteAllText(Path.Combine(dir, "endless_rounds.tsv"), sb.ToString());
        }

        /// <summary>A pawn and a rook at each tier, side by side (normal, Dark I, II, III), plus their stats.</summary>
        IEnumerator EliteTiers()
        {
            game.ClearEnemies();
            var spawned = new List<EnemyBase>();
            for (int t = 0; t <= 3; t++)
            {
                float x = (t - 1.5f) * 2.6f;
                spawned.Add(game.SpawnEmpowered(PieceType.Pawn, t, new Vector3(x, 0f, 4f)));
                spawned.Add(game.SpawnEmpowered(PieceType.Rook, t, new Vector3(x, 0f, 6.5f)));
            }
            foreach (var e in spawned)
                stats.Append($"Tier\t{e.Type}\tT{e.EliteTier}\thp={e.MaxHealth:0}\t{e.DisplayName}\n");
            game.Player.transform.position = new Vector3(0f, 0f, -7f);
            game.Rig.UseOverride = true;
            game.Rig.OverridePosition = new Vector3(0f, 3.4f, -0.8f);
            game.Rig.OverrideLookAt = new Vector3(0f, 1f, 5f);
            yield return Frames(100);   // drop-in, then the auras build up
            // Freeze the moment (pause) without the pause screen, so the pieces stand still for the shot.
            var hud = game.GetComponent<Hud>();
            game.DebugTogglePause();
            if (hud != null) hud.enabled = false;
            foreach (var e in spawned)
            {
                int alive = 0, systems = 0;
                string shapes = "";
                foreach (var ps in e.Visual.GetComponentsInChildren<ParticleSystem>())
                {
                    systems++;
                    alive += ps.particleCount;
                    shapes += $"{ps.name}:{ps.shape.shapeType}/{(ps.shape.mesh != null ? ps.shape.mesh.vertexCount.ToString() : "-")} ";
                }
                var mf = e.Visual.GetComponentInChildren<MeshFilter>();
                stats.Append($"Aura\t{e.Type}\tT{e.EliteTier}\tsystems={systems}\tparticles={alive}\treadable={(mf != null && mf.sharedMesh.isReadable)}\t{shapes}\n");
            }
            yield return Shot("20_Dark_Tiers.png");
            if (hud != null) hud.enabled = true;
            game.DebugTogglePause();
            game.Rig.UseOverride = false;
            game.ClearEnemies();
        }

        IEnumerator RoundShot(int round, string file, bool closeOnElite, bool top = false)
        {
            game.DebugStartWave(round - 1);
            game.Player.GodMode = true;
            yield return Frames(80);
            int dark = 0;
            EnemyBase focus = null;
            foreach (var e in game.Enemies)
            {
                if (e == null) continue;
                if (e.EliteTier > 0) { dark++; if (focus == null || e.EliteTier > focus.EliteTier) focus = e; }
            }
            stats.Append($"Round\t{round}\tpieces={game.Enemies.Count}\tdark={dark}\tboard={BoardSlots.Size}\tbanner={game.Banner}\n");
            game.Rig.UseOverride = true;
            if (top)
            {
                float h = GameManager.BoardHalf;
                game.Rig.OverridePosition = new Vector3(0f, h * 2.1f, -h * 1.2f);
                game.Rig.OverrideLookAt = Vector3.zero;
            }
            else if (closeOnElite && focus != null)
            {
                Vector3 p = focus.FlatPos;
                game.Rig.OverridePosition = p + new Vector3(2.2f, 2.6f, -4.2f) * (focus.IsBoss ? 1.4f : 1f);
                game.Rig.OverrideLookAt = p + Vector3.up * (focus.IsBoss ? 1.6f : 0.9f);
            }
            else
            {
                game.Rig.OverridePosition = new Vector3(0f, 13f, -12f);
                game.Rig.OverrideLookAt = new Vector3(0f, 0f, 2f);
            }
            yield return Frames(3);
            yield return Shot(file);
            game.Rig.UseOverride = false;
        }

        /// <summary>Clears a round to bring up the cards, takes two, and opens the stats tab.</summary>
        IEnumerator UpgradeShots()
        {
            game.DebugStartWave(2);
            game.Player.GodMode = true;
            yield return Frames(60);
            var before = game.Player.Stats.Clone();
            foreach (var e in game.Enemies.ToArray())
                if (e != null) e.TakeHit(99999f, 0f, game.Player.transform.position);
            for (int f = 0; f < 600 && game.State != GameManager.GameState.Upgrade; f++) yield return null;
            yield return Frames(30);
            yield return Shot("25_Upgrade_Cards.png");
            var offered = new StringBuilder();
            foreach (var o in game.Offers) offered.Append($"{o.Title} {o.AmountText}; ");
            stats.Append($"Offers\t{offered}\n");
            game.OfferCursor = 2;
            game.PickUpgrade(0);
            yield return Frames(10);
            yield return Shot("26_Upgrade_One_Taken.png");
            game.PickUpgrade(2);
            for (int f = 0; f < 200 && game.State != GameManager.GameState.Playing; f++) yield return null;
            var after = game.Player.Stats;
            stats.Append($"Upgraded\tround={game.Round}\tpower {before.attackPower:0.00}->{after.attackPower:0.00}\thp {before.maxHealth}->{after.maxHealth}\t" +
                         $"def {before.defense}->{after.defense}\tflasks {before.maxFlasks}->{after.maxFlasks}\trolls {before.maxDodges}->{after.maxDodges}\n");
            yield return Frames(30);
            game.DebugTogglePause();
            var hud = game.GetComponent<Hud>();
            if (hud != null) hud.PauseTab = 1;
            yield return Frames(3);
            yield return Shot("27_Pause_Stats.png");
            game.DebugTogglePause();
        }
    }
}

using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KingLastPiece
{
    /// <summary>
    /// Test scene controller (Scenes/AttackLab.unity) for comparing the King's body-attack styles
    /// against training dummies placed all around him.
    /// Keys: 1-6 switch style, C reset dummies, G spawn hostile pawns.
    /// Run a player build with "-autoshot &lt;dir&gt;" to capture contact sheets + stats for every style.
    /// </summary>
    public class AttackLab : MonoBehaviour
    {
        public static AttackLab Instance { get; private set; }

        public int StyleIndex { get; private set; } = AttackStyles.DefaultIndex;
        public AttackStyle Style => AttackStyles.All[StyleIndex];

        static readonly Vector3 PlayerStart = new Vector3(0f, 0f, -3f);

        // Front, front-left/right (~40°), sides (90°), behind — shows each style's coverage.
        static readonly Vector3[] DummySlots =
        {
            new Vector3(0f, 0f, -0.5f),
            new Vector3(-1.9f, 0f, -0.9f), new Vector3(1.9f, 0f, -0.9f),
            new Vector3(-2.4f, 0f, -3f), new Vector3(2.4f, 0f, -3f),
            new Vector3(0f, 0f, -5.4f),
        };

        readonly TrainingDummy[] dummies = new TrainingDummy[DummySlots.Length];
        readonly float[] respawnTimers = new float[DummySlots.Length];
        GameManager game;
        bool automated;

        static readonly Key[] DigitKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };

        void Awake()
        {
            Instance = this;
            GameManager.StartMode = GameManager.Mode.Lab;
            game = new GameObject("GameManager").AddComponent<GameManager>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            SelectStyle(StyleIndex);
            ResetLab();

            string dir = GetArg("-autoshot");
            if (dir != null) StartCoroutine(AutoShot(dir));
        }

        public void SelectStyle(int index)
        {
            StyleIndex = Mathf.Clamp(index, 0, AttackStyles.All.Length - 1);
            game.Player.Style = Style;
        }

        void ResetLab()
        {
            game.ClearEnemies();
            game.Player.ResetForWave(PlayerStart, 0f);
            game.Rig.Snap();
            for (int i = 0; i < DummySlots.Length; i++) SpawnDummy(i);
        }

        void SpawnDummy(int slot)
        {
            dummies[slot] = game.SpawnCustom<TrainingDummy>(PieceType.Pawn, DummySlots[slot]);
            respawnTimers[slot] = 0f;
        }

        void Update()
        {
            if (automated || game == null) return;

            var kb = Keyboard.current;
            if (kb != null)
            {
                for (int i = 0; i < AttackStyles.All.Length && i < DigitKeys.Length; i++)
                    if (kb[DigitKeys[i]].wasPressedThisFrame) SelectStyle(i);
                if (kb.cKey.wasPressedThisFrame) ResetLab();
                if (kb.gKey.wasPressedThisFrame)
                    foreach (var sq in new[] { "c7", "e7", "g7" }) game.SpawnEnemy(PieceType.Pawn, GameManager.SquareToWorld(sq));
            }

            if (game.Player.Health < game.Player.MaxHealth * 0.3f) game.Player.RefillForLab();

            for (int i = 0; i < dummies.Length; i++)
            {
                if (dummies[i] != null && !dummies[i].IsDead) continue;
                respawnTimers[i] += Time.deltaTime;
                if (respawnTimers[i] > 2.5f) SpawnDummy(i);
            }
        }

        // ---------------------------------------------------------------- automated capture

        static string GetArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        IEnumerator AutoShot(string dir)
        {
            automated = true;
            Directory.CreateDirectory(dir);
            Time.captureDeltaTime = 1f / 60f;
            game.HitStopEnabled = false;
            game.Player.GodMode = true;
            var hud = game.GetComponent<Hud>();
            if (hud != null) hud.enabled = false;

            game.Rig.UseOverride = true;
            game.Rig.OverridePosition = new Vector3(4.0f, 3.6f, -7.0f);
            game.Rig.OverrideLookAt = new Vector3(0f, 0.9f, -2.2f);

            var stats = new StringBuilder();
            stats.AppendLine("style\tattack\tstartup\tactive_end\tduration\tcancel\treach\tcoverage\tdamage\tdummies_hit");

            for (int s = 0; s < AttackStyles.All.Length; s++)
            {
                SelectStyle(s);
                var style = Style;
                Texture2D sheet = null;
                int tileW = 0, tileH = 0;
                const int cols = 5;
                int lightRows = Mathf.Clamp(style.lights.Length, 2, 3);
                int rows = lightRows + 1;

                for (int row = 0; row < rows; row++)
                {
                    ResetLab();
                    for (int f = 0; f < 50; f++) yield return null;

                    var player = game.Player;
                    AttackDef target;
                    if (row == rows - 1)
                    {
                        target = style.heavy;
                        player.Press(PlayerController.Buffered.Heavy);
                    }
                    else
                    {
                        player.Press(PlayerController.Buffered.Light);
                        yield return null;
                        for (int chain = 0; chain < row; chain++)
                        {
                            // Chain into the next combo hit at the earliest cancel point.
                            var prev = player.CurrentAttack;
                            while (player.StateTime < prev.cancelAt) yield return null;
                            player.Press(PlayerController.Buffered.Light);
                            yield return null;
                        }
                        target = player.CurrentAttack;
                    }
                    yield return null;

                    int hitsBefore = game.DamageEvents;
                    float[] samples =
                    {
                        target.FirstHit * 0.6f, target.FirstHit, (target.FirstHit + target.LastHit) * 0.5f,
                        target.LastHit, Mathf.Min(target.duration - 0.02f, target.LastHit + 0.14f),
                    };

                    for (int c = 0; c < cols; c++)
                    {
                        while (player.CurrentAttack == target && player.StateTime < samples[c]) yield return null;
                        yield return new WaitForEndOfFrame();
                        var shot = ScreenCapture.CaptureScreenshotAsTexture();
                        if (sheet == null)
                        {
                            tileW = shot.width;
                            tileH = shot.height;
                            sheet = new Texture2D(tileW * cols, tileH * rows, TextureFormat.RGB24, false);
                        }
                        if (shot.width == tileW && shot.height == tileH)
                            sheet.SetPixels32(c * tileW, (rows - 1 - row) * tileH, tileW, tileH, shot.GetPixels32());
                        Destroy(shot);
                    }

                    while (player.CurrentAttack == target) yield return null;
                    int hits = game.DamageEvents - hitsBefore;
                    stats.AppendLine($"{style.name}\t{target.name}\t{target.FirstHit:0.00}\t{target.LastHit:0.00}\t{target.duration:0.00}\t" +
                                     $"{target.cancelAt:0.00}\t{target.Reach:0.0}\t{target.Coverage:0}\t{target.TotalDamage:0}\t{hits}");
                }

                stats.AppendLine($"{style.name}\tCOMBO_DPS\t{style.ComboDps:0.0}");
                if (sheet != null)
                {
                    sheet.Apply();
                    File.WriteAllBytes(Path.Combine(dir, $"{s + 1}_{style.name.Replace(' ', '_')}.png"), sheet.EncodeToPNG());
                    Destroy(sheet);
                }
            }

            File.WriteAllText(Path.Combine(dir, "stats.tsv"), stats.ToString());
            Application.Quit();
        }
    }
}

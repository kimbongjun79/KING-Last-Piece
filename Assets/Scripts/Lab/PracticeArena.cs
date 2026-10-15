using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KingLastPiece
{
    /// <summary>
    /// Practice arena (Scenes/Practice.unity): the final combat kit (Royal Ensemble) against training dummies,
    /// plus real pieces on demand. Keys: 1-5 spawn Pawn/Knight/Bishop/Rook/Queen, X clear pieces, C reset,
    /// G invincible, I infinite stamina, H hit-stop.
    /// A player build run with "-autoshot &lt;dir&gt;" captures regression sheets + stats (styles, dodge, stagger…).
    /// </summary>
    public class PracticeArena : MonoBehaviour
    {
        public static PracticeArena Instance { get; private set; }

        public int StyleIndex { get; private set; } = AttackStyles.DefaultIndex;
        public bool Invincible { get; private set; }
        public bool InfiniteStamina { get; private set; } = true;

        static readonly PieceType[] SpawnKeys = { PieceType.Pawn, PieceType.Knight, PieceType.Bishop, PieceType.Rook, PieceType.Queen };
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
            // The scene has a baked GameManager (set to Practice); older scenes get one created here.
            GameManager.StartMode = GameManager.Mode.Practice;
            game = FindFirstObjectByType<GameManager>();
            if (game == null) game = new GameObject("GameManager").AddComponent<GameManager>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            game.Player.InfiniteStamina = true;
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

        /// <summary>V: show the board slots (attack squares red, waiting squares grey, the King's square gold).</summary>
        public bool SlotViewOn { get { var v = GetComponent<SlotDebugView>(); return v != null && v.enabled; } }

        void ToggleSlotView()
        {
            var view = GetComponent<SlotDebugView>();
            if (view == null) gameObject.AddComponent<SlotDebugView>();
            else view.enabled = !view.enabled;
        }

        void ResetLab()
        {
            game.ClearEnemies();
            game.Player.ResetForWave(PlayerStart, 0f);
            game.Rig.Snap();
            for (int i = 0; i < DummySlots.Length; i++) SpawnDummy(i);
        }

        /// <summary>Drops a hostile piece on a free square of the 7th/8th rank, facing the King.</summary>
        void SpawnPiece(PieceType type)
        {
            string rank = type == PieceType.Pawn ? "7" : "8";
            string file = "abcdefgh".Substring(Random.Range(1, 7), 1);
            game.SpawnEnemy(type, GameManager.SquareToWorld(file + rank));
        }

        /// <summary>Removes every hostile piece but keeps the training dummies.</summary>
        void ClearPieces()
        {
            foreach (var e in game.Enemies.ToArray())
                if (e != null && !(e is TrainingDummy)) Destroy(e.gameObject);
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
                for (int i = 0; i < SpawnKeys.Length; i++)
                    if (kb[DigitKeys[i]].wasPressedThisFrame) SpawnPiece(SpawnKeys[i]);
                if (kb.xKey.wasPressedThisFrame) ClearPieces();
                if (kb.cKey.wasPressedThisFrame) ResetLab();
                if (kb.hKey.wasPressedThisFrame) game.HitStopEnabled = !game.HitStopEnabled;
                if (kb.gKey.wasPressedThisFrame) game.Player.GodMode = Invincible = !Invincible;
                if (kb.iKey.wasPressedThisFrame) game.Player.InfiniteStamina = InfiniteStamina = !InfiniteStamina;
                if (kb.vKey.wasPressedThisFrame) ToggleSlotView();
                if (kb.xKey.wasPressedThisFrame || kb.cKey.wasPressedThisFrame || kb.hKey.wasPressedThisFrame ||
                    kb.gKey.wasPressedThisFrame || kb.iKey.wasPressedThisFrame) Sfx.Play("UI_Confirm");
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

        /// <summary>
        /// Automated perfect dodge → Royal Throw: a rook attacks, the King rolls just before it connects,
        /// then counters. Captures a 5×2 sheet (slow-mo, grab, swing, release, flight) and logs the outcome.
        /// </summary>
        IEnumerator JustDodgeScenario(string dir, StringBuilder stats)
        {
            var player = game.Player;
            var hud = game.GetComponent<Hud>();
            if (hud != null) hud.enabled = true;
            SelectStyle(AttackStyles.DefaultIndex);
            game.ClearEnemies();
            player.ResetForWave(PlayerStart, 0f);
            var attacker = game.SpawnEnemy(PieceType.Rook, PlayerStart + new Vector3(0f, 0f, 2.0f));
            game.SpawnCustom<TrainingDummy>(PieceType.Pawn, PlayerStart + new Vector3(-2.2f, 0f, -0.6f));
            game.SpawnCustom<TrainingDummy>(PieceType.Pawn, PlayerStart + new Vector3(2.2f, 0f, -0.6f));
            int justBefore = player.JustDodgeCount, throwsBefore = player.ThrowCount, hitsBefore = game.DamageEvents;

            // Roll ~0.12s before the attack would connect.
            var rookLine = new StringBuilder();
            for (int f = 0; f < 600 && attacker != null && attacker.TimeToImpact > 0.12f; f++)
            {
                if (f % 6 == 0) rookLine.Append(attacker.StateCode);
                yield return null;
            }
            player.Press(PlayerController.Buffered.Dodge);
            for (int f = 0; f < 60 && !player.CounterReady; f++) yield return null;
            bool perfect = player.CounterReady;

            const int cols = 5, rows = 2;
            Texture2D sheet = null;
            int tileW = 0, tileH = 0, shot = 0;
            // Frames to wait (at the fixed 60 fps capture step) before each capture.
            int[] waits = { 0, 12, 0, 8, 10, 10, 8, 6, 14, 24 };
            for (int i = 0; i < waits.Length; i++)
            {
                for (int f = 0; f < waits[i]; f++) yield return null;
                if (i == 2) { player.Press(PlayerController.Buffered.Light); yield return null; yield return null; }
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (sheet == null)
                {
                    tileW = tex.width;
                    tileH = tex.height;
                    sheet = new Texture2D(tileW * cols, tileH * rows, TextureFormat.RGB24, false);
                }
                int c = shot % cols, r = shot / cols;
                if (tex.width == tileW && tex.height == tileH)
                    sheet.SetPixels32(c * tileW, (rows - 1 - r) * tileH, tileW, tileH, tex.GetPixels32());
                Destroy(tex);
                shot++;
            }

            stats.AppendLine($"JustDodge\tperfect={perfect}\tjust_count={player.JustDodgeCount - justBefore}\t" +
                             $"throws={player.ThrowCount - throwsBefore}\tdamage_events={game.DamageEvents - hitsBefore}\t" +
                             $"rook_dead={(attacker == null || attacker.IsDead)}\trook={rookLine}");
            if (sheet != null)
            {
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(dir, "7_Perfect_Dodge_Royal_Throw.png"), sheet.EncodeToPNG());
                Destroy(sheet);
            }
        }

        /// <summary>All piece models side by side, turned toward the King: front, side and close-up views.</summary>
        IEnumerator PieceLineup(string dir)
        {
            var hud = game.GetComponent<Hud>();
            if (hud != null) hud.enabled = false;
            game.ClearEnemies();
            var player = game.Player;
            player.ResetForWave(new Vector3(0f, 0f, -4f), 0f);
            var types = new[] { PieceType.Pawn, PieceType.Knight, PieceType.Bishop, PieceType.Rook, PieceType.Queen };
            for (int i = 0; i < types.Length; i++)
                game.SpawnCustom<TrainingDummy>(types[i], new Vector3((i - 2) * 1.8f, 0f, 0f));
            for (int f = 0; f < 150; f++) yield return null;  // land + turn toward the King

            var views = new[]
            {
                (new Vector3(0f, 2.2f, -8.5f), new Vector3(0f, 1.1f, 0f)),   // front (from behind the King)
                (new Vector3(9f, 1.8f, 0.5f), new Vector3(0f, 1.0f, 0f)),     // side
                (new Vector3(-1.5f, 1.8f, -3.2f), new Vector3(-0.9f, 1.2f, 0f)), // close-up pawn / knight
            };
            Texture2D sheet = null;
            int tileW = 0, tileH = 0;
            for (int v = 0; v < views.Length; v++)
            {
                game.Rig.OverridePosition = views[v].Item1;
                game.Rig.OverrideLookAt = views[v].Item2;
                yield return null;
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (sheet == null)
                {
                    tileW = tex.width;
                    tileH = tex.height;
                    sheet = new Texture2D(tileW * views.Length, tileH, TextureFormat.RGB24, false);
                }
                if (tex.width == tileW && tex.height == tileH) sheet.SetPixels32(v * tileW, 0, tileW, tileH, tex.GetPixels32());
                Destroy(tex);
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(dir, "8_Piece_Models.png"), sheet.EncodeToPNG());
            Destroy(sheet);
        }

        /// <summary>
        /// Stagger system check: hold light attack on a real pawn and log its state every 6 frames
        /// (m move, w/a attack, r recover, s flinch, U airborne, g get-up, x dead; uppercase-V marks Vulnerable).
        /// Captures a 5×2 sheet of the sequence.
        /// </summary>
        IEnumerator StaggerScenario(string dir, StringBuilder stats)
        {
            var hud = game.GetComponent<Hud>();
            if (hud != null) hud.enabled = false;
            game.ClearEnemies();
            var player = game.Player;
            player.ResetForWave(PlayerStart, 0f);
            var pawn = game.SpawnEnemy(PieceType.Pawn, PlayerStart + new Vector3(0f, 0f, 1.8f));
            game.Rig.OverridePosition = PlayerStart + new Vector3(4.5f, 2.6f, 1.2f);
            game.Rig.OverrideLookAt = PlayerStart + new Vector3(0f, 1.2f, 2.2f);
            for (int f = 0; f < 50; f++) yield return null;
            player.LockOn(pawn);

            var timeline = new StringBuilder();
            const int cols = 5, rows = 2, frames = 300;
            Texture2D sheet = null;
            int tileW = 0, tileH = 0, shot = 0;
            float startHp = pawn.Health;
            int launches = 0, airFrames = 0;
            float maxAir = 0f;
            bool wasAir = false;
            for (int f = 0; f < frames && pawn != null; f++)
            {
                player.Press(PlayerController.Buffered.Light);  // like holding LMB
                yield return null;
                if (pawn == null) break;
                if (pawn.IsAirborne && !wasAir) launches++;
                wasAir = pawn.IsAirborne;
                if (pawn.IsAirborne) airFrames++;
                maxAir = Mathf.Max(maxAir, pawn.AirHeight);
                if (f % 6 == 0) timeline.Append(pawn.IsAirborne ? 'U' : pawn.IsVulnerable ? 'V' : pawn.StateCode);
                if (f % 30 == 15 && shot < cols * rows)
                {
                    yield return new WaitForEndOfFrame();
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    if (sheet == null)
                    {
                        tileW = tex.width;
                        tileH = tex.height;
                        sheet = new Texture2D(tileW * cols, tileH * rows, TextureFormat.RGB24, false);
                    }
                    int c = shot % cols, r = shot / cols;
                    if (tex.width == tileW && tex.height == tileH)
                        sheet.SetPixels32(c * tileW, (rows - 1 - r) * tileH, tileW, tileH, tex.GetPixels32());
                    Destroy(tex);
                    shot++;
                }
                if (pawn.IsDead) break;
            }

            stats.AppendLine($"Stagger\tpawn_hp={startHp:0}\tlaunches={launches}\tair_time={airFrames / 60f:0.00}s\tmax_height={maxAir:0.00}\tdead={(pawn == null || pawn.IsDead)}\ttimeline={timeline}");
            if (sheet != null)
            {
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(dir, "9_Stagger_System.png"), sheet.EncodeToPNG());
                Destroy(sheet);
            }
        }

        /// <summary>
        /// Auto-dodge: a bishop / rook dashes at the King and he rolls with NO direction input — the roll should be
        /// perpendicular to the dash and take no damage. Auto-target: with no lock-on, a light attack should turn to
        /// a dummy at the King's side, but ignore one directly behind him.
        /// </summary>
        IEnumerator AutoAssistScenario(string dir, StringBuilder stats)
        {
            var hud = game.GetComponent<Hud>();
            if (hud != null) hud.enabled = false;
            var player = game.Player;
            Texture2D sheet = null;
            int tileW = 0, tileH = 0, shot = 0;
            const int cols = 5, rows = 2;

            var cases = new[]
            {
                (PieceType.Bishop, new Vector3(-4.2f, 0f, 4.2f)),
                (PieceType.Rook, new Vector3(0f, 0f, 6.5f)),
            };
            for (int c = 0; c < cases.Length; c++)
            {
                game.ClearEnemies();
                player.ResetForWave(PlayerStart, 0f);
                player.GodMode = false;
                var enemy = game.SpawnEnemy(cases[c].Item1, PlayerStart + cases[c].Item2);
                game.Rig.OverridePosition = PlayerStart + new Vector3(7f, 6f, -3f);
                game.Rig.OverrideLookAt = PlayerStart + new Vector3(0f, 0.5f, 1.5f);

                bool dashing = false;
                for (int f = 0; f < 900 && enemy != null; f++)
                {
                    yield return null;
                    if (enemy.TryGetThreat(player.FlatPos, player.Radius, out _, out var line, out var tti) && line && tti < 0.3f)
                    {
                        dashing = true;
                        break;
                    }
                }
                float hpBefore = player.Health;
                Vector3 atkDir = enemy != null ? enemy.AttackDirection : Vector3.zero;
                player.Press(PlayerController.Buffered.Dodge);  // no movement input
                for (int i = 0; i < cols; i++)
                {
                    for (int f = 0; f < 7; f++) yield return null;
                    yield return new WaitForEndOfFrame();
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    if (sheet == null)
                    {
                        tileW = tex.width;
                        tileH = tex.height;
                        sheet = new Texture2D(tileW * cols, tileH * rows, TextureFormat.RGB24, false);
                    }
                    if (tex.width == tileW && tex.height == tileH)
                        sheet.SetPixels32(i * tileW, (rows - 1 - c) * tileH, tileW, tileH, tex.GetPixels32());
                    Destroy(tex);
                    shot++;
                }
                for (int f = 0; f < 40; f++) yield return null;
                float angle = Vector3.Angle(atkDir, player.LastDodgeDirection);
                stats.AppendLine($"AutoDodge\t{cases[c].Item1}\tdashing={dashing}\troll_vs_attack_angle={angle:0}\tdamage_taken={hpBefore - player.Health:0}");
            }
            player.GodMode = true;

            // Auto-aim. King at PlayerStart facing +Z, no lock-on, no input.
            // 1) Cluster of three to the front-right vs. one closer piece to the left: should face the cluster.
            game.ClearEnemies();
            player.ResetForWave(PlayerStart, 0f);
            var cluster = new[] { 50f, 72f, 94f };
            foreach (var a in cluster)
                game.SpawnCustom<TrainingDummy>(PieceType.Pawn, PlayerStart + Quaternion.Euler(0f, a, 0f) * Vector3.forward * 2.2f);
            game.SpawnCustom<TrainingDummy>(PieceType.Pawn, PlayerStart + Quaternion.Euler(0f, -70f, 0f) * Vector3.forward * 1.7f);
            for (int f = 0; f < 50; f++) yield return null;
            int before = game.DamageEvents;
            player.Press(PlayerController.Buffered.Light);
            for (int f = 0; f < 8; f++) yield return null;
            float facing = Vector3.SignedAngle(Vector3.forward, player.transform.forward, Vector3.up);
            for (int f = 0; f < 32; f++) yield return null;
            int clusterHits = game.DamageEvents - before;

            // 2) A single close piece directly behind: close enemies are always faced.
            game.ClearEnemies();
            player.ResetForWave(PlayerStart, 0f);
            var behind = game.SpawnCustom<TrainingDummy>(PieceType.Pawn, PlayerStart + new Vector3(0f, 0f, -2.2f));
            for (int f = 0; f < 50; f++) yield return null;
            player.Press(PlayerController.Buffered.Light);
            for (int f = 0; f < 8; f++) yield return null;
            float behindAngle = Vector3.Angle(player.transform.forward, behind.FlatPos - player.FlatPos);

            // 3) A far piece (6 units, to the side): no assist.
            game.ClearEnemies();
            player.ResetForWave(PlayerStart, 0f);
            var far = game.SpawnCustom<TrainingDummy>(PieceType.Pawn, PlayerStart + new Vector3(6f, 0f, 0f));
            for (int f = 0; f < 50; f++) yield return null;
            player.Press(PlayerController.Buffered.Light);
            for (int f = 0; f < 8; f++) yield return null;
            float farAngle = Vector3.Angle(player.transform.forward, far.FlatPos - player.FlatPos);

            // 4) Input wins: a close piece behind, but the player steers right — the attack goes right.
            game.ClearEnemies();
            player.ResetForWave(PlayerStart, 0f);
            game.SpawnCustom<TrainingDummy>(PieceType.Pawn, PlayerStart + new Vector3(0f, 0f, -2.2f));
            for (int f = 0; f < 50; f++) yield return null;
            player.DebugMove = Vector3.right;
            player.Press(PlayerController.Buffered.Light);
            for (int f = 0; f < 8; f++) yield return null;
            float inputAngle = Vector3.Angle(player.transform.forward, Vector3.right);
            player.DebugMove = Vector3.zero;
            stats.AppendLine($"AutoAim\tcluster_facing={facing:0}deg(cluster 50-94, lone -70)\tcluster_hits={clusterHits}" +
                             $"\tbehind_facing_err={behindAngle:0}\tfar_facing_err={farAngle:0}(should stay ~90)\tinput_facing_err={inputAngle:0}");

            // Dodge cancel: during a light wind-up, and right after the heavy's first hit (long before its old cancel point).
            game.ClearEnemies();
            player.ResetForWave(PlayerStart, 0f);
            for (int f = 0; f < 10; f++) yield return null;
            player.Press(PlayerController.Buffered.Light);
            for (int f = 0; f < 4; f++) yield return null;
            player.Press(PlayerController.Buffered.Dodge);
            for (int f = 0; f < 2; f++) yield return null;
            bool cancelWindup = player.IsDodging;
            for (int f = 0; f < 60; f++) yield return null;
            player.Press(PlayerController.Buffered.Heavy);
            yield return null;
            var heavy = player.CurrentAttack;
            while (player.CurrentAttack == heavy && player.StateTime < heavy.hits[0].end + 0.03f) yield return null;
            float pressedAt = player.StateTime;
            player.Press(PlayerController.Buffered.Dodge);
            for (int f = 0; f < 2; f++) yield return null;
            stats.AppendLine($"DodgeCancel\tduring_light_windup={cancelWindup}\tafter_heavy_hit={player.IsDodging}" +
                             $"\tat={pressedAt:0.00}s_vs_old_cancel={heavy.cancelAt:0.00}s");

            if (sheet != null)
            {
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(dir, "10_Auto_Dodge.png"), sheet.EncodeToPNG());
                Destroy(sheet);
            }
        }

        /// <summary>Death crumble: a pawn, rook and queen are captured at once; 5×2 frames over ~3s.</summary>
        IEnumerator CrumbleScenario(string dir)
        {
            var hud = game.GetComponent<Hud>();
            if (hud != null) hud.enabled = false;
            game.ClearEnemies();
            var player = game.Player;
            player.ResetForWave(new Vector3(0f, 0f, -5f), 0f);
            var victims = new[]
            {
                game.SpawnEnemy(PieceType.Pawn, new Vector3(-2.2f, 0f, 0f)),
                game.SpawnEnemy(PieceType.Rook, new Vector3(0f, 0f, 0f)),
                game.SpawnEnemy(PieceType.Queen, new Vector3(2.6f, 0f, 0f)),
            };
            game.Rig.OverridePosition = new Vector3(0.5f, 2.6f, -6.5f);
            game.Rig.OverrideLookAt = new Vector3(0.2f, 1.1f, 0f);
            for (int f = 0; f < 60; f++)
            {
                yield return null;
                if (f == 40)
                {
                    // Spawn landing: the dark ripple under each piece.
                    yield return new WaitForEndOfFrame();
                    var spawnTex = ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(Path.Combine(dir, "13_Spawn_Wave.png"), spawnTex.EncodeToPNG());
                    Destroy(spawnTex);
                }
            }
            foreach (var v in victims) v.TakeHit(99999f, 0f, player.transform.position);

            const int cols = 5, rows = 2;
            int[] waits = { 4, 6, 8, 10, 14, 18, 24, 30, 36, 40 };
            Texture2D sheet = null;
            int tileW = 0, tileH = 0;
            for (int i = 0; i < waits.Length; i++)
            {
                for (int f = 0; f < waits[i]; f++) yield return null;
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (sheet == null)
                {
                    tileW = tex.width;
                    tileH = tex.height;
                    sheet = new Texture2D(tileW * cols, tileH * rows, TextureFormat.RGB24, false);
                }
                if (tex.width == tileW && tex.height == tileH)
                    sheet.SetPixels32((i % cols) * tileW, (rows - 1 - i / cols) * tileH, tileW, tileH, tex.GetPixels32());
                Destroy(tex);
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(dir, "11_Death_Crumble.png"), sheet.EncodeToPNG());
            Destroy(sheet);
        }

        /// <summary>Mid-range opener: a dummy 5 units ahead; holding light should dash in, then run hits 1-4.</summary>
        IEnumerator GapCloserScenario(string dir, StringBuilder stats)
        {
            var hud = game.GetComponent<Hud>();
            if (hud != null) hud.enabled = false;
            game.ClearEnemies();
            var player = game.Player;
            player.ResetForWave(PlayerStart, 0f);
            game.SpawnCustom<TrainingDummy>(PieceType.Pawn, PlayerStart + new Vector3(0f, 0f, 5f));
            game.Rig.OverridePosition = PlayerStart + new Vector3(5.5f, 3.2f, 1.5f);
            game.Rig.OverrideLookAt = PlayerStart + new Vector3(0f, 0.8f, 3f);
            for (int f = 0; f < 50; f++) yield return null;

            var seq = new StringBuilder();
            AttackDef last = null;
            int before = game.DamageEvents;
            const int cols = 5, rows = 2;
            Texture2D sheet = null;
            int tileW = 0, tileH = 0, shot = 0;
            for (int f = 0; f < 200; f++)
            {
                player.Press(PlayerController.Buffered.Light);  // like holding LMB
                yield return null;
                var cur = player.CurrentAttack;
                if (cur != null && cur != last) { seq.Append(seq.Length > 0 ? " > " : "").Append(cur.name); last = cur; }
                if (f % 18 == 4 && shot < cols * rows)
                {
                    yield return new WaitForEndOfFrame();
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    if (sheet == null)
                    {
                        tileW = tex.width;
                        tileH = tex.height;
                        sheet = new Texture2D(tileW * cols, tileH * rows, TextureFormat.RGB24, false);
                    }
                    if (tex.width == tileW && tex.height == tileH)
                        sheet.SetPixels32((shot % cols) * tileW, (rows - 1 - shot / cols) * tileH, tileW, tileH, tex.GetPixels32());
                    Destroy(tex);
                    shot++;
                }
            }
            stats.AppendLine($"GapCloser	sequence={seq}	hits={game.DamageEvents - before}");
            if (sheet != null)
            {
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(dir, "12_Gap_Closer.png"), sheet.EncodeToPNG());
                Destroy(sheet);
            }
        }

        static string GetArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        /// <summary>
        /// Board slots: the final round's army against a still King (invincible, on e4, moving to c3 halfway).
        /// Logs every attack with the square it started from relative to the King's square, checks it against the
        /// piece's move pattern, and captures a top-down 5×2 sheet with the slot view on.
        /// </summary>
        IEnumerator SlotScenario(string dir, StringBuilder stats, bool detour = true)
        {
            EnemyBase.DetourAroundKing = detour;
            var hud = game.GetComponent<Hud>();
            if (hud != null) hud.enabled = false;
            game.ClearEnemies();
            var player = game.Player;
            player.ResetForWave(GameManager.SquareToWorld("e4"), 0f);
            player.GodMode = true;
            (PieceType, string)[] army =
            {
                (PieceType.Pawn, "b7"), (PieceType.Pawn, "c7"), (PieceType.Pawn, "d7"), (PieceType.Pawn, "e7"),
                (PieceType.Pawn, "f7"), (PieceType.Pawn, "g7"), (PieceType.Knight, "b8"), (PieceType.Queen, "d8"),
                (PieceType.Bishop, "f8"), (PieceType.Rook, "h8"),
            };
            foreach (var (type, sq) in army) game.SpawnEnemy(type, GameManager.SquareToWorld(sq));
            var view = gameObject.AddComponent<SlotDebugView>();
            game.Rig.OverridePosition = new Vector3(0f, 15.5f, -8.5f);
            game.Rig.OverrideLookAt = new Vector3(0f, 0f, -0.5f);

            var attacking = new Dictionary<EnemyBase, bool>();
            var byType = new Dictionary<PieceType, (int ok, int bad)>();
            var bad = new StringBuilder();
            int maxWaiting = 0;
            int pawnThroughKing = 0;   // frames a walking pawn is right on top of the King (should stay ~0)
            var pawnDist = new Dictionary<EnemyBase, float>();
            const int cols = 5, rows = 2, frames = 900;
            Texture2D sheet = null;
            int tileW = 0, tileH = 0, shot = 0;
            for (int f = 0; f < frames; f++)
            {
                if (f == frames / 2) player.ResetForWave(GameManager.SquareToWorld("c3"), 0f);
                yield return null;

                int waiting = 0;
                foreach (var e in game.Enemies)
                {
                    if (e == null || e.IsDead) continue;
                    if (!e.HoldsAttackSlot && BoardSlots.TryGet(e, out _)) waiting++;
                    if (e.Type == PieceType.Pawn && e.StateCode == 'm')
                    {
                        // Walking INTO the King (closer than 1.2 m and getting closer) — leaving after a thrust doesn't count.
                        float dk = (e.FlatPos - player.FlatPos).magnitude;
                        if (pawnDist.TryGetValue(e, out float prev) && dk < 1.2f && dk < prev - 0.001f) pawnThroughKing++;
                        pawnDist[e] = dk;
                    }
                    bool now = e.IsAttacking;
                    attacking.TryGetValue(e, out bool was);
                    attacking[e] = now;
                    if (!now || was || e.WindupProgress > 0.2f || (f >= frames / 2 && f < frames / 2 + 3)) continue;   // skip the teleport frames
                    var off = BoardSlots.ToSquare(e.FlatPos) - BoardSlots.KingSquare;
                    bool ok = FitsPattern(e.Type, off);
                    byType.TryGetValue(e.Type, out var c);
                    byType[e.Type] = ok ? (c.ok + 1, c.bad) : (c.ok, c.bad + 1);
                    if (!ok) bad.Append($" {e.Type}{off}");
                }
                maxWaiting = Mathf.Max(maxWaiting, waiting);

                if (f % 90 == 45 && shot < cols * rows)
                {
                    yield return new WaitForEndOfFrame();
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    if (sheet == null)
                    {
                        tileW = tex.width;
                        tileH = tex.height;
                        sheet = new Texture2D(tileW * cols, tileH * rows, TextureFormat.RGB24, false);
                    }
                    int c = shot % cols, r = shot / cols;
                    if (tex.width == tileW && tex.height == tileH)
                        sheet.SetPixels32(c * tileW, (rows - 1 - r) * tileH, tileW, tileH, tex.GetPixels32());
                    Destroy(tex);
                    shot++;
                }
            }

            EnemyBase.DetourAroundKing = true;
            var line = new StringBuilder(detour ? "Slots" : "SlotsNoDetour");
            foreach (var kv in byType) line.Append($"\t{kv.Key}={kv.Value.ok}ok/{kv.Value.bad}bad");
            line.Append($"\tmax_waiting={maxWaiting}\tpawn_through_king={pawnThroughKing}\toff_pattern:{(bad.Length > 0 ? bad.ToString() : " none")}");
            stats.AppendLine(line.ToString());
            Destroy(view);
            if (sheet != null)
            {
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(dir, "14_Board_Slots.png"), sheet.EncodeToPNG());
                Destroy(sheet);
            }
        }

        /// <summary>Does an attack starting <paramref name="o"/> squares from the King follow the piece's move?</summary>
        static bool FitsPattern(PieceType type, Vector2Int o)
        {
            int ax = Mathf.Abs(o.x), ay = Mathf.Abs(o.y), m = Mathf.Max(ax, ay);
            bool line = ax == 0 || ay == 0 || ax == ay;
            switch (type)
            {
                case PieceType.Pawn: return true;                                                 // pawns are outside the slot system
                case PieceType.Knight: return (ax == 1 && ay == 2) || (ax == 2 && ay == 1);
                case PieceType.Bishop: return ax == ay && m >= 2;
                case PieceType.Rook: return ((ax == 0 || ay == 0) && m >= 2) || m <= 1;               // ram from anywhere on the line, or slam when crowded
                default: return true;                                                            // queen: lines, slam, or a leap from anywhere
            }
        }

        /// <summary>
        /// The King at low health (red vignette closing in), then killed by a blow from the front: he shatters into
        /// physics rubble with white insides. 5 frames.
        /// </summary>
        IEnumerator KingFallScenario(string dir)
        {
            var hud = game.GetComponent<Hud>();
            if (hud != null) hud.enabled = true;
            game.ClearEnemies();
            var player = game.Player;
            player.ResetForWave(PlayerStart, 0f);
            player.GodMode = false;
            game.Rig.OverridePosition = PlayerStart + new Vector3(3.6f, 2.4f, 3.2f);
            game.Rig.OverrideLookAt = PlayerStart + new Vector3(0f, 0.9f, -0.6f);
            for (int f = 0; f < 20; f++) yield return null;
            player.TakeHit(80f, 0f, player.transform.position + Vector3.forward);
            Texture2D sheet = null;
            int tileW = 0, tileH = 0;
            int[] waits = { 70, 70, 12, 14, 30 };
            for (int i = 0; i < waits.Length; i++)
            {
                if (i == 2) player.TakeHit(999f, 0f, player.transform.position + Vector3.forward * 1.5f);
                for (int f = 0; f < waits[i]; f++) yield return null;
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (sheet == null)
                {
                    tileW = tex.width;
                    tileH = tex.height;
                    sheet = new Texture2D(tileW * waits.Length, tileH, TextureFormat.RGB24, false);
                }
                if (tex.width == tileW && tex.height == tileH) sheet.SetPixels32(i * tileW, 0, tileW, tileH, tex.GetPixels32());
                Destroy(tex);
            }
            player.GodMode = true;
            if (sheet != null)
            {
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(dir, "15_King_Fall.png"), sheet.EncodeToPNG());
                Destroy(sheet);
            }
            for (int f = 0; f < 120; f++) yield return null;   // revive (practice mode) before the next scenario
        }

        /// <summary>
        /// Attack warnings, one piece at a time against the King on e4 (pawn d5, knight f6, bishop g6, rook e7,
        /// queen b4): top row mid wind-up (lit squares filling in, arrow / L route / ghost), bottom row the final blink.
        /// </summary>
        IEnumerator TelegraphScenario(string dir)
        {
            var hud = game.GetComponent<Hud>();
            if (hud != null) hud.enabled = false;
            var player = game.Player;
            (PieceType type, string square)[] cases =
            {
                (PieceType.Pawn, "d5"), (PieceType.Knight, "f6"), (PieceType.Bishop, "g6"), (PieceType.Rook, "e7"), (PieceType.Queen, "b4"),
            };
            game.Rig.OverridePosition = GameManager.SquareToWorld("e4") + new Vector3(2.5f, 11f, -9.5f);
            game.Rig.OverrideLookAt = GameManager.SquareToWorld("e4") + new Vector3(-0.5f, 0f, 2.2f);
            Texture2D sheet = null;
            int tileW = 0, tileH = 0;
            for (int c = 0; c < cases.Length; c++)
            {
                game.ClearEnemies();
                player.ResetForWave(GameManager.SquareToWorld("e4"), 0f);
                player.GodMode = true;
                var e = game.SpawnEnemy(cases[c].type, GameManager.SquareToWorld(cases[c].square));
                for (int f = 0; f < 900 && e != null && !(e.IsAttacking && e.WindupProgress >= 0.6f); f++) yield return null;
                for (int row = 0; row < 2; row++)
                {
                    if (row == 1)
                        for (int f = 0; f < 120 && e != null && e.IsAttacking && e.WindupProgress < 0.92f; f++) yield return null;
                    yield return new WaitForEndOfFrame();
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    if (sheet == null)
                    {
                        tileW = tex.width;
                        tileH = tex.height;
                        sheet = new Texture2D(tileW * cases.Length, tileH * 2, TextureFormat.RGB24, false);
                    }
                    if (tex.width == tileW && tex.height == tileH)
                        sheet.SetPixels32(c * tileW, (1 - row) * tileH, tileW, tileH, tex.GetPixels32());
                    Destroy(tex);
                }
            }
            game.ClearEnemies();
            if (sheet != null)
            {
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(dir, "16_Telegraphs.png"), sheet.EncodeToPNG());
                Destroy(sheet);
            }
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
                int lightRows = Mathf.Clamp(style.lights.Length, 2, 4);
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

            yield return JustDodgeScenario(dir, stats);
            yield return PieceLineup(dir);
            yield return StaggerScenario(dir, stats);
            yield return AutoAssistScenario(dir, stats);
            yield return CrumbleScenario(dir);
            yield return GapCloserScenario(dir, stats);
            yield return SlotScenario(dir, stats, false);   // A/B: pawns walking straight through the King
            yield return SlotScenario(dir, stats);
            yield return TelegraphScenario(dir);
            yield return KingFallScenario(dir);

            File.WriteAllText(Path.Combine(dir, "stats.tsv"), stats.ToString());
            Application.Quit();
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Sound reference images: a player build run with "-soundrefs &lt;dir&gt;" saves one screenshot per sound id
    /// (<c>&lt;dir&gt;/&lt;id&gt;.jpg</c>) the first time <see cref="Sfx.Play(string, Vector3)"/> fires for it, a few frames
    /// later so the moment reads on screen. A 3D sound whose source is off screen gets a camera framed on it for
    /// that shot. Ids that already have an image are skipped, so several runs (practice arena, then the game) add up.
    /// In the game build, <see cref="SoundRefDirector"/> plays through the title, rounds, the Queen, death and victory.
    /// </summary>
    public class SoundRefCapture : MonoBehaviour
    {
        struct Pending
        {
            public string id;
            public Vector3 pos;
            public bool is2D;
            public int due;
        }

        string dir;
        readonly HashSet<string> seen = new HashSet<string>();
        readonly List<Pending> pending = new List<Pending>();
        readonly StringBuilder log = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            string dir = GetArg("-soundrefs");
            if (dir == null) return;
            Directory.CreateDirectory(dir);
            var go = new GameObject("SoundRefCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<SoundRefCapture>().dir = dir;
        }

        public static string GetArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        void OnEnable() => Sfx.Played += OnPlayed;
        void OnDisable() => Sfx.Played -= OnPlayed;

        void Start()
        {
            var game = GameManager.Instance;
            if (game != null && game.CurrentMode == GameManager.Mode.Game) gameObject.AddComponent<SoundRefDirector>();
            StartCoroutine(CaptureLoop());
        }

        string PathFor(string id) => Path.Combine(dir, id + ".jpg");

        void OnPlayed(string id, Vector3 pos, bool is2D)
        {
            if (!seen.Add(id) || File.Exists(PathFor(id))) return;
            pending.Add(new Pending { id = id, pos = pos, is2D = is2D, due = Time.frameCount + DelayFrames(id) });
        }

        /// <summary>How long after the sound starts the picture is taken (the moment it describes).</summary>
        static int DelayFrames(string id)
        {
            switch (id)
            {
                case "EN_Windup": case "EN_Telegraph": return 22;
                case "QUEEN_Check_Windup": return 32;
                case "ATK_Stomp_Charge": return 14;
                case "STG_Vulnerable_Loop": return 25;
                case "THR_Swing_Loop": case "EN_Move_Slide": case "STG_Getup": case "THR_Fly": return 10;
                case "KNIGHT_Leap": case "QUEEN_Leap": case "ATK_Meteor_Jump": case "ATK_Stomp_Jump": return 12;
                case "GAME_RoundStart": case "GAME_FinalRound": case "UI_LockOn": case "IMP_Dust": case "QUEEN_Death": return 20;
                case "EN_Crumble": return 8;
                case "EN_Debris": return 25;
                case "GAME_RoundClear": return 15;
                case "PL_Death": return 30;
                case "GAME_Defeat": case "GAME_Victory": return 70;
                case "BGM_Title": case "BGM_Battle": case "BGM_Boss": case "BGM_Practice": case "AMB_Hall": return 40;
                default: return 3;
            }
        }

        IEnumerator CaptureLoop()
        {
            var due = new List<Pending>();
            while (true)
            {
                yield return new WaitForEndOfFrame();
                if (pending.Count == 0) continue;
                due.Clear();
                for (int i = pending.Count - 1; i >= 0; i--)
                    if (Time.frameCount >= pending[i].due) { due.Add(pending[i]); pending.RemoveAt(i); }
                if (due.Count == 0) continue;

                // Everything already in view shares this frame's shot; off-screen 3D sources get their own framing.
                var cam = GameManager.Instance != null ? GameManager.Instance.Rig.Cam : Camera.main;
                var now = new List<string>();
                var reframe = new List<Pending>();
                foreach (var p in due)
                {
                    if (p.is2D || cam == null || InView(cam, p.pos)) now.Add(p.id);
                    else reframe.Add(p);
                }
                if (now.Count > 0) Save(now, false);
                foreach (var p in reframe) yield return CaptureFramed(p);
            }
        }

        static bool InView(Camera cam, Vector3 pos)
        {
            Vector3 v = cam.WorldToViewportPoint(pos + Vector3.up * 0.6f);
            return v.z > 0f && v.z < 18f && v.x > 0.08f && v.x < 0.92f && v.y > 0.08f && v.y < 0.92f;
        }

        IEnumerator CaptureFramed(Pending p)
        {
            var rig = GameManager.Instance.Rig;
            bool wasOverride = rig.UseOverride;
            Vector3 oldPos = rig.OverridePosition, oldLook = rig.OverrideLookAt;
            Vector3 back = rig.Cam.transform.position - p.pos;
            back.y = 0f;
            back = back.sqrMagnitude > 0.01f ? back.normalized : Vector3.back;
            rig.UseOverride = true;
            rig.OverrideLookAt = p.pos + Vector3.up * 0.9f;
            rig.OverridePosition = p.pos + back * 6.5f + Vector3.up * 3.6f;
            yield return null;
            yield return new WaitForEndOfFrame();
            Save(new List<string> { p.id }, true);
            rig.UseOverride = wasOverride;
            rig.OverridePosition = oldPos;
            rig.OverrideLookAt = oldLook;
        }

        void Save(List<string> ids, bool reframed)
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            byte[] jpg = tex.EncodeToJPG(88);
            Destroy(tex);
            foreach (var id in ids)
            {
                File.WriteAllBytes(PathFor(id), jpg);
                log.AppendLine($"{id}\tframe={Time.frameCount}\treframed={reframed}");
            }
            File.AppendAllText(Path.Combine(dir, "captured.tsv"), log.ToString());
            log.Clear();
        }
    }

    /// <summary>
    /// Plays the real game hands-free for <see cref="SoundRefCapture"/>: title, a few rounds of each piece (the King
    /// walks up to them and swings), lock-on, pause, the Queen (phase 2, stagger), a thrown piece, running out of
    /// stamina and rolls, getting hurt, healing, dying, and finally the victory.
    /// </summary>
    public class SoundRefDirector : MonoBehaviour
    {
        GameManager game;
        PlayerController player;

        IEnumerator Start()
        {
            game = GameManager.Instance;
            player = game.Player;
            Time.captureDeltaTime = 1f / 60f;
            // Round transitions run on real time: keep real time in step with the 60 fps game clock.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            yield return Frames(90);                       // title screen + music
            game.DebugStartGame();                         // round I (pawns)
            player.GodMode = true;
            yield return Frames(100);
            yield return LockOnTour();
            game.DebugTogglePause();
            yield return Frames(30);
            game.DebugTogglePause();
            yield return Fight(420);                        // pawns thrust; the King walks and sprints over
            yield return ClearRound();                      // capture all → round clear → back to e1 → round II

            yield return WaitPlaying(1);
            yield return Fight(480);                        // knight hops and leaps
            game.DebugStartWave(2);                         // bishop
            yield return Fight(480);
            game.DebugStartWave(4);                         // rook: ram + close-range slam
            yield return Fight(720, PieceType.Rook, swing: false);   // walk up and let it slam

            game.DebugStartWave(9);                         // final round: the Queen
            yield return Fight(720, PieceType.Queen);
            var queen = game.Boss;
            if (queen != null)
            {
                queen.TakeHit(queen.Health - queen.MaxHealth * 0.45f, 0f, player.transform.position);   // phase 2: summons
                yield return Fight(420, PieceType.Queen);
                queen.TakeHit(1f, 999f, player.transform.position);                                      // poise break
                yield return Fight(120, PieceType.Queen);
            }

            // Quiet board for the King's own sounds: one inert dummy keeps the round from ending.
            game.ClearEnemies();
            game.SpawnCustom<TrainingDummy>(PieceType.Pawn, player.FlatPos + new Vector3(0f, 0f, 7f));
            var pawn = game.SpawnEnemy(PieceType.Pawn, player.FlatPos + new Vector3(3f, 0f, 2f));
            yield return Frames(60);
            pawn.BeginGrab();                               // a thrown piece flying and crashing down
            pawn.Launch(new Vector3(1f, 0f, 1f), 9f, 7f, 10f);
            yield return Frames(120);
            if (pawn != null && !pawn.IsDead) pawn.TakeHit(99999f, 0f, player.transform.position);
            yield return Frames(60);

            yield return DeniedActions();
            player.GodMode = false;
            player.TakeHit(18f, 10f, player.transform.position + player.transform.forward);
            yield return Frames(50);
            player.TakeHit(60f, 40f, player.transform.position + player.transform.forward);
            yield return Frames(50);
            for (int i = 0; i < PlayerController.MaxFlasks + 1; i++)
            {
                player.Press(PlayerController.Buffered.Heal);
                yield return Frames(100);
            }
            player.TakeHit(9999f, 0f, player.transform.position + player.transform.forward);
            yield return Frames(200);

            // Victory: replay the final round and capture everything at once.
            game.DebugStartWave(9);
            player.GodMode = true;
            yield return Frames(90);
            foreach (var e in game.Enemies.ToArray())
                if (e != null && !e.IsDead) e.TakeHit(99999f, 0f, player.transform.position);
            for (int i = 0; i < 400 && game.State != GameManager.GameState.Victory; i++) yield return null;
            yield return Frames(150);
            Application.Quit();
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        IEnumerator WaitPlaying(int wave)
        {
            for (int i = 0; i < 900 && !(game.State == GameManager.GameState.Playing && game.WaveIndex == wave); i++) yield return null;
        }

        /// <summary>Lets the pieces act while the King walks (sprinting when far) up to one and swings at it.</summary>
        IEnumerator Fight(int frames, PieceType? prefer = null, bool swing = true)
        {
            for (int f = 0; f < frames; f++)
            {
                EnemyBase target = null;
                if (prefer.HasValue)
                    foreach (var e in game.Enemies)
                        if (e != null && !e.IsDead && e.Type == prefer.Value) { target = e; break; }
                if (target == null)
                {
                    float best = float.MaxValue;
                    foreach (var e in game.Enemies)
                    {
                        if (e == null || e.IsDead) continue;
                        float d = (e.FlatPos - player.FlatPos).magnitude;
                        if (d < best) { best = d; target = e; }
                    }
                }
                Vector3 move = Vector3.zero;
                bool sprint = false;
                if (target != null)
                {
                    Vector3 to = target.FlatPos - player.FlatPos;
                    if (to.magnitude > 1.8f) move = to.normalized;
                    sprint = to.magnitude > 5f;
                }
                player.DebugMove = move;
                player.DebugSprint = sprint;
                if (game.State == GameManager.GameState.Playing && swing && player.CurrentAttack == null && f % 45 == 0 && move == Vector3.zero)
                    player.Press(PlayerController.Buffered.Light);
                yield return null;
            }
            player.DebugMove = Vector3.zero;
            player.DebugSprint = false;
        }

        IEnumerator LockOnTour()
        {
            EnemyBase first = null, second = null;
            foreach (var e in game.Enemies)
            {
                if (e == null || e.IsDead) continue;
                if (first == null) first = e;
                else if (second == null) second = e;
            }
            player.LockOn(first);
            yield return Frames(40);
            player.LockOn(second);
            yield return Frames(30);
            player.LockOn(null);
            yield return Frames(20);
        }

        IEnumerator ClearRound()
        {
            foreach (var e in game.Enemies.ToArray())
                if (e != null && !e.IsDead) e.TakeHit(99999f, 0f, player.transform.position);
            yield return Frames(30);
        }

        IEnumerator DeniedActions()
        {
            player.InfiniteStamina = false;
            for (int i = 0; i < 400 && player.Stamina >= 1f; i++)
            {
                if (player.CurrentAttack == null) player.Press(PlayerController.Buffered.Heavy);
                yield return null;
            }
            player.Press(PlayerController.Buffered.Light);
            yield return Frames(20);
            // Roll until the charges run out, then try once more.
            for (int i = 0; i < 300 && player.DodgeCharges > 0; i++)
            {
                player.Press(PlayerController.Buffered.Dodge);
                yield return null;
            }
            player.Press(PlayerController.Buffered.Dodge);
            yield return Frames(60);
            player.InfiniteStamina = true;
        }
    }
}

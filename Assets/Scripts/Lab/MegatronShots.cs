using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// 체스메가트론 check: a practice build run with "-megashot &lt;dir&gt;" drops the boss on e6 and captures its drop, idle
    /// (wide, front, side), a few walking frames, then every pattern from a fitting King square — the wind-up with its
    /// squares shown, each impact and the recovery — then the promoted crown, the stun and the collapse. Logs each
    /// attack's label and struck squares to megatron.tsv. The King is invincible throughout.
    /// </summary>
    public class MegatronShots : MonoBehaviour
    {
        string dir;
        GameManager game;
        ChessMegatron boss;
        readonly StringBuilder log = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            string d = SoundRefCapture.GetArg("-megashot");
            var g = GameManager.Instance;
            if (d == null || g == null || g.CurrentMode != GameManager.Mode.Practice) return;
            Directory.CreateDirectory(d);
            new GameObject("MegatronShots").AddComponent<MegatronShots>().dir = d;
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

        void View(Vector3 pos, Vector3 lookAt)
        {
            game.Rig.UseOverride = true;
            game.Rig.OverridePosition = pos;
            game.Rig.OverrideLookAt = lookAt;
        }

        static Vector3 Sq(string name) => GameManager.SquareToWorld(name);

        static readonly Vector3 WidePos = new Vector3(-10f, 15f, -12f), WideLook = new Vector3(1f, 0.5f, 1f);

        IEnumerator Start()
        {
            game = GameManager.Instance;
            Application.targetFrameRate = 60;
            yield return Frames(30);
            var arena = PracticeArena.Instance;
            if (arena != null) arena.enabled = false;
            game.ClearEnemies();
            var player = game.Player;
            player.GodMode = true;
            player.ResetForWave(Sq("e2"), 0f);
            var hud = game.GetComponent<Hud>();
            if (hud != null) hud.enabled = false;

            View(new Vector3(-11f, 9f, -12f), new Vector3(1f, 3f, 2f));
            boss = game.SpawnCustom<ChessMegatron>(PieceType.Pawn, Sq("e6"));
            boss.DebugHold = true;
            yield return Frames(20);
            yield return Shot("M00_Drop.png");
            yield return Frames(70);
            yield return Shot("M01_Idle_Wide.png");
            View(new Vector3(1f, 3.5f, -9f), new Vector3(1f, 4.2f, 3f));
            yield return Frames(2);
            yield return Shot("M02_Idle_Front.png");
            View(new Vector3(13f, 5f, 2f), new Vector3(1f, 3.6f, 3f));
            yield return Frames(2);
            yield return Shot("M03_Idle_Side.png");
            View(new Vector3(-5.5f, 3.2f, -2.5f), new Vector3(1f, 2.6f, 3f));
            yield return Frames(2);
            yield return Shot("M03b_Joints_Close.png");
            var b = boss.GetComponentInChildren<Renderer>().bounds;
            foreach (var r in boss.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            log.Append($"Body\trenderers={boss.GetComponentsInChildren<Renderer>().Length}\tsize={b.size}\tcenter={b.center}\tradius={boss.Radius}\thp={boss.MaxHealth}\n");

            // The play camera (following the King, HUD on): the head must be in the picture.
            player.ResetForWave(Sq("e3"), 0f);
            game.Rig.UseOverride = false;
            game.Rig.Snap();
            if (hud != null) hud.enabled = true;
            yield return Frames(30);
            yield return Shot("M05_PlayCamera.png");
            player.LockOn(boss);
            yield return Frames(40);
            yield return Shot("M05b_PlayCamera_LockOn.png");
            player.LockOn(null);
            if (hud != null) hud.enabled = false;

            // Walking: the King moves off to a corner; the colossus follows, square by square.
            boss.DebugHold = false;
            boss.DebugNoAttack = true;
            player.ResetForWave(Sq("a1"), 0f);
            View(WidePos, WideLook);
            for (int i = 0; i < 4; i++)
            {
                yield return Frames(i == 0 ? 50 : 24);
                yield return Shot($"M04_Walk_{i}.png");
            }
            boss.DebugNoAttack = false;
            boss.DebugHold = true;

            // Every pattern, from a King square it fits (the boss on e6 facing the King's side of the board).
            // moveTo: the King steps there after the first impact (the smash's second fist must follow him).
            var cases = new (ChessMegatron.Pattern p, string king, string moveTo, bool phase2)[]
            {
                (ChessMegatron.Pattern.Charge, "e3", null, false),
                (ChessMegatron.Pattern.Smash, "d4", "g4", false),
                (ChessMegatron.Pattern.Sweep, "e4", null, false),
                (ChessMegatron.Pattern.Drop, "b2", null, false),
                (ChessMegatron.Pattern.Sit, "e6", null, false),
                (ChessMegatron.Pattern.Wheel, "a1", null, false),
                (ChessMegatron.Pattern.Promotion, "e3", null, false),
                (ChessMegatron.Pattern.DoubleSweep, "e4", null, true),
                (ChessMegatron.Pattern.Charge, "e3", null, true),
                (ChessMegatron.Pattern.Smash, "d4", null, true),
                (ChessMegatron.Pattern.Sweep, "e4", null, true),
                (ChessMegatron.Pattern.Sit, "e6", null, true),
                (ChessMegatron.Pattern.Wheel, "a1", null, true),
                (ChessMegatron.Pattern.QueenLines, "e3", null, true),
                (ChessMegatron.Pattern.Storm, "e2", null, true),
            };
            log.Append("pattern\tking\tattack\tsquares\twindup_frames\n");
            int n = 10;
            foreach (var c in cases)
            {
                boss.DebugPlace(new Vector2Int(4, 5), new Vector2Int(0, -1));
                player.ResetForWave(Sq(c.king), 0f);
                View(WidePos, WideLook);
                yield return Frames(30);
                if (c.phase2 && !boss.Phase2) boss.DebugPhase2();
                boss.DebugPattern(c.p);
                string name = $"M{n++}_{c.p}{(c.phase2 ? "_2P" : "")}";
                bool windupShot = false;
                int impacts = 0, frames = 0, windupFrames = 0;
                char prev = boss.StateCode;
                while (frames++ < 900)
                {
                    char s = boss.StateCode;
                    if (s == 'w') windupFrames++;
                    if (s == 'w' && !windupShot && boss.WindupProgress >= 0.75f)
                    {
                        windupShot = true;
                        yield return Shot(name + "_a_Windup.png");
                        log.Append($"{c.p}{(c.phase2 ? "(2P)" : "")}\t{c.king}\t{boss.AttackLabel}\t{Names(boss.DebugSquares)}\t");
                    }
                    if (prev == 'a' && s != 'a')
                    {
                        impacts++;
                        yield return Shot($"{name}_b_Impact{impacts}.png");
                        if (impacts == 1)
                        {
                            log.Append($"{windupFrames}\n");
                            if (c.moveTo != null) player.ResetForWave(Sq(c.moveTo), 0f);
                        }
                        else log.Append($"{c.p}\t\t{boss.AttackLabel}\t{Names(boss.DebugSquares)}\t\n");
                    }
                    if (impacts > 0 && s == 'm') break;
                    prev = s;
                    yield return null;
                }
                yield return Frames(10);
                yield return Shot(name + "_c_After.png");
            }

            // The wheel's timing: the King (not invincible here) on one square at a time while it leaps to the middle and
            // sweeps; every drop in his health is logged with the arm's angle then — it must be the moment an arm passes
            // over him. Under the body (e5) only the leap's landing may hit.
            player.GodMode = false;
            var spots = new[] { "a1", "h8", "b6", "g3", "e8", "e5" };
            for (int k = 0; k < spots.Length; k++)
            {
                boss.DebugPlace(new Vector2Int(4, 5), new Vector2Int(0, -1));
                player.ResetForWave(Sq(spots[k]), 0f);
                View(WidePos, WideLook);
                yield return Frames(20);
                boss.DebugPattern(ChessMegatron.Pattern.Wheel);
                float hp = player.Health;
                var hits = new StringBuilder();
                bool midShot = false;
                int guard = 0;
                while (guard++ < 900)
                {
                    if (k == 0 && !midShot && boss.StateCode == 'a' && boss.DebugWheelPending > 0 && boss.DebugWheelPending < 32)
                    {
                        midShot = true;
                        yield return Shot("M28_Wheel_MidSweep.png");
                    }
                    if (player.Health < hp - 0.01f)
                    {
                        Vector3 l = boss.transform.InverseTransformPoint(player.FlatPos);
                        float phi = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
                        float yaw = boss.DebugWheelYaw;
                        float off = Mathf.Min(Mathf.Abs(Mathf.DeltaAngle(phi, yaw + 90f)), Mathf.Abs(Mathf.DeltaAngle(phi, yaw - 90f)));
                        hits.Append($"[{boss.AttackLabel} king_angle={phi:0} arm_yaw={yaw:0} arm_off={off:0.0}deg hp {hp:0}->{player.Health:0}] ");
                        hp = player.Health;
                        if (k == 2) yield return Shot("M28_Wheel_HitMoment.png");
                    }
                    if (boss.StateCode == 'm') break;
                    yield return null;
                }
                log.Append($"WheelTiming\t{spots[k]}\t{(hits.Length > 0 ? hits.ToString() : "no hit")}\n");
                player.ResetForWave(Sq("e2"), 0f);
            }
            player.GodMode = true;

            // The sit-down's trigger: the King stands under the body; the AI alone must sit down on him after 1.5 s.
            boss.DebugPlace(new Vector2Int(4, 5), new Vector2Int(0, -1));
            player.ResetForWave(Sq("e6"), 0f);
            yield return Frames(2);
            boss.DebugHold = false;
            int waited = 0;
            while (waited++ < 400 && !(boss.CurrentPattern == ChessMegatron.Pattern.Sit && boss.StateCode == 'w')) yield return null;
            log.Append($"SitTrigger\tframes_under={waited}\tstarted={boss.CurrentPattern == ChessMegatron.Pattern.Sit && boss.StateCode == 'w'}\n");
            View(WidePos, WideLook);
            yield return Frames(30);
            yield return Shot("M29_Sit_Trigger.png");
            while (waited++ < 900 && boss.StateCode != 'm') yield return null;
            boss.DebugHold = true;

            // The promoted crown, close up.
            View(new Vector3(1f, 6f, -6f), new Vector3(1f, 6.5f, 3f));
            yield return Frames(30);
            yield return Shot("M30_Crown.png");

            // Stunned: the springs go slack.
            View(new Vector3(-8f, 6f, -7f), new Vector3(1f, 2.5f, 3f));
            boss.AddStagger(9999f, player.transform.position);
            yield return Frames(40);
            yield return Shot("M31_Stagger.png");
            log.Append($"Stagger\tstate={boss.StateCode}\tstunned={boss.IsStunned}\n");
            yield return Frames(120);

            // Captured: it comes apart from the head down.
            View(new Vector3(-12f, 8f, -12f), new Vector3(1f, 2f, 2f));
            boss.TakeHit(99999f, 0f, player.transform.position);
            yield return Frames(12);
            yield return Shot("M32_Death_0.png");
            yield return Frames(25);
            yield return Shot("M33_Death_1.png");
            yield return Frames(40);
            yield return Shot("M34_Death_2.png");
            log.Append($"Death\tremains={(GameObject.Find("ChessMegatron_Remains") != null)}\n");

            File.WriteAllText(Path.Combine(dir, "megatron.tsv"), log.ToString());
            yield return Frames(5);
            Application.Quit();
        }

        static string Names(Vector2Int[] squares)
        {
            if (squares == null) return "-";
            var sb = new StringBuilder();
            foreach (var s in squares) sb.Append(BoardSlots.Name(s)).Append(' ');
            return sb.ToString().TrimEnd();
        }
    }
}

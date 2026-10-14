using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KingLastPiece
{
    /// <summary>
    /// Effects comparison scene (Scenes/EffectsLab.unity). One station per <see cref="VfxStyle"/>, side by side;
    /// a demo loop fires the same events (light hit, heavy hit, King slam, enemy slam) on every station at once.
    /// Keys: 1-3 pick the style for the King's own attacks (play-test it), P pause/resume demo, F fire demo now.
    /// A player build run with "-autoshot &lt;dir&gt;" saves comparison sheets (rows = styles, columns = time).
    /// </summary>
    public class EffectsLab : MonoBehaviour
    {
        public static EffectsLab Instance { get; private set; }

        static readonly float[] StationX = { -5.5f, 0f, 5.5f };
        const float StationZ = 0.5f, DemoPeriod = 3.4f;
        static readonly Vector3 PlayerStart = new Vector3(0f, 0f, -5.5f);

        GameManager game;
        readonly TrainingDummy[] dummies = new TrainingDummy[3];
        bool demo = true, automated;
        float demoTime;
        int demoStep;
        GUIStyle labelStyle, panelStyle;

        static readonly Key[] StyleKeys = { Key.Digit1, Key.Digit2, Key.Digit3 };

        void Awake()
        {
            Instance = this;
            GameManager.StartMode = GameManager.Mode.Effects;
            game = new GameObject("GameManager").AddComponent<GameManager>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            game.Player.GodMode = true;
            game.Player.InfiniteStamina = true;
            game.Player.ResetForWave(PlayerStart, 0f);
            game.Rig.Snap();
            for (int i = 0; i < dummies.Length; i++) SpawnDummy(i);

            string dir = GetArg("-autoshot");
            if (dir != null) StartCoroutine(AutoShot(dir));
        }

        static Vector3 Station(int i) => new Vector3(StationX[i], 0f, StationZ);
        Vector3 DummyPos(int i) => Station(i) + new Vector3(0f, 0f, 2f);

        void SpawnDummy(int i) => dummies[i] = game.SpawnCustom<TrainingDummy>(PieceType.Pawn, DummyPos(i));

        void Update()
        {
            if (automated || game == null) return;
            var kb = Keyboard.current;
            if (kb != null)
            {
                for (int i = 0; i < StyleKeys.Length; i++)
                    if (kb[StyleKeys[i]].wasPressedThisFrame) Vfx.Style = (VfxStyle)i;
                if (kb.pKey.wasPressedThisFrame) demo = !demo;
                if (kb.fKey.wasPressedThisFrame) { demoTime = 0f; demoStep = 0; }
            }

            for (int i = 0; i < dummies.Length; i++)
                if (dummies[i] == null || dummies[i].IsDead) SpawnDummy(i);

            if (demo) RunDemo(Time.deltaTime);
        }

        /// <summary>The same sequence on every station, each in its own style.</summary>
        void RunDemo(float dt)
        {
            demoTime += dt;
            float[] at = { 0.2f, 0.7f, 1.3f, 2.4f };
            while (demoStep < at.Length && demoTime >= at[demoStep])
            {
                for (int i = 0; i < StationX.Length; i++) Fire((VfxStyle)i, i, demoStep);
                demoStep++;
            }
            if (demoTime >= DemoPeriod) { demoTime = 0f; demoStep = 0; }
        }

        void Fire(VfxStyle style, int station, int step)
        {
            Vector3 hitPos = DummyPos(station) + Vector3.up * 1.0f - Vector3.forward * 0.45f;
            switch (step)
            {
                case 0: Vfx.Hit(style, hitPos, Vector3.forward, false); break;
                case 1: Vfx.Hit(style, hitPos, Vector3.forward, true); break;
                case 2: Vfx.GroundImpact(style, Station(station), 2.4f, 1.2f); break;
                case 3: Vfx.GroundImpact(style, Station(station) + new Vector3(0f, 0f, 1.6f), 1.8f, 0.9f, hostile: true); break;
            }
            if (step >= 1 && dummies[station] != null) dummies[station].Flash(new Color(1.6f, 1.5f, 1.3f), 0.09f);
        }

        // ---------------------------------------------------------------- labels

        void OnGUI()
        {
            if (game == null || game.Rig == null) return;
            float scale = Screen.height / 1080f;
            labelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            panelStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
            labelStyle.fontSize = Mathf.RoundToInt(26 * scale);
            panelStyle.fontSize = Mathf.RoundToInt(17 * scale);

            var cam = game.Rig.Cam;
            for (int i = 0; i < StationX.Length; i++)
            {
                Vector3 sp = cam.WorldToScreenPoint(Station(i) + new Vector3(0f, 3.4f, 2f));
                if (sp.z < 0f) continue;
                bool sel = (int)Vfx.Style == i;
                var r = new Rect(sp.x - 160 * scale, Screen.height - sp.y - 18 * scale, 320 * scale, 36 * scale);
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = sel ? new Color(1f, 0.85f, 0.45f) : Color.white;
                GUI.Label(r, Vfx.Names[i] + (sel ? "  ◀ King" : ""), labelStyle);
                GUI.color = Color.white;
            }

            if (automated) return;
            var pr = new Rect(20 * scale, 20 * scale, 560 * scale, 150 * scale);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(pr, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(pr.x + 12 * scale, pr.y + 8 * scale, pr.width - 24 * scale, pr.height - 16 * scale),
                $"EFFECTS LAB — 1-3: King's style ({Vfx.Names[(int)Vfx.Style]})   P: demo {(demo ? "ON" : "OFF")}   F: fire now\n\n" +
                Vfx.Descriptions[(int)Vfx.Style], panelStyle);
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
            var hud = game.GetComponent<Hud>();
            if (hud != null) hud.enabled = false;
            game.Rig.UseOverride = true;
            for (int f = 0; f < 60; f++) yield return null;

            // E1: King ground slam (rows = styles).
            yield return Sheet(Path.Combine(dir, "E1_Ground_Slam.png"), new[] { 2, 8, 18, 40, 80 },
                i => Vfx.GroundImpact((VfxStyle)i, Station(i), 2.6f, 1.25f),
                i => (Station(i) + new Vector3(0f, 4.2f, -5.2f), Station(i) + new Vector3(0f, 0.2f, 0.4f)));

            // E2: heavy hit on a piece.
            yield return Sheet(Path.Combine(dir, "E2_Hit.png"), new[] { 1, 4, 8, 14, 26 },
                i =>
                {
                    Vfx.Hit((VfxStyle)i, DummyPos(i) + Vector3.up - Vector3.forward * 0.45f, Vector3.forward, true);
                    if (dummies[i] != null) dummies[i].Flash(new Color(1.6f, 1.5f, 1.3f), 0.09f);
                },
                i => (DummyPos(i) + new Vector3(2.2f, 1.8f, -3.2f), DummyPos(i) + new Vector3(0f, 0.9f, -0.3f)));

            // E3: enemy slam (hostile accent).
            yield return Sheet(Path.Combine(dir, "E3_Enemy_Slam.png"), new[] { 2, 8, 18, 40, 80 },
                i => Vfx.GroundImpact((VfxStyle)i, Station(i), 2.2f, 1f, hostile: true),
                i => (Station(i) + new Vector3(0f, 4.2f, -5.2f), Station(i) + new Vector3(0f, 0.2f, 0.4f)));

            // E4: real gameplay — the King's Meteor Stomp among dummies, per style.
            yield return Sheet(Path.Combine(dir, "E4_Meteor_Stomp_Gameplay.png"), new[] { 52, 58, 66, 80, 110 },
                i =>
                {
                    Vfx.Style = (VfxStyle)i;
                    game.Player.ResetForWave(Station(i) - new Vector3(0f, 0f, 1.5f), 0f);
                    game.Player.Press(PlayerController.Buffered.Heavy);
                },
                i => (Station(i) + new Vector3(3.5f, 4f, -6.5f), Station(i) + new Vector3(0f, 0.6f, 1f)));

            // E6: top-down whip check — King faces +Z (up in the image), dummy ahead; row 0 = hit 1, row 1 = hit 2.
            yield return WhipTopDown(Path.Combine(dir, "E6_Whip_TopDown.png"));

            // E5: NamuFX slash prefabs as authored (identity rotation, at chest height) — rows = prefabs.
            var lib = FxLibrary.Instance;
            if (lib != null)
            {
                var prefabs = new[] { lib.slashA, lib.slashB, lib.slashC, lib.slashD, lib.slashInk, lib.slashWater, lib.slashAoeA };
                yield return PrefabSheet(Path.Combine(dir, "E5_Slash_Prefabs.png"), prefabs, new[] { 3, 8, 14, 22, 36 });
            }

            Application.Quit();
        }

        IEnumerator WhipTopDown(string path)
        {
            var player = game.Player;
            Vector3 start = Station(1) - new Vector3(0f, 0f, 1.2f);
            game.Rig.OverridePosition = start + new Vector3(0f, 9f, -0.6f);
            game.Rig.OverrideLookAt = start + new Vector3(0f, 0f, 0.4f);
            int[] frames = { 8, 12, 16, 22, 30 };
            const int rows = 2;
            Texture2D sheet = null;
            int tileW = 0, tileH = 0;
            for (int row = 0; row < rows; row++)
            {
                if (dummies[1] != null) Destroy(dummies[1].gameObject);
                SpawnDummy(1);
                player.ResetForWave(start, 0f);
                for (int f = 0; f < 90; f++) yield return null;
                player.Press(PlayerController.Buffered.Light);
                yield return null;
                if (row == 1)
                {
                    var first = player.CurrentAttack;
                    while (first != null && player.StateTime < first.cancelAt) yield return null;
                    player.Press(PlayerController.Buffered.Light);
                    yield return null;
                }
                int elapsed = 0;
                for (int c = 0; c < frames.Length; c++)
                {
                    for (; elapsed < frames[c]; elapsed++) yield return null;
                    yield return new WaitForEndOfFrame();
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    if (sheet == null)
                    {
                        tileW = tex.width;
                        tileH = tex.height;
                        sheet = new Texture2D(tileW * frames.Length, tileH * rows, TextureFormat.RGB24, false);
                    }
                    if (tex.width == tileW && tex.height == tileH)
                        sheet.SetPixels32(c * tileW, (rows - 1 - row) * tileH, tileW, tileH, tex.GetPixels32());
                    Destroy(tex);
                }
            }
            sheet.Apply();
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Destroy(sheet);
        }

        /// <summary>Spawns each prefab at the middle station and captures it over time (one row per prefab).</summary>
        IEnumerator PrefabSheet(string path, GameObject[] prefabs, int[] frames)
        {
            game.Rig.OverridePosition = Station(1) + new Vector3(3.2f, 3.4f, -5.5f);
            game.Rig.OverrideLookAt = Station(1) + new Vector3(0f, 1f, 0.5f);
            game.Player.ResetForWave(Station(1) + new Vector3(0f, 0f, -0.5f), 0f);
            int rows = prefabs.Length, cols = frames.Length;
            Texture2D sheet = null;
            int tileW = 0, tileH = 0;
            for (int i = 0; i < rows; i++)
            {
                for (int f = 0; f < 70; f++) yield return null;
                if (prefabs[i] == null) continue;
                var fx = Instantiate(prefabs[i], Station(1) + Vector3.up, Quaternion.identity);
                int elapsed = 0;
                for (int c = 0; c < cols; c++)
                {
                    for (; elapsed < frames[c]; elapsed++) yield return null;
                    yield return new WaitForEndOfFrame();
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    if (sheet == null)
                    {
                        tileW = tex.width;
                        tileH = tex.height;
                        sheet = new Texture2D(tileW * cols, tileH * rows, TextureFormat.RGB24, false);
                    }
                    if (tex.width == tileW && tex.height == tileH)
                        sheet.SetPixels32(c * tileW, (rows - 1 - i) * tileH, tileW, tileH, tex.GetPixels32());
                    Destroy(tex);
                }
                Destroy(fx, 2f);
            }
            if (sheet == null) yield break;
            sheet.Apply();
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Destroy(sheet);
        }

        /// <summary>For each style: place the camera, trigger the event, capture frames at the given delays.</summary>
        IEnumerator Sheet(string path, int[] frames, System.Action<int> trigger, System.Func<int, (Vector3 pos, Vector3 look)> view)
        {
            int rows = StationX.Length, cols = frames.Length;
            Texture2D sheet = null;
            int tileW = 0, tileH = 0;
            for (int i = 0; i < rows; i++)
            {
                var v = view(i);
                game.Rig.OverridePosition = v.pos;
                game.Rig.OverrideLookAt = v.look;
                for (int f = 0; f < 150; f++) yield return null;  // let earlier effects finish
                trigger(i);
                int elapsed = 0;
                for (int c = 0; c < cols; c++)
                {
                    for (; elapsed < frames[c]; elapsed++) yield return null;
                    yield return new WaitForEndOfFrame();
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    if (sheet == null)
                    {
                        tileW = tex.width;
                        tileH = tex.height;
                        sheet = new Texture2D(tileW * cols, tileH * rows, TextureFormat.RGB24, false);
                    }
                    if (tex.width == tileW && tex.height == tileH)
                        sheet.SetPixels32(c * tileW, (rows - 1 - i) * tileH, tileW, tileH, tex.GetPixels32());
                    Destroy(tex);
                }
            }
            sheet.Apply();
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Destroy(sheet);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Builds the chessboard arena at runtime, runs the wave flow
    /// (Title → waves → boss → victory, death restarts the current wave) and resolves body collisions.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class GameManager : MonoBehaviour
    {
        public enum GameState { Title, Playing, WaveClear, Dead, Victory }

        public const float TileSize = 2f;
        public const float BoardHalf = 8f;
        public const float PlayableHalf = 7.3f;

        public static GameManager Instance { get; private set; }

        public GameState State { get; private set; }
        public bool Paused { get; private set; }
        public PlayerController Player { get; private set; }
        public CameraRig Rig { get; private set; }
        public readonly List<EnemyBase> Enemies = new List<EnemyBase>();

        public int WaveIndex { get; private set; }
        public int WaveCount => Waves.Length;
        public string WaveName => Waves[Mathf.Clamp(WaveIndex, 0, Waves.Length - 1)].name;
        public string Banner { get; private set; }
        public float BannerTime { get; private set; }
        public float StateTime { get; private set; }
        public int Deaths { get; private set; }
        public float RunTime { get; private set; }

        public enum Mode { Game, Lab }

        /// <summary>Set before the GameManager is created to boot into a different mode (e.g. the Attack Lab).</summary>
        public static Mode StartMode = Mode.Game;
        public Mode CurrentMode { get; private set; }
        public bool HitStopEnabled { get; set; } = true;

        public struct DamagePopup
        {
            public Vector3 position;
            public float amount, time;
        }

        public readonly List<DamagePopup> Popups = new List<DamagePopup>();


        public void RevivePlayerInPlace()
        {
            Player.ResetForWave(Player.FlatPos, Player.transform.eulerAngles.y);
            EnterState(GameState.Playing);
        }

        struct WaveDef
        {
            public string name;
            public (PieceType type, string square)[] pieces;
        }

        static readonly WaveDef[] Waves =
        {
            new WaveDef { name = "I. The Opening", pieces = new[] {
                (PieceType.Pawn, "d7"), (PieceType.Pawn, "e7"), (PieceType.Pawn, "f7") } },
            new WaveDef { name = "II. Knight's Gambit", pieces = new[] {
                (PieceType.Knight, "g8"), (PieceType.Pawn, "c7"), (PieceType.Pawn, "f7") } },
            new WaveDef { name = "III. The Long Diagonal", pieces = new[] {
                (PieceType.Bishop, "c8"), (PieceType.Knight, "b8"), (PieceType.Pawn, "d7"), (PieceType.Pawn, "e7") } },
            new WaveDef { name = "IV. Castle Walls", pieces = new[] {
                (PieceType.Rook, "a8"), (PieceType.Bishop, "f8"), (PieceType.Pawn, "c7"), (PieceType.Pawn, "g7") } },
            new WaveDef { name = "V. The Black Queen", pieces = new[] {
                (PieceType.Queen, "d8") } },
        };

        public static Vector3 SquareToWorld(string sq)
        {
            int file = char.ToLower(sq[0]) - 'a';
            int rank = sq[1] - '1';
            return new Vector3((file - 3.5f) * TileSize, 0f, (rank - 3.5f) * TileSize);
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            BuildArena();
            SetupLighting();

            var playerGo = new GameObject("WhiteKing");
            Player = playerGo.AddComponent<PlayerController>();
            Player.Build();
            Player.ResetForWave(SquareToWorld("e1"), 0f);

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.025f, 0.025f, 0.04f);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            Rig = cam.GetComponent<CameraRig>();
            if (Rig == null) Rig = cam.gameObject.AddComponent<CameraRig>();
            Rig.Init(cam, Player);

            gameObject.AddComponent<Hud>();
            CurrentMode = StartMode;
            StartMode = Mode.Game;
            EnterState(CurrentMode == Mode.Lab ? GameState.Playing : GameState.Title);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Time.timeScale = 1f;
        }

        // ---------------------------------------------------------------- flow

        void EnterState(GameState s)
        {
            State = s;
            StateTime = 0f;
            bool lockCursor = s == GameState.Playing && !Paused;
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockCursor;
        }

        void StartWave(int index)
        {
            if (index == 0 && State != GameState.WaveClear)
            {
                Deaths = 0;
                RunTime = 0f;
            }
            WaveIndex = index;
            Paused = false;
            ResetTime();

            foreach (var e in Enemies) if (e != null) Destroy(e.gameObject);
            Enemies.Clear();

            bool keepPlayer = State == GameState.WaveClear;
            if (!keepPlayer)
            {
                Player.ResetForWave(SquareToWorld("e1"), 0f);
                Rig.Snap();
            }

            foreach (var (type, square) in Waves[index].pieces)
                SpawnEnemy(type, SquareToWorld(square));

            ShowBanner(Waves[index].name);
            EnterState(GameState.Playing);
        }

        public EnemyBase SpawnEnemy(PieceType type, Vector3 pos)
        {
            switch (type)
            {
                case PieceType.Knight: return SpawnCustom<KnightEnemy>(type, pos);
                case PieceType.Bishop: return SpawnCustom<BishopEnemy>(type, pos);
                case PieceType.Rook: return SpawnCustom<RookEnemy>(type, pos);
                case PieceType.Queen: return SpawnCustom<QueenEnemy>(type, pos);
                default: return SpawnCustom<PawnEnemy>(type, pos);
            }
        }

        public T SpawnCustom<T>(PieceType modelType, Vector3 pos) where T : EnemyBase
        {
            var go = new GameObject(typeof(T).Name);
            go.transform.SetPositionAndRotation(new Vector3(pos.x, 0f, pos.z), Quaternion.LookRotation(Vector3.back));
            var e = go.AddComponent<T>();
            var model = PieceFactory.Build(modelType, go.transform, false);
            e.Setup(modelType, model);
            Enemies.Add(e);
            return e;
        }

        public void ClearEnemies()
        {
            foreach (var e in Enemies) if (e != null) Destroy(e.gameObject);
            Enemies.Clear();
        }

        public int DamageEvents { get; private set; }

        public void ReportDamage(EnemyBase e, float amount)
        {
            DamageEvents++;
            Popups.Add(new DamagePopup
            {
                position = e.transform.position + Vector3.up * (e.IsBoss ? 3.3f : 2.3f) + Random.insideUnitSphere * 0.3f,
                amount = amount,
                time = Time.unscaledTime,
            });
            if (Popups.Count > 32) Popups.RemoveAt(0);
        }

        public void ShowBanner(string text)
        {
            Banner = text;
            BannerTime = Time.unscaledTime;
        }

        public void OnPlayerDied()
        {
            Deaths++;
            ResetTime();
            EnterState(GameState.Dead);
        }

        public int ActiveAttackers(EnemyBase except)
        {
            int n = 0;
            foreach (var e in Enemies)
                if (e != null && e != except && !e.IsDead && e.IsAttacking) n++;
            return n;
        }

        public EnemyBase Boss
        {
            get
            {
                foreach (var e in Enemies)
                    if (e != null && e.IsBoss && !e.IsDead) return e;
                return null;
            }
        }

        int AliveCount()
        {
            int n = 0;
            foreach (var e in Enemies) if (e != null && !e.IsDead) n++;
            return n;
        }

        void Update()
        {
            Enemies.RemoveAll(e => e == null);
            StateTime += Time.unscaledDeltaTime;
            TickTime();

            switch (State)
            {
                case GameState.Title:
                    if (GameInput.Confirm) StartWave(0);
                    break;

                case GameState.Playing:
                    if (GameInput.Pause) TogglePause();
                    if (Paused) break;
                    RunTime += Time.deltaTime;
                    ResolveCollisions();
                    if (CurrentMode == Mode.Game && AliveCount() == 0 && StateTime > 1f)
                    {
                        if (WaveIndex >= Waves.Length - 1)
                        {
                            EnterState(GameState.Victory);
                        }
                        else
                        {
                            Player.Rest();
                            ShowBanner("Pieces Captured");
                            EnterState(GameState.WaveClear);
                        }
                    }
                    break;

                case GameState.WaveClear:
                    if (StateTime > 3.2f) StartWave(WaveIndex + 1);
                    break;

                case GameState.Dead:
                    if (CurrentMode == Mode.Lab)
                    {
                        if (StateTime > 1.5f) RevivePlayerInPlace();
                        break;
                    }
                    if (StateTime > 1.8f && GameInput.Confirm) StartWave(WaveIndex);
                    break;

                case GameState.Victory:
                    if (StateTime > 2.5f && GameInput.Confirm) StartWave(0);
                    break;
            }
        }

        void TogglePause()
        {
            Paused = !Paused;
            Cursor.lockState = Paused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = Paused;
            ApplyTimeScale();
        }

        // ---------------------------------------------------------------- time control
        // Hit-stop and slow motion are timers in real time; the world's timeScale is their product.
        // The King runs on PlayerDeltaTime, which ignores slow motion (but not hit-stop),
        // so a perfect dodge slows the enemies while the King keeps full speed.

        const float HitStopScale = 0.1f;
        float hitStopTimer, slowTimer, slowScale = 1f;

        /// <summary>Real-time frame delta (uses the fixed capture step when frames are being recorded).</summary>
        static float RealDelta => Time.captureDeltaTime > 0f ? Time.captureDeltaTime : Time.unscaledDeltaTime;

        public bool SlowMoActive => slowTimer > 0f;
        public float SlowMoRemaining => slowTimer;

        public float PlayerDeltaTime
        {
            get
            {
                if (Paused) return 0f;
                if (!SlowMoActive) return Time.deltaTime;
                return RealDelta * (hitStopTimer > 0f ? HitStopScale : 1f);
            }
        }

        public void HitStop(float duration)
        {
            if (!HitStopEnabled || Paused || hitStopTimer > 0f) return;
            hitStopTimer = duration;
            ApplyTimeScale();
        }

        public void SlowMo(float scale, float duration)
        {
            slowScale = scale;
            slowTimer = duration;
            ApplyTimeScale();
        }

        public void EndSlowMo()
        {
            slowTimer = 0f;
            ApplyTimeScale();
        }

        void ResetTime()
        {
            hitStopTimer = slowTimer = 0f;
            ApplyTimeScale();
        }

        void TickTime()
        {
            if (Paused) return;
            float dt = RealDelta;
            if (hitStopTimer > 0f) hitStopTimer -= dt;
            if (slowTimer > 0f) slowTimer -= dt;
            ApplyTimeScale();
        }

        void ApplyTimeScale()
        {
            if (Paused) { Time.timeScale = 0f; return; }
            Time.timeScale = (hitStopTimer > 0f ? HitStopScale : 1f) * (slowTimer > 0f ? slowScale : 1f);
        }

        void ResolveCollisions()
        {
            for (int i = 0; i < Enemies.Count; i++)
            {
                var a = Enemies[i];
                if (a == null || a.IgnoresCollision) continue;

                for (int j = i + 1; j < Enemies.Count; j++)
                {
                    var b = Enemies[j];
                    if (b == null || b.IgnoresCollision) continue;
                    Push(a.transform, b.transform, a.Radius + b.Radius, a.IsBoss ? 0.1f : b.IsBoss ? 0.9f : 0.5f);
                }

                if (!Player.IsDead && !Player.IsDodging)
                    Push(Player.transform, a.transform, Player.Radius + a.Radius, a.IsBoss ? 0.9f : 0.6f);
            }
        }

        /// <summary>Separates two bodies; <paramref name="shareA"/> is how much of the overlap A absorbs.</summary>
        static void Push(Transform a, Transform b, float minDist, float shareA)
        {
            Vector3 d = a.position - b.position;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist >= minDist) return;
            Vector3 n = dist > 0.001f ? d / dist : Vector3.right;
            float overlap = minDist - dist;
            a.position += n * (overlap * shareA);
            b.position -= n * (overlap * (1f - shareA));
        }

        // ---------------------------------------------------------------- world

        void BuildArena()
        {
            var root = new GameObject("Arena").transform;
            var light = MaterialLib.Get(new Color(0.74f, 0.7f, 0.62f), 0.35f);
            var dark = MaterialLib.Get(new Color(0.26f, 0.12f, 0.1f), 0.45f);
            var wood = MaterialLib.Get(new Color(0.16f, 0.09f, 0.05f), 0.5f);
            var stone = MaterialLib.Get(new Color(0.16f, 0.16f, 0.18f), 0.15f);
            var floor = MaterialLib.Get(new Color(0.04f, 0.04f, 0.05f), 0.1f);

            for (int f = 0; f < 8; f++)
            for (int r = 0; r < 8; r++)
            {
                var pos = new Vector3((f - 3.5f) * TileSize, -0.2f, (r - 3.5f) * TileSize);
                PieceFactory.Part(PrimitiveType.Cube, root, pos, new Vector3(TileSize, 0.4f, TileSize), (f + r) % 2 == 0 ? dark : light);
            }

            float edge = BoardHalf + 0.35f;
            float len = BoardHalf * 2f + 1.4f;
            PieceFactory.Part(PrimitiveType.Cube, root, new Vector3(0, -0.15f, edge), new Vector3(len, 0.5f, 0.7f), wood);
            PieceFactory.Part(PrimitiveType.Cube, root, new Vector3(0, -0.15f, -edge), new Vector3(len, 0.5f, 0.7f), wood);
            PieceFactory.Part(PrimitiveType.Cube, root, new Vector3(edge, -0.15f, 0), new Vector3(0.7f, 0.5f, len), wood);
            PieceFactory.Part(PrimitiveType.Cube, root, new Vector3(-edge, -0.15f, 0), new Vector3(0.7f, 0.5f, len), wood);
            PieceFactory.Part(PrimitiveType.Cube, root, new Vector3(0, -1.2f, 0), new Vector3(160f, 1f, 160f), floor);

            // Ring of pillars with braziers around the board.
            var fire = MaterialLib.Emissive(new Color(1f, 0.45f, 0.12f), 4f);
            for (int i = 0; i < 8; i++)
            {
                float a = (i * 45f + 22.5f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 15f;
                PieceFactory.Part(PrimitiveType.Cylinder, root, p + Vector3.up * 2.3f, new Vector3(1.3f, 3f, 1.3f), stone);
                PieceFactory.Part(PrimitiveType.Cube, root, p + Vector3.up * 5.5f, new Vector3(1.8f, 0.4f, 1.8f), stone);
                if (i % 2 == 0)
                {
                    PieceFactory.Part(PrimitiveType.Sphere, root, p + Vector3.up * 6f, new Vector3(0.8f, 0.6f, 0.8f), fire);
                    var lg = new GameObject("Brazier Light").AddComponent<Light>();
                    lg.transform.SetParent(root, false);
                    lg.transform.position = p + Vector3.up * 6.5f;
                    lg.type = LightType.Point;
                    lg.color = new Color(1f, 0.55f, 0.25f);
                    lg.range = 18f;
                    lg.intensity = 4f;
                }
            }
        }

        void SetupLighting()
        {
            Light sun = null;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
            if (sun == null)
            {
                sun = new GameObject("Directional Light").AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            sun.color = new Color(0.75f, 0.78f, 0.95f);
            sun.intensity = 0.9f;
            sun.shadows = LightShadows.Soft;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.2f, 0.19f, 0.24f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.025f, 0.025f, 0.04f);
            RenderSettings.fogStartDistance = 20f;
            RenderSettings.fogEndDistance = 55f;
        }
    }

    static class GameBootstrap
    {
        /// <summary>Auto-starts the game in any scene that doesn't already contain a GameManager.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<GameManager>() != null) return;
            new GameObject("GameManager").AddComponent<GameManager>();
        }
    }
}

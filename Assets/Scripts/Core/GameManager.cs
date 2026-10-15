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
        public static readonly Color LightSquare = new Color(0.74f, 0.7f, 0.62f);
        public static readonly Color DarkSquare = new Color(0.26f, 0.12f, 0.1f);

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

        public enum Mode { Game, Practice, Effects }

        [Tooltip("Mode this scene boots into (set by the world bake).")]
        [SerializeField] Mode sceneMode = Mode.Game;
        [Tooltip("Baked piece prefabs (enemy component + model), used instead of building pieces at runtime.")]
        [SerializeField] GameObject[] piecePrefabs = new GameObject[0];

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


        bool returnedToStart;
        bool musicStarted;

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

        // Ten rounds. Pieces enter on their real starting squares; difficulty ramps up to the Queen.
        static readonly WaveDef[] Waves =
        {
            new WaveDef { name = "I. The Opening", pieces = new[] {
                (PieceType.Pawn, "c7"), (PieceType.Pawn, "d7"), (PieceType.Pawn, "e7"), (PieceType.Pawn, "f7") } },
            new WaveDef { name = "II. Knight's Gambit", pieces = new[] {
                (PieceType.Knight, "g8"), (PieceType.Pawn, "d7"), (PieceType.Pawn, "e7") } },
            new WaveDef { name = "III. The Long Diagonal", pieces = new[] {
                (PieceType.Bishop, "c8"), (PieceType.Pawn, "c7"), (PieceType.Pawn, "d7"), (PieceType.Pawn, "f7") } },
            new WaveDef { name = "IV. Minor Pieces", pieces = new[] {
                (PieceType.Knight, "b8"), (PieceType.Bishop, "f8"), (PieceType.Pawn, "d7"), (PieceType.Pawn, "e7") } },
            new WaveDef { name = "V. Castle Walls", pieces = new[] {
                (PieceType.Rook, "a8"), (PieceType.Pawn, "b7"), (PieceType.Pawn, "d7"), (PieceType.Pawn, "f7") } },
            new WaveDef { name = "VI. Twin Cavalry", pieces = new[] {
                (PieceType.Knight, "b8"), (PieceType.Knight, "g8"), (PieceType.Bishop, "c8") } },
            new WaveDef { name = "VII. Fianchetto", pieces = new[] {
                (PieceType.Bishop, "c8"), (PieceType.Bishop, "f8"), (PieceType.Rook, "h8") } },
            new WaveDef { name = "VIII. Rook Lift", pieces = new[] {
                (PieceType.Rook, "a8"), (PieceType.Rook, "h8"), (PieceType.Knight, "g8"), (PieceType.Pawn, "d7"), (PieceType.Pawn, "e7") } },
            new WaveDef { name = "IX. The Vanguard", pieces = new[] {
                (PieceType.Knight, "b8"), (PieceType.Knight, "g8"), (PieceType.Bishop, "c8"), (PieceType.Bishop, "f8"),
                (PieceType.Rook, "a8"), (PieceType.Pawn, "d7"), (PieceType.Pawn, "e7") } },
            // Final round: the Queen with 6 pawns and one rook, knight and bishop.
            new WaveDef { name = "X. The Black Army", pieces = new[] {
                (PieceType.Pawn, "b7"), (PieceType.Pawn, "c7"), (PieceType.Pawn, "d7"),
                (PieceType.Pawn, "e7"), (PieceType.Pawn, "f7"), (PieceType.Pawn, "g7"),
                (PieceType.Knight, "b8"), (PieceType.Queen, "d8"), (PieceType.Bishop, "f8"), (PieceType.Rook, "h8") } },
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

            CurrentMode = StartMode != Mode.Game ? StartMode : sceneMode;
            StartMode = Mode.Game;

            // The world is baked into the scene (KING ▸ Setup ▸ Bake World Into Scenes); anything missing is built here.
            var board = FindFirstObjectByType<ArenaBoard>();
            if (board == null)
            {
                board = WorldBuilder.BuildArena();
                WorldBuilder.ApplyLighting();
            }
            WorldBuilder.EnsureColliders(board);
            BoardFx.Register(board.Grid);

            Player = FindFirstObjectByType<PlayerController>();
            if (Player == null) Player = WorldBuilder.BuildPlayer();
            else Player.Build();
            Player.ResetForWave(SquareToWorld("e1"), 0f);

            var cam = WorldBuilder.SetupCamera();
            Rig = cam.GetComponent<CameraRig>();
            Rig.Init(cam, Player);

            if (GetComponent<Hud>() == null) gameObject.AddComponent<Hud>();
            EnterState(CurrentMode != Mode.Game ? GameState.Playing : GameState.Title);
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
            BoardSlots.Clear();

            bool keepPlayer = State == GameState.WaveClear;
            if (!keepPlayer)
            {
                Player.ResetForWave(SquareToWorld("e1"), 0f);
                Rig.Snap();
            }

            foreach (var (type, square) in Waves[index].pieces)
                SpawnEnemy(type, SquareToWorld(square));

            ShowBanner(Waves[index].name);
            Sfx.Play("GAME_RoundStart");
            bool final = index == Waves.Length - 1;
            if (final) Sfx.Play("GAME_FinalRound");
            Sfx.Play(final ? "BGM_Boss" : "BGM_Battle");
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
            var at = new Vector3(pos.x, 0f, pos.z);
            var facing = Quaternion.LookRotation(Vector3.back);
            T e;
            Transform model;
            var prefab = FindPiecePrefab<T>();
            if (prefab != null)
            {
                // Baked prefab: the enemy component plus its model (first child).
                e = Instantiate(prefab, at, facing).GetComponent<T>();
                e.name = prefab.name;
                model = e.transform.GetChild(0);
            }
            else
            {
                var go = new GameObject(typeof(T).Name);
                go.transform.SetPositionAndRotation(at, facing);
                e = go.AddComponent<T>();
                model = PieceFactory.BuildEnemy(modelType, go.transform);
            }
            e.Setup(modelType, model);
            Enemies.Add(e);
            return e;
        }

        GameObject FindPiecePrefab<T>() where T : EnemyBase
        {
            if (piecePrefabs == null) return null;
            foreach (var p in piecePrefabs)
            {
                if (p == null) continue;
                var c = p.GetComponent<EnemyBase>();
                if (c != null && c.GetType() == typeof(T)) return p;
            }
            return null;
        }

        public void ClearEnemies()
        {
            foreach (var e in Enemies) if (e != null) Destroy(e.gameObject);
            Enemies.Clear();
            BoardSlots.Clear();
        }

        public int DamageEvents { get; private set; }

        public void ReportDamage(EnemyBase e, float amount)
        {
            DamageEvents++;
            Sfx.Play("UI_DamagePopup");
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
            Sfx.Play("GAME_Defeat");
            ResetTime();
            EnterState(GameState.Dead);
        }

        /// <summary>
        /// Pieces attacking right now in the same turn pool as <paramref name="except"/>: pawns share their own pool, the
        /// other pieces another, so pawns keep pressing while the bigger pieces take their turns.
        /// </summary>
        public int ActiveAttackers(EnemyBase except)
        {
            int n = 0;
            bool pawn = except != null && except.Type == PieceType.Pawn;
            foreach (var e in Enemies)
                if (e != null && e != except && !e.IsDead && e.IsAttacking && (e.Type == PieceType.Pawn) == pawn) n++;
            return n;
        }

        /// <summary>No other (non-boss) piece in the same turn pool has been waiting longer for its turn to strike.</summary>
        public bool IsNextInLine(EnemyBase e)
        {
            float mine = e.ReadySince;
            bool pawn = e.Type == PieceType.Pawn;
            foreach (var o in Enemies)
                if (o != null && o != e && !o.IsDead && !o.IsBoss && (o.Type == PieceType.Pawn) == pawn && o.ReadySince < mine) return false;
            return true;
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
            if (Player != null) BoardSlots.UpdateKing(Player.FlatPos);
            if (!musicStarted)
            {
                // First frame (listeners are attached by now): ambience and the opening music.
                musicStarted = true;
                Sfx.Play("AMB_Hall");
                if (CurrentMode == Mode.Practice) Sfx.Play("BGM_Practice");
                else if (State == GameState.Title) Sfx.Play("BGM_Title");
            }
            StateTime += Time.unscaledDeltaTime;
            TickTime();

            switch (State)
            {
                case GameState.Title:
                    if (GameInput.Confirm) { Sfx.Play("UI_Title_Start"); StartWave(0); }
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
                            Sfx.Play("GAME_Victory");
                        }
                        else
                        {
                            Player.Rest();
                            returnedToStart = false;
                            ShowBanner("Pieces Captured");
                            Sfx.Play("GAME_RoundClear");
                            EnterState(GameState.WaveClear);
                        }
                    }
                    break;

                case GameState.WaveClear:
                    // Let the King finish his last move, then put him back on e1 before the next round.
                    if (!returnedToStart && StateTime > 2.3f)
                    {
                        returnedToStart = true;
                        Player.ReturnToStart(SquareToWorld("e1"), 0f);
                        Rig.Snap();
                    }
                    if (StateTime > 3.4f) StartWave(WaveIndex + 1);
                    break;

                case GameState.Dead:
                    if (CurrentMode != Mode.Game)
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

        // ---------------------------------------------------------------- lab automation hooks

        /// <summary>Title → first round, as if Confirm was pressed (used by the sound-reference capture).</summary>
        public void DebugStartGame()
        {
            Sfx.Play("UI_Title_Start");
            StartWave(0);
        }

        public void DebugStartWave(int index) => StartWave(Mathf.Clamp(index, 0, Waves.Length - 1));
        public void DebugTogglePause() => TogglePause();

        void TogglePause()
        {
            Paused = !Paused;
            Sfx.Play(Paused ? "GAME_Pause" : "UI_Back");
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
            Sfx.Play("PL_SlowMo_Loop");
            slowScale = scale;
            slowTimer = duration;
            ApplyTimeScale();
        }

        public void EndSlowMo()
        {
            if (slowTimer > 0f) Sfx.Play("PL_SlowMo_End");
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
            if (slowTimer > 0f)
            {
                slowTimer -= dt;
                if (slowTimer <= 0f) Sfx.Play("PL_SlowMo_End");
            }
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

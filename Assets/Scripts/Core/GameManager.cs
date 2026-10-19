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
        public enum GameState { Title, Playing, WaveClear, Upgrade, Dead, Victory }

        public const float TileSize = 2f;
        /// <summary>Half the board's width (8 m for the 8×8 board; the endless board grows a ring every 20 rounds).</summary>
        public static float BoardHalf => BoardSlots.Size * TileSize * 0.5f;
        public static float PlayableHalf => BoardHalf - 0.7f;
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

        /// <summary>Game: the classic ten rounds (scene NormalMode). Endless: endless rounds with upgrades (scene EndlessMode).</summary>
        public enum Mode { Game, Practice, Effects, Endless }

        /// <summary>Endless mode: the round number (1-based).</summary>
        public int Round => WaveIndex + 1;
        public bool IsEndless => CurrentMode == Mode.Endless;
        /// <summary>Round-end items on offer, and the one chosen this time (endless mode).</summary>
        public readonly List<Item> Offers = new List<Item>();
        public readonly List<int> Picked = new List<int>();
        /// <summary>Item card under the cursor / selected with the keys or pad (−1: none).</summary>
        public int OfferCursor { get; set; } = -1;
        ArenaBoard board;

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
            public bool crit;
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
            new WaveDef { name = "I. 오프닝", pieces = new[] {
                (PieceType.Pawn, "c7"), (PieceType.Pawn, "d7"), (PieceType.Pawn, "e7"), (PieceType.Pawn, "f7") } },
            new WaveDef { name = "II. 나이트 갬빗", pieces = new[] {
                (PieceType.Knight, "g8"), (PieceType.Pawn, "d7"), (PieceType.Pawn, "e7") } },
            new WaveDef { name = "III. 긴 대각선", pieces = new[] {
                (PieceType.Bishop, "c8"), (PieceType.Pawn, "c7"), (PieceType.Pawn, "d7"), (PieceType.Pawn, "f7") } },
            new WaveDef { name = "IV. 마이너 피스", pieces = new[] {
                (PieceType.Knight, "b8"), (PieceType.Bishop, "f8"), (PieceType.Pawn, "d7"), (PieceType.Pawn, "e7") } },
            new WaveDef { name = "V. 성벽", pieces = new[] {
                (PieceType.Rook, "a8"), (PieceType.Pawn, "b7"), (PieceType.Pawn, "d7"), (PieceType.Pawn, "f7") } },
            new WaveDef { name = "VI. 쌍둥이 기병", pieces = new[] {
                (PieceType.Knight, "b8"), (PieceType.Knight, "g8"), (PieceType.Bishop, "c8") } },
            new WaveDef { name = "VII. 피앙케토", pieces = new[] {
                (PieceType.Bishop, "c8"), (PieceType.Bishop, "f8"), (PieceType.Rook, "h8") } },
            new WaveDef { name = "VIII. 룩 리프트", pieces = new[] {
                (PieceType.Rook, "a8"), (PieceType.Rook, "h8"), (PieceType.Knight, "g8"), (PieceType.Pawn, "d7"), (PieceType.Pawn, "e7") } },
            new WaveDef { name = "IX. 선봉대", pieces = new[] {
                (PieceType.Knight, "b8"), (PieceType.Knight, "g8"), (PieceType.Bishop, "c8"), (PieceType.Bishop, "f8"),
                (PieceType.Rook, "a8"), (PieceType.Pawn, "d7"), (PieceType.Pawn, "e7") } },
            // Final round: the Queen with 6 pawns and one rook, knight and bishop.
            new WaveDef { name = "X. 흑색 군대", pieces = new[] {
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
            // One per scene (a manager left over from the previous scene while switching modes doesn't count).
            if (Instance != null && Instance != this && Instance.gameObject.scene == gameObject.scene) { Destroy(gameObject); return; }
            Instance = this;

            CurrentMode = StartMode != Mode.Game ? StartMode : sceneMode;
            StartMode = Mode.Game;

            // The world is baked into the scene (KING ▸ Setup ▸ Bake World Into Scenes); anything missing is built here.
            board = FindFirstObjectByType<ArenaBoard>();
            if (board == null)
            {
                board = WorldBuilder.BuildArena();
                WorldBuilder.ApplyLighting();
            }
            WorldBuilder.EnsureColliders(board);
            BoardSlots.SetSize(8);   // static: a previous endless run may have grown it
            BoardFx.Register(board.Grid);

            Player = FindFirstObjectByType<PlayerController>();
            if (Player == null) Player = WorldBuilder.BuildPlayer();
            else Player.Build();
            if (IsEndless) Player.Stats = PlayerStats.EndlessStart();
            Player.ResetForWave(SquareToWorld("e1"), 0f);

            var cam = WorldBuilder.SetupCamera();
            Rig = cam.GetComponent<CameraRig>();
            Rig.Init(cam, Player);

            if (GetComponent<Hud>() == null) gameObject.AddComponent<Hud>();
            EnterState(UsesRounds ? GameState.Title : GameState.Playing);
            TitleChoice = IsEndless ? 1 : 0;
            // Picked on the other scene's title: start right away instead of showing this title again.
            skipTitle = UsesRounds && startFromMenu;
            startFromMenu = false;
        }

        /// <summary>The classic game and the endless mode run rounds (title, round clears, restarts); the labs don't.</summary>
        public bool UsesRounds => CurrentMode == Mode.Game || CurrentMode == Mode.Endless;

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
                Record = new RunRecord();
            }
            WaveIndex = index;
            Paused = false;
            ResetTime();

            foreach (var e in Enemies) if (e != null) Destroy(e.gameObject);
            Enemies.Clear();
            BoardSlots.Clear();

            bool keepPlayer = State == GameState.WaveClear || State == GameState.Upgrade;
            // A new endless run starts the King from scratch (weak stats, no upgrades) on the small board.
            if (IsEndless && index == 0 && !keepPlayer) Player.Stats = PlayerStats.EndlessStart();
            if (!keepPlayer)
            {
                Player.ResetForWave(SquareToWorld("e1"), 0f);
                Rig.Snap();
            }

            if (IsEndless)
            {
                StartEndlessRound(index + 1);
                return;
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

        // ---------------------------------------------------------------- endless mode

        void StartEndlessRound(int round)
        {
            // Every 20 rounds the board grows a ring of squares (and shrinks back for a new run).
            int size = EndlessRounds.BoardSize(round);
            bool grew = size > BoardSlots.Size;
            if (size != BoardSlots.Size) board.Resize(size);

            var army = EndlessRounds.Army(round);
            var squares = EndlessRounds.SpawnSquares(BoardSlots.ToSquare(Player.FlatPos), army.Count);
            for (int i = 0; i < army.Count && i < squares.Count; i++)
                SpawnEmpowered(army[i].type, army[i].tier, BoardSlots.Center(squares[i]));

            bool boss = EndlessRounds.IsBossRound(round);
            string banner = EndlessRounds.Title(round);
            if (grew) banner += "  ·  체스판이 넓어진다";
            if (round % 40 == 0) banner += "  ·  흑색 군대가 더 강해진다";
            ShowBanner(banner);
            Sfx.Play("GAME_RoundStart");
            if (boss) Sfx.Play("GAME_FinalRound");
            Sfx.Play(boss ? "BGM_Boss" : "BGM_Battle");
            EnterState(GameState.Playing);
        }

        /// <summary>
        /// Spawns a piece at the given empowered tier (0 = normal) with the endless mode's health scaling (every 40
        /// rounds; queens are sturdier there too). In the other modes only the tier applies.
        /// </summary>
        public EnemyBase SpawnEmpowered(PieceType type, int tier, Vector3 pos)
        {
            var e = SpawnEnemy(type, pos);
            float hp = IsEndless ? EndlessRounds.HealthMultiplier(Round) * (type == PieceType.Queen ? EndlessRounds.QueenHealthBoost : 1f) : 1f;
            e.Empower(tier, hp, IsEndless ? EndlessRounds.StaggerMultiplier(Round) : 1f);
            return e;
        }

        /// <summary>A queen at half health calls her pieces onto the squares around her.</summary>
        public void SummonFor(EnemyBase queen)
        {
            if (!IsEndless)
            {
                // Classic: two pawns on the squares to either side of her.
                Vector3 side = queen.transform.right * 3f;
                SpawnEnemy(PieceType.Pawn, BoardSlots.Center(BoardSlots.ToSquare(queen.FlatPos + side)));
                SpawnEnemy(PieceType.Pawn, BoardSlots.Center(BoardSlots.ToSquare(queen.FlatPos - side)));
                return;
            }
            var summons = EndlessRounds.QueenSummons(Round);
            var center = BoardSlots.ToSquare(queen.FlatPos);
            var king = BoardSlots.KingSquare;
            var free = new List<Vector2Int>();
            for (int ring = 1; ring <= 3 && free.Count < summons.Count; ring++)
                for (int x = -ring; x <= ring; x++)
                for (int y = -ring; y <= ring; y++)
                {
                    var s = center + new Vector2Int(x, y);
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != ring || !BoardSlots.OnBoard(s) || BoardSlots.Chebyshev(s, king) < 2) continue;
                    if (!free.Contains(s)) free.Add(s);
                }
            for (int i = 0; i < summons.Count && i < free.Count; i++)
                SpawnEmpowered(summons[i].type, summons[i].tier, BoardSlots.Center(free[i]));
        }

        /// <summary>How many cards must be chosen before the confirm button unlocks (one item).</summary>
        public int PicksNeeded => Mathf.Min(bossPick ? Upgrades.BossPicks : Upgrades.Picks, Offers.Count);
        bool bossPick;   // after a boss round: five items offered, two kept
        public bool CanConfirmUpgrades => State == GameState.Upgrade && upgradeDoneAt < 0f && Picked.Count == PicksNeeded;
        /// <summary>The cursor index of the confirm button (after the cards).</summary>
        public int ConfirmSlot => Offers.Count;

        /// <summary>Selects item card <paramref name="i"/> (another card switches to it), or unselects it if it was selected.</summary>
        public void PickUpgrade(int i)
        {
            if (State != GameState.Upgrade || upgradeDoneAt >= 0f || i < 0 || i >= Offers.Count) return;
            if (Picked.Remove(i)) { Sfx.Play("UI_Back"); return; }
            if (Picked.Count >= PicksNeeded) Picked.RemoveAt(0);   // one item: choosing another card switches to it
            Picked.Add(i);
            Sfx.Play("UI_Confirm");
        }

        /// <summary>Confirm button: the chosen item is kept for good and the next round starts.</summary>
        public void ConfirmUpgrades()
        {
            if (!CanConfirmUpgrades) return;
            foreach (int i in Picked) Player.ApplyItem(Offers[i]);
            Record.upgrades += Picked.Count;
            Sfx.Play("UI_Title_Start");
            upgradeDoneAt = StateTime;
        }

        float upgradeDoneAt = -1f;

        void EnterUpgrade()
        {
            Offers.Clear();
            bossPick = EndlessRounds.IsBossRound(Round);
            Offers.AddRange(Upgrades.Roll(Player.Stats, Player.Items, Round, bossPick, bossPick ? Upgrades.BossOffered : Upgrades.Offered));
            Picked.Clear();
            OfferCursor = -1;
            upgradeDoneAt = Offers.Count == 0 ? 0f : -1f;
            EnterState(GameState.Upgrade);
        }

        void UpdateUpgrade()
        {
            if (upgradeDoneAt >= 0f)
            {
                // A short beat to see the upgrades land, then on to the next round.
                if (StateTime - upgradeDoneAt > 0.5f) StartWave(WaveIndex + 1);
                return;
            }
            int key = GameInput.NumberPressed;
            if (key >= 1 && key <= Offers.Count) PickUpgrade(key - 1);
            // ←/→ walk the cards and the confirm button; Enter / A toggles a card or presses the button.
            int step = GameInput.MenuStep, slots = Offers.Count + 1;
            if (step != 0)
            {
                OfferCursor = OfferCursor < 0 ? 0 : (OfferCursor + step + slots) % slots;
                Sfx.Play("UI_Hover");
            }
            if (GameInput.Confirm)
            {
                if (OfferCursor == ConfirmSlot || (OfferCursor < 0 && CanConfirmUpgrades)) ConfirmUpgrades();
                else if (OfferCursor >= 0) PickUpgrade(OfferCursor);
            }
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

        /// <summary>Play record of the current run (shown on the endless mode's results screen).</summary>
        public class RunRecord
        {
            public float damageDealt, damageTaken, biggestHit;
            public int kills, darkKills, crits, flasksUsed, upgrades;
        }

        public RunRecord Record { get; private set; } = new RunRecord();

        public void ReportKill(EnemyBase e)
        {
            Record.kills++;
            if (e.EliteTier > 0) Record.darkKills++;
            if (Player != null)
            {
                Player.Restore(Player.Stats.lifeSteal);   // life steal: a fixed amount of health back for every capture
                Player.OnCapture();
            }
        }

        public void ReportPlayerDamage(float amount) => Record.damageTaken += amount;
        public void ReportFlask() => Record.flasksUsed++;

        /// <summary>Damage over time (Black Aurora): counted in the run record, but no popup (it ticks too often).</summary>
        public void ReportDotDamage(float amount) => Record.damageDealt += amount;

        public void ReportDamage(EnemyBase e, float amount, bool crit = false)
        {
            DamageEvents++;
            Record.damageDealt += amount;
            Record.biggestHit = Mathf.Max(Record.biggestHit, amount);
            if (crit) Record.crits++;
            Sfx.Play("UI_DamagePopup");
            Popups.Add(new DamagePopup
            {
                position = e.transform.position + Vector3.up * (e.IsBoss ? 3.3f : 2.3f) + Random.insideUnitSphere * 0.3f,
                amount = amount,
                time = Time.unscaledTime,
                crit = crit,
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
            // An endless run ends here: keep it if it's the best yet.
            NewBest = IsEndless && BestRecord.Submit(Round, RunTime, Record.damageDealt, Record.kills);
            EnterState(GameState.Dead);
        }

        /// <summary>The endless run that just ended beat the saved best.</summary>
        public bool NewBest { get; private set; }

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
                    if (skipTitle) { skipTitle = false; StartWave(0); break; }
                    int step = GameInput.MenuStep;
                    if (step != 0) { TitleChoice = (TitleChoice + step + 2) % 2; Sfx.Play("UI_Hover"); }
                    if (GameInput.Confirm) ChooseMode(TitleChoice);
                    break;

                case GameState.Playing:
                    if (GameInput.Pause) TogglePause();
                    if (Paused && GameInput.MainMenu) { QuitToTitle(); break; }
                    if (Paused) break;
                    RunTime += Time.deltaTime;
                    ResolveCollisions();
                    if (UsesRounds && AliveCount() == 0 && StateTime > 1f)
                    {
                        if (!IsEndless && WaveIndex >= Waves.Length - 1)
                        {
                            EnterState(GameState.Victory);
                            Sfx.Play("GAME_Victory");
                        }
                        else
                        {
                            Player.Rest();
                            returnedToStart = false;
                            ShowBanner("기물 포획");
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
                    if (StateTime > 3.4f)
                    {
                        // Endless: pick two of five permanent upgrades before the next round.
                        if (IsEndless) EnterUpgrade();
                        else StartWave(WaveIndex + 1);
                    }
                    break;

                case GameState.Upgrade:
                    UpdateUpgrade();
                    break;

                case GameState.Dead:
                    if (!UsesRounds)
                    {
                        if (StateTime > 1.5f) RevivePlayerInPlace();
                        break;
                    }
                    // Endless: the run ends — the results screen's confirm goes back to the title. Classic: retry the round.
                    // (The endless results screen takes the nickname and its own confirm — see SubmitRun.)
                    if (!IsEndless && StateTime > 1.8f && GameInput.Confirm) StartWave(WaveIndex);
                    break;

                case GameState.Victory:
                    if (StateTime > 2.5f && GameInput.Confirm) StartWave(0);
                    break;
            }
        }

        // ---------------------------------------------------------------- lab automation hooks

        // ---------------------------------------------------------------- main menu (mode select)

        /// <summary>Scenes of the two game modes (both in the build settings).</summary>
        public const string NormalScene = "NormalMode", EndlessScene = "EndlessMode";

        /// <summary>Title menu selection: 0 normal mode (ten rounds), 1 endless mode.</summary>
        public int TitleChoice { get; set; }

        static bool startFromMenu;   // set before loading the other mode's scene: it skips its title
        bool skipTitle;

        /// <summary>
        /// Starts the chosen mode: this scene's own mode begins at once; the other loads its scene, which then starts
        /// without showing the title again.
        /// </summary>
        public void ChooseMode(int choice)
        {
            if (State != GameState.Title) return;
            Sfx.Play("UI_Title_Start");
            bool endless = choice == 1;
            if (endless == IsEndless)
            {
                StartWave(0);
                return;
            }
            startFromMenu = true;
            UnityEngine.SceneManagement.SceneManager.LoadScene(endless ? EndlessScene : NormalScene);
        }

        /// <summary>True while the finished run is on its way to the online ranking.</summary>
        public bool Submitting { get; private set; }

        /// <summary>
        /// Endless results confirm: sends the run to the online ranking under <paramref name="nickname"/> (at most a
        /// few seconds — offline just skips it), then back to the title.
        /// </summary>
        public async void SubmitRun(string nickname)
        {
            if (Submitting || State != GameState.Dead || !IsEndless) return;
            Submitting = true;
            nickname = OnlineBoard.Sanitize(nickname);
            if (nickname.Length > 0) OnlineBoard.SavedName = nickname;
            Sfx.Play("UI_Confirm");
            var send = OnlineBoard.Submit(nickname, Round, RunTime, Record.kills, Record.damageDealt);
            await System.Threading.Tasks.Task.WhenAny(send, System.Threading.Tasks.Task.Delay(6000));
            if (this == null) return;   // the scene went away meanwhile
            Submitting = false;
            BackToTitle();
        }

        /// <summary>Pause ▸ 메인 메뉴: abandons the run (nothing is recorded) and reloads this scene's title.</summary>
        public void QuitToTitle()
        {
            if (!UsesRounds) return;
            Paused = false;
            Time.timeScale = 1f;
            Sfx.Play("UI_Back");
            UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.name);
        }

        /// <summary>Endless results → the title: the scene reloads (a fresh board, King and run).</summary>
        public void BackToTitle()
        {
            if (State != GameState.Dead && State != GameState.Victory) return;
            Sfx.Play("UI_Confirm");
            UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.name);
        }

        /// <summary>Title → first round, as if Confirm was pressed (used by the sound-reference capture).</summary>
        public void DebugStartGame()
        {
            Sfx.Play("UI_Title_Start");
            StartWave(0);
        }

        public void DebugStartWave(int index) => StartWave(IsEndless ? Mathf.Max(0, index) : Mathf.Clamp(index, 0, Waves.Length - 1));
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
}

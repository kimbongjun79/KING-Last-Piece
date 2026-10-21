using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// 무쌍 모드 (scene EndlessMode): survive 30 minutes on a 12×12 board while the black army keeps coming — a
    /// Vampire-Survivors-style spawn director.
    /// <list type="bullet">
    /// <item>A floor of pieces on the board that rises with time (a few at first, ~60 by the end), topped up a piece at
    /// a time, and a steady drip on top of it.</item>
    /// <item>Spawn patterns (<see cref="MusouPattern"/>), one at a time: each sets pieces down and waits for a kill
    /// condition (all of them taken, a key piece taken, every 5 pawns…), then the next is chosen by the time and by what
    /// is still standing on the board.</item>
    /// <item>Every 2 minutes the army's strength is refreshed from logarithmic curves (health, attack); every fifth refresh
    /// the pieces grow a size step. More dark pieces as time goes on.</item>
    /// <item>Bosses on a schedule: a queen at 7:30, a dark I queen at 15:00, a dark II queen at 22:30. Each one taken
    /// opens a boss chest (five items, keep two).</item>
    /// <item>Captures give experience; every level is an item choice (three, keep one).</item>
    /// </list>
    /// </summary>
    public class MusouDirector
    {
        public const float Duration = 30f * 60f, EraLength = 10f * 60f;
        public const int BoardSize = 12, MaxAlive = 70;
        static readonly (float at, int tier)[] BossTimes = { (450f, 0), (900f, 1), (1350f, 2) };

        readonly GameManager game;
        public MusouDirector(GameManager game) => this.game = game;

        /// <summary>Seconds survived (game time while fighting: not during level-ups or the pause).</summary>
        public float Elapsed { get; private set; }
        public float Minutes => Elapsed / 60f;
        public float Remaining => Mathf.Max(0f, Duration - Elapsed);
        /// <summary>Strength step: 0 for the first ten minutes, then 1, then 2.</summary>
        public int Era => Mathf.Min(2, Mathf.FloorToInt(Elapsed / EraLength));
        public bool Cleared => Elapsed >= Duration;

        /// <summary>Lab: jumps the clock forward (the eras, the boss schedule and the patterns follow).</summary>
        public void DebugAdvanceTo(float seconds) => Elapsed = Mathf.Max(Elapsed, seconds);

        // ---------------------------------------------------------------- experience

        public int Level { get; private set; } = 1;
        public float Xp { get; private set; }
        /// <summary>Experience for the next level (6 at level 1, +2.2 a level).</summary>
        public float XpNeeded => Mathf.Round(4f + 2.2f * Level);
        public int PendingLevels { get; set; }
        public bool PendingChest { get; set; }

        static float XpFor(EnemyBase e)
        {
            float b = e.Type switch
            {
                PieceType.Knight => 3f, PieceType.Bishop => 3f, PieceType.Rook => 5f, PieceType.Queen => 30f, _ => 1f,
            };
            return b * (1f + 0.5f * e.EliteTier);
        }

        void AddXp(float xp)
        {
            Xp += xp * (1f + 0.25f * Era);
            while (Xp >= XpNeeded)
            {
                Xp -= XpNeeded;
                Level++;
                PendingLevels++;
            }
        }

        // ---------------------------------------------------------------- strength

        // The army's strength is refreshed every two minutes ("strength level" k = 0…15) from logarithmic curves, so it
        // climbs fast early and levels off: health ×(1 + 0.9·ln(1 + 0.6k)) — ×2.25 at 10 min, ×3.07 at 30 — and
        // attack ×(1 + 0.45·ln(1 + 0.6k)) — ×1.62 at 10 min, ×2.04 at 30. Every five refreshes (10, 20 min) the pieces
        // also grow a size step (+10%). Pieces keep the strength they were set down with.
        public const float StrengthInterval = 120f;
        public const int SizeEvery = 5;

        /// <summary>Strength level: 0 at the start, +1 every two minutes.</summary>
        public int StrengthLevel => Mathf.FloorToInt(Elapsed / StrengthInterval);
        static float Curve(int k, float scale) => 1f + scale * Mathf.Log(1f + 0.6f * k);
        public float HealthBoost => Curve(StrengthLevel, 0.9f);
        public float DamageBoost => Curve(StrengthLevel, 0.45f);
        public int SizeSteps => StrengthLevel / SizeEvery;
        public float SizeMult => 1f + 0.1f * SizeSteps;
        public float NextStrengthIn => StrengthInterval - Elapsed % StrengthInterval;

        /// <summary>Health of a newly set piece at the current strength (queens are sturdier, as in the old endless mode).</summary>
        public float HealthMult(PieceType t) => HealthBoost * (t == PieceType.Queen ? EndlessRounds.QueenHealthBoost : 1f);
        public float StaggerMult => 1f + 0.3f * SizeSteps;

        /// <summary>How likely a common piece is dark: none for the first three minutes, rising to 60% by the end.</summary>
        float DarkChance => Mathf.Clamp((Minutes - 3f) / 27f * 0.6f + 0.12f * Era, 0f, 0.7f);

        /// <summary>Tier of a common piece: mostly plain; a dark one is tier I, II from 10 minutes, III from 20.</summary>
        public int RollTier(float bonus = 0f)
        {
            if (Random.value >= DarkChance + bonus) return 0;
            int top = 1 + Era;
            return Mathf.Clamp(Random.Range(1, top + 1), 1, EnemyBase.MaxEliteTier);
        }

        /// <summary>A dark piece's tier for the patterns that bring strong pieces (at least I, rising with the era).</summary>
        public int DarkTier(int extra = 0) => Mathf.Clamp(1 + Era + extra, 1, EnemyBase.MaxEliteTier);

        /// <summary>A common piece: pawns mostly; knights from 1 minute, bishops from 2, rooks from 4.</summary>
        public PieceType RollType()
        {
            float m = Minutes;
            float wP = 5f, wN = m >= 1f ? 1.2f : 0f, wB = m >= 2f ? 1.1f : 0f, wR = m >= 4f ? 0.8f + m / 30f : 0f;
            float r = Random.value * (wP + wN + wB + wR);
            if ((r -= wR) < 0f) return PieceType.Rook;
            if ((r -= wB) < 0f) return PieceType.Bishop;
            if ((r -= wN) < 0f) return PieceType.Knight;
            return PieceType.Pawn;
        }

        // ---------------------------------------------------------------- the board

        public Vector2Int King => BoardSlots.ToSquare(game.Player.FlatPos);

        public int Alive
        {
            get
            {
                int n = 0;
                foreach (var e in game.Enemies) if (e != null && !e.IsDead) n++;
                return n;
            }
        }

        public int Count(System.Predicate<EnemyBase> match)
        {
            int n = 0;
            foreach (var e in game.Enemies) if (e != null && !e.IsDead && match(e)) n++;
            return n;
        }

        public IEnumerable<EnemyBase> Living()
        {
            foreach (var e in game.Enemies) if (e != null && !e.IsDead) yield return e;
        }

        readonly HashSet<Vector2Int> taken = new HashSet<Vector2Int>();
        int takenFrame = -1;

        HashSet<Vector2Int> Taken()
        {
            if (takenFrame == Time.frameCount) return taken;
            takenFrame = Time.frameCount;
            taken.Clear();
            foreach (var e in game.Enemies) if (e != null && !e.IsDead) taken.Add(BoardSlots.ToSquare(e.FlatPos));
            return taken;
        }

        public bool IsFree(Vector2Int s) => BoardSlots.OnBoard(s) && !Taken().Contains(s) && BoardSlots.Chebyshev(s, King) >= 2;

        /// <summary>Free squares between <paramref name="minDist"/> and <paramref name="maxDist"/> squares from the King.</summary>
        public List<Vector2Int> FreeSquares(int minDist, int maxDist)
        {
            var list = new List<Vector2Int>();
            var k = King;
            for (int x = 0; x < BoardSlots.Size; x++)
            for (int y = 0; y < BoardSlots.Size; y++)
            {
                var s = new Vector2Int(x, y);
                int d = BoardSlots.Chebyshev(s, k);
                if (d >= minDist && d <= maxDist && IsFree(s)) list.Add(s);
            }
            return list;
        }

        public bool RandomSquare(int minDist, int maxDist, out Vector2Int s)
        {
            var list = FreeSquares(minDist, maxDist);
            if (list.Count == 0) list = FreeSquares(2, BoardSlots.Size);
            s = list.Count > 0 ? list[Random.Range(0, list.Count)] : default;
            return list.Count > 0;
        }

        /// <summary>Free squares in a row along the board edge farthest from the King, centred across from him.</summary>
        public List<Vector2Int> EdgeRow(int count)
        {
            var k = King;
            int n = BoardSlots.Size - 1;
            // Distances to the four edges: west, east, south, north.
            int[] dist = { k.x, n - k.x, k.y, n - k.y };
            int side = 0;
            for (int i = 1; i < 4; i++) if (dist[i] > dist[side] || (dist[i] == dist[side] && Random.value < 0.5f)) side = i;
            var result = new List<Vector2Int>();
            for (int inward = 0; inward < 3 && result.Count < count; inward++)
                for (int o = 0; o <= n && result.Count < count; o++)
                {
                    int off = (o % 2 == 0 ? 1 : -1) * ((o + 1) / 2);
                    int along = (side < 2 ? k.y : k.x) + off;
                    if (along < 0 || along > n) continue;
                    int line = side switch { 0 => inward, 1 => n - inward, 2 => inward, _ => n - inward };
                    var s = side < 2 ? new Vector2Int(line, along) : new Vector2Int(along, line);
                    if (IsFree(s) && !result.Contains(s)) result.Add(s);
                }
            return result;
        }

        /// <summary>Free squares on a ring <paramref name="radius"/> squares around the King, evenly spread.</summary>
        public List<Vector2Int> Ring(int radius, int count)
        {
            var k = King;
            var ring = new List<(float a, Vector2Int s)>();
            for (int x = -radius; x <= radius; x++)
            for (int y = -radius; y <= radius; y++)
            {
                if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != radius) continue;
                var s = k + new Vector2Int(x, y);
                if (IsFree(s)) ring.Add((Mathf.Atan2(y, x), s));
            }
            ring.Sort((p, q) => p.a.CompareTo(q.a));
            var result = new List<Vector2Int>();
            if (ring.Count == 0) return result;
            float step = Mathf.Max(1f, ring.Count / (float)count);
            for (float i = Random.value * step; i < ring.Count && result.Count < count; i += step) result.Add(ring[(int)i].s);
            return result;
        }

        public EnemyBase Spawn(PieceType type, int tier, Vector2Int s)
        {
            if (Alive >= MaxAlive) return null;
            Taken().Add(s);
            return game.SpawnEmpowered(type, tier, BoardSlots.Center(s));
        }

        /// <summary>A piece on a random free square a few squares from the King.</summary>
        public EnemyBase SpawnNear(PieceType type, int tier, int minDist = 3, int maxDist = 8) =>
            RandomSquare(minDist, maxDist, out var s) ? Spawn(type, tier, s) : null;

        /// <summary>A piece on a free square next to <paramref name="around"/> (summons around a queen, a fallen rook…).</summary>
        public EnemyBase SpawnAround(PieceType type, int tier, Vector2Int around)
        {
            for (int r = 1; r <= 3; r++)
            {
                var options = new List<Vector2Int>();
                for (int x = -r; x <= r; x++)
                for (int y = -r; y <= r; y++)
                {
                    var s = around + new Vector2Int(x, y);
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) == r && IsFree(s)) options.Add(s);
                }
                if (options.Count > 0) return Spawn(type, tier, options[Random.Range(0, options.Count)]);
            }
            return SpawnNear(type, tier);
        }

        // ---------------------------------------------------------------- flow

        /// <summary>Pieces always on the board: a few at first, about 48 at the end (on the 12×12 board).</summary>
        public int MinAlive => Mathf.Min(48, Mathf.RoundToInt(3f + 1.5f * Minutes));
        /// <summary>Seconds between extra pieces dripping in on top of the floor (4 s at first, 0.8 s at the end).</summary>
        float DripInterval => Mathf.Lerp(4f, 0.8f, Elapsed / Duration);

        public MusouPattern Current { get; private set; }
        public string CurrentName => Current?.name;
        int nextBoss, lastStrength;
        float gap = 2.5f, fillTimer, dripTimer = 3f;
        string lastPattern;
        readonly List<EnemyBase> bosses = new List<EnemyBase>();

        public float NextBossIn => nextBoss < BossTimes.Length ? BossTimes[nextBoss].at - Elapsed : -1f;
        public string NextBossName => nextBoss < BossTimes.Length ? QueenName(BossTimes[nextBoss].tier) : "";
        static string QueenName(int tier) => tier == 0 ? "흑색 여왕" : $"다크 {new string('I', tier)} 여왕";

        public void Begin()
        {
            game.ShowBanner("무쌍 모드  ·  30분을 버텨라");
            for (int i = 0; i < 3; i++) SpawnNear(PieceType.Pawn, 0, 4, 7);
        }

        public void Update(float dt)
        {
            Elapsed += dt;
            if (StrengthLevel != lastStrength)
            {
                // Every two minutes: the strength refreshes (every fifth time the pieces grow too).
                lastStrength = StrengthLevel;
                bool grow = StrengthLevel % SizeEvery == 0;
                game.ShowBanner($"흑색 군대 강화 {StrengthLevel}단계  ·  체력 ×{HealthBoost:0.00}  공격 ×{DamageBoost:0.00}" +
                                (grow ? "  ·  기물이 커진다" : ""));
                Sfx.Play(grow ? "GAME_FinalRound" : "GAME_RoundStart");
            }
            if (nextBoss < BossTimes.Length && Elapsed >= BossTimes[nextBoss].at) SpawnBoss(BossTimes[nextBoss++].tier);

            // The floor: top the board up a piece at a time; and a steady drip above it.
            int alive = Alive;
            fillTimer -= dt;
            if (alive < MinAlive && fillTimer <= 0f)
            {
                fillTimer = 0.3f;
                SpawnNear(RollType(), RollTier());
            }
            dripTimer -= dt;
            if (dripTimer <= 0f)
            {
                dripTimer = DripInterval;
                if (alive < MaxAlive - 8) SpawnNear(RollType(), RollTier(), 4, 9);
            }

            // One pattern at a time; a short breath between them (shorter as time goes on).
            if (Current == null)
            {
                gap -= dt;
                if (gap <= 0f) StartPattern();
            }
            else
            {
                Current.Tick(this, dt);
                if (Current.Done)
                {
                    lastPattern = Current.name;
                    Current = null;
                    gap = Mathf.Lerp(3f, 1f, Elapsed / Duration);
                }
            }
        }

        void StartPattern()
        {
            var options = new List<MusouPattern>();
            float total = 0f;
            foreach (var make in MusouPatterns.All)
            {
                var p = make();
                if (Minutes < p.from || Minutes >= p.to || !p.Fits(this)) continue;
                if (p.name == lastPattern) p.weight *= 0.25f;
                options.Add(p);
                total += p.weight;
            }
            if (options.Count == 0) { gap = 2f; return; }
            float r = Random.value * total;
            foreach (var p in options)
            {
                if ((r -= p.weight) > 0f) continue;
                Current = p;
                break;
            }
            Current ??= options[options.Count - 1];
            Current.Begin(this);
        }

        void SpawnBoss(int tier)
        {
            if (!RandomSquare(5, 9, out var s)) return;
            var q = game.SpawnEmpowered(PieceType.Queen, tier, BoardSlots.Center(s));
            if (q == null) return;
            bosses.Add(q);
            game.ShowBanner($"{QueenName(tier)}이 나타났다");
            Sfx.Play("GAME_FinalRound");
            Sfx.Play("BGM_Boss");
        }

        public void OnKill(EnemyBase e)
        {
            AddXp(XpFor(e));
            Current?.OnKill(this, e);
            if (bosses.Remove(e))
            {
                PendingChest = true;
                game.ShowBanner("여왕을 쓰러뜨렸다  ·  보스 보상");
                if (bosses.Count == 0) Sfx.Play("BGM_Battle");
            }
        }
    }
}

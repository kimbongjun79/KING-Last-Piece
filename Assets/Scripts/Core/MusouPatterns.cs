using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// One spawn pattern of the 무쌍 mode: it sets pieces down (all at once, in bursts, or as kills come in) and is done
    /// when its kill condition is met — then the director picks the next one by the time and by what is left on the
    /// board. <see cref="from"/> / <see cref="to"/> are the minutes it can be chosen in.
    /// </summary>
    public abstract class MusouPattern
    {
        public string name;
        public float from, to = 31f, weight = 1f;
        public bool Done { get; protected set; }

        /// <summary>Whether the pattern suits the board as it stands (what is left of the previous patterns).</summary>
        public virtual bool Fits(MusouDirector d) => true;
        public abstract void Begin(MusouDirector d);
        public virtual void Tick(MusouDirector d, float dt) { }
        public virtual void OnKill(MusouDirector d, EnemyBase e) { }

        /// <summary>The pieces this pattern put down (or took charge of).</summary>
        protected readonly List<EnemyBase> mine = new List<EnemyBase>();
        protected int total;

        protected void Add(EnemyBase e)
        {
            if (e == null) return;
            mine.Add(e);
            total++;
        }

        protected int MineAlive
        {
            get
            {
                int n = 0;
                foreach (var e in mine) if (e != null && !e.IsDead) n++;
                return n;
            }
        }

        /// <summary>Share of this pattern's pieces already taken (1 when it put none down).</summary>
        protected float TakenShare => total == 0 ? 1f : 1f - MineAlive / (float)total;
    }

    /// <summary>The pattern catalogue, by the time they come in (the director picks among those that fit).</summary>
    public static class MusouPatterns
    {
        public static readonly System.Func<MusouPattern>[] All =
        {
            () => new PawnLine(), () => new KnightRaid(), () => new TwinRooks(), () => new PawnHorde(),
            () => new HordeEscort(), () => new Court(), () => new Encircle(), () => new EliteFew(),
            () => new BishopCorners(), () => new RookPincer(), () => new KnightSwarm(), () => new BlackTide(),
            () => new DarkPawnWall(), () => new Relief(), () => new Retaliation(),
        };

        static bool IsMinor(EnemyBase e) => e.Type == PieceType.Knight || e.Type == PieceType.Bishop || e.Type == PieceType.Rook;

        /// <summary>폰 전열: a row of pawns along the far edge; done when four in five are taken.</summary>
        class PawnLine : MusouPattern
        {
            public PawnLine() { name = "폰 전열"; from = 0f; weight = 1.3f; }
            public override void Begin(MusouDirector d)
            {
                int n = Mathf.Min(14, 5 + Mathf.RoundToInt(d.Minutes * 0.5f));
                foreach (var s in d.EdgeRow(n)) Add(d.Spawn(PieceType.Pawn, d.RollTier(), s));
            }
            public override void Tick(MusouDirector d, float dt) { if (TakenShare >= 0.8f) Done = true; }
        }

        /// <summary>기병 습격: knights one after another; done when all are taken.</summary>
        class KnightRaid : MusouPattern
        {
            int left;
            float timer;
            public KnightRaid() { name = "기병 습격"; from = 1f; }
            public override void Begin(MusouDirector d) => left = Mathf.Min(4, 1 + Mathf.FloorToInt(d.Minutes / 6f));
            public override void Tick(MusouDirector d, float dt)
            {
                if (left > 0 && (timer -= dt) <= 0f)
                {
                    timer = 0.8f;
                    left--;
                    Add(d.SpawnNear(PieceType.Knight, d.RollTier(0.1f), 4, 8));
                }
                if (left == 0 && MineAlive == 0) Done = true;
            }
        }

        /// <summary>두 개의 성: two rooks, one dark; when the dark one falls, pawns pour out where it stood.</summary>
        class TwinRooks : MusouPattern
        {
            EnemyBase key;
            public TwinRooks() { name = "두 개의 성"; from = 2.5f; }
            public override void Begin(MusouDirector d)
            {
                var row = d.EdgeRow(8);
                if (row.Count < 2) { Done = true; return; }
                Add(d.Spawn(PieceType.Rook, d.RollTier(), row[0]));
                key = d.Spawn(PieceType.Rook, d.DarkTier(-1), row[row.Count - 1]);
                Add(key);
                if (key == null) Done = true;
            }
            public override void OnKill(MusouDirector d, EnemyBase e)
            {
                if (e != key) return;
                var at = BoardSlots.ToSquare(e.FlatPos);
                int n = 5 + Mathf.FloorToInt(d.Minutes / 3f);
                for (int i = 0; i < n; i++) d.SpawnAround(PieceType.Pawn, d.RollTier(), at);
                Done = true;
            }
        }

        /// <summary>폰 무더기: a heap of pawns from three edges in quick bursts; done when 70% are taken.</summary>
        class PawnHorde : MusouPattern
        {
            int bursts = 3, perBurst;
            float timer;
            public PawnHorde() { name = "폰 무더기"; from = 3f; weight = 1.2f; }
            public override bool Fits(MusouDirector d) => d.Count(e => e.Type == PieceType.Pawn) < 25;
            public override void Begin(MusouDirector d) => perBurst = Mathf.CeilToInt(Mathf.Min(40, 10 + 1.2f * d.Minutes) / bursts);
            public override void Tick(MusouDirector d, float dt)
            {
                if (bursts > 0 && (timer -= dt) <= 0f)
                {
                    timer = 0.7f;
                    bursts--;
                    foreach (var s in d.EdgeRow(perBurst)) Add(d.Spawn(PieceType.Pawn, d.RollTier(), s));
                }
                if (bursts == 0 && TakenShare >= 0.7f) Done = true;
            }
        }

        /// <summary>
        /// 폰 사이의 기물: with a crowd of pawns on the board, every 5 pawns taken bring a dark knight, then a dark
        /// bishop, then a dark rook.
        /// </summary>
        class HordeEscort : MusouPattern
        {
            static readonly PieceType[] Order = { PieceType.Knight, PieceType.Bishop, PieceType.Rook };
            int next, pawns;
            public HordeEscort() { name = "폰 사이의 기물"; from = 3.5f; weight = 1.5f; }
            public override bool Fits(MusouDirector d) => d.Count(e => e.Type == PieceType.Pawn) >= 12;
            public override void Begin(MusouDirector d) { }
            public override void OnKill(MusouDirector d, EnemyBase e)
            {
                if (e.Type != PieceType.Pawn || ++pawns % 5 != 0) return;
                Add(d.SpawnNear(Order[next], d.DarkTier(-1), 3, 7));
                if (++next >= Order.Length) Done = true;
            }
        }

        /// <summary>
        /// 궁정: the dark knight, bishop and rook on the board (set down if missing); when all are taken a queen comes,
        /// calling 2–5 pawns at a time for as long as she stands.
        /// </summary>
        class Court : MusouPattern
        {
            EnemyBase queen;
            float timer;
            public Court() { name = "궁정"; from = 4.5f; weight = 1.1f; }
            public override bool Fits(MusouDirector d) => d.Count(e => e.EliteTier > 0 && IsMinor(e)) >= 2 || d.Minutes >= 6f;
            public override void Begin(MusouDirector d)
            {
                bool hasN = false, hasB = false, hasR = false;
                foreach (var e in d.Living())
                {
                    if (e.EliteTier == 0 || !IsMinor(e) || mine.Count >= 3) continue;
                    if (e.Type == PieceType.Knight && !hasN) { hasN = true; Add(e); }
                    else if (e.Type == PieceType.Bishop && !hasB) { hasB = true; Add(e); }
                    else if (e.Type == PieceType.Rook && !hasR) { hasR = true; Add(e); }
                }
                if (!hasN) Add(d.SpawnNear(PieceType.Knight, d.DarkTier(-1)));
                if (!hasB) Add(d.SpawnNear(PieceType.Bishop, d.DarkTier(-1)));
                if (!hasR) Add(d.SpawnNear(PieceType.Rook, d.DarkTier(-1)));
            }
            public override void Tick(MusouDirector d, float dt)
            {
                if (queen == null)
                {
                    if (MineAlive > 0) return;
                    queen = d.SpawnNear(PieceType.Queen, Mathf.Clamp(d.Era, 0, 2), 5, 9);
                    if (queen == null) { Done = true; return; }
                    timer = 3f;
                    return;
                }
                if (queen.IsDead) { Done = true; return; }
                if ((timer -= dt) > 0f) return;
                timer = Random.Range(6f, 8f);
                int n = Random.Range(2, 6);
                var at = BoardSlots.ToSquare(queen.FlatPos);
                for (int i = 0; i < n; i++) d.SpawnAround(PieceType.Pawn, d.RollTier(), at);
            }
        }

        /// <summary>포위: a ring of pawns around the King; done when three in four are taken.</summary>
        class Encircle : MusouPattern
        {
            public Encircle() { name = "포위"; from = 5f; }
            public override void Begin(MusouDirector d)
            {
                int n = Mathf.Min(28, 12 + Mathf.RoundToInt(d.Minutes * 0.5f));
                foreach (var s in d.Ring(4, n)) Add(d.Spawn(PieceType.Pawn, d.RollTier(), s));
            }
            public override void Tick(MusouDirector d, float dt) { if (TakenShare >= 0.75f) Done = true; }
        }

        /// <summary>정예: one to three strong dark pieces; done when all are taken.</summary>
        class EliteFew : MusouPattern
        {
            public EliteFew() { name = "정예"; from = 6f; weight = 0.9f; }
            public override void Begin(MusouDirector d)
            {
                int n = Random.Range(1, 3 + d.Era / 2);
                for (int i = 0; i < n; i++)
                {
                    var t = Random.value < 0.4f ? PieceType.Rook : Random.value < 0.5f ? PieceType.Bishop : PieceType.Knight;
                    Add(d.SpawnNear(t, d.DarkTier(), 5, 9));
                }
            }
            public override void Tick(MusouDirector d, float dt) { if (MineAlive == 0) Done = true; }
        }

        /// <summary>대각 포화: bishops on the four diagonals around the King; done when all are taken.</summary>
        class BishopCorners : MusouPattern
        {
            public BishopCorners() { name = "대각 포화"; from = 5f; weight = 0.8f; }
            public override void Begin(MusouDirector d)
            {
                var k = d.King;
                foreach (var dir in new[] { new Vector2Int(1, 1), new Vector2Int(-1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1) })
                    for (int r = 5; r >= 3; r--)
                    {
                        var s = k + dir * r;
                        if (!d.IsFree(s)) continue;
                        Add(d.Spawn(PieceType.Bishop, d.RollTier(0.15f), s));
                        break;
                    }
                if (total == 0) Done = true;
            }
            public override void Tick(MusouDirector d, float dt) { if (MineAlive == 0) Done = true; }
        }

        /// <summary>룩 협공: rooks on both sides of the King; done when 70% are taken.</summary>
        class RookPincer : MusouPattern
        {
            public RookPincer() { name = "룩 협공"; from = 8f; weight = 0.8f; }
            public override void Begin(MusouDirector d)
            {
                var k = d.King;
                int each = 2 + d.Era;
                foreach (int side in new[] { -1, 1 })
                    for (int i = 0; i < each; i++)
                    {
                        int off = (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 2;
                        for (int r = 6; r >= 3; r--)
                        {
                            var s = new Vector2Int(k.x + side * r, k.y + off);
                            if (!d.IsFree(s)) continue;
                            Add(d.Spawn(PieceType.Rook, d.RollTier(), s));
                            break;
                        }
                    }
                if (total == 0) Done = true;
            }
            public override void Tick(MusouDirector d, float dt) { if (TakenShare >= 0.7f) Done = true; }
        }

        /// <summary>기병대: a stream of knights; done when four in five are taken.</summary>
        class KnightSwarm : MusouPattern
        {
            int left;
            float timer;
            public KnightSwarm() { name = "기병대"; from = 9f; weight = 0.9f; }
            public override void Begin(MusouDirector d) => left = 6 + Mathf.FloorToInt(d.Minutes / 4f);
            public override void Tick(MusouDirector d, float dt)
            {
                if (left > 0 && (timer -= dt) <= 0f)
                {
                    timer = 1f;
                    left--;
                    Add(d.SpawnNear(PieceType.Knight, d.RollTier(), 4, 9));
                }
                if (left == 0 && TakenShare >= 0.8f) Done = true;
            }
        }

        /// <summary>흑색 물결: half a minute of pieces pouring in; done when it has ended and 60% are taken.</summary>
        class BlackTide : MusouPattern
        {
            float left = 30f, timer;
            public BlackTide() { name = "흑색 물결"; from = 12f; weight = 0.8f; }
            public override void Begin(MusouDirector d) { }
            public override void Tick(MusouDirector d, float dt)
            {
                if (left > 0f)
                {
                    left -= dt;
                    if ((timer -= dt) <= 0f)
                    {
                        timer = Mathf.Max(0.3f, 1.2f - d.Minutes / 40f);
                        Add(d.SpawnNear(d.RollType(), d.RollTier(), 4, 9));
                    }
                    return;
                }
                if (TakenShare >= 0.6f) Done = true;
            }
        }

        /// <summary>다크 폰 벽: a wall of dark pawns along the far edge; done when four in five are taken.</summary>
        class DarkPawnWall : MusouPattern
        {
            public DarkPawnWall() { name = "다크 폰 벽"; from = 10f; weight = 0.9f; }
            public override void Begin(MusouDirector d)
            {
                int n = 8 + Mathf.Min(6, d.Era * 2);
                foreach (var s in d.EdgeRow(n)) Add(d.Spawn(PieceType.Pawn, d.DarkTier(Random.value < 0.3f ? 0 : -1), s));
            }
            public override void Tick(MusouDirector d, float dt) { if (TakenShare >= 0.8f) Done = true; }
        }

        /// <summary>구원군: the board nearly cleared — a mixed army comes to its aid; done when 70% are taken.</summary>
        class Relief : MusouPattern
        {
            public Relief() { name = "구원군"; from = 8f; weight = 2f; }
            public override bool Fits(MusouDirector d) => d.Alive <= 3;
            public override void Begin(MusouDirector d)
            {
                int n = 8 + Mathf.RoundToInt(d.Minutes / 2f);
                for (int i = 0; i < n; i++) Add(d.SpawnNear(d.RollType(), d.RollTier(0.1f), 4, 9));
            }
            public override void Tick(MusouDirector d, float dt) { if (TakenShare >= 0.7f) Done = true; }
        }

        /// <summary>
        /// 보복: few pawns but a strong piece standing — when that piece falls, pawns pour out where it stood (five, more
        /// as time goes on).
        /// </summary>
        class Retaliation : MusouPattern
        {
            EnemyBase key;
            public Retaliation() { name = "보복"; from = 2f; weight = 1.2f; }
            public override bool Fits(MusouDirector d) =>
                d.Count(e => e.Type == PieceType.Pawn) <= 4 && d.Count(e => e.Type == PieceType.Rook || e.Type == PieceType.Bishop) >= 1;
            public override void Begin(MusouDirector d)
            {
                foreach (var e in d.Living())
                    if ((e.Type == PieceType.Rook || e.Type == PieceType.Bishop) && (key == null || e.EliteTier > key.EliteTier)) key = e;
                if (key == null) Done = true;
            }
            public override void Tick(MusouDirector d, float dt) { if (key == null) Done = true; }
            public override void OnKill(MusouDirector d, EnemyBase e)
            {
                if (e != key) return;
                var at = BoardSlots.ToSquare(e.FlatPos);
                int n = 5 + Mathf.FloorToInt(d.Minutes / 4f);
                for (int i = 0; i < n; i++) d.SpawnAround(PieceType.Pawn, d.RollTier(), at);
                Done = true;
            }
        }
    }
}

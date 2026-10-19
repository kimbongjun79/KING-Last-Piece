using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Level design of the endless mode (scene EndlessMode). Rounds never end; each one is generated from its number:
    /// <list type="bullet">
    /// <item>A point budget buys the army (pawn 1, knight/bishop 3, rook 5), unlocking the pieces like the classic
    /// opening rounds; the number of pieces is capped (more room on a bigger board).</item>
    /// <item>Empowered ("dark") pieces from round 11: one tier-1 pawn first, then a growing share — about half the army
    /// by round 29 — with higher tiers and bigger pieces joining over time. Past round 30 the spare budget empowers
    /// pieces further.</item>
    /// <item>Every 10th round is a boss round with queens only: 10 a queen, 20 a dark I queen, 30 dark II, 40 two queens,
    /// 50 two dark I queens… Her summons at half health grow with the boss count.</item>
    /// <item>Every 40 rounds all pieces gain health; every 20 rounds the board grows a ring of squares.</item>
    /// </list>
    /// </summary>
    public static class EndlessRounds
    {
        public struct Spawn
        {
            public PieceType type;
            public int tier;
        }

        /// <summary>Queens get this much more health in the endless mode than in the classic final round.</summary>
        public const float QueenHealthBoost = 1.5f;

        public static bool IsBossRound(int round) => round % 10 == 0;

        /// <summary>Health multiplier for every piece: +40% for each 40 rounds reached.</summary>
        public static float HealthMultiplier(int round) => 1f + 0.4f * (round / 40);

        /// <summary>Stagger resistance multiplier for every piece: +30% for each 40 rounds reached (permanent).</summary>
        public static float StaggerMultiplier(int round) => 1f + 0.3f * (round / 40);

        /// <summary>Life steal efficiency: grows steadily with the round, without a ceiling.</summary>
        public static float SustainScale(int round) => 0.3f + 0.035f * round;

        /// <summary>Board size: 8×8, plus a ring of squares (2 per side) for every 20 rounds cleared.</summary>
        public static int BoardSize(int round) => 8 + 2 * ((round - 1) / 20);

        public static string Title(int round)
        {
            if (IsBossRound(round))
            {
                int b = round / 10, count = (b - 1) / 3 + 1, tier = (b - 1) % 3;
                string who = tier == 0 ? "흑색 여왕" : $"다크 {new string('I', tier)} 여왕";
                return count == 1 ? $"{round} 라운드 — {who}" : $"{round} 라운드 — {who} {count}명";
            }
            return $"{round} 라운드";
        }

        public static List<Spawn> Army(int round)
        {
            var list = new List<Spawn>();
            if (IsBossRound(round))
            {
                int b = round / 10, count = (b - 1) / 3 + 1, tier = (b - 1) % 3;
                for (int i = 0; i < count; i++) list.Add(new Spawn { type = PieceType.Queen, tier = tier });
                return list;
            }

            // 1. Buy the army.
            float budget = 4f + 1.6f * (round - 1);
            int cap = Mathf.Min(12, 4 + round / 2) + (BoardSize(round) - 8);
            while (budget >= 1f && list.Count < cap)
            {
                var type = PickType(round, budget);
                budget -= Cost(type);
                list.Add(new Spawn { type = type });
            }

            // 2. Empower a share of it.
            int elites = 0;
            if (round == 11) elites = 1;
            else if (round > 11)
            {
                float share = round <= 29 ? Mathf.Lerp(0.1f, 0.5f, (round - 12) / 17f) : Mathf.Min(0.85f, 0.5f + (round - 29) * 0.01f);
                elites = Mathf.Clamp(Mathf.RoundToInt(list.Count * share), 1, list.Count);
            }
            for (int i = 0; i < elites; i++)
            {
                int k = PickForEmpower(list, round);
                if (k < 0) break;
                var s = list[k];
                s.tier = round == 11 ? 1 : RollTier(round);
                list[k] = s;
            }

            // 3. Late rounds: the budget left over (the army is at its cap) empowers pieces further — a step more every
            // two rounds, so the dark tiers keep creeping up instead of jumping.
            if (round > 30)
            {
                int guard = 64, steps = (round - 30) / 2;
                while (budget >= 2f && steps > 0 && guard-- > 0)
                {
                    int k = Random.Range(0, list.Count);
                    var s = list[k];
                    if (s.tier >= EnemyBase.MaxEliteTier) continue;
                    s.tier++;
                    steps--;
                    budget -= Cost(s.type) * 0.8f;
                    list[k] = s;
                }
            }
            return list;
        }

        static float Cost(PieceType t) => t switch
        {
            PieceType.Knight => 3f, PieceType.Bishop => 3f, PieceType.Rook => 5f, _ => 1f,
        };

        static PieceType PickType(int round, float budget)
        {
            // Pieces unlock as in the classic opening: knights from round 2, bishops 3, rooks 5.
            float wPawn = 3f;
            float wKnight = round >= 2 && budget >= 3f ? 1.4f : 0f;
            float wBishop = round >= 3 && budget >= 3f ? 1.3f : 0f;
            float wRook = round >= 5 && budget >= 5f ? 1f : 0f;
            float r = Random.value * (wPawn + wKnight + wBishop + wRook);
            if ((r -= wRook) < 0f) return PieceType.Rook;
            if ((r -= wBishop) < 0f) return PieceType.Bishop;
            if ((r -= wKnight) < 0f) return PieceType.Knight;
            return PieceType.Pawn;
        }

        /// <summary>A normal piece to empower: pawns at first, then knights (14+), bishops (17+) and rooks (21+).</summary>
        static int PickForEmpower(List<Spawn> list, int round)
        {
            var options = new List<int>();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].tier > 0) continue;
                var t = list[i].type;
                bool ok = t == PieceType.Pawn || (t == PieceType.Knight && round >= 14) ||
                          (t == PieceType.Bishop && round >= 17) || (t == PieceType.Rook && round >= 21);
                if (ok) options.Add(i);
            }
            return options.Count > 0 ? options[Random.Range(0, options.Count)] : -1;
        }

        /// <summary>Tier I at first; tier II joins from round 20 and grows more common (as likely as tier I by round 32).</summary>
        static int RollTier(int round)
        {
            float w1 = 1f, w2 = Mathf.Max(0f, (round - 20) / 12f);
            return Random.value * (w1 + w2) < w2 ? 2 : 1;
        }

        /// <summary>
        /// What one queen summons at half health in boss round <paramref name="round"/>: two pawns at the first boss,
        /// then more pieces, bigger ones, and empowered ones as the bosses go on.
        /// </summary>
        public static List<Spawn> QueenSummons(int round)
        {
            int b = Mathf.Max(1, round / 10);
            int count = Mathf.Min(6, 2 + (b - 1) / 2);
            var list = new List<Spawn>();
            for (int i = 0; i < count; i++)
            {
                PieceType type = PieceType.Pawn;
                float r = Random.value;
                if (b >= 4 && r < 0.25f) type = PieceType.Rook;
                else if (b >= 3 && r < 0.5f) type = PieceType.Bishop;
                else if (b >= 2 && r < 0.75f) type = PieceType.Knight;
                int tier = b >= 3 ? Mathf.Clamp(Random.Range(0, b - 1), 0, EnemyBase.MaxEliteTier) : 0;
                list.Add(new Spawn { type = type, tier = tier });
            }
            return list;
        }

        /// <summary>
        /// Free squares to set an army on: the far ranks first (as the black pieces stand), at least 3 squares from the
        /// King, spreading toward him as needed.
        /// </summary>
        public static List<Vector2Int> SpawnSquares(Vector2Int king, int needed)
        {
            int n = BoardSlots.Size;
            var all = new List<Vector2Int>();
            for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
            {
                var s = new Vector2Int(x, y);
                if (BoardSlots.Chebyshev(s, king) >= 3) all.Add(s);
            }
            // Far ranks first, a little shuffle within a rank so armies don't line up the same way every time.
            var keyed = new List<(float key, Vector2Int s)>();
            foreach (var s in all) keyed.Add((-(s.y) + Random.value * 0.9f, s));
            keyed.Sort((a, b) => a.key.CompareTo(b.key));
            var result = new List<Vector2Int>();
            for (int i = 0; i < keyed.Count && result.Count < needed; i++) result.Add(keyed[i].s);
            return result;
        }
    }
}

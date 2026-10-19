using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// The King's stats. The classic 10-round game uses the defaults (the values the game was tuned with); the endless
    /// mode starts far weaker and grows through the items picked after every round (<see cref="Upgrades"/>).
    /// </summary>
    [System.Serializable]
    public class PlayerStats
    {
        public float maxHealth = 100f;
        public float maxStamina = 100f;
        /// <summary>Damage multiplier on every attack (items add to it).</summary>
        public float attackPower = 1f;
        /// <summary>Final damage bonus, multiplied on top of the attack power (0.1 = +10%).</summary>
        public float damage;
        /// <summary>Attack animation speed (wind-up, swing and recovery all play faster).</summary>
        public float attackSpeed = 1f;
        /// <summary>Hit reach multiplier.</summary>
        public float attackRange = 1f;
        /// <summary>Flat armor: incoming damage × 100 / (100 + defense).</summary>
        public float defense;
        public int maxFlasks = 3;
        public float healAmount = 45f;
        public int maxDodges = 3;
        /// <summary>Roll charge refill speed multiplier.</summary>
        public float dodgeRecharge = 1f;
        /// <summary>Stamina cost cut for light / heavy attacks (0.2 = 20% cheaper).</summary>
        public float lightCostCut, heavyCostCut;
        /// <summary>Chance a hit is critical, and the damage multiplier of a critical hit.</summary>
        public float critChance = 0.05f, critDamage = 1.5f;
        /// <summary>Walk / sprint speed multiplier, and stamina refill speed multiplier.</summary>
        public float moveSpeed = 1f, staminaRegen = 1f;
        /// <summary>Life steal ("흡혈"): health regained for every piece captured (scaled by the round, see GameManager.SustainScale).</summary>
        public float lifeSteal;
        /// <summary>Multiplier on the stagger every blow deals.</summary>
        public float staggerPower = 1f;
        /// <summary>The King's attack motion follows his power, speed and range (endless mode); classic keeps it fixed.</summary>
        public bool scalesMotion;

        public static PlayerStats Classic() => new PlayerStats();

        /// <summary>The endless mode's starting King: much weaker in every way, one flask, one roll.</summary>
        public static PlayerStats EndlessStart() => new PlayerStats
        {
            maxHealth = 70f,
            maxStamina = 70f,
            attackPower = 0.7f,
            attackSpeed = 0.9f,
            attackRange = 0.7f,
            scalesMotion = true,
            defense = 0f,
            maxFlasks = 1,
            healAmount = 20f,
            maxDodges = 1,
            dodgeRecharge = 0.45f,
        };

        /// <summary>
        /// How dynamic the attack motion is (lean, hop, squash, lunge, camera kick): 1.2 is the motion as tuned. In the
        /// endless mode it grows from 0.8 (the weak starting King) to 1.4 as attack power, speed and range are upgraded.
        /// </summary>
        public float Dynamism
        {
            get
            {
                if (!scalesMotion) return 1.2f;
                float p = Mathf.InverseLerp(0.7f, 1.7f, attackPower);
                float s = Mathf.InverseLerp(0.9f, 1.5f, attackSpeed);
                float r = Mathf.InverseLerp(0.7f, 1.3f, attackRange);
                return Mathf.Lerp(0.8f, 1.4f, (p + s + r) / 3f);
            }
        }

        public PlayerStats Clone() => (PlayerStats)MemberwiseClone();

        /// <summary>Defense below zero (an item's drawback) makes hits hurt more: −30 → ×1.43, never past ×2.5.</summary>
        public float DamageTaken(float damage) => damage * 100f / Mathf.Max(40f, 100f + defense);

        /// <summary>What every blow is multiplied by: attack power × (1 + damage bonus).</summary>
        public float DamageMultiplier => attackPower * (1f + damage);
    }

    public enum UpgradeKind
    {
        Flask, HealAmount, AttackPower, Defense, MaxHealth, AttackRange, AttackSpeed, DodgeRecharge, MaxDodges, MaxStamina,
        LightCost, HeavyCost, CritChance, CritDamage, MoveSpeed, StaminaRegen, LifeSteal, StaggerPower, Damage,
    }

    /// <summary>How strong an item is (its total worth in steps): the rarer the grade, the stronger the item.</summary>
    public enum UpgradeGrade { Common, Rare, Epic, Legendary }

    /// <summary>One stat an item changes, in steps (negative: the item's drawback).</summary>
    public struct ItemEffect
    {
        public UpgradeKind kind;
        public int steps;
        public ItemEffect(UpgradeKind kind, int steps) { this.kind = kind; this.steps = steps; }
    }

    /// <summary>A round-end item: a fixed set of stat steps and a grade set by how much they are worth.</summary>
    public class Item
    {
        public string id, name;
        public UpgradeGrade grade;
        public ItemEffect[] effects;
        public string GradeName => Upgrades.GradeName(grade);
        Texture2D icon;
        bool iconLoaded;
        /// <summary>The item's picture (Resources/ItemIcons/&lt;id&gt;), null if there is none.</summary>
        public Texture2D Icon
        {
            get
            {
                if (!iconLoaded) { iconLoaded = true; icon = Resources.Load<Texture2D>("ItemIcons/" + id); }
                return icon;
            }
        }
    }

    /// <summary>
    /// Round-end items (endless mode): three items are offered, the player keeps one. Every item is a fixed set of stat
    /// steps — one step is worth about 5% of damage or survival (see Docs) — with drawbacks on some; its grade follows
    /// its total worth (common ≈ 2 steps, rare ≈ 3, epic ≈ 4–5, legendary ≈ 6–7). Rarer grades turn up more as the
    /// rounds go on; after a boss round one of the three is epic or better. Stats stop at their caps (and floors).
    /// </summary>
    public static class Upgrades
    {
        public const int Offered = 3, Picks = 1;

        public struct UpgradeDef
        {
            public string title;
            public float step;     // one step
            public float cap;      // ∞ when uncapped
            public float floor;    // lowest a drawback can take it
            public bool percent;   // shown as a percentage (multiplier stats)
            public bool whole;     // whole steps (flasks, charges)
            public string desc;    // what it does (the pause menu's stats tab)
        }

        static readonly float Inf = float.PositiveInfinity;

        static readonly Dictionary<UpgradeKind, UpgradeDef> defs = new Dictionary<UpgradeKind, UpgradeDef>
        {
            { UpgradeKind.AttackPower,   new UpgradeDef { title = "공격력",        step = 0.05f, cap = Inf,   floor = 0.3f, percent = true, desc = "모든 공격의 기본 피해 배율. 1단계 +5% (더하기)." } },
            { UpgradeKind.Damage,        new UpgradeDef { title = "데미지",        step = 0.05f, cap = Inf,   floor = -0.5f, percent = true, desc = "공격력과 따로 곱해지는 최종 피해 증가. 1단계 +5%. 공격력이 높아도 효율이 줄지 않습니다. 단점으로 음수가 되면 피해가 줄어듭니다." } },
            { UpgradeKind.AttackSpeed,   new UpgradeDef { title = "공격 속도",     step = 0.04f, cap = 1.5f,  floor = 0.6f, percent = true, desc = "공격 동작(준비·휘두르기·회복) 속도. 1단계 +4%. 빠를수록 빈틈도 짧아집니다." } },
            { UpgradeKind.AttackRange,   new UpgradeDef { title = "공격 범위",     step = 0.07f, cap = 1.3f,  floor = 0.5f, percent = true, desc = "공격이 닿는 거리와 돌진 거리. 1단계 +7%." } },
            { UpgradeKind.CritChance,    new UpgradeDef { title = "치명타 확률",   step = 0.06f, cap = 0.6f,  floor = 0f,   percent = true, desc = "공격이 치명타가 될 확률. 1단계 +6%. 치명타는 경직 피해도 2배." } },
            { UpgradeKind.CritDamage,    new UpgradeDef { title = "치명타 피해",   step = 0.25f, cap = 3f,    floor = 1.2f, percent = true, desc = "치명타의 피해 배율. 1단계 +25%." } },
            { UpgradeKind.StaggerPower,  new UpgradeDef { title = "경직 피해",     step = 0.1f,  cap = 2f,    floor = 0.5f, percent = true, desc = "공격이 적의 경직도를 깎는 양. 1단계 +10%. 경직도가 다 깎인 적은 기절해 움직이지 못합니다." } },
            { UpgradeKind.MaxHealth,     new UpgradeDef { title = "최대 체력",     step = 8f,    cap = Inf,   floor = 30f,  desc = "최대 체력. 1단계 +8. 늘어난 만큼 바로 회복됩니다." } },
            { UpgradeKind.Defense,       new UpgradeDef { title = "방어력",        step = 6f,    cap = Inf,   floor = -60f, desc = "받는 피해 = 피해 × 100 / (100 + 방어력). 1단계 +6. 단점으로 음수가 되면 받는 피해가 늘어납니다 (−30이면 약 1.4배)." } },
            { UpgradeKind.HealAmount,    new UpgradeDef { title = "회복약 회복량", step = 4f,    cap = Inf,   floor = 5f,   desc = "회복약 하나가 회복하는 체력. 1단계 +4." } },
            { UpgradeKind.Flask,         new UpgradeDef { title = "회복약 개수",   step = 1f,    cap = 5f,    floor = 1f,   whole = true, desc = "가질 수 있는 회복약 수 (최대 5). 라운드를 마칠 때마다 1개 채워지고, 다 차 있으면 그만큼 체력이 회복됩니다." } },
            { UpgradeKind.LifeSteal,     new UpgradeDef { title = "흡혈",          step = 1f,    cap = 20f,   floor = 0f,   desc = "기물을 잡을 때마다 체력 회복. 1단계 +1. 라운드가 진행될수록 효율이 올라갑니다 (1라운드 ×0.34 · 20라운드 ×1.0 · 40라운드 ×1.7, 상한 없음)." } },
            { UpgradeKind.MoveSpeed,     new UpgradeDef { title = "이동 속도",     step = 0.06f, cap = 1.3f,  floor = 0.7f, percent = true, desc = "걷기·달리기 속도. 1단계 +6%." } },
            { UpgradeKind.DodgeRecharge, new UpgradeDef { title = "회피 충전 속도", step = 0.15f, cap = 1.6f, floor = 0.3f, percent = true, desc = "회피 횟수가 다시 차는 속도. 1단계 +15%." } },
            { UpgradeKind.MaxDodges,     new UpgradeDef { title = "회피 횟수",     step = 1f,    cap = 3f,    floor = 1f,   whole = true, desc = "연달아 쓸 수 있는 회피(무적 구르기) 수 (최대 3)." } },
            { UpgradeKind.MaxStamina,    new UpgradeDef { title = "최대 기력",     step = 15f,   cap = Inf,   floor = 30f,  desc = "최대 기력. 공격·강공격·달리기에 쓰입니다. 1단계 +15." } },
            { UpgradeKind.StaminaRegen,  new UpgradeDef { title = "기력 회복 속도", step = 0.2f, cap = 2f,    floor = 0.5f, percent = true, desc = "기력이 다시 차는 속도. 1단계 +20%." } },
            { UpgradeKind.LightCost,     new UpgradeDef { title = "일반공격 기력 절약", step = 0.08f, cap = 0.6f, floor = 0f, percent = true, desc = "일반공격의 기력 소모 감소. 1단계 8%." } },
            { UpgradeKind.HeavyCost,     new UpgradeDef { title = "강공격 기력 절약", step = 0.08f, cap = 0.5f, floor = 0f, percent = true, desc = "강공격의 기력 소모 감소. 1단계 8%. 강공격은 필요한 기력이 다 있어야 나갑니다." } },
        };

        public static UpgradeDef Def(UpgradeKind k) => defs[k];

        static readonly string[] GradeNames = { "일반", "희귀", "영웅", "전설" };
        public static string GradeName(UpgradeGrade g) => GradeNames[(int)g];

        static Item I(string id, string name, UpgradeGrade grade, params ItemEffect[] fx) => new Item { id = id, name = name, grade = grade, effects = fx };
        static ItemEffect E(UpgradeKind k, int steps) => new ItemEffect(k, steps);

        const UpgradeGrade C = UpgradeGrade.Common, R = UpgradeGrade.Rare, H = UpgradeGrade.Epic, L = UpgradeGrade.Legendary;
        const UpgradeKind Pow = UpgradeKind.AttackPower, Dmg = UpgradeKind.Damage, Spd = UpgradeKind.AttackSpeed, Rng = UpgradeKind.AttackRange,
            CrC = UpgradeKind.CritChance, CrD = UpgradeKind.CritDamage, Stg = UpgradeKind.StaggerPower, Hp = UpgradeKind.MaxHealth,
            Def_ = UpgradeKind.Defense, Heal = UpgradeKind.HealAmount, Fl = UpgradeKind.Flask, Ls = UpgradeKind.LifeSteal,
            Mv = UpgradeKind.MoveSpeed, DRc = UpgradeKind.DodgeRecharge, Dg = UpgradeKind.MaxDodges, St = UpgradeKind.MaxStamina,
            StR = UpgradeKind.StaminaRegen, LC = UpgradeKind.LightCost, HC = UpgradeKind.HeavyCost;

        /// <summary>Every item (stat items only for now; items that change how the King fights come later).</summary>
        public static readonly Item[] All =
        {
            // Common (≈ 2 steps)
            I("whetstone",      "숫돌",           C, E(Pow, 2)),
            I("light_gloves",   "가벼운 장갑",    C, E(Spd, 2)),
            I("leather_boots",  "가죽 장화",      C, E(Mv, 2), E(DRc, 1)),
            I("thick_cloak",    "두꺼운 망토",    C, E(Def_, 2)),
            I("training",       "체력 단련",      C, E(Hp, 2)),
            I("breathing",      "호흡법 교본",    C, E(StR, 1), E(St, 1)),
            I("big_flask",      "큰 약병",        C, E(Heal, 2)),
            I("sword_manual",   "검술 교본",      C, E(LC, 2), E(St, 1)),
            I("heavy_manual",   "강타 교본",      C, E(HC, 2), E(Stg, 1)),
            I("sharp_edge",     "날 선 칼날",     C, E(Dmg, 2)),
            I("vampire_fang",   "흡혈귀의 송곳니", C, E(Ls, 2)),
            I("loot_pouch",     "전리품 주머니",  C, E(Ls, 1), E(Heal, 1)),
            I("iron_flail",     "쇠사슬 철퇴",    C, E(Stg, 2)),
            I("waterskin",      "가죽 수통",      C, E(St, 2)),
            // Rare (≈ 3 steps)
            I("knight_sword",   "기사의 검",      R, E(Pow, 2), E(CrC, 2)),
            I("herald_wings",   "전령의 날개",    R, E(Mv, 2), E(DRc, 2)),
            I("hawk_eye",       "매의 눈",        R, E(CrC, 2), E(CrD, 2)),
            I("rampart_stone",  "성벽의 돌",      R, E(Def_, 2), E(Hp, 1)),
            I("crafted_grip",   "장인의 손잡이",  R, E(Rng, 2), E(Stg, 1)),
            I("heavy_crown",    "무거운 왕관",    R, E(Pow, 4), E(Spd, -1)),
            I("feather_crown",  "깃털 왕관",      R, E(Spd, 4), E(Pow, -1)),
            I("long_spear",     "장창",           R, E(Rng, 4), E(Spd, -1)),
            I("dagger",         "단검",           R, E(Spd, 4), E(Rng, -1)),
            I("iron_armor",     "철갑옷",         R, E(Def_, 4), E(Mv, -1)),
            I("headsman_blade", "처형인의 칼날",  R, E(Dmg, 3)),
            I("mad_seal",       "광기의 인장",    R, E(Dmg, 4), E(Def_, -1)),
            I("blood_cup",      "피의 잔",        R, E(Ls, 3)),
            I("battering_ram",  "파성추",         R, E(Stg, 4), E(Spd, -1)),
            I("incense",        "명상의 향로",    R, E(StR, 2), E(St, 1)),
            I("rapier",         "가벼운 세검",    R, E(LC, 2), E(Spd, 1)),
            I("assassin_poison","암살자의 독",    R, E(CrD, 3)),
            I("wind_gloves",    "바람의 장갑",    R, E(Spd, 3)),
            // Epic (≈ 4–5 steps)
            I("apothecary_bag", "약제사의 가방",  H, E(Fl, 1), E(Heal, 1)),
            I("guard_insignia", "근위대 휘장",    H, E(Pow, 2), E(Def_, 2), E(Hp, 1)),
            I("spare_boots",    "여분의 신발",    H, E(Dg, 1), E(DRc, 2)),
            I("glass_cannon",   "유리 대포",      H, E(Pow, 5), E(CrD, 3), E(Hp, -2)),
            I("berserker_blood","광전사의 피",    H, E(Ls, 3), E(Pow, 3), E(Def_, -2)),
            I("running_king",   "질주하는 왕",    H, E(Mv, 4), E(StR, 3), E(St, 1), E(Def_, -2)),
            I("black_oath",     "검은 서약",      H, E(Dmg, 3), E(CrC, 1), E(CrD, 2)),
            I("kings_warhammer","왕의 전쟁망치",  H, E(Stg, 3), E(HC, 2)),
            I("war_drum",       "전쟁의 북",      H, E(LC, 2), E(HC, 2), E(StR, 1)),
            I("shadow_cloak",   "그림자 망토",    H, E(Dg, 1), E(Mv, 2), E(DRc, 1)),
            I("sword_dance",    "검무의 두루마리", H, E(Spd, 3), E(LC, 1), E(StR, 1)),
            // Legendary (≈ 6–7 steps)
            I("kings_seal",     "왕의 인장",      L, E(Pow, 1), E(Spd, 1), E(Rng, 1), E(Hp, 1), E(Def_, 1), E(Heal, 1), E(Stg, 1)),
            I("sacrifice",      "희생",           L, E(Pow, 9), E(CrC, 1), E(Hp, -3)),
            I("crown_wrath",    "왕관의 분노",    L, E(Dmg, 4), E(Pow, 2), E(Rng, 1)),
            I("fountain_of_life","생명의 샘",     L, E(Fl, 1), E(Heal, 2), E(Hp, 2)),
        };

        /// <summary>
        /// Rolls a grade. Round 1: about 70% common, 22% rare, 7% epic, 1% legendary; the rarer grades grow more likely
        /// every round (by round 40 about 54 / 29 / 14 / 4).
        /// </summary>
        public static UpgradeGrade RollGrade(int round)
        {
            float r1 = Mathf.Max(0, round - 1);
            float common = 70f, rare = 22f + 0.4f * r1, epic = 7f + 0.28f * r1, legendary = 1f + 0.1f * r1;
            float r = Random.value * (common + rare + epic + legendary);
            if ((r -= legendary) < 0f) return UpgradeGrade.Legendary;
            if ((r -= epic) < 0f) return UpgradeGrade.Epic;
            if ((r -= rare) < 0f) return UpgradeGrade.Rare;
            return UpgradeGrade.Common;
        }

        public static float Current(PlayerStats s, UpgradeKind k) => k switch
        {
            UpgradeKind.Flask => s.maxFlasks,
            UpgradeKind.HealAmount => s.healAmount,
            UpgradeKind.AttackPower => s.attackPower,
            UpgradeKind.Defense => s.defense,
            UpgradeKind.MaxHealth => s.maxHealth,
            UpgradeKind.AttackRange => s.attackRange,
            UpgradeKind.AttackSpeed => s.attackSpeed,
            UpgradeKind.DodgeRecharge => s.dodgeRecharge,
            UpgradeKind.MaxDodges => s.maxDodges,
            UpgradeKind.LightCost => s.lightCostCut,
            UpgradeKind.HeavyCost => s.heavyCostCut,
            UpgradeKind.CritChance => s.critChance,
            UpgradeKind.CritDamage => s.critDamage,
            UpgradeKind.MoveSpeed => s.moveSpeed,
            UpgradeKind.StaminaRegen => s.staminaRegen,
            UpgradeKind.LifeSteal => s.lifeSteal,
            UpgradeKind.StaggerPower => s.staggerPower,
            UpgradeKind.Damage => s.damage,
            _ => s.maxStamina,
        };

        static void Set(PlayerStats s, UpgradeKind k, float v)
        {
            switch (k)
            {
                case UpgradeKind.Flask: s.maxFlasks = Mathf.RoundToInt(v); break;
                case UpgradeKind.HealAmount: s.healAmount = v; break;
                case UpgradeKind.AttackPower: s.attackPower = v; break;
                case UpgradeKind.Defense: s.defense = v; break;
                case UpgradeKind.MaxHealth: s.maxHealth = v; break;
                case UpgradeKind.AttackRange: s.attackRange = v; break;
                case UpgradeKind.AttackSpeed: s.attackSpeed = v; break;
                case UpgradeKind.DodgeRecharge: s.dodgeRecharge = v; break;
                case UpgradeKind.MaxDodges: s.maxDodges = Mathf.RoundToInt(v); break;
                case UpgradeKind.MaxStamina: s.maxStamina = v; break;
                case UpgradeKind.LightCost: s.lightCostCut = v; break;
                case UpgradeKind.HeavyCost: s.heavyCostCut = v; break;
                case UpgradeKind.CritChance: s.critChance = v; break;
                case UpgradeKind.CritDamage: s.critDamage = v; break;
                case UpgradeKind.MoveSpeed: s.moveSpeed = v; break;
                case UpgradeKind.StaminaRegen: s.staminaRegen = v; break;
                case UpgradeKind.LifeSteal: s.lifeSteal = v; break;
                case UpgradeKind.StaggerPower: s.staggerPower = v; break;
                case UpgradeKind.Damage: s.damage = v; break;
            }
        }

        /// <summary>Where one effect takes the stat: steps × step, kept between the stat's floor and cap.</summary>
        public static float After(PlayerStats s, ItemEffect fx)
        {
            var d = Def(fx.kind);
            float now = Current(s, fx.kind), next = now + fx.steps * d.step;
            // A gain never lowers a stat already past its cap; a drawback never raises one below its floor.
            return fx.steps >= 0 ? Mathf.Max(now, Mathf.Min(d.cap, next)) : Mathf.Min(now, Mathf.Max(d.floor, next));
        }

        /// <summary>Worth offering: at least one of its gains still has room under its cap.</summary>
        public static bool Useful(PlayerStats s, Item item)
        {
            foreach (var fx in item.effects)
                if (fx.steps > 0 && Current(s, fx.kind) < Def(fx.kind).cap - 0.0001f) return true;
            return false;
        }

        /// <summary>
        /// Three different items, each of a rolled grade (falling back to a neighbouring grade when every item of that
        /// grade is useless now). <paramref name="bossCleared"/>: the first one is epic or better.
        /// </summary>
        public static List<Item> Roll(PlayerStats s, int round = 1, bool bossCleared = false)
        {
            var offer = new List<Item>();
            for (int slot = 0; slot < Offered; slot++)
            {
                var grade = RollGrade(round);
                if (bossCleared && slot == 0 && grade < UpgradeGrade.Epic) grade = UpgradeGrade.Epic;
                Item pick = null;
                // The rolled grade first, then the nearest others.
                for (int dist = 0; dist < 4 && pick == null; dist++)
                foreach (int sign in new[] { 1, -1 })
                {
                    int g = (int)grade + dist * sign;
                    if (g < 0 || g > 3 || pick != null) continue;
                    var pool = new List<Item>();
                    foreach (var it in All)
                        if ((int)it.grade == g && !offer.Contains(it) && Useful(s, it)) pool.Add(it);
                    if (pool.Count > 0) pick = pool[Random.Range(0, pool.Count)];
                }
                if (pick == null) break;
                offer.Add(pick);
            }
            return offer;
        }

        public static void Apply(PlayerStats s, Item item)
        {
            foreach (var fx in item.effects) Set(s, fx.kind, After(s, fx));
        }

        /// <summary>"▲▲" / "▼" for the effect's steps.</summary>
        public static string Arrows(ItemEffect fx) => new string(fx.steps >= 0 ? '▲' : '▼', Mathf.Min(9, Mathf.Abs(fx.steps)));

        /// <summary>A stat's value as shown: "85%" / "3" / "1.5%".</summary>
        public static string Format(UpgradeKind k, float v)
        {
            var d = Def(k);
            if (!d.percent) return Mathf.RoundToInt(v).ToString();
            float pct = v * 100f;
            return Mathf.Abs(pct - Mathf.Round(pct)) > 0.05f ? $"{pct:0.0}%" : $"{Mathf.RoundToInt(pct)}%";
        }

        /// <summary>"85% → 95%" preview of one effect of an item.</summary>
        public static string Preview(PlayerStats s, ItemEffect fx) => $"{Format(fx.kind, Current(s, fx.kind))} → {Format(fx.kind, After(s, fx))}";
    }
}

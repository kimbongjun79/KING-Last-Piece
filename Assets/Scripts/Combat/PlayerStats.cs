using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// The King's stats. The classic 10-round game uses the defaults (the values the game was tuned with); the endless
    /// mode starts far weaker and grows through the upgrades picked after every round (<see cref="Upgrades"/>).
    /// </summary>
    [System.Serializable]
    public class PlayerStats
    {
        public float maxHealth = 100f;
        public float maxStamina = 100f;
        /// <summary>Damage multiplier on every attack.</summary>
        public float attackPower = 1f;
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
        /// <summary>Share of the damage dealt that heals the King (0.05 = 5%).</summary>
        public float lifeSteal;
        /// <summary>Multiplier on the stagger every blow deals.</summary>
        public float staggerPower = 1f;
        /// <summary>Health regained for every piece captured.</summary>
        public float killHeal;
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
                float s = Mathf.InverseLerp(0.9f, 1.35f, attackSpeed);
                float r = Mathf.InverseLerp(0.7f, 1.3f, attackRange);
                return Mathf.Lerp(0.8f, 1.4f, (p + s + r) / 3f);
            }
        }

        public PlayerStats Clone() => (PlayerStats)MemberwiseClone();

        public float DamageTaken(float damage) => damage * 100f / (100f + Mathf.Max(0f, defense));
    }

    public enum UpgradeKind
    {
        Flask, HealAmount, AttackPower, Defense, MaxHealth, AttackRange, AttackSpeed, DodgeRecharge, MaxDodges, MaxStamina,
        LightCost, HeavyCost, CritChance, CritDamage, MoveSpeed, StaminaRegen, LifeSteal, StaggerPower, KillHeal,
    }

    /// <summary>How big an offered upgrade is: the rarer the grade, the more the stat grows.</summary>
    public enum UpgradeGrade { Common, Rare, Epic, Legendary }

    /// <summary>One offered upgrade: a stat, its grade and the (rolled) amount it grows by.</summary>
    public struct Upgrade
    {
        public UpgradeKind kind;
        public UpgradeGrade grade;
        public float amount;

        public string Title => Upgrades.Def(kind).title;
        public string GradeName => Upgrades.GradeName(grade);

        /// <summary>"+12%" / "+1" / "+8" style label of the gain.</summary>
        public string AmountText => Upgrades.Def(kind).percent ? $"+{Mathf.RoundToInt(amount * 100f)}%" : $"+{Mathf.RoundToInt(amount)}";
    }

    /// <summary>
    /// Round-end upgrades: five different stats are offered, the player keeps two. Each offer rolls a grade — common,
    /// rare, epic or legendary, the rarer ones more likely as the rounds go on — that multiplies how much the stat grows
    /// (a random amount within its own range). Capped stats stop being offered once they reach their cap.
    /// </summary>
    public static class Upgrades
    {
        public const int Offered = 5, Picks = 2;

        public struct UpgradeDef
        {
            public string title;
            public float min, max;
            public float cap;      // ∞ when uncapped
            public bool percent;   // shown as a percentage (multiplier stats)
            public bool whole;     // whole steps (flasks, charges)
        }

        static readonly Dictionary<UpgradeKind, UpgradeDef> defs = new Dictionary<UpgradeKind, UpgradeDef>
        {
            { UpgradeKind.Flask,         new UpgradeDef { title = "회복약 개수",     min = 1f,    max = 1f,    cap = 5f,    whole = true } },
            { UpgradeKind.HealAmount,    new UpgradeDef { title = "회복약 회복량",   min = 6f,    max = 12f,   cap = float.PositiveInfinity } },
            { UpgradeKind.AttackPower,   new UpgradeDef { title = "공격력",          min = 0.06f, max = 0.12f, cap = float.PositiveInfinity, percent = true } },
            { UpgradeKind.Defense,       new UpgradeDef { title = "방어력",          min = 5f,    max = 12f,   cap = float.PositiveInfinity } },
            { UpgradeKind.MaxHealth,     new UpgradeDef { title = "최대 체력",       min = 10f,   max = 20f,   cap = float.PositiveInfinity } },
            { UpgradeKind.AttackRange,   new UpgradeDef { title = "공격 범위",       min = 0.04f, max = 0.08f, cap = 1.3f,  percent = true } },
            { UpgradeKind.AttackSpeed,   new UpgradeDef { title = "공격 속도",       min = 0.03f, max = 0.06f, cap = 1.35f, percent = true } },
            { UpgradeKind.DodgeRecharge, new UpgradeDef { title = "회피 충전 속도",  min = 0.1f,  max = 0.2f,  cap = 1.6f,  percent = true } },
            { UpgradeKind.MaxDodges,     new UpgradeDef { title = "회피 충전 횟수",  min = 1f,    max = 1f,    cap = 3f,    whole = true } },
            { UpgradeKind.MaxStamina,    new UpgradeDef { title = "최대 기력",       min = 8f,    max = 15f,   cap = float.PositiveInfinity } },
            { UpgradeKind.LightCost,     new UpgradeDef { title = "일반공격 기력 소모 감소", min = 0.06f, max = 0.12f, cap = 0.6f,  percent = true } },
            { UpgradeKind.HeavyCost,     new UpgradeDef { title = "강공격 기력 소모 감소", min = 0.06f, max = 0.12f, cap = 0.5f,  percent = true } },
            { UpgradeKind.CritChance,    new UpgradeDef { title = "치명타 확률",     min = 0.03f, max = 0.06f, cap = 0.6f,  percent = true } },
            { UpgradeKind.CritDamage,    new UpgradeDef { title = "치명타 피해",     min = 0.1f,  max = 0.25f, cap = 3f,    percent = true } },
            { UpgradeKind.MoveSpeed,     new UpgradeDef { title = "이동 속도",       min = 0.03f, max = 0.06f, cap = 1.3f,  percent = true } },
            { UpgradeKind.StaminaRegen,  new UpgradeDef { title = "기력 회복 속도",  min = 0.08f, max = 0.15f, cap = 2f,    percent = true } },
            { UpgradeKind.LifeSteal,     new UpgradeDef { title = "흡혈 (가한 피해만큼 회복)", min = 0.01f, max = 0.02f, cap = 0.15f, percent = true } },
            { UpgradeKind.StaggerPower,  new UpgradeDef { title = "경직 피해",       min = 0.06f, max = 0.12f, cap = 2f,    percent = true } },
            { UpgradeKind.KillHeal,      new UpgradeDef { title = "처치 시 체력 회복", min = 2f,  max = 4f,    cap = 20f } },
        };

        public static UpgradeDef Def(UpgradeKind k) => defs[k];

        static readonly string[] GradeNames = { "일반", "희귀", "영웅", "전설" };
        /// <summary>How much more a grade grows the stat than a common one.</summary>
        static readonly float[] GradeMult = { 1f, 1.5f, 2.1f, 3f };

        public static string GradeName(UpgradeGrade g) => GradeNames[(int)g];

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
            UpgradeKind.KillHeal => s.killHeal,
            _ => s.maxStamina,
        };

        public static bool AtCap(PlayerStats s, UpgradeKind k) => Current(s, k) >= Def(k).cap - 0.0001f;

        /// <summary>
        /// Five different stats (fewer if too many are capped), each with a rolled grade and an amount that respects its
        /// cap. Whole-step stats (flasks, roll charges) grow by 2 at epic and legendary.
        /// </summary>
        public static List<Upgrade> Roll(PlayerStats s, int round = 1)
        {
            var pool = new List<UpgradeKind>();
            foreach (UpgradeKind k in System.Enum.GetValues(typeof(UpgradeKind)))
                if (!AtCap(s, k)) pool.Add(k);
            var offer = new List<Upgrade>();
            while (offer.Count < Offered && pool.Count > 0)
            {
                int i = Random.Range(0, pool.Count);
                var k = pool[i];
                pool.RemoveAt(i);
                var d = Def(k);
                var grade = RollGrade(round);
                float amount = d.whole ? (grade >= UpgradeGrade.Epic ? 2f : 1f) : Random.Range(d.min, d.max) * GradeMult[(int)grade];
                if (d.percent) amount = Mathf.Max(0.01f, Mathf.Round(amount * 100f) / 100f);
                else if (!d.whole) amount = Mathf.Round(amount);
                amount = Mathf.Min(amount, d.cap - Current(s, k));
                offer.Add(new Upgrade { kind = k, grade = grade, amount = amount });
            }
            return offer;
        }

        public static void Apply(PlayerStats s, Upgrade u)
        {
            float cap = Def(u.kind).cap;
            switch (u.kind)
            {
                case UpgradeKind.Flask: s.maxFlasks = Mathf.Min((int)cap, s.maxFlasks + Mathf.RoundToInt(u.amount)); break;
                case UpgradeKind.HealAmount: s.healAmount += u.amount; break;
                case UpgradeKind.AttackPower: s.attackPower += u.amount; break;
                case UpgradeKind.Defense: s.defense += u.amount; break;
                case UpgradeKind.MaxHealth: s.maxHealth += u.amount; break;
                case UpgradeKind.AttackRange: s.attackRange = Mathf.Min(cap, s.attackRange + u.amount); break;
                case UpgradeKind.AttackSpeed: s.attackSpeed = Mathf.Min(cap, s.attackSpeed + u.amount); break;
                case UpgradeKind.DodgeRecharge: s.dodgeRecharge = Mathf.Min(cap, s.dodgeRecharge + u.amount); break;
                case UpgradeKind.MaxDodges: s.maxDodges = Mathf.Min((int)cap, s.maxDodges + Mathf.RoundToInt(u.amount)); break;
                case UpgradeKind.MaxStamina: s.maxStamina += u.amount; break;
                case UpgradeKind.LightCost: s.lightCostCut = Mathf.Min(cap, s.lightCostCut + u.amount); break;
                case UpgradeKind.HeavyCost: s.heavyCostCut = Mathf.Min(cap, s.heavyCostCut + u.amount); break;
                case UpgradeKind.CritChance: s.critChance = Mathf.Min(cap, s.critChance + u.amount); break;
                case UpgradeKind.CritDamage: s.critDamage = Mathf.Min(cap, s.critDamage + u.amount); break;
                case UpgradeKind.MoveSpeed: s.moveSpeed = Mathf.Min(cap, s.moveSpeed + u.amount); break;
                case UpgradeKind.StaminaRegen: s.staminaRegen = Mathf.Min(cap, s.staminaRegen + u.amount); break;
                case UpgradeKind.LifeSteal: s.lifeSteal = Mathf.Min(cap, s.lifeSteal + u.amount); break;
                case UpgradeKind.StaggerPower: s.staggerPower = Mathf.Min(cap, s.staggerPower + u.amount); break;
                case UpgradeKind.KillHeal: s.killHeal = Mathf.Min(cap, s.killHeal + u.amount); break;
            }
        }

        /// <summary>"85% → 91%" / "1 → 2" preview of an upgrade.</summary>
        public static string Preview(PlayerStats s, Upgrade u)
        {
            var d = Def(u.kind);
            float a = Current(s, u.kind), b = Mathf.Min(d.cap, a + u.amount);
            return d.percent ? $"{Mathf.RoundToInt(a * 100f)}% → {Mathf.RoundToInt(b * 100f)}%" : $"{Mathf.RoundToInt(a)} → {Mathf.RoundToInt(b)}";
        }
    }
}

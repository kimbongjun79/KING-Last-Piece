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

        public static PlayerStats Classic() => new PlayerStats();

        /// <summary>The endless mode's starting King: much weaker in every way, one flask, one roll.</summary>
        public static PlayerStats EndlessStart() => new PlayerStats
        {
            maxHealth = 70f,
            maxStamina = 70f,
            attackPower = 0.7f,
            attackSpeed = 0.85f,
            attackRange = 0.8f,
            defense = 0f,
            maxFlasks = 1,
            healAmount = 20f,
            maxDodges = 1,
            dodgeRecharge = 0.45f,
        };

        public PlayerStats Clone() => (PlayerStats)MemberwiseClone();

        public float DamageTaken(float damage) => damage * 100f / (100f + Mathf.Max(0f, defense));
    }

    public enum UpgradeKind { Flask, HealAmount, AttackPower, Defense, MaxHealth, AttackRange, AttackSpeed, DodgeRecharge, MaxDodges, MaxStamina }

    /// <summary>One offered upgrade: a stat and the (rolled) amount it grows by.</summary>
    public struct Upgrade
    {
        public UpgradeKind kind;
        public float amount;

        public string Title => Upgrades.Def(kind).title;

        /// <summary>"+12%" / "+1" / "+8" style label of the gain.</summary>
        public string AmountText => Upgrades.Def(kind).percent ? $"+{Mathf.RoundToInt(amount * 100f)}%" : $"+{Mathf.RoundToInt(amount)}";
    }

    /// <summary>
    /// Round-end upgrades: five different stats are offered, the player keeps two. Each stat grows by a random amount
    /// within its own range; capped stats (attack speed, range, flasks, roll charges, roll recharge) stop being offered
    /// once they reach their cap.
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
            { UpgradeKind.Flask,         new UpgradeDef { title = "Royal Flask",       min = 1f,    max = 1f,    cap = 5f,    whole = true } },
            { UpgradeKind.HealAmount,    new UpgradeDef { title = "Flask Potency",     min = 6f,    max = 12f,   cap = float.PositiveInfinity } },
            { UpgradeKind.AttackPower,   new UpgradeDef { title = "Attack Power",      min = 0.06f, max = 0.12f, cap = float.PositiveInfinity, percent = true } },
            { UpgradeKind.Defense,       new UpgradeDef { title = "Defense",           min = 5f,    max = 12f,   cap = float.PositiveInfinity } },
            { UpgradeKind.MaxHealth,     new UpgradeDef { title = "Max Health",        min = 10f,   max = 20f,   cap = float.PositiveInfinity } },
            { UpgradeKind.AttackRange,   new UpgradeDef { title = "Attack Range",      min = 0.04f, max = 0.08f, cap = 1.3f,  percent = true } },
            { UpgradeKind.AttackSpeed,   new UpgradeDef { title = "Attack Speed",      min = 0.03f, max = 0.06f, cap = 1.35f, percent = true } },
            { UpgradeKind.DodgeRecharge, new UpgradeDef { title = "Roll Recharge",     min = 0.1f,  max = 0.2f,  cap = 1.6f,  percent = true } },
            { UpgradeKind.MaxDodges,     new UpgradeDef { title = "Roll Charges",      min = 1f,    max = 1f,    cap = 3f,    whole = true } },
            { UpgradeKind.MaxStamina,    new UpgradeDef { title = "Max Stamina",       min = 8f,    max = 15f,   cap = float.PositiveInfinity } },
        };

        public static UpgradeDef Def(UpgradeKind k) => defs[k];

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
            _ => s.maxStamina,
        };

        public static bool AtCap(PlayerStats s, UpgradeKind k) => Current(s, k) >= Def(k).cap - 0.0001f;

        /// <summary>Five different stats (fewer if too many are capped), each with a rolled amount that respects its cap.</summary>
        public static List<Upgrade> Roll(PlayerStats s)
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
                float amount = d.whole ? d.min : Random.Range(d.min, d.max);
                if (d.percent) amount = Mathf.Round(amount * 100f) / 100f;
                else if (!d.whole) amount = Mathf.Round(amount);
                amount = Mathf.Min(amount, d.cap - Current(s, k));
                offer.Add(new Upgrade { kind = k, amount = amount });
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

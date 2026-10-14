using UnityEngine;

namespace KingLastPiece
{
    /// <summary>IMGUI HUD: health/stamina with damage trail, flasks, lock-on marker, enemy and boss bars, overlays.</summary>
    public class Hud : MonoBehaviour
    {
        GUIStyle big, mid, small, label;
        float hpTrail = 100f, bossTrail = 1f;
        float scale;

        static readonly Color Gold = new Color(0.95f, 0.78f, 0.35f);
        static readonly Color Blood = new Color(0.72f, 0.08f, 0.06f);

        void Update()
        {
            var g = GameManager.Instance;
            if (g == null || g.Player == null) return;
            float dt = Time.unscaledDeltaTime;
            float hp = g.Player.Health;
            hpTrail = hp > hpTrail ? hp : Mathf.MoveTowards(hpTrail, hp, 40f * dt);

            var boss = g.Boss;
            float bf = boss != null ? boss.Health / boss.MaxHealth : 0f;
            bossTrail = bf > bossTrail ? bf : Mathf.MoveTowards(bossTrail, bf, 0.25f * dt);
        }

        void EnsureStyles()
        {
            scale = Screen.height / 1080f;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                mid = new GUIStyle(big);
                small = new GUIStyle(big) { fontStyle = FontStyle.Normal };
                label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            }
            big.fontSize = Mathf.RoundToInt(96 * scale);
            mid.fontSize = Mathf.RoundToInt(34 * scale);
            small.fontSize = Mathf.RoundToInt(22 * scale);
            label.fontSize = Mathf.RoundToInt(20 * scale);
        }

        void OnGUI()
        {
            var g = GameManager.Instance;
            if (g == null || g.Player == null) return;
            EnsureStyles();

            switch (g.State)
            {
                case GameManager.GameState.Title:
                    DrawTitle();
                    return;
                case GameManager.GameState.Victory:
                    DrawVictory(g);
                    return;
            }

            DrawPlayerBars(g.Player);
            DrawEnemyBars(g);
            DrawBossBar(g);
            DrawLockMarker(g);
            DrawJustDodge(g);
            DrawBanner(g);
            DrawPopups(g);
            DrawControlsHint();
            if (g.CurrentMode == GameManager.Mode.Lab) DrawLabPanel();

            if (g.State == GameManager.GameState.Dead) DrawDeath(g);
            if (g.Paused) DrawPause();
        }

        // ---------------------------------------------------------------- pieces

        void DrawPlayerBars(PlayerController p)
        {
            float x = 40 * scale, y = 36 * scale;
            float w = 420 * scale, h = 18 * scale;

            Fill(new Rect(x - 2, y - 2, w + 4, h + 4), new Color(0, 0, 0, 0.75f));
            Fill(new Rect(x, y, w * Mathf.Clamp01(hpTrail / p.MaxHealth), h), new Color(0.95f, 0.7f, 0.25f));
            Fill(new Rect(x, y, w * Mathf.Clamp01(p.Health / p.MaxHealth), h), Blood);

            y += h + 8 * scale;
            float sw = 340 * scale, sh = 12 * scale;
            Fill(new Rect(x - 2, y - 2, sw + 4, sh + 4), new Color(0, 0, 0, 0.75f));
            Fill(new Rect(x, y, sw * (p.Stamina / PlayerController.MaxStamina), sh), new Color(0.25f, 0.62f, 0.3f));

            // Dodge charges: diamonds that refill one by one.
            y += sh + 8 * scale;
            float ds = 14 * scale;
            for (int i = 0; i < PlayerController.MaxDodges; i++)
            {
                var r = new Rect(x + i * (ds + 10 * scale), y, ds, ds);
                Fill(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(0, 0, 0, 0.7f));
                float fill = i < p.DodgeCharges ? 1f : i == p.DodgeCharges ? p.DodgeRechargeProgress : 0f;
                Fill(new Rect(r.x, r.y + r.height * (1f - fill), r.width, r.height * fill),
                    i < p.DodgeCharges ? new Color(0.5f, 0.85f, 1f) : new Color(0.3f, 0.5f, 0.65f));
            }
            y += ds + 12 * scale;
            float fs = 26 * scale;
            for (int i = 0; i < PlayerController.MaxFlasks; i++)
            {
                var r = new Rect(x + i * (fs + 8 * scale), y, fs, fs);
                Fill(r, new Color(0, 0, 0, 0.7f));
                var inner = new Rect(r.x + 4 * scale, r.y + 4 * scale, r.width - 8 * scale, r.height - 8 * scale);
                Fill(inner, i < p.Flasks ? Gold : new Color(0.2f, 0.2f, 0.2f, 0.8f));
            }
            Text(new Rect(x + PlayerController.MaxFlasks * (fs + 8 * scale) + 6 * scale, y - 2 * scale, 300 * scale, fs + 4 * scale),
                "Royal Flask", label, new Color(0.85f, 0.8f, 0.7f));
        }

        void DrawEnemyBars(GameManager g)
        {
            var cam = g.Rig.Cam;
            foreach (var e in g.Enemies)
            {
                if (e == null || e.IsDead || e.IsBoss) continue;
                bool recent = Time.time - e.LastHitTime < 4f;
                if (!recent && g.Player.LockTarget != e) continue;
                Vector3 sp = cam.WorldToScreenPoint(e.transform.position + Vector3.up * 2.3f);
                if (sp.z < 0f) continue;
                float w = 90 * scale, h = 7 * scale;
                var r = new Rect(sp.x - w / 2, Screen.height - sp.y, w, h);
                Fill(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), new Color(0, 0, 0, 0.8f));
                Fill(new Rect(r.x, r.y, w * (e.Health / e.MaxHealth), h), Blood);
            }
        }

        void DrawBossBar(GameManager g)
        {
            var boss = g.Boss;
            if (boss == null) return;
            float w = 900 * scale, h = 16 * scale;
            float x = (Screen.width - w) / 2f, y = Screen.height - 110 * scale;
            Text(new Rect(x, y - 36 * scale, w, 32 * scale), boss.DisplayName, label, new Color(0.9f, 0.85f, 0.75f));
            Fill(new Rect(x - 2, y - 2, w + 4, h + 4), new Color(0, 0, 0, 0.8f));
            Fill(new Rect(x, y, w * bossTrail, h), new Color(0.95f, 0.7f, 0.25f));
            Fill(new Rect(x, y, w * (boss.Health / boss.MaxHealth), h), Blood);
        }

        void DrawLockMarker(GameManager g)
        {
            var t = g.Player.LockTarget;
            if (t == null) return;
            Vector3 sp = g.Rig.Cam.WorldToScreenPoint(t.transform.position + Vector3.up * (t.IsBoss ? 1.8f : 1.1f));
            if (sp.z < 0f) return;
            float s = 12 * scale;
            var r = new Rect(sp.x - s / 2, Screen.height - sp.y - s / 2, s, s);
            Fill(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(0, 0, 0, 0.6f));
            Fill(r, new Color(1f, 0.95f, 0.85f));
        }

        void DrawBanner(GameManager g)
        {
            if (string.IsNullOrEmpty(g.Banner)) return;
            float age = Time.unscaledTime - g.BannerTime;
            if (age > 3f) return;
            float a = Mathf.Clamp01(age / 0.4f) * Mathf.Clamp01((3f - age) / 0.6f);
            var r = new Rect(0, Screen.height * 0.22f, Screen.width, 60 * scale);
            Fill(new Rect(0, r.y - 10 * scale, Screen.width, r.height + 20 * scale), new Color(0, 0, 0, 0.45f * a));
            Text(r, g.Banner.ToUpperInvariant(), mid, new Color(Gold.r, Gold.g, Gold.b, a));
        }

        void DrawPopups(GameManager g)
        {
            var cam = g.Rig.Cam;
            var style = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(22 * scale) };
            foreach (var p in g.Popups)
            {
                float age = Time.unscaledTime - p.time;
                if (age > 0.9f) continue;
                Vector3 sp = cam.WorldToScreenPoint(p.position + Vector3.up * age * 0.8f);
                if (sp.z < 0f) continue;
                float a = Mathf.Clamp01((0.9f - age) / 0.3f);
                Text(new Rect(sp.x - 60 * scale, Screen.height - sp.y - 15 * scale, 120 * scale, 30 * scale),
                    Mathf.RoundToInt(p.amount).ToString(), style, new Color(1f, 0.9f, 0.6f, a));
            }
        }

        void DrawLabPanel()
        {
            var lab = AttackLab.Instance;
            if (lab == null) return;
            float w = 620 * scale, x = Screen.width - w - 24 * scale, y = 24 * scale, line = 26 * scale;
            var styles = AttackStyles.All;
            float h = (styles.Length + 7) * line + 20 * scale;
            Fill(new Rect(x, y, w, h), new Color(0f, 0f, 0f, 0.6f));
            x += 14 * scale;
            y += 10 * scale;
            Text(new Rect(x, y, w, line), $"ATTACK LAB  —  1-6 style · C reset · G pawns · H hit-stop {(GameManager.Instance.HitStopEnabled ? "ON" : "OFF")} · Stamina ∞", label, Gold);
            y += line * 1.3f;
            for (int i = 0; i < styles.Length; i++)
            {
                bool sel = i == lab.StyleIndex;
                string star = i == AttackStyles.DefaultIndex ? "  ★ chosen" : "";
                Text(new Rect(x, y, w, line), $"{(sel ? "▶" : "  ")} {i + 1}. {styles[i].name} ({styles[i].koName}){star}", label,
                    sel ? Color.white : new Color(0.65f, 0.65f, 0.65f));
                y += line;
            }
            var s = lab.Style;
            y += line * 0.4f;
            var desc = new GUIStyle(label) { wordWrap = true, fontStyle = FontStyle.Normal, alignment = TextAnchor.UpperLeft };
            Text(new Rect(x, y, w - 28 * scale, line * 2.2f), s.description, desc, new Color(0.85f, 0.82f, 0.75f));
            y += line * 2.3f;
            var light = s.lights[0];
            Text(new Rect(x, y, w, line),
                $"Light ×{s.lights.Length}: startup {light.FirstHit:0.00}s  reach {light.Reach:0.0}  arc {light.Coverage:0}°  DPS {s.ComboDps:0}",
                desc, new Color(0.75f, 0.85f, 0.75f));
            y += line;
            Text(new Rect(x, y, w, line),
                $"Heavy: startup {s.heavy.FirstHit:0.00}s  reach {s.heavy.Reach:0.0}  dmg {s.heavy.TotalDamage:0}  arc {s.heavy.Coverage:0}°",
                desc, new Color(0.85f, 0.75f, 0.7f));
        }

        void DrawJustDodge(GameManager g)
        {
            if (!g.SlowMoActive) return;
            // Cool tint + letterbox while time is slowed.
            float a = Mathf.Clamp01(g.SlowMoRemaining / 0.3f);
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.25f, 0.45f, 0.8f, 0.12f * a));
            float bar = 50 * scale * a;
            Fill(new Rect(0, 0, Screen.width, bar), new Color(0, 0, 0, 0.8f));
            Fill(new Rect(0, Screen.height - bar, Screen.width, bar), new Color(0, 0, 0, 0.8f));

            var p = g.Player;
            if (p.IsCountering) return;
            Text(new Rect(0, Screen.height * 0.28f, Screen.width, 50 * scale), "PERFECT DODGE", mid,
                new Color(0.6f, 0.9f, 1f, a));
            if (p.CounterReady)
            {
                float blink = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 14f);
                Text(new Rect(0, Screen.height * 0.28f + 46 * scale, Screen.width, 36 * scale), "LMB — ROYAL THROW", small,
                    new Color(1f, 0.9f, 0.6f, a * blink));
            }
        }

        void DrawControlsHint()
        {
            var r = new Rect(20 * scale, Screen.height - 40 * scale, Screen.width, 30 * scale);
            Text(r, "WASD Move   Mouse Look   LMB Light (hold)   RMB Heavy   Space Roll (x3)   Q Lock-on   R Flask   Esc Pause",
                new GUIStyle(label) { fontSize = Mathf.RoundToInt(15 * scale), fontStyle = FontStyle.Normal },
                new Color(1f, 1f, 1f, 0.45f));
        }

        void DrawTitle()
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.45f));
            float cy = Screen.height * 0.3f;
            Text(new Rect(0, cy, Screen.width, 120 * scale), "KING", big, new Color(0.95f, 0.92f, 0.85f));
            Text(new Rect(0, cy + 105 * scale, Screen.width, 50 * scale), "T H E   L A S T   P I E C E", mid, Gold);
            Text(new Rect(0, cy + 175 * scale, Screen.width, 40 * scale),
                "Every white piece has fallen. Only the King remains on the board.", small, new Color(0.8f, 0.78f, 0.72f));

            string controls =
                "WASD / Left Stick — Move        Mouse / Right Stick — Camera\n" +
                "LMB / R1 — Light attack        RMB / R2 — Heavy attack\n" +
                "Space / B — Roll (3 charges)        Q / R3 — Lock-on        R / X — Royal Flask\n" +
                "Roll right before a hit → slow motion → LMB: Royal Throw";
            Text(new Rect(0, cy + 250 * scale, Screen.width, 160 * scale), controls, small, new Color(0.7f, 0.7f, 0.7f));

            float blink = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f);
            Text(new Rect(0, Screen.height * 0.82f, Screen.width, 40 * scale), "Press ENTER / START", mid,
                new Color(1f, 1f, 1f, blink));
        }

        void DrawDeath(GameManager g)
        {
            float a = Mathf.Clamp01((g.StateTime - 0.6f) / 1.0f);
            float bandH = 200 * scale;
            Fill(new Rect(0, (Screen.height - bandH) / 2f, Screen.width, bandH), new Color(0, 0, 0, 0.75f * a));
            Text(new Rect(0, (Screen.height - bandH) / 2f, Screen.width, bandH * 0.75f), "CHECKMATE", big,
                new Color(0.75f, 0.06f, 0.04f, a));
            if (g.StateTime > 1.8f)
                Text(new Rect(0, Screen.height / 2f + 50 * scale, Screen.width, 40 * scale),
                    "Press ENTER / START to rise again", small, new Color(0.85f, 0.8f, 0.75f, a));
        }

        void DrawVictory(GameManager g)
        {
            float a = Mathf.Clamp01(g.StateTime / 1.5f);
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.5f * a));
            float cy = Screen.height * 0.32f;
            Text(new Rect(0, cy, Screen.width, 120 * scale), "THE KING STANDS", big, new Color(Gold.r, Gold.g, Gold.b, a));
            int m = Mathf.FloorToInt(g.RunTime / 60f), s = Mathf.FloorToInt(g.RunTime % 60f);
            Text(new Rect(0, cy + 130 * scale, Screen.width, 40 * scale),
                $"Time {m:00}:{s:00}     Deaths {g.Deaths}", mid, new Color(0.9f, 0.88f, 0.8f, a));
            if (g.StateTime > 2.5f)
                Text(new Rect(0, Screen.height * 0.8f, Screen.width, 40 * scale), "Press ENTER / START to play again", small,
                    new Color(1f, 1f, 1f, a));
        }

        void DrawPause()
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.55f));
            Text(new Rect(0, Screen.height * 0.4f, Screen.width, 60 * scale), "PAUSED", mid, Color.white);
            Text(new Rect(0, Screen.height * 0.4f + 60 * scale, Screen.width, 40 * scale), "Esc / Start to resume", small,
                new Color(0.8f, 0.8f, 0.8f));
        }

        // ---------------------------------------------------------------- primitives

        static void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        static void Text(Rect r, string text, GUIStyle style, Color c)
        {
            var shadow = new Rect(r.x + 2, r.y + 2, r.width, r.height);
            style.normal.textColor = new Color(0, 0, 0, c.a * 0.8f);
            GUI.Label(shadow, text, style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
        }
    }
}

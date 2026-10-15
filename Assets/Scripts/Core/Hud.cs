using UnityEngine;

namespace KingLastPiece
{
    /// <summary>IMGUI HUD: health/stamina with damage trail, flasks, lock-on marker, enemy and boss bars, overlays.</summary>
    public class Hud : MonoBehaviour
    {
        GUIStyle big, mid, small, label;
        float hpTrail = 100f, bossTrail = 1f, rollAlpha = 1f;
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
            DrawRollCharges(g);
            DrawEnemyBars(g);
            DrawBossBar(g);
            DrawLockMarker(g);
            DrawJustDodge(g);
            DrawBanner(g);
            DrawPopups(g);
            if (g.CurrentMode == GameManager.Mode.Game) DrawRound(g);
            DrawControlsHint();
            if (g.CurrentMode == GameManager.Mode.Practice) DrawPracticePanel();

            if (g.State == GameManager.GameState.Dead) DrawDeath(g);
            if (g.Paused) DrawPause();
        }

        // ---------------------------------------------------------------- pieces

        void DrawPlayerBars(PlayerController p)
        {
            float u = scale * 1.25f;  // a touch larger than the base HUD scale so it reads mid-fight
            float x = 32 * scale, y = 28 * scale;
            float panelW = 500 * u, panelH = 118 * u;

            // Backing plate with a gold hairline, and a chess-tile emblem with the King's crown.
            Fill(new Rect(x - 12 * scale, y - 12 * scale, panelW, panelH), new Color(0f, 0f, 0f, 0.45f));
            Fill(new Rect(x - 12 * scale, y - 12 * scale, panelW, 2 * scale), new Color(Gold.r, Gold.g, Gold.b, 0.6f));
            float em = 66 * u;
            DrawEmblem(new Rect(x, y, em, em));

            float bx = x + em + 14 * u;

            // Health.
            float w = 380 * u, h = 20 * u;
            Gauge(new Rect(bx, y + 2 * u, w, h), p.Health / p.MaxHealth, hpTrail / p.MaxHealth,
                new Color(0.62f, 0.07f, 0.06f), new Color(0.95f, 0.3f, 0.22f));
            SmallText(new Rect(bx + 8 * u, y + 2 * u, w - 16 * u, h), $"{Mathf.CeilToInt(p.Health)} / {Mathf.RoundToInt(p.MaxHealth)}",
                TextAnchor.MiddleRight, 15);

            // Stamina (pulses amber when nearly empty).
            float sy = y + h + 10 * u, sw = 320 * u, sh = 13 * u;
            float st = p.Stamina / PlayerController.MaxStamina;
            bool low = st < 0.25f;
            float pulse = low ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f) : 0f;
            Color stMain = Color.Lerp(new Color(0.2f, 0.55f, 0.28f), new Color(0.85f, 0.55f, 0.15f), pulse);
            Color stHi = Color.Lerp(new Color(0.45f, 0.9f, 0.5f), new Color(1f, 0.8f, 0.35f), pulse);
            Gauge(new Rect(bx, sy, sw, sh), st, st, stMain, stHi);

            // Royal Flasks: little gold vials.
            float fy = sy + sh + 8 * u;
            for (int i = 0; i < PlayerController.MaxFlasks; i++)
                DrawVial(new Rect(bx + i * 28 * u, fy, 19 * u, 30 * u), i < p.Flasks);
            SmallText(new Rect(bx + PlayerController.MaxFlasks * 28 * u + 4 * u, fy + 6 * u, 120 * u, 22 * u),
                $"FLASK  x{p.Flasks}", TextAnchor.MiddleLeft, 15);
        }

        /// <summary>
        /// Roll charges float just under the King so they stay in view mid-fight: gold-rimmed diamonds, a recharging
        /// one grows from the middle. They fade back when all charges are full and the King isn't fighting.
        /// </summary>
        void DrawRollCharges(GameManager g)
        {
            var p = g.Player;
            if (p.IsDead) return;
            Vector3 sp = g.Rig.Cam.WorldToScreenPoint(p.transform.position);
            if (sp.z < 0f) return;
            bool full = p.DodgeCharges >= PlayerController.MaxDodges;
            float target = full ? 0.45f : 1f;
            rollAlpha = Mathf.MoveTowards(rollAlpha, target, Time.unscaledDeltaTime * 3f);

            float ds = 9 * scale, gap = 24 * scale;
            float cx = sp.x - (PlayerController.MaxDodges - 1) * gap / 2f;
            float cy = Screen.height - sp.y + 34 * scale;
            for (int i = 0; i < PlayerController.MaxDodges; i++)
            {
                var c = new Vector2(cx + i * gap, cy);
                Diamond(c, ds + 3 * scale, new Color(0f, 0f, 0f, 0.6f * rollAlpha));
                Diamond(c, ds + 2 * scale, new Color(Gold.r, Gold.g, Gold.b, 0.95f * rollAlpha));
                Diamond(c, ds, new Color(0.04f, 0.05f, 0.08f, 0.95f * rollAlpha));
                if (i < p.DodgeCharges) Diamond(c, ds - 2 * scale, new Color(0.55f, 0.88f, 1f, rollAlpha));
                else if (i == p.DodgeCharges)
                    Diamond(c, (ds - 2 * scale) * p.DodgeRechargeProgress, new Color(0.3f, 0.5f, 0.65f, rollAlpha));
            }
        }

        // ---------------------------------------------------------------- styled primitives

        /// <summary>Framed gauge: gold rim, dark well, damage trail, two-tone fill with a highlight, 10% notches.</summary>
        void Gauge(Rect r, float frac, float trail, Color main, Color highlight)
        {
            frac = Mathf.Clamp01(frac);
            trail = Mathf.Clamp01(Mathf.Max(trail, frac));
            float b = Mathf.Max(1f, 1.5f * scale);
            Fill(new Rect(r.x - b - 1, r.y - b - 1, r.width + 2 * b + 2, r.height + 2 * b + 2), new Color(0f, 0f, 0f, 0.8f));
            Fill(new Rect(r.x - b, r.y - b, r.width + 2 * b, r.height + 2 * b), new Color(Gold.r * 0.8f, Gold.g * 0.8f, Gold.b * 0.8f, 0.9f));
            Fill(r, new Color(0.05f, 0.03f, 0.03f, 0.95f));
            if (trail > frac) Fill(new Rect(r.x + r.width * frac, r.y, r.width * (trail - frac), r.height), new Color(0.95f, 0.75f, 0.35f, 0.9f));
            var f = new Rect(r.x, r.y, r.width * frac, r.height);
            Fill(f, main);
            Fill(new Rect(f.x, f.y, f.width, f.height * 0.42f), new Color(highlight.r, highlight.g, highlight.b, 0.55f));
            Fill(new Rect(f.x, f.y + f.height * 0.82f, f.width, f.height * 0.18f), new Color(0f, 0f, 0f, 0.25f));
            if (frac > 0.01f) Fill(new Rect(f.xMax - 2 * scale, f.y, 2 * scale, f.height), new Color(1f, 1f, 1f, 0.5f));
            for (int i = 1; i < 10; i++)
                Fill(new Rect(r.x + r.width * i / 10f, r.y, Mathf.Max(1f, scale), r.height), new Color(0f, 0f, 0f, 0.28f));
        }

        static void Diamond(Vector2 center, float half, Color c)
        {
            if (half <= 0.5f) return;
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, center);
            Fill(new Rect(center.x - half, center.y - half, half * 2f, half * 2f), c);
            GUI.matrix = m;
        }

        /// <summary>2×2 chess tile in a gold frame with a little crown.</summary>
        void DrawEmblem(Rect r)
        {
            Fill(new Rect(r.x - 3 * scale, r.y - 3 * scale, r.width + 6 * scale, r.height + 6 * scale), Gold);
            float hw = r.width / 2f, hh = r.height / 2f;
            var ivory = new Color(0.9f, 0.87f, 0.78f);
            var onyx = new Color(0.1f, 0.09f, 0.1f);
            Fill(new Rect(r.x, r.y, hw, hh), ivory);
            Fill(new Rect(r.x + hw, r.y, hw, hh), onyx);
            Fill(new Rect(r.x, r.y + hh, hw, hh), onyx);
            Fill(new Rect(r.x + hw, r.y + hh, hw, hh), ivory);
            // Crown: band + three points + jewel.
            float cw = r.width * 0.62f, cx = r.center.x - cw / 2f, cy = r.center.y + r.height * 0.06f;
            var crown = new Color(1f, 0.82f, 0.35f);
            var shadow = new Color(0f, 0f, 0f, 0.6f);
            Fill(new Rect(cx + 2 * scale, cy + 2 * scale, cw, r.height * 0.16f), shadow);
            Fill(new Rect(cx, cy, cw, r.height * 0.16f), crown);
            float pw = cw * 0.2f, ph = r.height * 0.22f;
            for (int i = 0; i < 3; i++)
            {
                float px = cx + i * (cw - pw) / 2f;
                Fill(new Rect(px, cy - ph, pw, ph), crown);
            }
            Diamond(new Vector2(r.center.x, cy - ph - 3 * scale), 3.5f * scale, crown);
            Diamond(new Vector2(r.center.x, cy + r.height * 0.08f), 3f * scale, new Color(0.75f, 0.1f, 0.1f));
        }

        void DrawVial(Rect r, bool full)
        {
            var glass = new Color(0.05f, 0.05f, 0.07f, 0.9f);
            var rim = new Color(Gold.r, Gold.g, Gold.b, 0.85f);
            float neckW = r.width * 0.45f, neckH = r.height * 0.22f;
            var neck = new Rect(r.center.x - neckW / 2f, r.y, neckW, neckH);
            var body = new Rect(r.x, r.y + neckH, r.width, r.height - neckH);
            Fill(new Rect(body.x - 1.5f * scale, body.y - 1.5f * scale, body.width + 3 * scale, body.height + 3 * scale), rim);
            Fill(new Rect(neck.x - 1.5f * scale, neck.y - 1.5f * scale, neck.width + 3 * scale, neck.height + 1.5f * scale), rim);
            Fill(body, glass);
            Fill(neck, glass);
            if (!full) return;
            var liquid = new Rect(body.x + 2 * scale, body.y + body.height * 0.2f, body.width - 4 * scale, body.height * 0.8f - 2 * scale);
            Fill(liquid, new Color(0.95f, 0.65f, 0.18f));
            Fill(new Rect(liquid.x, liquid.y, liquid.width * 0.35f, liquid.height), new Color(1f, 0.9f, 0.6f, 0.6f));
        }

        void SmallText(Rect r, string text, TextAnchor anchor, int size = 13)
        {
            var st = new GUIStyle(label) { fontSize = Mathf.RoundToInt(size * scale), alignment = anchor };
            Text(r, text, st, new Color(0.95f, 0.92f, 0.85f, 0.95f));
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
            var name = new GUIStyle(label) { fontSize = Mathf.RoundToInt(22 * scale) };
            Text(new Rect(x, y - 36 * scale, w, 32 * scale), boss.DisplayName.ToUpperInvariant(), name, Gold);
            Gauge(new Rect(x, y, w, h), boss.Health / boss.MaxHealth, bossTrail,
                new Color(0.5f, 0.06f, 0.1f), new Color(0.9f, 0.25f, 0.35f));
            // Ornamental diamonds capping both ends.
            Diamond(new Vector2(x - 12 * scale, y + h / 2f), 8 * scale, Gold);
            Diamond(new Vector2(x + w + 12 * scale, y + h / 2f), 8 * scale, Gold);
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

        void DrawRound(GameManager g)
        {
            var r = new Rect(0, 18 * scale, Screen.width, 30 * scale);
            Text(r, $"ROUND {g.WaveIndex + 1} / {g.WaveCount}", small, new Color(0.9f, 0.85f, 0.7f, 0.85f));
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

        void DrawPracticePanel()
        {
            var arena = PracticeArena.Instance;
            if (arena == null) return;
            var g = GameManager.Instance;
            float w = 430 * scale, x = Screen.width - w - 24 * scale, y = 24 * scale, line = 26 * scale;
            string[] rows =
            {
                "1-5   Spawn Pawn / Knight / Bishop / Rook / Queen",
                "X     Clear pieces          C   Reset arena",
                $"G     Invincible: {(arena.Invincible ? "ON" : "OFF")}",
                $"I     Infinite stamina: {(arena.InfiniteStamina ? "ON" : "OFF")}",
                $"H     Hit-stop: {(g.HitStopEnabled ? "ON" : "OFF")}",
                $"V     Board slots: {(arena.SlotViewOn ? "ON" : "OFF")}",
            };
            Fill(new Rect(x, y, w, (rows.Length + 1.6f) * line + 16 * scale), new Color(0f, 0f, 0f, 0.6f));
            x += 14 * scale;
            y += 8 * scale;
            Text(new Rect(x, y, w, line), "PRACTICE  —  Royal Ensemble", label, Gold);
            y += line * 1.4f;
            var row = new GUIStyle(label) { fontStyle = FontStyle.Normal };
            foreach (var r in rows)
            {
                Text(new Rect(x, y, w, line), r, row, new Color(0.85f, 0.82f, 0.75f));
                y += line;
            }
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
            Text(r, "WASD Move   Shift Sprint   Mouse Look   LMB Light (hold)   RMB Heavy   Space Roll (x3)   Q Lock-on   R Flask   Esc Pause",
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
                "WASD / Left Stick — Move        Shift / L3 — Sprint        Mouse / Right Stick — Camera\n" +
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

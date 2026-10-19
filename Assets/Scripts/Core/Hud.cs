using UnityEngine;

namespace KingLastPiece
{
    /// <summary>IMGUI HUD: health/stamina with damage trail, flasks, lock-on marker, enemy and boss bars, overlays.</summary>
    public class Hud : MonoBehaviour
    {
        GUIStyle big, mid, small, label, title, fieldStyle;
        string nickname;   // results screen: the name the run goes online under
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

            if (g.Paused && GameInput.MenuStep != 0) pauseTab = (pauseTab + GameInput.MenuStep + PauseTabs.Length) % PauseTabs.Length;

            var boss = g.Boss;
            float bf = boss != null ? boss.Health / boss.MaxHealth : 0f;
            bossTrail = bf > bossTrail ? bf : Mathf.MoveTowards(bossTrail, bf, 0.25f * dt);
        }

        void EnsureStyles()
        {
            scale = Screen.height / 1080f;
            if (big == null)
            {
                // Korean UI: NanumSquare Neo (regular / extra bold) for the HUD, Nanum Myeongjo for the title.
                // The faces are real weights, so no faux bold on top.
                var bold = Resources.Load<Font>("Fonts/NanumSquareNeo-dEb");
                var regular = Resources.Load<Font>("Fonts/NanumSquareNeo-bRg");
                var serif = Resources.Load<Font>("Fonts/NanumMyeongjoExtraBold");
                FontStyle heavy = bold != null ? FontStyle.Normal : FontStyle.Bold;
                big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = heavy, font = bold };
                mid = new GUIStyle(big);
                small = new GUIStyle(big) { fontStyle = FontStyle.Normal, font = regular };
                label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = heavy, font = bold };
                title = new GUIStyle(big) { font = serif ?? bold };
            }
            big.fontSize = Mathf.RoundToInt(96 * scale);
            mid.fontSize = Mathf.RoundToInt(34 * scale);
            small.fontSize = Mathf.RoundToInt(22 * scale);
            label.fontSize = Mathf.RoundToInt(20 * scale);
            title.fontSize = Mathf.RoundToInt(150 * scale);
        }

        void OnGUI()
        {
            var g = GameManager.Instance;
            if (g == null || g.Player == null) return;
            EnsureStyles();

            switch (g.State)
            {
                case GameManager.GameState.Title:
                    DrawTitle(g);
                    return;
                case GameManager.GameState.Victory:
                    DrawVictory(g);
                    return;
            }

            DrawLowHealthVignette(g.Player);
            DrawPlayerBars(g.Player);
            DrawRollCharges(g);
            DrawEnemyBars(g);
            DrawBossBar(g);
            DrawLockMarker(g);
            DrawJustDodge(g);
            if (!g.Paused) DrawBanner(g);   // the pause tabs would show it through their panels
            DrawPopups(g);
            if (g.UsesRounds) DrawRound(g);
            DrawControlsHint();
            if (g.CurrentMode == GameManager.Mode.Practice) DrawPracticePanel();

            if (g.State == GameManager.GameState.Dead) DrawDeath(g);
            if (g.State == GameManager.GameState.Upgrade) DrawUpgrades(g);
            if (g.Paused) DrawPause(g);
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
            float st = p.Stamina / p.MaxStamina;
            bool low = st < 0.25f;
            float pulse = low ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f) : 0f;
            Color stMain = Color.Lerp(new Color(0.2f, 0.55f, 0.28f), new Color(0.85f, 0.55f, 0.15f), pulse);
            Color stHi = Color.Lerp(new Color(0.45f, 0.9f, 0.5f), new Color(1f, 0.8f, 0.35f), pulse);
            Gauge(new Rect(bx, sy, sw, sh), st, st, stMain, stHi);

            // Royal Flasks: little gold vials.
            float fy = sy + sh + 8 * u;
            for (int i = 0; i < p.MaxFlasks; i++)
                DrawVial(new Rect(bx + i * 28 * u, fy, 19 * u, 30 * u), i < p.Flasks);
            SmallText(new Rect(bx + p.MaxFlasks * 28 * u + 4 * u, fy + 6 * u, 120 * u, 22 * u),
                $"회복약  x{p.Flasks}", TextAnchor.MiddleLeft, 15);

            // Special items with a live state: Momentum stacks, the Boomerang Crown in hand or in flight.
            string status = "";
            if (p.Has(Special.Momentum)) status += $"기세 {p.Momentum}/5 (+{p.Momentum * 5}%)    ";
            if (p.Has(Special.BoomerangCrown)) status += p.CrownOut ? "왕관: 비행 중" : "왕관: 준비";
            if (status.Length > 0)
                SmallText(new Rect(bx + p.MaxFlasks * 28 * u + 120 * u, fy + 6 * u, 260 * u, 22 * u), status, TextAnchor.MiddleLeft, 15);
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
            bool full = p.DodgeCharges >= p.MaxDodges;
            float target = full ? 0.45f : 1f;
            rollAlpha = Mathf.MoveTowards(rollAlpha, target, Time.unscaledDeltaTime * 3f);

            float ds = 9 * scale, gap = 24 * scale;
            float cx = sp.x - (p.MaxDodges - 1) * gap / 2f;
            float cy = Screen.height - sp.y + 34 * scale;
            for (int i = 0; i < p.MaxDodges; i++)
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
        // Generated icons (Resources/UiIcons): the King on the emblem, the flasks under the bars. Without them the HUD
        // draws its own crown and vials.
        static Texture2D kingIcon, flaskIcon;
        static bool iconsLoaded;

        static void LoadIcons()
        {
            if (iconsLoaded) return;
            iconsLoaded = true;
            kingIcon = Resources.Load<Texture2D>("UiIcons/king");
            flaskIcon = Resources.Load<Texture2D>("UiIcons/flask");
        }

        void DrawEmblem(Rect r)
        {
            LoadIcons();
            Fill(new Rect(r.x - 3 * scale, r.y - 3 * scale, r.width + 6 * scale, r.height + 6 * scale), Gold);
            if (kingIcon != null)
            {
                // The King's picture on a dark plate.
                Fill(r, new Color(0.05f, 0.045f, 0.05f));
                GUI.DrawTexture(r, kingIcon, ScaleMode.ScaleToFit);
                return;
            }
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
            LoadIcons();
            if (flaskIcon != null)
            {
                // The flask picture (square, a little wider than the old vial); an empty slot is a dim ghost of it.
                float s = r.height * 1.15f;
                var sq = new Rect(r.center.x - s / 2f, r.center.y - s / 2f, s, s);
                var old = GUI.color;
                GUI.color = full ? Color.white : new Color(0.35f, 0.35f, 0.38f, 0.45f);
                GUI.DrawTexture(sq, flaskIcon, ScaleMode.ScaleToFit);
                GUI.color = old;
                return;
            }
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
            string round = g.IsEndless ? $"{g.Round} 라운드     체스판 {BoardSlots.Size}×{BoardSlots.Size}" : $"{g.WaveIndex + 1} / {g.WaveCount} 라운드";
            Text(r, round, small, new Color(0.9f, 0.85f, 0.7f, 0.85f));
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
                if (p.crit)
                {
                    // Critical: bigger, hot orange-red, with a little pop at the start.
                    float pop = 1f + 0.35f * Mathf.Clamp01(1f - age / 0.15f);
                    var crit = new GUIStyle(style) { fontSize = Mathf.RoundToInt(34 * scale * pop) };
                    Text(new Rect(sp.x - 100 * scale, Screen.height - sp.y - 26 * scale, 200 * scale, 44 * scale),
                        Mathf.RoundToInt(p.amount).ToString(), crit, new Color(1f, 0.45f, 0.15f, a));
                    continue;
                }
                Text(new Rect(sp.x - 60 * scale, Screen.height - sp.y - 15 * scale, 120 * scale, 30 * scale),
                    Mathf.RoundToInt(p.amount).ToString(), style, new Color(1f, 0.9f, 0.6f, a));
            }
        }

        /// <summary>
        /// Practice ▸ T: every item as an icon (grouped by grade), click to give it to the King right away; the King's
        /// stats beside it. Hover an icon for what it does. Reset forgets every item; the base switches between the
        /// classic King and the endless mode's weak starting King.
        /// </summary>
        void DrawItemTestPanel(PracticeArena arena, PlayerController p)
        {
            var e = Event.current;
            Vector2 mouse = e.mousePosition;
            float pw = 1560 * scale, ph = 820 * scale;
            var panel = new Rect((Screen.width - pw) / 2f, (Screen.height - ph) / 2f, pw, ph);
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.45f));
            Fill(new Rect(panel.x - 2 * scale, panel.y - 2 * scale, panel.width + 4 * scale, panel.height + 4 * scale), new Color(Gold.r, Gold.g, Gold.b, 0.8f));
            Fill(panel, new Color(0.05f, 0.045f, 0.05f, 0.97f));
            Text(new Rect(panel.x + 20 * scale, panel.y + 12 * scale, 600 * scale, 40 * scale), "아이템 테스트  —  클릭하면 바로 적용", label, Gold);

            // Buttons: reset, base, close.
            bool Button(Rect r, string text)
            {
                bool hot = r.Contains(mouse);
                Fill(r, hot ? new Color(Gold.r, Gold.g, Gold.b, 0.9f) : new Color(1f, 1f, 1f, 0.1f));
                Text(r, text, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(17 * scale) },
                    hot ? new Color(0.08f, 0.06f, 0.05f) : new Color(0.9f, 0.88f, 0.8f));
                if (e.type == EventType.MouseDown && hot) { e.Use(); return true; }
                return false;
            }
            float bw = 200 * scale, bh = 40 * scale, by = panel.y + 12 * scale;
            if (Button(new Rect(panel.xMax - 3 * (bw + 12 * scale), by, bw, bh), "아이템 초기화")) arena.ResetItems();
            if (Button(new Rect(panel.xMax - 2 * (bw + 12 * scale), by, bw, bh), arena.EndlessBase ? "기본: 무한 모드 시작" : "기본: 일반 모드")) arena.ToggleBase();
            if (Button(new Rect(panel.xMax - (bw + 12 * scale), by, bw, bh), "닫기  (T)")) arena.ToggleItemPanel();

            // The items, a row per grade (wrapping), with how many the King holds.
            float cell = 66 * scale, icon = 54 * scale, gx = panel.x + 20 * scale, gw = 860 * scale;
            float y = panel.y + 66 * scale;
            int cols = Mathf.FloorToInt(gw / cell);
            Item hover = null;
            var counts = new System.Collections.Generic.Dictionary<Item, int>();
            foreach (var it in p.Items) counts[it] = counts.TryGetValue(it, out int c) ? c + 1 : 1;
            var countStyle = new GUIStyle(small) { fontSize = Mathf.RoundToInt(14 * scale), alignment = TextAnchor.LowerRight, fontStyle = FontStyle.Bold };
            for (int g = 0; g < 4; g++)
            {
                var grade = (UpgradeGrade)g;
                Color gc = GradeColor(grade);
                Text(new Rect(gx, y, 200 * scale, 24 * scale), Upgrades.GradeName(grade), new GUIStyle(small) { fontSize = Mathf.RoundToInt(16 * scale), fontStyle = FontStyle.Bold },
                    gc);
                y += 26 * scale;
                int i = 0;
                foreach (var it in Upgrades.All)
                {
                    if (it.grade != grade) continue;
                    var r = new Rect(gx + (i % cols) * cell, y + (i / cols) * cell, icon, icon);
                    bool usable = !it.IsSpecial || Upgrades.Useful(p.Stats, it, p.Items);
                    bool hot = r.Contains(mouse);
                    if (hot) hover = it;
                    Fill(new Rect(r.x - 2 * scale, r.y - 2 * scale, r.width + 4 * scale, r.height + 4 * scale),
                        hot ? Color.white : it.IsSpecial ? Gold : new Color(gc.r * 0.8f, gc.g * 0.8f, gc.b * 0.8f));
                    Fill(r, new Color(0.02f, 0.02f, 0.025f));
                    var old = GUI.color;
                    GUI.color = usable ? Color.white : new Color(1f, 1f, 1f, 0.3f);
                    if (it.Icon != null) GUI.DrawTexture(r, it.Icon, ScaleMode.ScaleToFit);
                    GUI.color = old;
                    if (counts.TryGetValue(it, out int n))
                        Text(new Rect(r.x, r.y, r.width - 3 * scale, r.height - 1 * scale), it.IsSpecial ? "보유" : $"×{n}", countStyle, new Color(1f, 0.95f, 0.6f));
                    if (hot && e.type == EventType.MouseDown) { arena.GiveItem(it); e.Use(); }
                    i++;
                }
                y += Mathf.CeilToInt(i / (float)cols) * cell + 6 * scale;
            }

            // The King's stats on the right.
            DrawStats(p, new Rect(panel.x + gw + 50 * scale, panel.y + 66 * scale, panel.xMax - (panel.x + gw + 70 * scale), 0f));

            if (hover != null) DrawTooltip(ItemSummary(hover, p), mouse);
        }

        /// <summary>An item's name, grade and what it does (effects with before → after, or a special's description).</summary>
        static string ItemSummary(Item it, PlayerController p)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"[{it.GradeName}{(it.IsSpecial ? " · 특수" : "")}] {it.name}\n");
            if (it.IsSpecial) sb.Append(it.desc);
            else
                foreach (var fx in it.effects)
                    sb.Append($"{Upgrades.Def(fx.kind).title} {Upgrades.Arrows(fx)}   {Upgrades.Preview(p.Stats, fx)}\n");
            return sb.ToString().TrimEnd();
        }

        void DrawPracticePanel()
        {
            var arena = PracticeArena.Instance;
            if (arena == null) return;
            if (arena.ItemPanelOpen)
            {
                DrawItemTestPanel(arena, GameManager.Instance.Player);
                return;
            }
            var g = GameManager.Instance;
            float w = 430 * scale, x = Screen.width - w - 24 * scale, y = 24 * scale, line = 26 * scale;
            string[] rows =
            {
                "1-5   폰 / 나이트 / 비숍 / 룩 / 퀸 소환",
                "X     기물 제거          C   연습장 초기화",
                $"G     무적: {(arena.Invincible ? "켜짐" : "꺼짐")}",
                $"I     무한 기력: {(arena.InfiniteStamina ? "켜짐" : "꺼짐")}",
                $"H     히트스톱: {(g.HitStopEnabled ? "켜짐" : "꺼짐")}",
                $"V     체스판 슬롯 표시: {(arena.SlotViewOn ? "켜짐" : "꺼짐")}",
                $"E     소환 단계: {(arena.SpawnTier == 0 ? "일반" : "다크 " + new string('I', arena.SpawnTier))}",
                "T     아이템 테스트 창 (클릭해서 적용)",
            };
            Fill(new Rect(x, y, w, (rows.Length + 1.6f) * line + 16 * scale), new Color(0f, 0f, 0f, 0.6f));
            x += 14 * scale;
            y += 8 * scale;
            Text(new Rect(x, y, w, line), "연습장  —  로열 앙상블", label, Gold);
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
            Text(new Rect(0, Screen.height * 0.28f, Screen.width, 50 * scale), "완벽한 회피", mid,
                new Color(0.6f, 0.9f, 1f, a));
            if (p.CounterReady)
            {
                float blink = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 14f);
                Text(new Rect(0, Screen.height * 0.28f + 46 * scale, Screen.width, 36 * scale), "좌클릭 — 로열 스로", small,
                    new Color(1f, 0.9f, 0.6f, a * blink));
            }
        }

        void DrawControlsHint()
        {
            var r = new Rect(20 * scale, Screen.height - 40 * scale, Screen.width, 30 * scale);
            Text(r, "WASD 이동   Shift 달리기   좌클릭 일반공격   우클릭 강공격   Space 회피   Q 락온   R 회복약   Esc 일시정지 · 조작법",
                new GUIStyle(label) { fontSize = Mathf.RoundToInt(15 * scale), fontStyle = FontStyle.Normal },
                new Color(1f, 1f, 1f, 0.45f));
        }

        /// <summary>
        /// Title: the hall darkened to a vignette, a serif "KING" between gold rules, the Korean subtitle and tagline, the
        /// mode cards, the best run and the online ranking. (How to play lives in the pause menu now.)
        /// </summary>
        void DrawTitle(GameManager g)
        {
            float t = Time.unscaledTime;
            float W = Screen.width, H = Screen.height;
            Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.5f));
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.DrawTexture(new Rect(-W * 0.1f, -H * 0.1f, W * 1.2f, H * 1.2f), Vignette, ScaleMode.StretchToFill, true);
            GUI.color = old;

            float fade = Mathf.Clamp01(g.StateTime / 1.2f);
            float cy = H * 0.24f;
            var ivory = new Color(0.95f, 0.92f, 0.85f, fade);
            var gold = new Color(Gold.r, Gold.g, Gold.b, fade);

            // Gold rule with a diamond above the title.
            float rule = 420 * scale;
            Fill(new Rect((W - rule) / 2f, cy - 18 * scale, rule, 2 * scale), new Color(Gold.r, Gold.g, Gold.b, 0.7f * fade));
            Diamond(new Vector2(W / 2f, cy - 17 * scale), 7 * scale, gold);

            Text(new Rect(0, cy, W, 170 * scale), "K I N G", title, ivory);
            Text(new Rect(0, cy + 168 * scale, W, 56 * scale), "마 지 막   기 물", new GUIStyle(mid) { fontSize = Mathf.RoundToInt(38 * scale) }, gold);

            // Divider: line · three diamonds · line.
            float dy = cy + 244 * scale, half = 190 * scale;
            Fill(new Rect(W / 2f - half - 30 * scale, dy, half, 1.5f * scale), new Color(Gold.r, Gold.g, Gold.b, 0.55f * fade));
            Fill(new Rect(W / 2f + 30 * scale, dy, half, 1.5f * scale), new Color(Gold.r, Gold.g, Gold.b, 0.55f * fade));
            for (int i = -1; i <= 1; i++) Diamond(new Vector2(W / 2f + i * 18 * scale, dy + 1 * scale), (i == 0 ? 6 : 4) * scale, gold);

            Text(new Rect(0, dy + 22 * scale, W, 40 * scale), "모든 백색 기물이 쓰러졌다.  판 위에 남은 것은 왕 하나뿐.", small,
                new Color(0.82f, 0.79f, 0.72f, fade));
            // Mode select: two framed cards; ←/→ (or the mouse) to choose, Enter / click to start.
            (string name, string line)[] modes =
            {
                ("일반 모드", "10 라운드  ·  흑색 군대와 여왕을 무너뜨려라"),
                ("무한 모드", "끝없는 라운드  ·  매 라운드 영구 강화"),
            };
            var e = Event.current;
            float bw = 330 * scale, bh = 104 * scale, gap = 34 * scale;
            float bx0 = (W - (2 * bw + gap)) / 2f, by = H * 0.62f;
            bool mouseMoved = e.type == EventType.Repaint && (e.mousePosition - lastMouse).sqrMagnitude > 1f;
            if (e.type == EventType.Repaint) lastMouse = e.mousePosition;
            float blink = 0.55f + 0.45f * Mathf.Sin(t * 3f);
            for (int i = 0; i < modes.Length; i++)
            {
                var b = new Rect(bx0 + i * (bw + gap), by, bw, bh);
                if (mouseMoved && b.Contains(e.mousePosition)) g.TitleChoice = i;
                if (e.type == EventType.MouseDown && b.Contains(e.mousePosition)) { g.TitleChoice = i; g.ChooseMode(i); e.Use(); }
                bool on = g.TitleChoice == i;
                if (on) b.y -= 6 * scale;
                Fill(new Rect(b.x - 2 * scale, b.y - 2 * scale, b.width + 4 * scale, b.height + 4 * scale),
                    on ? new Color(Gold.r, Gold.g, Gold.b, (0.6f + 0.4f * blink) * fade) : new Color(Gold.r, Gold.g, Gold.b, 0.25f * fade));
                Fill(b, on ? new Color(0.16f, 0.12f, 0.06f, 0.85f * fade) : new Color(0f, 0f, 0f, 0.6f * fade));
                // Chess-tile strip along the top of the card.
                for (int k = 0; k < 10; k++)
                    Fill(new Rect(b.x + k * b.width / 10f, b.y, b.width / 10f, 6 * scale),
                        k % 2 == 0 ? new Color(0.9f, 0.87f, 0.78f, (on ? 0.8f : 0.3f) * fade) : new Color(0.1f, 0.08f, 0.08f, (on ? 0.8f : 0.3f) * fade));
                Text(new Rect(b.x, b.y + 16 * scale, b.width, 44 * scale), modes[i].name, new GUIStyle(mid) { fontSize = Mathf.RoundToInt(30 * scale) },
                    on ? new Color(1f, 0.95f, 0.85f, fade) : new Color(0.75f, 0.72f, 0.66f, fade));
                Text(new Rect(b.x, b.y + 62 * scale, b.width, 28 * scale), modes[i].line, new GUIStyle(small) { fontSize = Mathf.RoundToInt(16 * scale) },
                    on ? new Color(Gold.r, Gold.g, Gold.b, fade) : new Color(0.6f, 0.58f, 0.54f, fade));
            }
            Text(new Rect(0, by + bh + 22 * scale, W, 30 * scale), "← →  모드 선택      ENTER / START  시작", new GUIStyle(small) { fontSize = Mathf.RoundToInt(19 * scale) },
                new Color(1f, 1f, 1f, (0.45f + 0.35f * blink) * fade));
            Text(new Rect(0, by + bh + 54 * scale, W, 28 * scale), "조작법은 게임 중  Esc  일시정지 메뉴에서 볼 수 있습니다",
                new GUIStyle(small) { fontSize = Mathf.RoundToInt(16 * scale) }, new Color(1f, 1f, 1f, 0.35f * fade));

            // The endless mode's best run, just above the cards.
            string best = BestRecord.Exists
                ? $"무한 모드 최고 기록   {BestRecord.Round} 라운드  ·  {BestRecord.FormatTime(BestRecord.Time)}  ·  포획 {BestRecord.Kills}  ·  {BestRecord.Date}"
                : "무한 모드 최고 기록   아직 없음";
            Diamond(new Vector2(W / 2f - 400 * scale, by - 26 * scale), 4 * scale, gold);
            Diamond(new Vector2(W / 2f + 400 * scale, by - 26 * scale), 4 * scale, gold);
            Text(new Rect(0, by - 42 * scale, W, 32 * scale), best, new GUIStyle(small) { fontSize = Mathf.RoundToInt(18 * scale) },
                new Color(Gold.r, Gold.g, Gold.b, 0.9f * fade));

            DrawOnlineRanking(new Rect(W - 400 * scale, H * 0.2f, 360 * scale, 0f), fade);
        }

        /// <summary>
        /// Endless mode's end of run: "체크메이트", then the play record (round reached, time, damage dealt and taken…) and a
        /// confirm button back to the title.
        /// </summary>
        void DrawResults(GameManager g)
        {
            float a = Mathf.Clamp01((g.StateTime - 0.6f) / 0.8f);
            float W = Screen.width, H = Screen.height;
            Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.72f * a));
            Text(new Rect(0, H * 0.07f, W, 120 * scale), "체크메이트", big, new Color(0.78f, 0.07f, 0.05f, a));
            string sub = g.NewBest ? "왕이 쓰러졌다  ·  신기록!"
                : $"왕이 쓰러졌다  ·  최고 기록 {BestRecord.Round} 라운드 ({BestRecord.FormatTime(BestRecord.Time)})";
            float glow = g.NewBest ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f) : 1f;
            Text(new Rect(0, H * 0.07f + 112 * scale, W, 36 * scale), sub, small, new Color(Gold.r, Gold.g, Gold.b, a * glow));
            if (g.StateTime < 1.2f) return;

            var r = g.Record;
            var p = g.Player;
            int m = Mathf.FloorToInt(g.RunTime / 60f), s = Mathf.FloorToInt(g.RunTime % 60f);
            DrawTable(new Rect((W - 640 * scale) / 2f, H * 0.07f + 168 * scale, 640 * scale, 0f), new[]
            {
                ("도달 라운드", $"{g.Round} 라운드   (체스판 {BoardSlots.Size}×{BoardSlots.Size})"),
                ("플레이 시간", $"{m:00}:{s:00}"),
                ("입힌 피해", $"{Mathf.RoundToInt(r.damageDealt):N0}   (최대 한 방 {Mathf.RoundToInt(r.biggestHit)})"),
                ("받은 피해", $"{Mathf.RoundToInt(r.damageTaken):N0}"),
                ("포획한 기물", $"{r.kills}   (다크 기물 {r.darkKills})"),
                ("치명타", $"{r.crits}회"),
                ("완벽한 회피  ·  로열 스로", $"{p.JustDodgeCount}회  ·  {p.ThrowCount}회"),
                ("사용한 회복약", $"{r.flasksUsed}개"),
                ("획득한 강화", $"{r.upgrades}개"),
            }, 260 * scale);

            // Nickname for the online ranking, then confirm → the run is sent and the title comes back.
            var e = Event.current;
            float fy = H * 0.07f + 168 * scale + 9 * 40 * scale + 44 * scale;
            bool ready = g.StateTime > 1.8f && !g.Submitting;
            nickname ??= OnlineBoard.SavedName;
            var nameLabel = new GUIStyle(label) { fontSize = Mathf.RoundToInt(20 * scale), alignment = TextAnchor.MiddleRight };
            var field = new Rect(W / 2f - 120 * scale, fy, 300 * scale, 46 * scale);
            Text(new Rect(field.x - 190 * scale, fy, 176 * scale, 46 * scale), "닉네임", nameLabel, Gold);
            Fill(new Rect(field.x - 2 * scale, field.y - 2 * scale, field.width + 4 * scale, field.height + 4 * scale), new Color(Gold.r, Gold.g, Gold.b, 0.7f * a));
            Fill(field, new Color(0.04f, 0.035f, 0.04f, 0.95f * a));
            fieldStyle ??= new GUIStyle(GUI.skin.textField) { font = small.font, alignment = TextAnchor.MiddleLeft };
            fieldStyle.fontSize = Mathf.RoundToInt(22 * scale);
            fieldStyle.normal.textColor = fieldStyle.focused.textColor = fieldStyle.hover.textColor = new Color(1f, 0.96f, 0.88f);
            fieldStyle.normal.background = fieldStyle.focused.background = fieldStyle.hover.background = Texture2D.blackTexture;
            fieldStyle.padding = new RectOffset(Mathf.RoundToInt(12 * scale), 0, 0, 0);
            Input.imeCompositionMode = IMECompositionMode.On;   // Korean names
            // Enter inside the field confirms too.
            bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            GUI.SetNextControlName("Nickname");
            if (!g.Submitting) nickname = GUI.TextField(field, nickname, 12, fieldStyle);
            else GUI.Label(field, nickname, fieldStyle);
            if (ready && g.StateTime < 2.2f) GUI.FocusControl("Nickname");

            var b = new Rect((W - 300 * scale) / 2f, fy + 68 * scale, 300 * scale, 58 * scale);
            bool click = e.type == EventType.MouseDown && b.Contains(e.mousePosition);
            bool padConfirm = e.type == EventType.Repaint && GameInput.Confirm;   // gamepad A / Start (keys come as events)
            if (ready && (click || enter || padConfirm))
            {
                g.SubmitRun(nickname);
                if (e.type != EventType.Repaint) e.Use();
            }
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 5f);
            Fill(new Rect(b.x - 3 * scale, b.y - 3 * scale, b.width + 6 * scale, b.height + 6 * scale),
                new Color(Gold.r, Gold.g, Gold.b, (ready ? pulse : 0.3f) * a));
            Fill(b, new Color(0.22f, 0.16f, 0.07f, a));
            Text(b, g.Submitting ? "기록 전송 중…" : "확인", new GUIStyle(mid) { fontSize = Mathf.RoundToInt(26 * scale) },
                new Color(1f, 0.95f, 0.85f, (ready || g.Submitting ? 1f : 0.4f) * a));
            Text(new Rect(0, b.yMax + 10 * scale, W, 26 * scale), "닉네임을 입력하고 확인  —  온라인 순위에 기록하고 타이틀로",
                new GUIStyle(small) { fontSize = Mathf.RoundToInt(15 * scale) }, new Color(1f, 1f, 1f, 0.45f * a));
        }

        bool rankingRequested;

        /// <summary>Title side panel: the endless mode's online top 10 and this player's own rank (UGS Leaderboards).</summary>
        void DrawOnlineRanking(Rect area, float fade)
        {
            if (!rankingRequested) { rankingRequested = true; OnlineBoard.Refresh(); }
            float line = 30 * scale;
            int rows = Mathf.Max(1, OnlineBoard.Top.Count);
            var r = new Rect(area.x, area.y, area.width, 58 * scale + rows * line + 54 * scale);
            Fill(r, new Color(0f, 0f, 0f, 0.55f * fade));
            Fill(new Rect(r.x, r.y, r.width, 2 * scale), new Color(Gold.r, Gold.g, Gold.b, 0.7f * fade));
            Text(new Rect(r.x, r.y + 10 * scale, r.width, 36 * scale), "무한 모드  온라인 순위", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(20 * scale) },
                new Color(Gold.r, Gold.g, Gold.b, fade));
            var row = new GUIStyle(small) { fontSize = Mathf.RoundToInt(17 * scale), alignment = TextAnchor.MiddleLeft };
            var right = new GUIStyle(row) { alignment = TextAnchor.MiddleRight };
            float y = r.y + 54 * scale;
            var dim = new Color(0.85f, 0.82f, 0.75f, fade);
            switch (OnlineBoard.State)
            {
                case OnlineBoard.Status.Idle:
                case OnlineBoard.Status.Loading:
                    Text(new Rect(r.x, y, r.width, line), "불러오는 중…", new GUIStyle(row) { alignment = TextAnchor.MiddleCenter }, dim);
                    break;
                case OnlineBoard.Status.Unavailable:
                    Text(new Rect(r.x, y, r.width, line), "온라인 순위를 불러올 수 없습니다", new GUIStyle(row) { alignment = TextAnchor.MiddleCenter }, dim);
                    break;
                default:
                    if (OnlineBoard.Top.Count == 0)
                        Text(new Rect(r.x, y, r.width, line), "아직 기록이 없습니다", new GUIStyle(row) { alignment = TextAnchor.MiddleCenter }, dim);
                    foreach (var en in OnlineBoard.Top)
                    {
                        var c = en.mine ? new Color(1f, 0.85f, 0.45f, fade) : dim;
                        if (en.mine) Fill(new Rect(r.x + 6 * scale, y, r.width - 12 * scale, line), new Color(Gold.r, Gold.g, Gold.b, 0.12f * fade));
                        Text(new Rect(r.x + 16 * scale, y, 44 * scale, line), $"{en.rank}", row, c);
                        Text(new Rect(r.x + 58 * scale, y, r.width * 0.5f, line), en.name, row, c);
                        Text(new Rect(r.x, y, r.width - 16 * scale, line), $"{en.round}R  {BestRecord.FormatTime(en.time)}", right, c);
                        y += line;
                    }
                    break;
            }
            float my = r.yMax - 40 * scale;
            Fill(new Rect(r.x + 12 * scale, my - 4 * scale, r.width - 24 * scale, 1 * scale), new Color(Gold.r, Gold.g, Gold.b, 0.4f * fade));
            string mine = OnlineBoard.Mine.HasValue
                ? $"내 순위  {OnlineBoard.Mine.Value.rank}위  ·  {OnlineBoard.Mine.Value.round}R  {BestRecord.FormatTime(OnlineBoard.Mine.Value.time)}"
                : OnlineBoard.State == OnlineBoard.Status.Ready ? "내 순위  기록 없음" : "";
            Text(new Rect(r.x, my, r.width, 32 * scale), mine, new GUIStyle(row) { alignment = TextAnchor.MiddleCenter }, new Color(1f, 0.9f, 0.6f, fade));
        }

        void DrawDeath(GameManager g)
        {
            if (g.IsEndless) { DrawResults(g); return; }
            float a = Mathf.Clamp01((g.StateTime - 0.6f) / 1.0f);
            float bandH = 200 * scale;
            Fill(new Rect(0, (Screen.height - bandH) / 2f, Screen.width, bandH), new Color(0, 0, 0, 0.75f * a));
            Text(new Rect(0, (Screen.height - bandH) / 2f, Screen.width, bandH * 0.75f), "체크메이트", big,
                new Color(0.75f, 0.06f, 0.04f, a));
            if (g.StateTime > 1.8f)
                Text(new Rect(0, Screen.height / 2f + 50 * scale, Screen.width, 40 * scale),
                    "ENTER / START 를 눌러 다시 일어서기", small, new Color(0.85f, 0.8f, 0.75f, a));
        }

        void DrawVictory(GameManager g)
        {
            float a = Mathf.Clamp01(g.StateTime / 1.5f);
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.5f * a));
            float cy = Screen.height * 0.32f;
            Text(new Rect(0, cy, Screen.width, 120 * scale), "왕은 살아남았다", big, new Color(Gold.r, Gold.g, Gold.b, a));
            int m = Mathf.FloorToInt(g.RunTime / 60f), s = Mathf.FloorToInt(g.RunTime % 60f);
            Text(new Rect(0, cy + 130 * scale, Screen.width, 40 * scale),
                $"시간 {m:00}:{s:00}     사망 {g.Deaths}회", mid, new Color(0.9f, 0.88f, 0.8f, a));
            if (g.StateTime > 2.5f)
                Text(new Rect(0, Screen.height * 0.8f, Screen.width, 40 * scale), "ENTER / START 를 눌러 다시 시작", small,
                    new Color(1f, 1f, 1f, a));
        }

        // ---------------------------------------------------------------- pause (tabs: game / stats / controls)

        int pauseTab;
        /// <summary>Pause screen tab: 0 game, 1 stats, 2 controls.</summary>
        public int PauseTab { get => pauseTab; set => pauseTab = value; }
        Vector2 lastMouse;
        static readonly string[] PauseTabs = { "게임", "능력치", "조작법" };

        void DrawPause(GameManager g)
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.62f));

            // Tab bar: click, ←/→ (A/D) or the pad's shoulders (keys handled in Update).
            float tw = 190 * scale, th = 46 * scale, tx = (Screen.width - tw * PauseTabs.Length) / 2f, ty = Screen.height * 0.13f;
            Text(new Rect(0, ty - 64 * scale, Screen.width, 50 * scale), "일시정지", mid, Color.white);
            for (int i = 0; i < PauseTabs.Length; i++)
            {
                var r = new Rect(tx + i * tw, ty, tw - 8 * scale, th);
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition)) { pauseTab = i; Event.current.Use(); }
                bool on = i == pauseTab;
                Fill(r, on ? new Color(Gold.r, Gold.g, Gold.b, 0.9f) : new Color(1f, 1f, 1f, 0.08f));
                Text(r, PauseTabs[i], small, on ? new Color(0.08f, 0.06f, 0.05f) : new Color(0.85f, 0.82f, 0.75f));
            }
            Text(new Rect(0, ty + th + 6 * scale, Screen.width, 26 * scale), "← / →  탭 전환      Esc / Start  계속하기",
                new GUIStyle(small) { fontSize = Mathf.RoundToInt(15 * scale) }, new Color(1f, 1f, 1f, 0.45f));

            var area = new Rect((Screen.width - 760 * scale) / 2f, ty + th + 50 * scale, 760 * scale, 0f);
            if (pauseTab == 0) DrawPauseGame(g, area);
            else if (pauseTab == 1) DrawStats(g.Player, area);
            else DrawControls(area);
        }

        void DrawPauseGame(GameManager g, Rect area)
        {
            string where = g.IsEndless ? $"무한 모드  ·  {g.Round} 라운드  ·  체스판 {BoardSlots.Size}×{BoardSlots.Size}"
                : g.UsesRounds ? $"{g.WaveIndex + 1} / {g.WaveCount} 라운드" : "연습장";
            Text(new Rect(0, area.y + 60 * scale, Screen.width, 40 * scale), where, small, new Color(Gold.r, Gold.g, Gold.b));
            int m = Mathf.FloorToInt(g.RunTime / 60f), s = Mathf.FloorToInt(g.RunTime % 60f);
            if (g.UsesRounds)
                Text(new Rect(0, area.y + 104 * scale, Screen.width, 36 * scale), $"진행 시간 {m:00}:{s:00}     사망 {g.Deaths}회", small,
                    new Color(0.85f, 0.82f, 0.75f));
            Text(new Rect(0, area.y + 190 * scale, Screen.width, 36 * scale), "능력치와 조작법은 위의 탭에서 볼 수 있습니다", small,
                new Color(1f, 1f, 1f, 0.5f));
            if (!g.UsesRounds) return;

            // Back to the main menu (the run is abandoned, nothing recorded).
            var e = Event.current;
            var b = new Rect((Screen.width - 280 * scale) / 2f, area.y + 260 * scale, 280 * scale, 56 * scale);
            bool hot = b.Contains(e.mousePosition);
            if (e.type == EventType.MouseDown && hot) { g.QuitToTitle(); e.Use(); return; }
            Fill(new Rect(b.x - 2 * scale, b.y - 2 * scale, b.width + 4 * scale, b.height + 4 * scale), new Color(Gold.r, Gold.g, Gold.b, hot ? 0.9f : 0.5f));
            Fill(b, hot ? new Color(0.24f, 0.18f, 0.08f, 0.95f) : new Color(0.06f, 0.055f, 0.06f, 0.95f));
            Text(b, "메인 메뉴로", new GUIStyle(mid) { fontSize = Mathf.RoundToInt(24 * scale) }, new Color(1f, 0.95f, 0.85f));
            Text(new Rect(0, b.yMax + 8 * scale, Screen.width, 26 * scale), g.IsEndless ? "진행 중인 기록은 저장되지 않습니다   ·   M 키" : "M 키",
                new GUIStyle(small) { fontSize = Mathf.RoundToInt(15 * scale) }, new Color(1f, 1f, 1f, 0.45f));
        }

        /// <summary>A gold-ruled panel of name/value rows.</summary>
        void DrawTable(Rect area, (string name, string value)[] rows, float nameWidth)
        {
            var full = new (string, string, string)[rows.Length];
            for (int i = 0; i < rows.Length; i++) full[i] = (rows[i].name, rows[i].value, null);
            DrawTable(area, full, nameWidth);
        }

        /// <summary>
        /// A gold-ruled panel of name/value rows; a row with a description shows it in a box by the mouse while hovered.
        /// </summary>
        void DrawTable(Rect area, (string name, string value, string desc)[] rows, float nameWidth)
        {
            float line = (rows.Length > 12 ? 35 : 40) * scale;
            var r = new Rect(area.x, area.y, area.width, rows.Length * line + 28 * scale);
            Fill(r, new Color(0f, 0f, 0f, 0.55f));
            Fill(new Rect(r.x, r.y, r.width, 2 * scale), new Color(Gold.r, Gold.g, Gold.b, 0.7f));
            var name = new GUIStyle(label) { fontSize = Mathf.RoundToInt(20 * scale) };
            var value = new GUIStyle(small) { fontSize = Mathf.RoundToInt(20 * scale), alignment = TextAnchor.MiddleRight };
            Vector2 mouse = DebugMouse ?? Event.current.mousePosition;
            string hover = null;
            float y = r.y + 14 * scale;
            for (int i = 0; i < rows.Length; i++)
            {
                var row = new Rect(r.x + 8 * scale, y, r.width - 16 * scale, line);
                bool hot = rows[i].desc != null && row.Contains(mouse);
                if (hot) { hover = rows[i].desc; Fill(row, new Color(Gold.r, Gold.g, Gold.b, 0.16f)); }
                else if (i % 2 == 0) Fill(row, new Color(1f, 1f, 1f, 0.04f));
                Text(new Rect(r.x + 24 * scale, y, nameWidth, line), rows[i].name, name, Gold);
                Text(new Rect(r.x + 24 * scale, y, r.width - 48 * scale, line), rows[i].value, value, new Color(0.93f, 0.9f, 0.83f));
                y += line;
            }
            if (hover == ItemsTooltip) DrawItemTooltip(tooltipPlayer, mouse);
            else if (hover != null) DrawTooltip(hover, mouse);
        }

        /// <summary>Lab: stands in for the mouse position over the stats rows (null: the real mouse).</summary>
        public static Vector2? DebugMouse;

        /// <summary>A small dark box with a wrapped description next to the mouse (kept on screen).</summary>
        void DrawTooltip(string text, Vector2 at)
        {
            var style = new GUIStyle(small) { fontSize = Mathf.RoundToInt(18 * scale), alignment = TextAnchor.UpperLeft, wordWrap = true };
            float w = 430 * scale, pad = 14 * scale;
            float h = style.CalcHeight(new GUIContent(text), w - 2 * pad) + 2 * pad;
            var box = new Rect(at.x + 18 * scale, at.y + 14 * scale, w, h);
            if (box.xMax > Screen.width - 8) box.x = at.x - w - 18 * scale;
            if (box.yMax > Screen.height - 8) box.y = Screen.height - 8 - h;
            Fill(new Rect(box.x - 2 * scale, box.y - 2 * scale, box.width + 4 * scale, box.height + 4 * scale), new Color(Gold.r, Gold.g, Gold.b, 0.8f));
            Fill(box, new Color(0.04f, 0.035f, 0.04f, 0.97f));
            Text(new Rect(box.x + pad, box.y + pad, w - 2 * pad, h - 2 * pad), text, style, new Color(0.95f, 0.92f, 0.85f));
        }

        static string D(UpgradeKind k) => Upgrades.Def(k).desc;

        /// <summary>A row description that asks for the owned items as icons instead of text.</summary>
        const string ItemsTooltip = "\u0001items";
        PlayerController tooltipPlayer;

        /// <summary>The King's stats (pause ▸ 능력치); hover a row for what it does, the last row for the items held.</summary>
        void DrawStats(PlayerController p, Rect area)
        {
            var s = p.Stats;
            float dmgCut = 1f - s.DamageTaken(100f) / 100f;
            tooltipPlayer = p;
            DrawTable(area, new (string, string, string)[]
            {
                ("공격력", $"{Mathf.RoundToInt(s.attackPower * 100f)}%", D(UpgradeKind.AttackPower)),
                ("데미지", $"{(s.damage >= 0f ? "+" : "−")}{Mathf.RoundToInt(Mathf.Abs(s.damage) * 100f)}%   (최종 피해 ×{s.DamageMultiplier:0.00})", D(UpgradeKind.Damage)),
                ("공격 속도", $"{Mathf.RoundToInt(s.attackSpeed * 100f)}%   (최대 150%)", D(UpgradeKind.AttackSpeed)),
                ("공격 범위", $"{Mathf.RoundToInt(s.attackRange * 100f)}%   (최대 130%)", D(UpgradeKind.AttackRange)),
                ("치명타", $"확률 {Mathf.RoundToInt(s.critChance * 100f)}%   ·   피해 {Mathf.RoundToInt(s.critDamage * 100f)}% (최대 300%)",
                    D(UpgradeKind.CritChance) + "\n" + D(UpgradeKind.CritDamage)),
                ("경직 피해", $"{Mathf.RoundToInt(s.staggerPower * 100f)}%   (최대 200%)", D(UpgradeKind.StaggerPower)),
                ("최대 체력", $"{Mathf.RoundToInt(p.MaxHealth)}", D(UpgradeKind.MaxHealth)),
                ("방어력", $"{Mathf.RoundToInt(s.defense)}   (받는 피해 {(dmgCut >= 0f ? "−" : "+")}{Mathf.RoundToInt(Mathf.Abs(dmgCut) * 100f)}%)", D(UpgradeKind.Defense)),
                ("회복약", $"{p.MaxFlasks}개 (최대 5)   ·   회복량 {Mathf.RoundToInt(s.healAmount)}", D(UpgradeKind.Flask) + "\n" + D(UpgradeKind.HealAmount)),
                ("흡혈", $"처치마다 {Mathf.RoundToInt(s.lifeSteal)} 회복   (최대 20)", D(UpgradeKind.LifeSteal)),
                ("기력", $"최대 {Mathf.RoundToInt(p.MaxStamina)}   ·   회복 속도 {Mathf.RoundToInt(s.staminaRegen * 100f)}% (최대 200%)",
                    D(UpgradeKind.MaxStamina) + "\n" + D(UpgradeKind.StaminaRegen)),
                ("공격당 기력 소모", $"일반공격 {p.LightAttackCost:0.#} (−{Mathf.RoundToInt(s.lightCostCut * 100f)}%)   ·   강공격 {p.HeavyAttackCost:0.#} (−{Mathf.RoundToInt(s.heavyCostCut * 100f)}%)",
                    D(UpgradeKind.LightCost) + "\n" + D(UpgradeKind.HeavyCost)),
                ("이동 속도", $"{Mathf.RoundToInt(s.moveSpeed * 100f)}%   (최대 130%)", D(UpgradeKind.MoveSpeed)),
                ("회피", $"{p.MaxDodges}회 (최대 3)   ·   1회 충전 {p.DodgeRechargeTime:0.00}초", D(UpgradeKind.MaxDodges) + "\n" + D(UpgradeKind.DodgeRecharge)),
                ("보유 아이템", p.Items.Count == 0 ? "없음" : $"{p.Items.Count}개   (마우스를 올려 보기)", ItemsTooltip),
            }, area.width * 0.36f);
        }

        /// <summary>
        /// The items held, as icons on grade-coloured plates with their names, oldest first (pause ▸ 능력치 ▸ 보유 아이템).
        /// </summary>
        void DrawItemTooltip(PlayerController p, Vector2 at)
        {
            if (p == null || p.Items.Count == 0)
            {
                DrawTooltip("라운드를 마칠 때마다 아이템 3개 중 하나를 고릅니다.", at);
                return;
            }
            int n = p.Items.Count, cols = Mathf.Min(6, n), rows = (n + cols - 1) / cols;
            float icon = 64 * scale, cell = 92 * scale, nameH = 22 * scale, pad = 12 * scale;
            float w = cols * cell + 2 * pad, h = rows * (icon + nameH + 8 * scale) + 2 * pad;
            var box = new Rect(at.x + 18 * scale, at.y + 14 * scale, w, h);
            if (box.xMax > Screen.width - 8) box.x = at.x - w - 18 * scale;
            if (box.yMax > Screen.height - 8) box.y = Mathf.Max(8, Screen.height - 8 - h);
            Fill(new Rect(box.x - 2 * scale, box.y - 2 * scale, box.width + 4 * scale, box.height + 4 * scale), new Color(Gold.r, Gold.g, Gold.b, 0.8f));
            Fill(box, new Color(0.04f, 0.035f, 0.04f, 0.97f));
            var nameStyle = new GUIStyle(small) { fontSize = Mathf.RoundToInt(13 * scale), alignment = TextAnchor.UpperCenter, clipping = TextClipping.Clip };
            for (int i = 0; i < n; i++)
            {
                var it = p.Items[i];
                Color gc = GradeColor(it.grade);
                float cx = box.x + pad + (i % cols) * cell + cell / 2f, cy = box.y + pad + (i / cols) * (icon + nameH + 8 * scale);
                var plate = new Rect(cx - icon / 2f, cy, icon, icon);
                Fill(new Rect(plate.x - 2 * scale, plate.y - 2 * scale, plate.width + 4 * scale, plate.height + 4 * scale), gc);
                Fill(plate, new Color(0.02f, 0.02f, 0.025f));
                if (it.Icon != null) GUI.DrawTexture(plate, it.Icon, ScaleMode.ScaleToFit);
                else Diamond(plate.center, 12f * scale, gc);
                Text(new Rect(cx - cell / 2f, plate.yMax + 3 * scale, cell, nameH), it.name, nameStyle, new Color(0.92f, 0.89f, 0.82f));
            }
        }

        /// <summary>How to play (pause ▸ 조작법): keyboard and mouse, then the gamepad.</summary>
        void DrawControls(Rect area)
        {
            (string action, string keys)[] rows =
            {
                ("이동", "W A S D          왼쪽 스틱"),
                ("카메라", "마우스          오른쪽 스틱"),
                ("달리기", "Shift (누르고 있기)          L3"),
                ("일반공격 · 콤보", "좌클릭 / J (누르고 있으면 연속)          R1"),
                ("강공격 · 띄우기", "우클릭 / K          R2"),
                ("회피 (무적 구르기)", "Space          B"),
                ("락온", "Q / Tab / 휠 클릭          R3"),
                ("회복약", "R          X"),
                ("일시정지", "Esc          Start"),
            };
            DrawTable(area, rows, area.width * 0.36f);
            float y = area.y + rows.Length * 40 * scale + 46 * scale;
            var tip = new GUIStyle(small) { fontSize = Mathf.RoundToInt(18 * scale) };
            Text(new Rect(0, y, Screen.width, 30 * scale), "공격이 닿기 직전에 구르면  완벽한 회피  →  느려진 시간 속에서 좌클릭:  로열 스로 (퀸에게는 도약 반격)",
                tip, new Color(0.6f, 0.9f, 1f, 0.9f));
            Text(new Rect(0, y + 32 * scale, Screen.width, 30 * scale), "적의 경직 게이지가 다 차면 기절  —  강공격으로 띄워 공중 콤보",
                tip, new Color(0.85f, 0.82f, 0.75f, 0.85f));
        }

        // ---------------------------------------------------------------- round-end items (endless)

        /// <summary>Item grade colours: common grey, rare blue, epic violet, legendary orange-gold.</summary>
        static Color GradeColor(UpgradeGrade g) => g switch
        {
            UpgradeGrade.Rare => new Color(0.36f, 0.62f, 1f),
            UpgradeGrade.Epic => new Color(0.74f, 0.42f, 1f),
            UpgradeGrade.Legendary => new Color(1f, 0.62f, 0.12f),
            _ => new Color(0.7f, 0.68f, 0.64f),
        };

        static readonly Color Gain = new Color(0.55f, 0.95f, 0.5f), Loss = new Color(1f, 0.42f, 0.36f);

        /// <summary>
        /// Three item cards: click (or 1–3, or ←/→ and Enter) to choose one — another card switches, the same card again
        /// cancels; the confirm button keeps it for good and starts the next round. Each card: picture, grade, name and
        /// its stat steps (▲ gains, ▼ drawbacks) with the before → after values.
        /// </summary>
        void DrawUpgrades(GameManager g)
        {
            var p = g.Player;
            float a = Mathf.Clamp01(g.StateTime / 0.35f);
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.6f * a));
            Text(new Rect(0, Screen.height * 0.1f, Screen.width, 50 * scale), $"{g.Round} 라운드 클리어", mid, new Color(Gold.r, Gold.g, Gold.b, a));
            Text(new Rect(0, Screen.height * 0.1f + 52 * scale, Screen.width, 34 * scale),
                "아이템 하나를 고르세요   —   다른 카드를 누르면 바꾸고, 같은 카드를 다시 누르면 취소", small, new Color(0.9f, 0.88f, 0.8f, a));

            int n = g.Offers.Count;
            float cw = 300 * scale, ch = 410 * scale, gap = 30 * scale;
            float x0 = (Screen.width - (n * cw + (n - 1) * gap)) / 2f, y0 = Screen.height * 0.1f + 110 * scale;
            var title = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(26 * scale), wordWrap = true };
            var info = new GUIStyle(small) { fontSize = Mathf.RoundToInt(19 * scale) };
            var fxName = new GUIStyle(small) { fontSize = Mathf.RoundToInt(18 * scale), alignment = TextAnchor.MiddleLeft };
            var fxValue = new GUIStyle(small) { fontSize = Mathf.RoundToInt(17 * scale), alignment = TextAnchor.MiddleRight };
            var e = Event.current;
            // Hover follows the mouse only when it moves, so the keys / pad can steer the cursor too.
            bool mouseMoved = e.type == EventType.Repaint && (e.mousePosition - lastMouse).sqrMagnitude > 1f;
            if (e.type == EventType.Repaint) lastMouse = e.mousePosition;
            for (int i = 0; i < n; i++)
            {
                var r = new Rect(x0 + i * (cw + gap), y0, cw, ch);
                bool picked = g.Picked.Contains(i);
                if (mouseMoved && r.Contains(e.mousePosition)) g.OfferCursor = i;
                if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) { g.PickUpgrade(i); e.Use(); }
                bool hot = g.OfferCursor == i;
                r.y -= picked ? 18 * scale : hot ? 8 * scale : 0f;

                var item = g.Offers[i];
                Color gc = GradeColor(item.grade);
                // The frame wears the grade's colour (brighter when hovered, gold when chosen).
                Fill(new Rect(r.x - 3 * scale, r.y - 3 * scale, r.width + 6 * scale, r.height + 6 * scale),
                    picked ? new Color(Gold.r, Gold.g, Gold.b, a) : hot ? new Color(Mathf.Lerp(gc.r, 1f, 0.45f), Mathf.Lerp(gc.g, 1f, 0.45f), Mathf.Lerp(gc.b, 1f, 0.45f), a)
                        : new Color(gc.r * 0.75f, gc.g * 0.75f, gc.b * 0.75f, a));
                Fill(r, picked ? new Color(0.22f, 0.17f, 0.08f, 0.97f * a) : new Color(0.06f + gc.r * 0.05f, 0.055f + gc.g * 0.05f, 0.06f + gc.b * 0.05f, 0.97f * a));
                // A solid grade-coloured band across the top, a soft glow under it fading into the card.
                Fill(new Rect(r.x, r.y, r.width, 6 * scale), new Color(gc.r, gc.g, gc.b, a));
                for (int k = 0; k < 4; k++)
                    Fill(new Rect(r.x, r.y + (6 + k * 10) * scale, r.width, 10 * scale), new Color(gc.r, gc.g, gc.b, (0.16f - k * 0.04f) * a));
                if (item.IsSpecial)
                {
                    // Special items: a gold "특수" tag at the top left.
                    var tag = new Rect(r.x + 10 * scale, r.y + 14 * scale, 52 * scale, 26 * scale);
                    Fill(tag, new Color(Gold.r, Gold.g, Gold.b, a));
                    Text(tag, "특수", new GUIStyle(info) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(16 * scale) },
                        new Color(0.1f, 0.07f, 0.03f, a));
                }

                Text(new Rect(r.x + 4 * scale, r.yMax - 34 * scale, 34 * scale, 30 * scale), (i + 1).ToString(), info, new Color(1f, 1f, 1f, 0.4f * a));
                Text(new Rect(r.x, r.y + 14 * scale, r.width - 12 * scale, 30 * scale), item.GradeName,
                    new GUIStyle(info) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold }, new Color(gc.r, gc.g, gc.b, a));

                // The item's picture on a grade-tinted plate.
                float isz = 112 * scale;
                var plate = new Rect(r.center.x - isz / 2f, r.y + 46 * scale, isz, isz);
                Fill(new Rect(plate.x - 2 * scale, plate.y - 2 * scale, plate.width + 4 * scale, plate.height + 4 * scale), new Color(gc.r * 0.6f, gc.g * 0.6f, gc.b * 0.6f, a));
                Fill(plate, new Color(0.02f, 0.02f, 0.025f, a));
                if (item.Icon != null)
                {
                    var old = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, a);
                    GUI.DrawTexture(plate, item.Icon, ScaleMode.ScaleToFit);
                    GUI.color = old;
                }
                else Diamond(plate.center, 22f * scale, new Color(gc.r, gc.g, gc.b, 0.5f * a));

                Text(new Rect(r.x + 10 * scale, plate.yMax + 8 * scale, r.width - 20 * scale, 40 * scale), item.name, title, new Color(Gold.r, Gold.g, Gold.b, a));

                // Stat steps: ▲ gains in green, ▼ drawbacks in red, with the value before → after.
                float fy = plate.yMax + 58 * scale, lh = 27 * scale;
                if (item.IsSpecial)
                    Text(new Rect(r.x + 16 * scale, fy, r.width - 32 * scale, r.yMax - fy - 40 * scale), item.desc,
                        new GUIStyle(small) { fontSize = Mathf.RoundToInt(17 * scale), wordWrap = true, alignment = TextAnchor.UpperCenter }, new Color(0.92f, 0.89f, 0.82f, a));
                foreach (var fx in item.effects)
                {
                    var c = fx.steps >= 0 ? Gain : Loss;
                    var row = new Rect(r.x + 16 * scale, fy, r.width - 32 * scale, lh);
                    Text(row, $"{Upgrades.Def(fx.kind).title}  {Upgrades.Arrows(fx)}", fxName, new Color(c.r, c.g, c.b, a));
                    Text(row, Upgrades.Preview(p.Stats, fx), fxValue, new Color(0.8f, 0.78f, 0.72f, a));
                    fy += lh;
                }
                if (picked)
                    Text(new Rect(r.x, r.yMax - 36 * scale, r.width, 30 * scale), "선택됨",
                        new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(19 * scale) }, new Color(Gold.r, Gold.g, Gold.b, a));
            }

            // Confirm button: unlocks with an item chosen.
            bool ready = g.CanConfirmUpgrades;
            var b = new Rect((Screen.width - 300 * scale) / 2f, y0 + ch + 34 * scale, 300 * scale, 62 * scale);
            if (mouseMoved && b.Contains(e.mousePosition)) g.OfferCursor = g.ConfirmSlot;
            if (e.type == EventType.MouseDown && b.Contains(e.mousePosition)) { g.ConfirmUpgrades(); e.Use(); }
            bool bHot = g.OfferCursor == g.ConfirmSlot;
            float pulse = ready ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 5f) : 1f;
            Fill(new Rect(b.x - 3 * scale, b.y - 3 * scale, b.width + 6 * scale, b.height + 6 * scale),
                ready ? new Color(Gold.r, Gold.g, Gold.b, a * pulse) : bHot ? new Color(0.6f, 0.58f, 0.52f, 0.8f * a) : new Color(0.4f, 0.38f, 0.34f, 0.6f * a));
            Fill(b, ready ? (bHot ? new Color(0.36f, 0.27f, 0.1f, a) : new Color(0.24f, 0.18f, 0.07f, a)) : new Color(0.08f, 0.08f, 0.08f, 0.9f * a));
            Text(b, "확인", new GUIStyle(mid) { fontSize = Mathf.RoundToInt(28 * scale) },
                ready ? new Color(1f, 0.95f, 0.85f, a) : new Color(1f, 1f, 1f, 0.3f * a));
            Text(new Rect(0, b.yMax + 10 * scale, Screen.width, 26 * scale), "클릭  ·  숫자 1–3  ·  ← → 와 Enter",
                new GUIStyle(small) { fontSize = Mathf.RoundToInt(15 * scale) }, new Color(1f, 1f, 1f, 0.4f * a));
        }

        // ---------------------------------------------------------------- primitives

        // ---------------------------------------------------------------- low health

        static Texture2D vignette;

        /// <summary>Radial mask: clear in the middle, opaque at the edges (soft falloff).</summary>
        static Texture2D Vignette
        {
            get
            {
                if (vignette != null) return vignette;
                const int N = 128;
                vignette = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Vignette" };
                var px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.41421f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 1f, d));
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                vignette.SetPixels32(px);
                vignette.Apply();
                return vignette;
            }
        }

        LowHealthFx lowHealth;

        /// <summary>Low health is shown by URP post-processing (<see cref="LowHealthFx"/>), not drawn here: just make sure it runs.</summary>
        void DrawLowHealthVignette(PlayerController p)
        {
            if (lowHealth == null && !TryGetComponent(out lowHealth)) lowHealth = gameObject.AddComponent<LowHealthFx>();
        }

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

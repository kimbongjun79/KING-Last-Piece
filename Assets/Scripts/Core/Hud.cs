using UnityEngine;

namespace KingLastPiece
{
    /// <summary>IMGUI HUD: health/stamina with damage trail, flasks, lock-on marker, enemy and boss bars, overlays.</summary>
    public class Hud : MonoBehaviour
    {
        GUIStyle big, mid, small, label, title;
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

        void DrawPracticePanel()
        {
            var arena = PracticeArena.Instance;
            if (arena == null) return;
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
        /// mode, and a framed start prompt over a chess-tile band. (How to play lives in the pause menu now.)
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

            // Chess-tile band along the bottom edge.
            float tile = 28 * scale;
            int count = Mathf.CeilToInt(W / tile) + 1;
            for (int i = 0; i < count; i++)
            for (int row = 0; row < 2; row++)
                Fill(new Rect(i * tile, H - (2 - row) * tile, tile, tile),
                    (i + row) % 2 == 0 ? new Color(0.9f, 0.87f, 0.78f, 0.12f * fade) : new Color(0f, 0f, 0f, 0.35f * fade));
            Fill(new Rect(0, H - 2 * tile - 2 * scale, W, 2 * scale), new Color(Gold.r, Gold.g, Gold.b, 0.5f * fade));
        }

        void DrawDeath(GameManager g)
        {
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
        }

        /// <summary>A gold-ruled panel of name/value rows.</summary>
        void DrawTable(Rect area, (string name, string value)[] rows, float nameWidth)
        {
            float line = 40 * scale;
            var r = new Rect(area.x, area.y, area.width, rows.Length * line + 28 * scale);
            Fill(r, new Color(0f, 0f, 0f, 0.55f));
            Fill(new Rect(r.x, r.y, r.width, 2 * scale), new Color(Gold.r, Gold.g, Gold.b, 0.7f));
            var name = new GUIStyle(label) { fontSize = Mathf.RoundToInt(20 * scale) };
            var value = new GUIStyle(small) { fontSize = Mathf.RoundToInt(20 * scale), alignment = TextAnchor.MiddleRight };
            float y = r.y + 14 * scale;
            for (int i = 0; i < rows.Length; i++)
            {
                if (i % 2 == 0) Fill(new Rect(r.x + 8 * scale, y, r.width - 16 * scale, line), new Color(1f, 1f, 1f, 0.04f));
                Text(new Rect(r.x + 24 * scale, y, nameWidth, line), rows[i].name, name, Gold);
                Text(new Rect(r.x + 24 * scale, y, r.width - 48 * scale, line), rows[i].value, value, new Color(0.93f, 0.9f, 0.83f));
                y += line;
            }
        }

        /// <summary>The King's stats (pause ▸ 능력치).</summary>
        void DrawStats(PlayerController p, Rect area)
        {
            var s = p.Stats;
            float dmgCut = 1f - s.DamageTaken(100f) / 100f;
            DrawTable(area, new[]
            {
                ("공격력", $"{Mathf.RoundToInt(s.attackPower * 100f)}%"),
                ("방어력", $"{Mathf.RoundToInt(s.defense)}   (받는 피해 −{Mathf.RoundToInt(dmgCut * 100f)}%)"),
                ("최대 체력", $"{Mathf.RoundToInt(p.MaxHealth)}"),
                ("공격 속도", $"{Mathf.RoundToInt(s.attackSpeed * 100f)}%   (최대 135%)"),
                ("공격 범위", $"{Mathf.RoundToInt(s.attackRange * 100f)}%   (최대 130%)"),
                ("공격 역동감", $"{Mathf.RoundToInt(s.Dynamism * 100f)}%   (공격력·속도·범위에 따라 80~140%)"),
                ("최대 기력", $"{Mathf.RoundToInt(p.MaxStamina)}"),
                ("치명타", $"확률 {Mathf.RoundToInt(s.critChance * 100f)}% (최대 60%)   ·   피해 {Mathf.RoundToInt(s.critDamage * 100f)}% (최대 300%)"),
                ("공격당 기력 소모", $"일반공격 {p.LightAttackCost:0.#} (−{Mathf.RoundToInt(s.lightCostCut * 100f)}%)   ·   강공격 {p.HeavyAttackCost:0.#} (−{Mathf.RoundToInt(s.heavyCostCut * 100f)}%)"),
                ("회복약", $"{p.MaxFlasks}개 (최대 5)   ·   회복량 {Mathf.RoundToInt(s.healAmount)}"),
                ("회피 충전", $"{p.MaxDodges}회 (최대 3)   ·   1회 충전 {p.DodgeRechargeTime:0.00}초"),
            }, area.width * 0.4f);
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
            Text(new Rect(0, y, Screen.width, 30 * scale), "공격이 닿기 직전에 구르면  완벽한 회피  →  느려진 시간 속에서 좌클릭:  로열 스로 (잡아 돌려 던지기)",
                tip, new Color(0.6f, 0.9f, 1f, 0.9f));
            Text(new Rect(0, y + 32 * scale, Screen.width, 30 * scale), "적의 경직 게이지가 다 차면 무방비  —  강공격으로 띄워 공중 콤보",
                tip, new Color(0.85f, 0.82f, 0.75f, 0.85f));
        }

        // ---------------------------------------------------------------- round-end upgrades (endless)

        /// <summary>
        /// Five cards: click (or 1–5, or ←/→ and Enter) to choose — again to cancel; with two chosen, the confirm button
        /// makes them permanent and starts the next round.
        /// </summary>
        void DrawUpgrades(GameManager g)
        {
            var p = g.Player;
            float a = Mathf.Clamp01(g.StateTime / 0.35f);
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.6f * a));
            int need = g.PicksNeeded;
            Text(new Rect(0, Screen.height * 0.15f, Screen.width, 50 * scale), $"{g.Round} 라운드 클리어", mid, new Color(Gold.r, Gold.g, Gold.b, a));
            Text(new Rect(0, Screen.height * 0.15f + 52 * scale, Screen.width, 34 * scale),
                $"영구 강화 {need}개를 고르세요  ({g.Picked.Count} / {need})   —   다시 누르면 선택 취소", small, new Color(0.9f, 0.88f, 0.8f, a));

            int n = g.Offers.Count;
            float cw = 250 * scale, ch = 300 * scale, gap = 22 * scale;
            float x0 = (Screen.width - (n * cw + (n - 1) * gap)) / 2f, y0 = Screen.height * 0.32f;
            var title = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(25 * scale), wordWrap = true };
            var amount = new GUIStyle(big) { fontSize = Mathf.RoundToInt(56 * scale) };
            var info = new GUIStyle(small) { fontSize = Mathf.RoundToInt(19 * scale) };
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

                Fill(new Rect(r.x - 3 * scale, r.y - 3 * scale, r.width + 6 * scale, r.height + 6 * scale),
                    picked ? new Color(Gold.r, Gold.g, Gold.b, a) : hot ? new Color(0.95f, 0.92f, 0.85f, a) : new Color(Gold.r * 0.5f, Gold.g * 0.5f, Gold.b * 0.5f, a));
                Fill(r, picked ? new Color(0.22f, 0.17f, 0.08f, 0.97f * a) : new Color(0.06f, 0.055f, 0.06f, 0.97f * a));
                // Chess-tile header strip.
                for (int k = 0; k < 8; k++)
                    Fill(new Rect(r.x + k * r.width / 8f, r.y, r.width / 8f, 10 * scale), (k % 2 == 0 ? new Color(0.9f, 0.87f, 0.78f, a) : new Color(0.12f, 0.1f, 0.1f, a)));

                var u = g.Offers[i];
                Text(new Rect(r.x, r.y + 14 * scale, 34 * scale, 34 * scale), (i + 1).ToString(), info, new Color(1f, 1f, 1f, 0.5f * a));
                Text(new Rect(r.x + 12 * scale, r.y + 40 * scale, r.width - 24 * scale, 64 * scale), u.Title, title, new Color(Gold.r, Gold.g, Gold.b, a));
                Text(new Rect(r.x, r.y + 110 * scale, r.width, 80 * scale), u.AmountText, amount, new Color(0.96f, 0.94f, 0.88f, a));
                Text(new Rect(r.x, r.y + 200 * scale, r.width, 34 * scale), Upgrades.Preview(p.Stats, u), info, new Color(0.75f, 0.73f, 0.68f, a));
                var d = Upgrades.Def(u.kind);
                if (!float.IsInfinity(d.cap))
                    Text(new Rect(r.x, r.y + 232 * scale, r.width, 26 * scale), d.percent ? $"최대 {Mathf.RoundToInt(d.cap * 100f)}%" : $"최대 {Mathf.RoundToInt(d.cap)}",
                        new GUIStyle(info) { fontSize = Mathf.RoundToInt(15 * scale) }, new Color(1f, 1f, 1f, 0.35f * a));
                if (picked)
                    Text(new Rect(r.x, r.yMax - 40 * scale, r.width, 32 * scale), "선택됨",
                        new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(19 * scale) }, new Color(Gold.r, Gold.g, Gold.b, a));
            }

            // Confirm button: unlocks with two chosen.
            bool ready = g.CanConfirmUpgrades;
            var b = new Rect((Screen.width - 300 * scale) / 2f, y0 + ch + 44 * scale, 300 * scale, 62 * scale);
            if (mouseMoved && b.Contains(e.mousePosition)) g.OfferCursor = g.ConfirmSlot;
            if (e.type == EventType.MouseDown && b.Contains(e.mousePosition)) { g.ConfirmUpgrades(); e.Use(); }
            bool bHot = g.OfferCursor == g.ConfirmSlot;
            float pulse = ready ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 5f) : 1f;
            Fill(new Rect(b.x - 3 * scale, b.y - 3 * scale, b.width + 6 * scale, b.height + 6 * scale),
                ready ? new Color(Gold.r, Gold.g, Gold.b, a * pulse) : bHot ? new Color(0.6f, 0.58f, 0.52f, 0.8f * a) : new Color(0.4f, 0.38f, 0.34f, 0.6f * a));
            Fill(b, ready ? (bHot ? new Color(0.36f, 0.27f, 0.1f, a) : new Color(0.24f, 0.18f, 0.07f, a)) : new Color(0.08f, 0.08f, 0.08f, 0.9f * a));
            Text(b, "확인", new GUIStyle(mid) { fontSize = Mathf.RoundToInt(28 * scale) },
                ready ? new Color(1f, 0.95f, 0.85f, a) : new Color(1f, 1f, 1f, 0.3f * a));
            Text(new Rect(0, b.yMax + 10 * scale, Screen.width, 26 * scale), "클릭  ·  숫자 1–5  ·  ← → 와 Enter",
                new GUIStyle(small) { fontSize = Mathf.RoundToInt(15 * scale) }, new Color(1f, 1f, 1f, 0.4f * a));
        }

        // ---------------------------------------------------------------- primitives

        // ---------------------------------------------------------------- low health

        static Texture2D vignette;
        float vignetteLevel;

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

        /// <summary>
        /// Below 35% health the screen edges close in with a pulsing blood-red vignette: the lower the health, the
        /// further it reaches toward the centre and the harder it beats.
        /// </summary>
        void DrawLowHealthVignette(PlayerController p)
        {
            float frac = p.MaxHealth > 0f ? p.Health / p.MaxHealth : 1f;
            float target = p.IsDead ? 0f : Mathf.Clamp01((0.35f - frac) / 0.3f);
            vignetteLevel = Mathf.MoveTowards(vignetteLevel, target, Time.unscaledDeltaTime * 1.5f);
            if (vignetteLevel <= 0.001f) return;
            float beat = Mathf.Pow(Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.Lerp(3.2f, 5.5f, vignetteLevel))), 6f);
            // Draw the mask oversized when mild (edges only), at screen size when critical (closing in).
            float grow = Mathf.Lerp(0.45f, 0.02f, vignetteLevel) - 0.06f * beat * vignetteLevel;
            var r = new Rect(-Screen.width * grow, -Screen.height * grow, Screen.width * (1f + 2f * grow), Screen.height * (1f + 2f * grow));
            var old = GUI.color;
            GUI.color = new Color(0.55f, 0.02f, 0.02f, Mathf.Lerp(0.55f, 0.9f, vignetteLevel) * (0.85f + 0.15f * beat));
            GUI.DrawTexture(r, Vignette, ScaleMode.StretchToFill, true);
            GUI.color = old;
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

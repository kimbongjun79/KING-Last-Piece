using UnityEngine;
using UnityEngine.InputSystem;

namespace KingLastPiece
{
    /// <summary>Play-test three one-blow heavy cuts against passive targets in the Effects Lab.</summary>
    public class EffectsLab : MonoBehaviour
    {
        static readonly float[] StationX = { -5.5f, 0f, 5.5f };
        static readonly Key[] Keys = { Key.Digit1, Key.Digit2, Key.Digit3 };
        static readonly string[] Names = { "1. 측면 일격", "2. 공중 내려베기", "3. 회전 일격" };
        static readonly string[] Details =
        {
            "몸을 옆으로 깊게 젖혀 힘을 모았다가 한 번 크게 베기",
            "높이 도약해 공중제비 후 수직으로 단 한 번 내려베기",
            "긴 회전으로 힘을 모아 마지막 순간 한 번 크게 베기",
        };
        static readonly AttackDef[] Trials =
        {
            AttackStyles.Default.heavy, AttackStyles.HeavyCut(1), AttackStyles.HeavyCut(2),
        };

        GameManager game;
        readonly TrainingDummy[] dummies = new TrainingDummy[3];
        int selected;
        GUIStyle labelStyle, panelStyle;

        void Awake()
        {
            GameManager.StartMode = GameManager.Mode.Effects;
            game = FindFirstObjectByType<GameManager>();
            if (game == null) game = new GameObject("GameManager").AddComponent<GameManager>();
        }

        void Start()
        {
            game.Player.GodMode = true;
            game.Player.InfiniteStamina = true;
            for (int i = 0; i < dummies.Length; i++) SpawnDummy(i);
            Select(0);
        }

        static Vector3 Station(int i) => new Vector3(StationX[i], 0f, 0.5f);
        static Vector3 PlayerStart(int i) => Station(i) - Vector3.forward * 2.4f;
        void SpawnDummy(int i) => dummies[i] = game.SpawnCustom<TrainingDummy>(PieceType.Pawn, Station(i) + Vector3.forward * 2f);

        void Select(int i)
        {
            selected = i;
            game.Player.HeavyMotionTrial = Trials[i];
            game.Player.ResetForWave(PlayerStart(i), 0f);
            game.Rig.Snap();
        }

        void Update()
        {
            if (game == null) return;
            var kb = Keyboard.current;
            if (kb != null)
                for (int i = 0; i < Keys.Length; i++)
                    if (kb[Keys[i]].wasPressedThisFrame) Select(i);
            for (int i = 0; i < dummies.Length; i++)
                if (dummies[i] == null || dummies[i].IsDead) SpawnDummy(i);
        }

        void OnGUI()
        {
            if (game == null || game.Rig == null) return;
            float scale = Screen.height / 1080f;
            labelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            panelStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
            labelStyle.fontSize = Mathf.RoundToInt(26 * scale);
            panelStyle.fontSize = Mathf.RoundToInt(19 * scale);
            for (int i = 0; i < StationX.Length; i++)
            {
                Vector3 sp = game.Rig.Cam.WorldToScreenPoint(Station(i) + new Vector3(0f, 3.4f, 2f));
                if (sp.z < 0f) continue;
                var r = new Rect(sp.x - 160 * scale, Screen.height - sp.y - 18 * scale, 320 * scale, 36 * scale);
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = selected == i ? new Color(1f, 0.85f, 0.45f) : Color.white;
                GUI.Label(r, Names[i] + (selected == i ? "  ◀ 킹" : ""), labelStyle);
                GUI.color = Color.white;
            }
            var pr = new Rect(20 * scale, 20 * scale, 600 * scale, 135 * scale);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(pr, Texture2D.whiteTexture);
            GUI.color = Color.white;
            var attack = game.Player.CurrentAttack;
            string active = attack == Trials[selected] ? $"실행 중: {attack.name}" : "우클릭/K로 모션 확인";
            GUI.Label(new Rect(pr.x + 12 * scale, pr.y + 8 * scale, pr.width - 24 * scale, pr.height - 16 * scale),
                $"강공격 한방 모션 비교 — 1·2·3: 위치와 모션 선택\n{Names[selected]}: {Details[selected]}\n{active} / 피해·범위·기력 동일, Board Ripple 공통", panelStyle);
        }
    }
}

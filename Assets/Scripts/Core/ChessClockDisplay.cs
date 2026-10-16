using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// The chess clock's two LED faces: the left one shows the play time (MM:SS, H:MM after an hour), the right one the
    /// round. Seven-segment digits made of small glowing bars, built at start on the face anchors placed by the bake.
    /// </summary>
    public class ChessClockDisplay : MonoBehaviour
    {
        [Tooltip("Centre of each face's digit area; local +X runs along the digits, +Y up the face.")]
        public Transform leftFace, rightFace;
        [Tooltip("Digit height in the anchors' local units.")]
        public float digitHeight = 1f;
        public Color lit = new Color(1f, 0.32f, 0.12f);
        public float litIntensity = 3.5f;
        public Color unlit = new Color(0.12f, 0.03f, 0.02f);

        // Segment order a b c d e f g; which ones are on for 0–9 (and blank = 10).
        static readonly bool[][] Digits =
        {
            new[] { true, true, true, true, true, true, false },
            new[] { false, true, true, false, false, false, false },
            new[] { true, true, false, true, true, false, true },
            new[] { true, true, true, true, false, false, true },
            new[] { false, true, true, false, false, true, true },
            new[] { true, false, true, true, false, true, true },
            new[] { true, false, true, true, true, true, true },
            new[] { true, true, true, false, false, false, false },
            new[] { true, true, true, true, true, true, true },
            new[] { true, true, true, true, false, true, true },
            new[] { false, false, false, false, false, false, false },
        };

        Renderer[][] left, right;
        Material on, off;
        string shownLeft, shownRight;

        void Start()
        {
            on = MaterialLib.Emissive(lit, litIntensity);
            off = MaterialLib.Get(unlit, 0.6f);
            left = BuildFace(leftFace, true);
            right = BuildFace(rightFace, false);
        }

        /// <summary>Four digits; with a colon between the pairs on the time face.</summary>
        Renderer[][] BuildFace(Transform face, bool colon)
        {
            if (face == null) return null;
            float h = digitHeight, w = h * 0.55f, t = h * 0.12f, gap = w * 0.45f;
            var digits = new Renderer[4][];
            float total = 4 * w + 3 * gap + (colon ? gap : 0f);
            float x = -total / 2f + w / 2f;
            for (int d = 0; d < 4; d++)
            {
                if (colon && d == 2)
                {
                    // Colon: two dots between the minutes and the seconds.
                    float cx = x - gap - w / 2f + gap;
                    Bar(face, new Vector3(cx - gap * 0.5f, h * 0.22f, 0f), new Vector2(t, t), on);
                    Bar(face, new Vector3(cx - gap * 0.5f, -h * 0.22f, 0f), new Vector2(t, t), on);
                    x += gap;
                }
                digits[d] = new[]
                {
                    Bar(face, new Vector3(x, h / 2f, 0f), new Vector2(w, t), off),              // a top
                    Bar(face, new Vector3(x + w / 2f, h / 4f, 0f), new Vector2(t, h / 2f), off),  // b top right
                    Bar(face, new Vector3(x + w / 2f, -h / 4f, 0f), new Vector2(t, h / 2f), off), // c bottom right
                    Bar(face, new Vector3(x, -h / 2f, 0f), new Vector2(w, t), off),             // d bottom
                    Bar(face, new Vector3(x - w / 2f, -h / 4f, 0f), new Vector2(t, h / 2f), off), // e bottom left
                    Bar(face, new Vector3(x - w / 2f, h / 4f, 0f), new Vector2(t, h / 2f), off),  // f top left
                    Bar(face, new Vector3(x, 0f, 0f), new Vector2(w, t), off),                  // g middle
                };
                x += w + gap;
            }
            return digits;
        }

        Renderer Bar(Transform face, Vector3 pos, Vector2 size, Material mat)
        {
            var t = PieceFactory.Part(PrimitiveType.Cube, face, pos, new Vector3(size.x, size.y, digitHeight * 0.04f), mat);
            t.name = "Segment";
            var r = t.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        void Update()
        {
            var g = GameManager.Instance;
            float time = g != null ? g.RunTime : 0f;
            int secs = Mathf.FloorToInt(time);
            string l = secs < 3600 ? $"{secs / 60:00}{secs % 60:00}" : $"{secs / 3600,2}{secs / 60 % 60:00}";
            string r = g != null && g.UsesRounds ? $"{Mathf.Min(9999, g.IsEndless ? g.Round : g.WaveIndex + 1),4}" : "    ";
            if (l != shownLeft) { Show(left, l); shownLeft = l; }
            if (r != shownRight) { Show(right, r); shownRight = r; }
        }

        void Show(Renderer[][] face, string text)
        {
            if (face == null) return;
            for (int d = 0; d < face.Length && d < text.Length; d++)
            {
                int v = text[d] >= '0' && text[d] <= '9' ? text[d] - '0' : 10;
                for (int s = 0; s < 7; s++) face[d][s].sharedMaterial = Digits[v][s] ? on : off;
            }
        }
    }
}

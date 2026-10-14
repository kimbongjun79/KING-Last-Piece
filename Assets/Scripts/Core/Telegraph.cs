using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Ground danger marker shown while an enemy winds up. The bright fill grows until the hit lands.
    /// </summary>
    public class Telegraph : MonoBehaviour
    {
        Transform outline, fill;
        bool isRect;
        Vector3 start, dir, center;
        float length, width, radius;

        static Material OutlineMat => MaterialLib.Get(new Color(0.12f, 0.02f, 0.02f), 0f, 0f, new Color(0.45f, 0.03f, 0.02f));
        static Material FillMat => MaterialLib.Get(new Color(0.4f, 0.04f, 0.02f), 0f, 0f, new Color(2.4f, 0.3f, 0.08f));

        public static Telegraph Circle(Vector3 c, float r)
        {
            var t = Create("Telegraph_Circle", PrimitiveType.Cylinder);
            t.isRect = false;
            t.center = new Vector3(c.x, 0f, c.z);
            t.radius = r;
            t.SetProgress(0f);
            return t;
        }

        public static Telegraph Rect(Vector3 s, Vector3 d, float len, float w)
        {
            var t = Create("Telegraph_Rect", PrimitiveType.Cube);
            t.isRect = true;
            t.SetRect(s, d, len, w);
            t.SetProgress(0f);
            return t;
        }

        static Telegraph Create(string name, PrimitiveType shape)
        {
            var go = new GameObject(name);
            var t = go.AddComponent<Telegraph>();
            t.outline = PieceFactory.Part(shape, go.transform, Vector3.zero, Vector3.one, OutlineMat);
            t.fill = PieceFactory.Part(shape, go.transform, Vector3.zero, Vector3.one, FillMat);
            return t;
        }

        public void SetRect(Vector3 s, Vector3 d, float len, float w)
        {
            start = new Vector3(s.x, 0f, s.z);
            dir = d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.forward;
            length = Mathf.Max(0.1f, len);
            width = w;
        }

        public void SetProgress(float p)
        {
            p = Mathf.Clamp01(p);
            if (isRect)
            {
                var rot = Quaternion.LookRotation(dir);
                outline.SetPositionAndRotation(start + dir * (length * 0.5f) + Vector3.up * 0.012f, rot);
                outline.localScale = new Vector3(width, 0.012f, length);
                float fl = Mathf.Max(0.01f, length * p);
                fill.SetPositionAndRotation(start + dir * (fl * 0.5f) + Vector3.up * 0.02f, rot);
                fill.localScale = new Vector3(width * 0.8f, 0.016f, fl);
            }
            else
            {
                outline.position = center + Vector3.up * 0.012f;
                outline.localScale = new Vector3(radius * 2f, 0.006f, radius * 2f);
                float fr = Mathf.Max(0.02f, radius * 2f * p);
                fill.position = center + Vector3.up * 0.02f;
                fill.localScale = new Vector3(fr, 0.008f, fr);
            }
        }
    }
}

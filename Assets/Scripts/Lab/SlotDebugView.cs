using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Shows the board slots: a red frame on every square a piece holds to attack from, a grey frame on waiting
    /// squares, and a gold frame on the King's square. Toggled with V in the practice arena.
    /// </summary>
    public class SlotDebugView : MonoBehaviour
    {
        readonly List<Transform[]> frames = new List<Transform[]>();
        Material attackMat, waitMat, kingMat;

        void Awake()
        {
            attackMat = MaterialLib.Emissive(new Color(1f, 0.2f, 0.1f), 2.5f);
            waitMat = MaterialLib.Emissive(new Color(0.55f, 0.55f, 0.6f), 1.2f);
            kingMat = MaterialLib.Emissive(new Color(1f, 0.8f, 0.35f), 2.5f);
        }

        void OnDisable()
        {
            foreach (var f in frames) foreach (var t in f) if (t != null) t.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            int used = 0;
            Draw(ref used, BoardSlots.KingSquare, kingMat, 0.9f);
            foreach (var kv in BoardSlots.Reservations)
            {
                if (kv.Value == null) continue;
                Draw(ref used, kv.Key, kv.Value.HoldsAttackSlot ? attackMat : waitMat, 0.8f);
            }
            for (int i = used; i < frames.Count; i++) foreach (var t in frames[i]) t.gameObject.SetActive(false);
        }

        void Draw(ref int used, Vector2Int square, Material mat, float size)
        {
            if (used >= frames.Count)
            {
                var f = new Transform[4];
                for (int i = 0; i < 4; i++) f[i] = PieceFactory.Part(PrimitiveType.Cube, transform, Vector3.zero, Vector3.one, mat);
                frames.Add(f);
            }
            var bars = frames[used++];
            Vector3 c = BoardSlots.Center(square) + Vector3.up * 0.03f;
            float h = GameManager.TileSize * size * 0.5f, w = 0.08f;
            Vector3[] pos = { c + new Vector3(0, 0, h), c - new Vector3(0, 0, h), c + new Vector3(h, 0, 0), c - new Vector3(h, 0, 0) };
            Vector3[] scl = { new Vector3(h * 2 + w, 0.02f, w), new Vector3(h * 2 + w, 0.02f, w), new Vector3(w, 0.02f, h * 2 + w), new Vector3(w, 0.02f, h * 2 + w) };
            for (int i = 0; i < 4; i++)
            {
                var t = bars[i];
                t.gameObject.SetActive(true);
                t.position = pos[i];
                t.localScale = scl[i];
                t.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }
    }
}

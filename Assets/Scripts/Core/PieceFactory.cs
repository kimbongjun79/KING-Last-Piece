using UnityEngine;

namespace KingLastPiece
{
    public enum PieceType { Pawn, Knight, Bishop, Rook, Queen, King }

    /// <summary>Builds chess piece models out of primitives. Model origin is at the base of the piece.</summary>
    public static class PieceFactory
    {
        public static readonly Color BlackPiece = new Color(0.07f, 0.07f, 0.085f);
        public static readonly Color WhitePiece = new Color(0.93f, 0.9f, 0.82f);
        public static readonly Color EyeGlow = new Color(1f, 0.15f, 0.05f);

        public static Transform Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(euler);
            t.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            return t;
        }

        public static Transform Build(PieceType type, Transform parent, bool white)
        {
            var root = new GameObject(type + "_Model").transform;
            root.SetParent(parent, false);
            var mat = white ? MaterialLib.Get(WhitePiece, 0.55f) : MaterialLib.Get(BlackPiece, 0.85f, 0.15f);
            var eye = MaterialLib.Emissive(EyeGlow, 3f);

            Part(PrimitiveType.Cylinder, root, new Vector3(0, 0.08f, 0), new Vector3(0.95f, 0.08f, 0.95f), mat);
            Part(PrimitiveType.Cylinder, root, new Vector3(0, 0.2f, 0), new Vector3(0.78f, 0.05f, 0.78f), mat);

            switch (type)
            {
                case PieceType.Pawn:
                    Part(PrimitiveType.Cylinder, root, new Vector3(0, 0.5f, 0), new Vector3(0.42f, 0.26f, 0.42f), mat);
                    Part(PrimitiveType.Cylinder, root, new Vector3(0, 0.8f, 0), new Vector3(0.62f, 0.04f, 0.62f), mat);
                    Part(PrimitiveType.Sphere, root, new Vector3(0, 1.08f, 0), Vector3.one * 0.52f, mat);
                    if (!white) Part(PrimitiveType.Cube, root, new Vector3(0, 1.1f, 0.24f), new Vector3(0.22f, 0.04f, 0.06f), eye);
                    break;

                case PieceType.Rook:
                    Part(PrimitiveType.Cylinder, root, new Vector3(0, 0.75f, 0), new Vector3(0.62f, 0.52f, 0.62f), mat);
                    Part(PrimitiveType.Cylinder, root, new Vector3(0, 1.36f, 0), new Vector3(0.82f, 0.1f, 0.82f), mat);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                        Part(PrimitiveType.Cube, root, new Vector3(Mathf.Cos(a) * 0.3f, 1.58f, Mathf.Sin(a) * 0.3f),
                            new Vector3(0.24f, 0.26f, 0.24f), mat, new Vector3(0, i * 90f + 45f, 0));
                    }
                    if (!white) Part(PrimitiveType.Cube, root, new Vector3(0, 1.1f, 0.3f), new Vector3(0.36f, 0.05f, 0.06f), eye);
                    break;

                case PieceType.Bishop:
                    Part(PrimitiveType.Cylinder, root, new Vector3(0, 0.65f, 0), new Vector3(0.44f, 0.42f, 0.44f), mat);
                    Part(PrimitiveType.Cylinder, root, new Vector3(0, 1.1f, 0), new Vector3(0.62f, 0.04f, 0.62f), mat);
                    Part(PrimitiveType.Sphere, root, new Vector3(0, 1.45f, 0), new Vector3(0.5f, 0.68f, 0.5f), mat);
                    Part(PrimitiveType.Sphere, root, new Vector3(0, 1.86f, 0), Vector3.one * 0.16f, mat);
                    Part(PrimitiveType.Cube, root, new Vector3(0, 1.55f, 0.2f), new Vector3(0.05f, 0.3f, 0.14f),
                        white ? mat : eye, new Vector3(0, 0, 35f));
                    break;

                case PieceType.Knight:
                    Part(PrimitiveType.Cylinder, root, new Vector3(0, 0.45f, 0), new Vector3(0.55f, 0.22f, 0.55f), mat);
                    Part(PrimitiveType.Cube, root, new Vector3(0, 0.95f, -0.05f), new Vector3(0.42f, 0.7f, 0.5f), mat, new Vector3(-12f, 0, 0));
                    Part(PrimitiveType.Cube, root, new Vector3(0, 1.32f, 0.22f), new Vector3(0.36f, 0.32f, 0.75f), mat, new Vector3(18f, 0, 0));
                    Part(PrimitiveType.Cube, root, new Vector3(0, 1.05f, -0.32f), new Vector3(0.12f, 0.75f, 0.12f), mat, new Vector3(-15f, 0, 0));
                    Part(PrimitiveType.Cube, root, new Vector3(0.1f, 1.55f, -0.02f), new Vector3(0.08f, 0.2f, 0.08f), mat);
                    Part(PrimitiveType.Cube, root, new Vector3(-0.1f, 1.55f, -0.02f), new Vector3(0.08f, 0.2f, 0.08f), mat);
                    if (!white)
                    {
                        Part(PrimitiveType.Cube, root, new Vector3(0.185f, 1.4f, 0.24f), new Vector3(0.02f, 0.06f, 0.08f), eye, new Vector3(18f, 0, 0));
                        Part(PrimitiveType.Cube, root, new Vector3(-0.185f, 1.4f, 0.24f), new Vector3(0.02f, 0.06f, 0.08f), eye, new Vector3(18f, 0, 0));
                    }
                    break;

                case PieceType.Queen:
                case PieceType.King:
                    Part(PrimitiveType.Cylinder, root, new Vector3(0, 0.8f, 0), new Vector3(0.6f, 0.56f, 0.6f), mat);
                    Part(PrimitiveType.Cylinder, root, new Vector3(0, 1.4f, 0), new Vector3(0.74f, 0.05f, 0.74f), mat);
                    Part(PrimitiveType.Cylinder, root, new Vector3(0, 1.7f, 0), new Vector3(0.42f, 0.28f, 0.42f), mat);
                    Part(PrimitiveType.Cylinder, root, new Vector3(0, 2.02f, 0), new Vector3(0.72f, 0.06f, 0.72f), mat);
                    if (type == PieceType.Queen)
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            float a = i * 45f * Mathf.Deg2Rad;
                            Part(PrimitiveType.Sphere, root, new Vector3(Mathf.Cos(a) * 0.33f, 2.16f, Mathf.Sin(a) * 0.33f), Vector3.one * 0.15f, white ? mat : eye);
                        }
                        Part(PrimitiveType.Sphere, root, new Vector3(0, 2.2f, 0), new Vector3(0.4f, 0.3f, 0.4f), mat);
                        Part(PrimitiveType.Sphere, root, new Vector3(0, 2.42f, 0), Vector3.one * 0.18f, mat);
                        if (!white) Part(PrimitiveType.Cube, root, new Vector3(0, 1.75f, 0.21f), new Vector3(0.3f, 0.05f, 0.04f), eye);
                    }
                    else
                    {
                        Part(PrimitiveType.Sphere, root, new Vector3(0, 2.18f, 0), new Vector3(0.45f, 0.25f, 0.45f), mat);
                        Part(PrimitiveType.Cube, root, new Vector3(0, 2.48f, 0), new Vector3(0.09f, 0.4f, 0.09f), mat);
                        Part(PrimitiveType.Cube, root, new Vector3(0, 2.55f, 0), new Vector3(0.3f, 0.09f, 0.09f), mat);
                    }
                    break;
            }
            return root;
        }

        /// <summary>Loads the White King FBX from Resources and normalizes it to the given height.
        /// Falls back to a primitive king if the model is missing.</summary>
        public static Transform BuildPlayerKing(Transform parent, float height)
        {
            var prefab = Resources.Load<GameObject>("Models/White_Chess_King_v02");
            if (prefab == null) return Build(PieceType.King, parent, true);

            var container = new GameObject("King_Model").transform;
            container.SetParent(parent, false);

            var inst = Object.Instantiate(prefab);
            inst.name = "WhiteKing_FBX";
            // Keep the FBX root rotation (Blender exports carry a -90° X axis conversion).
            inst.transform.SetPositionAndRotation(Vector3.zero, prefab.transform.rotation);
            foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

            var renderers = inst.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Object.Destroy(inst);
                Object.Destroy(container.gameObject);
                return Build(PieceType.King, parent, true);
            }

            var mat = MaterialLib.Get(PieceFactory.WhitePiece, 0.6f);
            foreach (var r in renderers)
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }

            Bounds b = WorldBounds(renderers);
            // If the model was exported lying down, stand it up.
            if (b.size.y < Mathf.Max(b.size.x, b.size.z) * 0.8f)
            {
                inst.transform.rotation = Quaternion.Euler(b.size.z > b.size.x ? -90f : 0f, 0f, b.size.x >= b.size.z ? 90f : 0f)
                                          * inst.transform.rotation;
                b = WorldBounds(renderers);
            }

            float s = height / Mathf.Max(0.001f, b.size.y);
            inst.transform.localScale *= s;
            b = WorldBounds(renderers);
            inst.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);

            inst.transform.SetParent(container, false);
            return container;
        }

        static Bounds WorldBounds(Renderer[] renderers)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }
    }
}

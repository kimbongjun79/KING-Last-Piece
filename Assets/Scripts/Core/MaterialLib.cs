using System.Collections.Generic;
using UnityEngine;

namespace KingLastPiece
{
    /// <summary>
    /// Creates and caches runtime materials based on the pipeline's default Lit material,
    /// so everything works without authored material assets.
    /// </summary>
    public static class MaterialLib
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        static Material template;
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        static Material Template
        {
            get
            {
                if (template != null) return template;
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var src = tmp.GetComponent<Renderer>().sharedMaterial;
                Object.DestroyImmediate(tmp);
                if (src == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit");
                    template = new Material(shader != null ? shader : Shader.Find("Standard"));
                }
                else
                {
                    template = new Material(src);
                }
                return template;
            }
        }

        public static Material Get(Color color, float smoothness = 0.4f, float metallic = 0f, Color? emission = null)
        {
            Color e = emission ?? Color.black;
            string key = $"{color}|{smoothness}|{metallic}|{e}";
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var m = new Material(Template) { name = "KLP_" + cache.Count };
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, color);
            m.color = color;
            if (m.HasProperty(SmoothnessId)) m.SetFloat(SmoothnessId, smoothness);
            if (m.HasProperty(MetallicId)) m.SetFloat(MetallicId, metallic);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor(EmissionId, e);
            cache[key] = m;
            return m;
        }

        public static Material Emissive(Color color, float intensity)
        {
            return Get(color * 0.3f, 0.2f, 0f, color * intensity);
        }

        public static bool HasEmission(Material m)
        {
            if (m == null || !m.HasProperty(EmissionId)) return false;
            return m.GetColor(EmissionId).maxColorComponent > 0.01f;
        }
    }
}

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
                // Authored template keeps the _EMISSION shader variant alive in player builds.
                var authored = Resources.Load<Material>("Materials/KLP_Emissive");
                if (authored != null) return template = new Material(authored);
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

        static Material normalTemplate;
        static readonly Dictionary<Material, Material> converted = new Dictionary<Material, Material>();
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
        static readonly int BumpScaleId = Shader.PropertyToID("_BumpScale");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>
        /// Copies an imported model material (its base color, base map, normal map and surface values)
        /// onto our emissive-capable Lit material so hit flashes and glows still work on designed models.
        /// </summary>
        public static Material FromImported(Material src)
        {
            if (src == null) return Get(new Color(0.5f, 0.5f, 0.5f));
            if (converted.TryGetValue(src, out var cached) && cached != null) return cached;

            Texture baseMap = src.HasProperty(BaseMapId) ? src.GetTexture(BaseMapId) : null;
            if (baseMap == null && src.HasProperty(MainTexId)) baseMap = src.GetTexture(MainTexId);
            Texture bump = src.HasProperty(BumpMapId) ? src.GetTexture(BumpMapId) : null;
            Color color = src.HasProperty(BaseColorId) ? src.GetColor(BaseColorId)
                : src.HasProperty(ColorId) ? src.GetColor(ColorId) : Color.white;

            if (bump != null && normalTemplate == null)
            {
                var authored = Resources.Load<Material>("Materials/KLP_EmissiveNormal");
                normalTemplate = authored != null ? new Material(authored) : new Material(Template);
            }

            var m = new Material(bump != null ? normalTemplate : Template) { name = "KLP_" + src.name };
            m.SetColor(BaseColorId, color);
            if (baseMap != null) m.SetTexture(BaseMapId, baseMap);
            if (bump != null)
            {
                m.SetTexture(BumpMapId, bump);
                m.SetFloat(BumpScaleId, src.HasProperty(BumpScaleId) ? src.GetFloat(BumpScaleId) : 1f);
                m.EnableKeyword("_NORMALMAP");
            }
            if (src.HasProperty(SmoothnessId)) m.SetFloat(SmoothnessId, src.GetFloat(SmoothnessId));
            if (src.HasProperty(MetallicId)) m.SetFloat(MetallicId, src.GetFloat(MetallicId));
            m.EnableKeyword("_EMISSION");
            m.SetColor(EmissionId, Color.black);
            converted[src] = m;
            return m;
        }

        public static Material Emissive(Color color, float intensity)
        {
            return Get(color * 0.3f, 0.2f, 0f, color * intensity);
        }

        static Material transparentTemplate;

        /// <summary>Alpha-blended Lit material (from the authored KLP_Transparent template so the variant ships).</summary>
        public static Material Transparent(Color color, Color? emission = null)
        {
            Color e = emission ?? Color.black;
            string key = $"T|{color}|{e}";
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;
            if (transparentTemplate == null)
            {
                var authored = Resources.Load<Material>("Materials/KLP_Transparent");
                transparentTemplate = authored != null ? new Material(authored) : new Material(Template);
            }
            var m = new Material(transparentTemplate) { name = "KLP_T" + cache.Count };
            m.SetColor(BaseColorId, color);
            m.SetFloat(SmoothnessId, 0.2f);
            m.EnableKeyword("_EMISSION");
            m.SetColor(EmissionId, e);
            cache[key] = m;
            return m;
        }

        public static bool HasEmission(Material m)
        {
            if (m == null || !m.HasProperty(EmissionId)) return false;
            return m.GetColor(EmissionId).maxColorComponent > 0.01f;
        }
    }
}

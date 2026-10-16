using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KingLastPiece.EditorTools
{
    /// <summary>
    /// KING ▸ Setup ▸ Endless Assets: registers the AC Little Enchant Mesh VFX "Dark Lvl 1–3" auras of the empowered
    /// pieces in the FxLibrary, with black alpha-blended URP copies of their (additive) particle materials.
    /// CLI: -executeMethod KingLastPiece.EditorTools.EndlessSetup.SetupCli
    /// </summary>
    public static class EndlessSetup
    {
        const string LibraryPath = "Assets/Resources/FxLibrary.asset";
        const string AuraPrefabs = "Assets/AC Little Enchant Mesh VFX/Prefabs/";
        const string MaterialDir = "Assets/Materials/EliteAura";
        const string DizzyPrefab = "Assets/Tazo_fx/_Loop_effect/L_Dizzy(click play).prefab";
        static readonly Color Ink = new Color(0.03f, 0.03f, 0.035f, 1f);

        [MenuItem("KING/Setup/Endless Assets")]
        public static void Setup()
        {
            Run();
            AssetDatabase.SaveAssets();
        }

        public static void SetupCli()
        {
            Run();
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }

        static void Run()
        {
            var lib = AssetDatabase.LoadAssetAtPath<FxLibrary>(LibraryPath);
            if (lib == null) { Debug.LogError("[EndlessSetup] FxLibrary missing — run KING ▸ Setup ▸ Effects Assets first."); return; }

            var auras = new GameObject[3];
            for (int i = 0; i < 3; i++) auras[i] = AssetDatabase.LoadAssetAtPath<GameObject>($"{AuraPrefabs}Dark Lvl {i + 1}.prefab");

            var sources = new List<Material>();
            foreach (var a in auras)
            {
                if (a == null) continue;
                foreach (var r in a.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m != null && !sources.Contains(m)) sources.Add(m);
            }

            var black = MakeBlack(sources);

            lib.eliteAura = auras;
            lib.eliteAuraSource = sources.ToArray();
            lib.eliteAuraBlack = black;

            // The Dizzy swirl over a staggered piece (Tazo FX), recoloured black the same way.
            var dizzy = AssetDatabase.LoadAssetAtPath<GameObject>(DizzyPrefab);
            var dizzySources = new List<Material>();
            if (dizzy != null)
                foreach (var r in dizzy.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    foreach (var m in r.sharedMaterials)
                        if (m != null && !dizzySources.Contains(m)) dizzySources.Add(m);
                    if (r.trailMaterial != null && !dizzySources.Contains(r.trailMaterial)) dizzySources.Add(r.trailMaterial);
                }
            if (dizzy != null)
                foreach (var t in dizzy.GetComponentsInChildren<TrailRenderer>(true))
                    foreach (var m in t.sharedMaterials)
                        if (m != null && !dizzySources.Contains(m)) dizzySources.Add(m);
            lib.dizzy = dizzy;
            lib.dizzySource = dizzySources.ToArray();
            lib.dizzyBlack = MakeBlack(dizzySources);
            EditorUtility.SetDirty(lib);
            Debug.Log($"[EndlessSetup] {auras.Length} auras, {black.Length} black materials; dizzy {(dizzy != null)} with {lib.dizzyBlack.Length}.");
        }

        /// <summary>Alpha-blended black URP particle copies of (additive) effect materials, keeping their textures.</summary>
        static Material[] MakeBlack(List<Material> sources)
        {
            Directory.CreateDirectory(MaterialDir);
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var black = new Material[sources.Count];
            for (int i = 0; i < sources.Count; i++)
            {
                var src = sources[i];
                string path = $"{MaterialDir}/Dark {src.name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(shader);
                    AssetDatabase.CreateAsset(m, path);
                }
                m.shader = shader;
                if (src.HasProperty("_MainTex")) m.SetTexture("_BaseMap", src.GetTexture("_MainTex"));
                m.SetColor("_BaseColor", Ink);
                m.SetFloat("_Surface", 1f);   // transparent
                m.SetFloat("_Blend", 0f);     // alpha blend: black stays black instead of vanishing like additive
                m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                BaseShaderGUI.SetupMaterialBlendMode(m);
                UnityEditor.Rendering.Universal.ShaderGUI.ParticleGUI.SetMaterialKeywords(m);
                EditorUtility.SetDirty(m);
                black[i] = m;
            }
            return black;
        }
    }
}

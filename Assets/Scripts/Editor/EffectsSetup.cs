using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace KingLastPiece.EditorTools
{
    /// <summary>
    /// One-shot project setup for the effects pass:
    /// adds the URP Decal renderer feature, bakes the crack images into transparent decal textures,
    /// creates the crack decal material, and builds Resources/FxLibrary.asset pointing at the NamuFX slashes.
    /// </summary>
    public static class EffectsSetup
    {
        static readonly string[] Renderers = { "Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset" };
        const string CrackSourceDir = "Assets/Resources/Textures";
        const string CrackOutDir = "Assets/Resources/Textures/Decals";
        const string DecalMaterialPath = "Assets/Resources/Materials/KLP_CrackDecal.mat";
        const string LibraryPath = "Assets/Resources/FxLibrary.asset";
        const string SlashPrefabs = "Assets/NamuFX/Simple Stylized Slash vol2/Prefabs/";

        [MenuItem("KING/Setup/Effects Assets")]
        public static void Run()
        {
            EnableDecals();
            var textures = BakeCracks();
            var decalMat = CreateDecalMaterial(textures.Length > 0 ? textures[0] : null);
            BuildLibrary(decalMat, textures);
            AssetDatabase.SaveAssets();
            Debug.Log("[EffectsSetup] done");
        }

        /// <summary>CLI: force-reimport the effect pack and scripts (recovers from failed or stale imports).</summary>
        public static void ReimportCli()
        {
            AssetDatabase.ImportAsset("Assets/NamuFX", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset("Assets/Scripts", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset("Assets/Scenes", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
            EditorApplication.Exit(0);
        }

        /// <summary>CLI entry: -executeMethod KingLastPiece.EditorTools.EffectsSetup.RunCli</summary>
        public static void RunCli()
        {
            Run();
            EditorApplication.Exit(0);
        }

        static void EnableDecals()
        {
            foreach (var path in Renderers)
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null || data.rendererFeatures.Exists(f => f is DecalRendererFeature)) continue;
                var feature = ScriptableObject.CreateInstance<DecalRendererFeature>();
                feature.name = "Decal";
                AssetDatabase.AddObjectToAsset(feature, data);
                data.rendererFeatures.Add(feature);
                // Keep the renderer's feature id map in sync (private serialized list).
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long id))
                {
                    var mapField = typeof(ScriptableRendererData).GetField("m_RendererFeatureMap",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (mapField?.GetValue(data) is System.Collections.Generic.List<long> map) map.Add(id);
                }
                data.SetDirty();
                EditorUtility.SetDirty(data);
                Debug.Log($"[EffectsSetup] Decal feature added to {path}");
            }
        }

        /// <summary>
        /// Source images are dark cracks on white. Output: black cracks whose alpha is their darkness,
        /// faded out toward the borders so cropped cracks never show a hard edge.
        /// </summary>
        static Texture2D[] BakeCracks()
        {
            Directory.CreateDirectory(CrackOutDir);
            var results = new System.Collections.Generic.List<Texture2D>();
            foreach (var src in Directory.GetFiles(CrackSourceDir, "Crack*.png"))
            {
                string srcPath = src.Replace('\\', '/');
                var importer = (TextureImporter)AssetImporter.GetAtPath(srcPath);
                if (importer != null && !importer.isReadable)
                {
                    importer.isReadable = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(srcPath);
                if (tex == null) continue;

                int w = tex.width, h = tex.height;
                var px = tex.GetPixels32();
                var outPx = new Color32[px.Length];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float lum = (px[i].r * 0.299f + px[i].g * 0.587f + px[i].b * 0.114f) / 255f;
                    float a = Mathf.Clamp01((1f - lum - 0.06f) / 0.94f);  // pure white → fully transparent
                    float dx = (x + 0.5f) / w * 2f - 1f, dy = (y + 0.5f) / h * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    a *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 0.98f, r));
                    byte shade = (byte)Mathf.RoundToInt(Mathf.Lerp(40f, 6f, a) );
                    outPx[i] = new Color32(shade, (byte)(shade * 0.9f), (byte)(shade * 0.8f), (byte)Mathf.RoundToInt(a * 255f));
                }
                var baked = new Texture2D(w, h, TextureFormat.RGBA32, false);
                baked.SetPixels32(outPx);
                baked.Apply();
                string outPath = $"{CrackOutDir}/{Path.GetFileNameWithoutExtension(srcPath)}_Decal.png";
                File.WriteAllBytes(outPath, baked.EncodeToPNG());
                Object.DestroyImmediate(baked);
                AssetDatabase.ImportAsset(outPath);
                var outImporter = (TextureImporter)AssetImporter.GetAtPath(outPath);
                outImporter.alphaIsTransparency = true;
                outImporter.wrapMode = TextureWrapMode.Clamp;
                outImporter.mipmapEnabled = true;
                outImporter.SaveAndReimport();
                results.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(outPath));
            }
            return results.ToArray();
        }

        static Material CreateDecalMaterial(Texture2D tex)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(DecalMaterialPath);
            if (mat == null)
            {
                var shader = Shader.Find("Shader Graphs/Decal");
                mat = new Material(shader) { name = "KLP_CrackDecal" };
                AssetDatabase.CreateAsset(mat, DecalMaterialPath);
            }
            if (tex != null) mat.SetTexture("Base_Map", tex);  // URP Decal shader graph property name
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void BuildLibrary(Material decal, Texture2D[] cracks)
        {
            var lib = AssetDatabase.LoadAssetAtPath<FxLibrary>(LibraryPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<FxLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }
            GameObject P(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(SlashPrefabs + name + ".prefab");
            lib.slashA = P("Slash_A");
            lib.slashB = P("Slash_B");
            lib.slashC = P("Slash_C");
            lib.slashD = P("Slash_D");
            lib.slashInk = P("Slash_Ink");
            lib.slashWater = P("Slash_Water");
            lib.slashAoeA = P("Slash_Aoe_A");
            lib.crackDecal = decal;
            lib.crackTextures = cracks;
            EditorUtility.SetDirty(lib);
        }
    }
}

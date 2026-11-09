using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>
    /// Единая библиотека материалов. Работает и с URP (основной вариант), и со встроенным конвейером.
    /// Материалы кешируются по ключу — одинаковые детали всегда используют один материал,
    /// что даёт SRP Batcher / GPU Instancing максимальную эффективность.
    /// </summary>
    public sealed class MaterialLibrary
    {
        static MaterialLibrary _instance;
        public static MaterialLibrary Instance => _instance ?? (_instance = new MaterialLibrary());

        /// <summary>Сброс при выходе из Play Mode, чтобы не держать уничтоженные материалы.</summary>
        public static void Reset() => _instance = null;

        readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        readonly Shader _lit, _particles;
        public readonly bool IsUrp;

        string ColorProp => IsUrp ? "_BaseColor" : "_Color";
        string TexProp => IsUrp ? "_BaseMap" : "_MainTex";
        string SmoothProp => IsUrp ? "_Smoothness" : "_Glossiness";

        MaterialLibrary()
        {
            IsUrp = GraphicsSettings.currentRenderPipeline != null;
            _lit = IsUrp ? Shader.Find("Universal Render Pipeline/Lit") : null;
            if (_lit == null) { _lit = Shader.Find("Standard"); IsUrp = false; }
            _particles = IsUrp ? Shader.Find("Universal Render Pipeline/Particles/Unlit") : Shader.Find("Particles/Standard Unlit");
            if (_particles == null) _particles = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        }

        public IEnumerable<KeyValuePair<string, Material>> AllMaterials => _materials;

        /// <summary>Принять материал из запечённой сцены, чтобы переключение день/ночь меняло именно его.</summary>
        public void Adopt(string key, Material m)
        {
            if (m != null) _materials[key] = m;
        }

        public bool TryGet(string key, out Material m) => _materials.TryGetValue(key, out m);

        // ---------------- Текстуры ----------------

        public Texture2D Texture(string name)
        {
            if (_textures.TryGetValue(name, out var t)) return t;
            switch (name)
            {
                case "asphalt": t = ProceduralTextures.Asphalt(); break;
                case "sidewalk": t = ProceduralTextures.SidewalkTiles(); break;
                case "pavers": t = ProceduralTextures.Pavers(); break;
                case "brick": t = ProceduralTextures.Brick(); break;
                case "plaster": t = ProceduralTextures.Plaster(); break;
                case "concrete": t = ProceduralTextures.Concrete(); break;
                case "corrugated": t = ProceduralTextures.Corrugated(); break;
                case "grass": t = ProceduralTextures.Grass(); break;
                case "wood": t = ProceduralTextures.Wood(); break;
                case "parquet": t = ProceduralTextures.Parquet(); break;
                case "tiles": t = ProceduralTextures.FloorTiles(); break;
                case "carpet": t = ProceduralTextures.Carpet(); break;
                case "gravel": t = ProceduralTextures.RoofGravel(); break;
                case "leaves": t = ProceduralTextures.Leaves(); break;
                case "glass": t = ProceduralTextures.GlassReflection(); break;
                case "litwindow": t = ProceduralTextures.LitWindow(); break;
                case "windowband": t = ProceduralTextures.WindowBand(); break;
                default: return null;
            }
            _textures[name] = t;
            return t;
        }

        // ---------------- Создание материалов ----------------

        Material NewMaterial(string key, Shader shader)
        {
            var m = new Material(shader) { name = "CB_" + key, enableInstancing = true };
            _materials[key] = m;
            return m;
        }

        /// <summary>Непрозрачный PBR-материал. tile — размер повтора текстуры в метрах.</summary>
        public Material Lit(string key, Color color, string texture = null, float tile = 1f, float smoothness = 0.15f, float metallic = 0f)
        {
            if (_materials.TryGetValue(key, out var m)) return m;
            m = NewMaterial(key, _lit);
            m.SetColor(ColorProp, color);
            m.SetFloat(SmoothProp, smoothness);
            m.SetFloat("_Metallic", metallic);
            if (texture != null)
            {
                var tex = Texture(texture);
                m.SetTexture(TexProp, tex);
                m.SetTextureScale(TexProp, new Vector2(1f / tile, 1f / tile));
            }
            return m;
        }

        public Material Emissive(string key, Color baseColor, Color emission, string texture = null)
        {
            if (_materials.TryGetValue(key, out var m)) return m;
            m = Lit(key, baseColor, texture, 1f, 0.5f);
            if (texture != null)
            {
                m.SetTexture("_EmissionMap", Texture(texture));
                m.SetTextureScale(TexProp, Vector2.one);
            }
            SetEmission(m, emission);
            return m;
        }

        public void SetEmission(Material m, Color emission)
        {
            if (m == null) return;
            bool on = emission.maxColorComponent > 0.001f;
            if (on) m.EnableKeyword("_EMISSION"); else m.DisableKeyword("_EMISSION");
            m.globalIlluminationFlags = on ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            m.SetColor("_EmissionColor", emission);
        }

        /// <summary>Прозрачное стекло витрин.</summary>
        public Material Glass(string key, Color tint, float smoothness = 0.95f)
        {
            if (_materials.TryGetValue(key, out var m)) return m;
            m = NewMaterial(key, _lit);
            m.SetColor(ColorProp, tint);
            m.SetFloat(SmoothProp, smoothness);
            m.SetFloat("_Metallic", 0.1f);
            if (IsUrp)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_AlphaClip", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetShaderPassEnabled("DepthOnly", false);
                m.SetShaderPassEnabled("ShadowCaster", false);
            }
            else
            {
                m.SetFloat("_Mode", 3f);
                m.SetInt("_SrcBlend", (int)BlendMode.One);
                m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                m.SetOverrideTag("RenderType", "Transparent");
            }
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        public Material Particles(string key, Color color)
        {
            if (_materials.TryGetValue(key, out var m)) return m;
            if (_particles == null) return Lit(key, color);
            m = NewMaterial(key, _particles);
            m.SetColor(IsUrp ? "_BaseColor" : "_Color", color);
            if (IsUrp)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
            }
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        public void SetColor(Material m, Color c) { if (m != null) m.SetColor(ColorProp, c); }
        public void SetSmoothness(Material m, float s) { if (m != null) m.SetFloat(SmoothProp, s); }

        // ---------------- Палитра проекта ----------------

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        public Material Asphalt => Lit("asphalt", new Color(0.36f, 0.36f, 0.38f), "asphalt", 6f, 0.12f);
        public Material AsphaltParking => Lit("asphalt_parking", new Color(0.3f, 0.3f, 0.32f), "asphalt", 6f, 0.1f);
        public Material Sidewalk => Lit("sidewalk", new Color(0.78f, 0.76f, 0.72f), "sidewalk", 2.4f, 0.1f);
        public Material Pavers => Lit("pavers", new Color(0.75f, 0.62f, 0.52f), "pavers", 2f, 0.1f);
        public Material Curb => Lit("curb", new Color(0.62f, 0.62f, 0.63f), "concrete", 2f, 0.2f);
        public Material Grass => Lit("grass", new Color(0.36f, 0.55f, 0.24f), "grass", 4f, 0.05f);
        public Material Soil => Lit("soil", new Color(0.3f, 0.22f, 0.16f), "gravel", 1.5f, 0.02f);
        public Material RoadPaint => Lit("road_paint", new Color(0.92f, 0.92f, 0.88f), null, 1f, 0.3f);
        public Material RoadPaintYellow => Lit("road_paint_y", new Color(0.95f, 0.75f, 0.15f), null, 1f, 0.3f);
        public Material Roof => Lit("roof", new Color(0.42f, 0.42f, 0.44f), "gravel", 3f, 0.05f);
        public Material MetalDark => Lit("metal_dark", new Color(0.12f, 0.13f, 0.14f), null, 1f, 0.55f, 0.8f);
        public Material Aluminium => Lit("aluminium", new Color(0.55f, 0.57f, 0.6f), null, 1f, 0.6f, 0.9f);
        public Material TrimWhite => Lit("trim_white", new Color(0.92f, 0.9f, 0.86f), "plaster", 2f, 0.2f);
        public Material WindowGlass => Lit("window_glass", new Color(0.32f, 0.4f, 0.48f), "glass", 1f, 0.95f, 0.4f);
        public Material StoreGlass => Glass("store_glass", new Color(0.6f, 0.75f, 0.8f, 0.22f));
        public Material WindowLit(int group) => Emissive("window_lit_" + group, new Color(0.9f, 0.8f, 0.6f), new Color(1.25f, 1.0f, 0.65f) * (0.75f + 0.2f * group), "litwindow");
        public Material Wood => Lit("wood", new Color(0.55f, 0.38f, 0.24f), "wood", 1.5f, 0.35f);
        public Material Dark => Lit("dark", new Color(0.08f, 0.08f, 0.09f), null, 1f, 0.3f);
        public Material Concrete => Lit("concrete", new Color(0.66f, 0.65f, 0.63f), "concrete", 4f, 0.1f);
        public Material Bark => Lit("bark", new Color(0.32f, 0.25f, 0.19f), "wood", 1f, 0.1f);
        public Material Leaves(int i)
        {
            var colors = new[] { new Color(0.27f, 0.45f, 0.18f), new Color(0.33f, 0.5f, 0.2f), new Color(0.22f, 0.38f, 0.2f) };
            return Lit("leaves_" + i, colors[i % colors.Length], "leaves", 2f, 0.05f);
        }
        public Material LampLens => Emissive("lamp_lens", new Color(0.95f, 0.9f, 0.8f), Color.black);
        public Material SignLight => Emissive("sign_light", Color.white, new Color(1.6f, 1.55f, 1.4f));
        public Material Headlight => Emissive("headlight", new Color(0.95f, 0.95f, 0.9f), Color.black);
        public Material Taillight => Emissive("taillight", new Color(0.6f, 0.05f, 0.05f), new Color(0.4f, 0f, 0f));
        public Material CarGlass => Lit("car_glass", new Color(0.12f, 0.15f, 0.18f), "glass", 2f, 0.95f, 0.3f);
        public Material Tire => Lit("tire", new Color(0.07f, 0.07f, 0.07f), null, 1f, 0.15f);

        public Material Paint(string family, Color c, string texture = null, float tile = 2f, float smoothness = 0.15f) =>
            Lit(family + "_" + Hex(c), c, texture, tile, smoothness);

        public Material Emission(string key, Color c, float intensity) => Emissive(key, c, c * intensity);
    }
}

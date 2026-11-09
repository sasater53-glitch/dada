using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>
    /// Единая библиотека материалов. Работает и с URP (основной вариант), и со встроенным конвейером.
    /// Материалы кешируются по ключу — одинаковые детали используют один материал (SRP Batcher / GPU Instancing).
    /// Поверхности ТЦ используют PBR-текстуры Poly Haven (CC0) из Resources/PolyHaven/Textures.
    /// </summary>
    public sealed class MaterialLibrary
    {
        static MaterialLibrary _instance;
        public static MaterialLibrary Instance => _instance ?? (_instance = new MaterialLibrary());

        /// <summary>
        /// Сброс при выходе из Play Mode, чтобы не держать уничтоженные материалы.
        /// Материалы-ассеты запечённой сцены возвращаются к исходным значениям (ночь/погода меняют их во время игры).
        /// </summary>
        public static void Reset()
        {
            _instance?.RestoreAdopted();
            _instance = null;
        }

        readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        readonly Shader _lit, _particles, _unlit;
        public readonly bool IsUrp;

        public string ColorProp => IsUrp ? "_BaseColor" : "_Color";
        public string TexProp => IsUrp ? "_BaseMap" : "_MainTex";
        string SmoothProp => IsUrp ? "_Smoothness" : "_Glossiness";

        MaterialLibrary()
        {
            IsUrp = GraphicsSettings.currentRenderPipeline != null;
            _lit = IsUrp ? Shader.Find("Universal Render Pipeline/Lit") : null;
            if (_lit == null) { _lit = Shader.Find("Standard"); IsUrp = false; }
            _particles = IsUrp ? Shader.Find("Universal Render Pipeline/Particles/Unlit") : Shader.Find("Particles/Standard Unlit");
            if (_particles == null) _particles = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            _unlit = IsUrp ? Shader.Find("Universal Render Pipeline/Unlit") : Shader.Find("Unlit/Texture");
        }

        public IEnumerable<KeyValuePair<string, Material>> AllMaterials => _materials;

        readonly List<(Material live, Material snapshot)> _adopted = new List<(Material, Material)>();

        public void Adopt(string key, Material m)
        {
            if (m == null) return;
            _materials[key] = m;
            foreach (var a in _adopted) if (a.live == m) return;
            // Копия исходных значений: в редакторе изменения материала-ассета в Play Mode иначе сохранились бы на диск
            _adopted.Add((m, new Material(m) { name = m.name + "_snapshot", hideFlags = HideFlags.HideAndDontSave }));
        }

        void RestoreAdopted()
        {
            foreach (var (live, snapshot) in _adopted)
            {
                if (live != null && snapshot != null) live.CopyPropertiesFromMaterial(snapshot);
                if (snapshot != null) Object.DestroyImmediate(snapshot);
            }
            _adopted.Clear();
        }

        public bool TryGet(string key, out Material m) => _materials.TryGetValue(key, out m);

        // ---------------- Текстуры ----------------

        /// <summary>Процедурные текстуры — только как запасной вариант, если ассет не найден.</summary>
        public Texture2D Procedural(string name)
        {
            if (_textures.TryGetValue("proc_" + name, out var t)) return t;
            switch (name)
            {
                case "concrete": t = ProceduralTextures.Concrete(); break;
                case "plaster": t = ProceduralTextures.Plaster(); break;
                case "tiles": t = ProceduralTextures.FloorTiles(); break;
                case "glass": t = ProceduralTextures.GlassReflection(); break;
                case "carpet": t = ProceduralTextures.Carpet(); break;
                default: return null;
            }
            _textures["proc_" + name] = t;
            return t;
        }

        public Texture2D Load(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath)) return null;
            if (_textures.TryGetValue(resourcePath, out var t)) return t;
            t = Resources.Load<Texture2D>(resourcePath);
            _textures[resourcePath] = t;
            return t;
        }

        // ---------------- Создание материалов ----------------

        Material NewMaterial(string key, Shader shader)
        {
            var m = new Material(shader) { name = "CB_" + key, enableInstancing = true };
            _materials[key] = m;
            return m;
        }

        public Material Lit(string key, Color color, Texture2D albedo = null, float tile = 1f, float smoothness = 0.15f, float metallic = 0f, Texture2D normal = null, float normalScale = 1f)
        {
            if (_materials.TryGetValue(key, out var m)) return m;
            m = NewMaterial(key, _lit);
            m.SetColor(ColorProp, color);
            m.SetFloat(SmoothProp, smoothness);
            m.SetFloat("_Metallic", metallic);
            if (albedo != null)
            {
                m.SetTexture(TexProp, albedo);
                if (!IsUrp) m.SetTexture("_MainTex", albedo);
                m.SetTextureScale(TexProp, new Vector2(1f / tile, 1f / tile));
            }
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", normalScale);
                m.EnableKeyword("_NORMALMAP");
            }
            return m;
        }

        /// <summary>PBR-поверхность Poly Haven. tile — метров на повтор текстуры (UV мешей — в метрах).</summary>
        public Material Surface(string polyHavenId, float tile, Color tint, float smoothness, float metallic = 0f)
        {
            string key = "ph_" + polyHavenId + "_" + tile + "_" + MaterialLibraryHex(tint);
            if (_materials.TryGetValue(key, out var m)) return m;
            var albedo = Load("PolyHaven/Textures/" + polyHavenId + "_diff");
            var normal = Load("PolyHaven/Textures/" + polyHavenId + "_nor_gl");
            if (albedo == null) albedo = Procedural("concrete");
            return Lit(key, tint, albedo, tile, smoothness, metallic, normal);
        }

        static string MaterialLibraryHex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        public Material Emissive(string key, Color baseColor, Color emission, Texture2D texture = null)
        {
            if (_materials.TryGetValue(key, out var m)) return m;
            m = Lit(key, baseColor, texture, 1f, 0.5f);
            if (texture != null) m.SetTexture("_EmissionMap", texture);
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

        public void MakeTransparent(Material m)
        {
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
        }

        /// <summary>Альфа-срез (листья растений), двусторонний в URP.</summary>
        public void MakeCutout(Material m)
        {
            if (IsUrp)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.45f);
                m.SetFloat("_Cull", 0f);
                m.EnableKeyword("_ALPHATEST_ON");
            }
            else
            {
                m.SetFloat("_Mode", 1f);
                m.SetFloat("_Cutoff", 0.45f);
                m.EnableKeyword("_ALPHATEST_ON");
            }
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)RenderQueue.AlphaTest;
        }

        public Material Glass(string key, Color tint, float smoothness = 0.95f)
        {
            if (_materials.TryGetValue(key, out var m)) return m;
            m = NewMaterial(key, _lit);
            m.SetColor(ColorProp, tint);
            m.SetFloat(SmoothProp, smoothness);
            m.SetFloat("_Metallic", 0.1f);
            MakeTransparent(m);
            return m;
        }

        public Material Unlit(string key, Color color, Texture2D tex = null)
        {
            if (_materials.TryGetValue(key, out var m)) return m;
            m = NewMaterial(key, _unlit != null ? _unlit : _lit);
            m.SetColor(ColorProp, color);
            if (!IsUrp) m.color = color;
            if (tex != null) m.SetTexture(TexProp, tex);
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

        public Color GetColor(Material m)
        {
            if (m == null) return Color.white;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.white;
        }

        public Texture GetAlbedo(Material m)
        {
            if (m == null) return null;
            if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) return m.GetTexture("_BaseMap");
            if (m.HasProperty("_MainTex")) return m.GetTexture("_MainTex");
            return null;
        }

        // ---------------- Палитра ТЦ ----------------

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        public Material MallFloor => Surface("marble_tiles", 2.5f, new Color(0.95f, 0.94f, 0.92f), 0.75f);
        public Material WalkwayFloor => Surface("terrazzo_tiles", 2.2f, Color.white, 0.6f);
        public Material FoodCourtFloor => Surface("large_grey_tiles", 2.5f, new Color(0.9f, 0.9f, 0.88f), 0.5f);
        public Material Wall => Surface("white_plaster_02", 3f, new Color(0.95f, 0.94f, 0.92f), 0.1f);
        public Material WallDark => Surface("white_plaster_02", 3f, new Color(0.25f, 0.27f, 0.3f), 0.15f);
        public Material Ceiling => Surface("ceiling_interior", 3f, Color.white, 0.05f);
        public Material ServiceFloor => Surface("concrete_floor_worn_001", 3f, new Color(0.85f, 0.85f, 0.83f), 0.2f);
        public Material MetalPanel => Surface("metal_plate", 2f, new Color(0.75f, 0.77f, 0.8f), 0.6f, 0.8f);
        public Material ColumnStone => Surface("marble_01", 2f, Color.white, 0.8f);
        public Material Aluminium => Lit("aluminium", new Color(0.62f, 0.64f, 0.67f), null, 1f, 0.65f, 0.9f);
        public Material MetalDark => Lit("metal_dark", new Color(0.12f, 0.13f, 0.14f), null, 1f, 0.55f, 0.8f);
        public Material Dark => Lit("dark", new Color(0.06f, 0.06f, 0.07f), null, 1f, 0.3f);
        public Material Rubber => Lit("rubber", new Color(0.04f, 0.04f, 0.04f), null, 1f, 0.35f);
        public Material TrimWhite => Lit("trim_white", new Color(0.93f, 0.93f, 0.91f), null, 1f, 0.35f);
        public Material StoreGlass => Glass("store_glass", new Color(0.65f, 0.78f, 0.82f, 0.16f));
        public Material RailGlass => Glass("rail_glass", new Color(0.6f, 0.8f, 0.8f, 0.22f));
        public Material SkyGlass => Glass("sky_glass", new Color(0.75f, 0.85f, 0.9f, 0.12f), 0.98f);
        public Material CeilingLight => Emissive("ceiling_light", new Color(1f, 0.98f, 0.94f), new Color(2.4f, 2.3f, 2.1f));
        public Material Water => Lit("water", new Color(0.25f, 0.5f, 0.6f), null, 1f, 0.97f, 0.1f);
        public Material Paper => Lit("paper", new Color(0.97f, 0.97f, 0.95f), null, 1f, 0.1f);

        public Material Paint(string family, Color c, float smoothness = 0.25f) =>
            Lit(family + "_" + Hex(c), c, null, 1f, smoothness);

        public Material Emission(string key, Color c, float intensity) => Emissive(key, c, c * intensity);
    }
}

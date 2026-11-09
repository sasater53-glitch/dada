using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>Как приводить модель к нужному размеру.</summary>
    public enum FitAxis { Height, Width, Depth, Longest }

    /// <summary>
    /// Загружает сторонние модели (Kenney, Poly Haven) из Resources, приводит их к метрическому размеру,
    /// ставит опорную точку в центр основания и заменяет материалы на материалы текущего конвейера
    /// (иначе импорт под встроенный конвейер даёт «розовые» материалы в URP).
    /// Подготовленный шаблон каждой модели создаётся один раз и затем клонируется.
    /// </summary>
    public static class AssetCatalog
    {
        public const string Furniture = "Kenney/Furniture/";
        public const string Food = "Kenney/Food/";
        public const string Characters = "Kenney/Characters/";
        public const string PolyModels = "PolyHaven/Models/";

        sealed class Template
        {
            public GameObject Root;   // неактивный, масштаб и опора уже настроены
            public Bounds Bounds;     // в координатах корня (после нормализации)
            public bool Missing;
        }

        static readonly Dictionary<string, Template> Templates = new Dictionary<string, Template>();
        static readonly Dictionary<Material, Material> Fixed = new Dictionary<Material, Material>();
        static Transform _templateRoot;
        static readonly HashSet<string> Warned = new HashSet<string>();

        public static void Reset()
        {
            Templates.Clear();
            Fixed.Clear();
            _templateRoot = null;
        }

        /// <summary>Удаляет скрытые шаблоны моделей (после запекания они не должны попасть в сцену).</summary>
        public static void DestroyTemplates()
        {
            if (_templateRoot != null)
            {
                if (Application.isPlaying) Object.Destroy(_templateRoot.gameObject);
                else Object.DestroyImmediate(_templateRoot.gameObject);
            }
            Reset();
        }

        static Transform TemplateRoot
        {
            get
            {
                if (_templateRoot == null)
                {
                    var go = new GameObject("[AssetTemplates]");
                    go.SetActive(false);
                    _templateRoot = go.transform;
                }
                return _templateRoot;
            }
        }

        public static string PolyHaven(string id) => PolyModels + id + "/" + id + "_1k";

        static string Key(string path, float size, FitAxis axis) => path + "|" + size.ToString("0.###") + "|" + axis;

        /// <summary>Есть ли модель в проекте (для выбора запасного варианта).</summary>
        public static bool Exists(string path) => Resources.Load<GameObject>(path) != null;

        static Template Get(string path, float size, FitAxis axis)
        {
            string key = Key(path, size, axis);
            if (Templates.TryGetValue(key, out var t)) return t;
            t = new Template();
            var prefab = Resources.Load<GameObject>(path);
            GameObject model;
            if (prefab == null)
            {
                if (Warned.Add(path)) Debug.LogWarning("[CityBiz] Модель не найдена в Resources: " + path + " — используется заглушка.");
                model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(model.GetComponent<Collider>());
                model.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Instance.Paint("missing", new Color(0.8f, 0.3f, 0.8f));
                t.Missing = true;
            }
            else
            {
                model = Object.Instantiate(prefab);
            }
            model.name = "Model";
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            FixMaterials(model, path);

            var root = new GameObject(System.IO.Path.GetFileName(path));
            root.transform.SetParent(TemplateRoot, false);
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            var raw = LocalBounds(root.transform, model);
            float dim;
            switch (axis)
            {
                case FitAxis.Width: dim = raw.size.x; break;
                case FitAxis.Depth: dim = raw.size.z; break;
                case FitAxis.Longest: dim = Mathf.Max(raw.size.x, raw.size.y, raw.size.z); break;
                default: dim = raw.size.y; break;
            }
            float scale = dim > 0.0001f && size > 0f ? size / dim : 1f;
            model.transform.localScale *= scale;
            model.transform.localPosition = new Vector3(-raw.center.x * scale, -raw.min.y * scale, -raw.center.z * scale);
            t.Bounds = new Bounds(new Vector3(0, raw.size.y * scale * 0.5f, 0), raw.size * scale);
            t.Root = root;
            Templates[key] = t;
            return t;
        }

        /// <summary>Границы всех мешей модели в координатах root (работает и для неактивных объектов).</summary>
        static Bounds LocalBounds(Transform root, GameObject model)
        {
            bool any = false;
            var b = new Bounds();
            var toRoot = root.worldToLocalMatrix;
            void Encapsulate(Mesh mesh, Transform t)
            {
                if (mesh == null) return;
                var m = toRoot * t.localToWorldMatrix;
                var mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true)) Encapsulate(mf.sharedMesh, mf.transform);
            foreach (var sm in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Encapsulate(sm.sharedMesh, sm.transform);
            return any ? b : new Bounds(Vector3.up * 0.5f, Vector3.one);
        }

        /// <summary>Создаёт экземпляр модели: опора — центр основания, размер по оси axis равен size метрам.</summary>
        public static GameObject Spawn(string path, Transform parent, Vector3 localPos, float yaw, float size, FitAxis axis = FitAxis.Height, bool collider = false)
        {
            var t = Get(path, size, axis);
            var go = Object.Instantiate(t.Root, parent, false);
            go.name = t.Root.name;
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            if (collider)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.center = t.Bounds.center;
                bc.size = t.Bounds.size;
            }
            return go;
        }

        public static Bounds SizeOf(string path, float size, FitAxis axis = FitAxis.Height) => Get(path, size, axis).Bounds;

        /// <summary>Добавляет модель в GPU-инстансинг (для массовых повторяющихся объектов: скамейки, кадки, столы фудкорта).</summary>
        public static void AddInstanced(InstancedRenderer ir, string path, Matrix4x4 world, float size, FitAxis axis = FitAxis.Height, bool shadows = true, float maxDistance = 120f)
        {
            var t = Get(path, size, axis);
            var rootInv = t.Root.transform.worldToLocalMatrix;
            foreach (var mf in t.Root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null) continue;
                ir.Add(mf.sharedMesh, mr.sharedMaterials, world * rootInv * mf.transform.localToWorldMatrix, shadows, maxDistance);
            }
        }

        // ---------------- Материалы ----------------

        static void FixMaterials(GameObject model, string path)
        {
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = Convert(mats[i], path);
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On;
            }
        }

        static string Clean(string name) => name.Replace(" (Instance)", "").Trim();

        /// <summary>
        /// Переводит импортированный материал в материал текущего конвейера: берёт цвет и текстуры исходника,
        /// а для моделей Poly Haven — подбирает текстуры по имени материала (diff / nor_gl).
        /// </summary>
        static Material Convert(Material src, string path)
        {
            var lib = MaterialLibrary.Instance;
            if (src == null) return lib.Paint("model_default", new Color(0.7f, 0.7f, 0.7f));
            if (Fixed.TryGetValue(src, out var done)) return done;

            string name = Clean(src.name);
            string lower = name.ToLowerInvariant();
            Color color = lib.GetColor(src);
            var albedo = lib.GetAlbedo(src) as Texture2D;
            Texture2D normal = src.HasProperty("_BumpMap") ? src.GetTexture("_BumpMap") as Texture2D : null;

            // Kenney Food / Characters: единая палитра colormap
            if (path.StartsWith(Food) || path.StartsWith(Characters))
            {
                if (albedo == null) albedo = lib.Load(System.IO.Path.GetDirectoryName(path).Replace('\\', '/') + "/Textures/colormap");
                color = Color.white;
            }

            bool cutout = false;
            if (path.StartsWith(PolyModels))
            {
                string folder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/') + "/textures/";
                string id = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
                var diff = lib.Load(folder + name + "_diff_1k") ?? lib.Load(folder + id + "_diff_1k");
                if (diff != null) { albedo = diff; color = Color.white; }
                var nor = lib.Load(folder + name + "_nor_gl_1k") ?? (diff != null ? lib.Load(folder + id + "_nor_gl_1k") : null);
                if (nor != null) normal = nor;
                cutout = lower.Contains("leaves") || lower.Contains("leaf");
            }

            Material result;
            if (lower == "glass" || lower.EndsWith("_glass"))
                result = lib.Glass("model_glass", new Color(0.7f, 0.85f, 0.9f, 0.25f));
            else if (lower == "lamp" || lower.StartsWith("lamps_"))
                result = lib.Emissive("model_lamp", new Color(1f, 0.95f, 0.85f), new Color(2.2f, 2.0f, 1.7f));
            else
            {
                float smooth = lower.Contains("metal") ? 0.6f : lower.Contains("wood") ? 0.3f : lower.Contains("carpet") || lower.Contains("fur") ? 0.05f : 0.25f;
                float metallic = lower.Contains("metal") ? 0.7f : 0f;
                // Одинаковые цвета/текстуры у разных моделей дают общий материал — меньше переключений состояния
                string key = albedo == null
                    ? $"model_{lower}_{MaterialLibrary.Hex(color)}"
                    : $"model_tex_{albedo.name}_{(normal != null ? normal.name : "-")}_{MaterialLibrary.Hex(color)}_{cutout}";
                result = lib.Lit(key, color, albedo, 1f, smooth, metallic, normal);
                if (albedo != null) result.SetTextureScale(lib.TexProp, Vector2.one);
                if (cutout) lib.MakeCutout(result);
            }
            Fixed[src] = result;
            return result;
        }
    }
}

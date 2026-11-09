using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;

namespace CityBiz.Game
{
    /// <summary>Точки интерьера для персонала и покупателей (x, y, z, yaw) в координатах помещения.</summary>
    public sealed class InteriorInfo : MonoBehaviour
    {
        public List<Vector4> WorkPoints = new List<Vector4>();
        public List<Vector4> CustomerPoints = new List<Vector4>();
    }

    /// <summary>
    /// Интерьеры помещений из моделей Kenney (мебель, еда — CC0) и Poly Haven (стеллажи, растения, люстры — CC0).
    /// Уровни развития видны глазами:
    ///  • интерьер — покрытие пола, растения, ковры, мягкая мебель, люстры;
    ///  • оборудование — число касс/стоек/кофемашин/плит, длина прилавка, рабочие места персонала;
    ///  • ассортимент — число стеллажей и плотность товара на полках.
    /// Мелкие товары рисуются GPU-инстансингом (один вызов на тип товара).
    /// </summary>
    public static class ShopInteriorBuilder
    {
        public struct Spec
        {
            public PropertyKind Kind;
            public BusinessType Type;
            public float Width, Depth, Height;
            public int Interior, Equipment, Assortment, Seed;
        }

        sealed class Ctx
        {
            public Transform Root;
            public InstancedRenderer Products;
            public InteriorInfo Info;
            public Spec S;
            public DeterministicRandom Rng;
            public float Hw, D;
            public MaterialLibrary M;

            public GameObject Spawn(string path, Vector3 pos, float yaw, float size, FitAxis axis = FitAxis.Height, bool collider = false) =>
                AssetCatalog.Spawn(path, Root, pos, yaw, size, axis, collider);

            public void Product(string path, Vector3 pos, float yaw, float size, FitAxis axis = FitAxis.Longest) =>
                AssetCatalog.AddInstanced(Products, path, Root.localToWorldMatrix * Matrix4x4.TRS(pos, Quaternion.Euler(0, yaw, 0), Vector3.one), size, axis, false, 40f);

            public void Work(float x, float z, float yaw) => Info.WorkPoints.Add(new Vector4(x, 0, z, yaw));
            public void Customer(float x, float z, float yaw) => Info.CustomerPoints.Add(new Vector4(x, 0, z, yaw));

            public void Box(Vector3 center, Vector3 size)
            {
                var bc = Root.gameObject.AddComponent<BoxCollider>();
                bc.center = center;
                bc.size = size;
            }
        }

        const string F = AssetCatalog.Furniture, Fd = AssetCatalog.Food;
        static string PH(string id) => AssetCatalog.PolyHaven(id);

        static readonly string[] Groceries = { "apple", "banana", "orange", "bread", "loaf-baguette", "carton", "carton-small", "can", "soda-bottle", "soda-can", "cheese", "bottle-ketchup", "bottle-oil", "tomato", "carrot", "chocolate", "candy-bar", "cookie", "wine-red", "wine-white" };
        static readonly string[] Pastry = { "croissant", "donut", "donut-chocolate", "donut-sprinkles", "cupcake", "muffin", "cookie", "cake" };
        static readonly string[] FastFoodItems = { "burger", "burger-cheese", "fries", "hot-dog", "pizza", "sandwich", "taco", "sushi-salmon", "maki-salmon", "soda" };
        static readonly string[] Produce = { "apple", "banana", "orange", "watermelon", "pineapple", "tomato", "carrot", "broccoli", "cabbage" };

        public static InteriorInfo Build(Transform parent, Spec s)
        {
            var root = new GameObject("Content_" + s.Type);
            root.transform.SetParent(parent, false);
            var c = new Ctx
            {
                Root = root.transform, S = s, Rng = new DeterministicRandom((ulong)(s.Seed * 977 + (int)s.Type * 31 + s.Interior * 7 + s.Equipment * 3 + s.Assortment)),
                Hw = s.Width * 0.5f - 0.4f, D = s.Depth - 0.4f, M = MaterialLibrary.Instance,
            };
            c.Info = root.AddComponent<InteriorInfo>();
            c.Products = root.AddComponent<InstancedRenderer>();

            if (s.Kind != PropertyKind.Kiosk) Floor(c);
            switch (s.Type)
            {
                case BusinessType.Cafe: Cafe(c); break;
                case BusinessType.Restaurant: Restaurant(c); break;
                case BusinessType.FastFood: FastFood(c); break;
                case BusinessType.IceCream: IceCream(c); break;
                case BusinessType.ClothingStore: Clothing(c); break;
                case BusinessType.Electronics: Electronics(c); break;
                case BusinessType.Toys: Toys(c); break;
                case BusinessType.Cosmetics: Cosmetics(c); break;
                case BusinessType.Bookstore: Bookstore(c); break;
                case BusinessType.Jewelry: Jewelry(c); break;
                case BusinessType.Supermarket: Supermarket(c); break;
                case BusinessType.Warehouse: Warehouse(c); break;
                default: Vacant(c); break;
            }
            if (s.Type != BusinessType.None && s.Type != BusinessType.Warehouse && s.Kind != PropertyKind.Kiosk) Decor(c);
            if (c.Info.CustomerPoints.Count == 0) c.Customer(0, Mathf.Min(2f, c.D * 0.5f), 0f);
            return c.Info;
        }

        // ---------------- Общие элементы ----------------

        static readonly string[][] FloorsByLevel =
        {
            new[] { "concrete_floor_worn_001", "laminate_floor", "large_grey_tiles", "long_white_tiles", "marble_01" },     // ритейл
            new[] { "square_tiles", "laminate_floor", "wood_floor", "herringbone_parquet", "herringbone_parquet" },        // общепит
        };

        static void Floor(Ctx c)
        {
            var s = c.S;
            if (s.Type == BusinessType.None || s.Type == BusinessType.Warehouse) return;
            int level = Mathf.Clamp(s.Interior, 1, 5) - 1;
            string id = FloorsByLevel[EconomyCatalog.Get(s.Type).IsFood ? 1 : 0][level];
            var mat = c.M.Surface(id, id.Contains("parquet") || id.Contains("wood") || id.Contains("laminate") ? 2f : 2.5f, Color.white, 0.25f + 0.1f * level);
            var mb = new MeshBuilder();
            float hw = s.Width * 0.5f - 0.1f, d = s.Depth - 0.1f;
            mb.AddQuad(0, new Vector3(-hw, 0.006f, 0.3f), new Vector3(-hw, 0.006f, d), new Vector3(hw, 0.006f, d), new Vector3(hw, 0.006f, 0.3f),
                new Vector2(-hw, 0.3f), new Vector2(-hw, d), new Vector2(hw, d), new Vector2(hw, 0.3f));
            // Акцентная стена (цвет бренда) — с ростом интерьера становится выразительнее
            var accent = c.M.Paint("accent_wall", Color.Lerp(new Color(0.85f, 0.84f, 0.82f), StorefrontView.BoardColor(s.Type, s.Interior), 0.15f + 0.12f * level));
            mb.AddQuad(1, new Vector3(-hw + 0.02f, 0, d - 0.02f), new Vector3(-hw + 0.02f, s.Height - 0.02f, d - 0.02f), new Vector3(hw - 0.02f, s.Height - 0.02f, d - 0.02f), new Vector3(hw - 0.02f, 0, d - 0.02f));
            var go = new GameObject("Finish");
            go.transform.SetParent(c.Root, false);
            GeneratedMesh.Assign(go, mb.Build("Finish"));
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { mat, accent };
        }

        /// <summary>Прилавок из секций kitchenBar длиной length, центр в (x, z), обращён к покупателям (−Z).</summary>
        static float Counter(Ctx c, float x, float z, float length, float yaw = 180f)
        {
            float seg = Mathf.Max(0.3f, AssetCatalog.SizeOf(F + "kitchenBar", 1.0f).size.x);
            int n = Mathf.Max(1, Mathf.RoundToInt(length / seg));
            var dir = Quaternion.Euler(0, yaw + 180f, 0) * Vector3.right;
            for (int i = 0; i < n; i++)
            {
                var p = new Vector3(x, 0, z) + dir * ((i - (n - 1) * 0.5f) * seg);
                c.Spawn(F + "kitchenBar", p, yaw, 1.0f);
            }
            var size = Quaternion.Euler(0, yaw, 0) * new Vector3(n * seg, 1.1f, 0.7f);
            c.Box(new Vector3(x, 0.55f, z), new Vector3(Mathf.Abs(size.x), 1.1f, Mathf.Abs(size.z)));
            return n * seg;
        }

        static void Register(Ctx c, float x, float z, float yaw = 180f)
        {
            c.Spawn(F + "computerScreen", new Vector3(x, 1.0f, z), yaw, 0.38f);
        }

        /// <summary>Стеллаж Poly Haven с товарами на полках. Возвращает число размещённых товаров.</summary>
        static void Shelf(Ctx c, Vector3 pos, float yaw, string[] items, float density, bool wooden = false, float itemSize = 0.22f)
        {
            string model = wooden ? PH("wooden_display_shelves_01") : PH("steel_frame_shelves_01");
            float height = wooden ? 1.55f : 2.1f;
            var go = c.Spawn(model, pos, yaw, height);
            var b = AssetCatalog.SizeOf(model, height);
            var rot = Quaternion.Euler(0, yaw, 0);
            var size = rot * b.size;
            c.Box(pos + Vector3.up * height * 0.5f, new Vector3(Mathf.Abs(size.x), height, Mathf.Abs(size.z)));
            if (items == null) return;
            float[] levels = wooden ? new[] { 0.08f, 0.52f, 0.98f, 1.42f } : new[] { 0.12f, 0.62f, 1.12f, 1.62f };
            float w = b.size.x - 0.15f;
            int perLevel = Mathf.Max(1, Mathf.RoundToInt(w / (itemSize * 1.25f) * density));
            foreach (float y in levels)
                for (int i = 0; i < perLevel; i++)
                {
                    float t = perLevel == 1 ? 0.5f : i / (float)(perLevel - 1);
                    var local = new Vector3(-w * 0.5f + t * w, y + 0.02f, c.Rng.Range(-0.08f, 0.08f));
                    c.Product(Fd + items[c.Rng.Range(0, items.Length)], pos + rot * local, yaw + c.Rng.Range(-20f, 20f), itemSize);
                }
        }

        static void Plant(Ctx c, Vector3 pos, float height = 1.2f) =>
            c.Spawn(height > 1.3f ? PH("potted_plant_01") : PH("potted_plant_02"), pos, c.Rng.Range(0f, 360f), height, FitAxis.Height, true);

        static void Decor(Ctx c)
        {
            var s = c.S;
            float hw = c.Hw, d = c.D;
            bool small = s.Kind == PropertyKind.FoodStall;
            if (s.Interior >= 2 && !small)
            {
                Plant(c, new Vector3(-hw + 0.5f, 0, 0.9f), 1.0f);
                Plant(c, new Vector3(hw - 0.5f, 0, 0.9f), 1.0f);
            }
            if (s.Interior >= 3 && !small)
            {
                c.Spawn(F + "rugRectangle", new Vector3(0, 0.01f, d * 0.4f), 0f, Mathf.Min(4f, hw * 1.2f), FitAxis.Width);
                Plant(c, new Vector3(hw - 0.6f, 0, d - 0.7f), 1.6f);
            }
            if (s.Interior >= 4 && !small)
            {
                for (float x = -hw + 2f; x < hw - 1f; x += 3.5f)
                    c.Spawn(F + "lampSquareCeiling", new Vector3(x, s.Height - 0.65f, d * 0.45f), 0f, 0.6f);
                c.Spawn(PH("modern_arm_chair_01"), new Vector3(-hw + 0.9f, 0, d - 1.2f), 135f, 0.95f, FitAxis.Height, true);
            }
            if (s.Interior >= 5 && !small && s.Width >= 12f)
            {
                c.Spawn(PH("Chandelier_01"), new Vector3(0, s.Height - 0.85f, d * 0.4f), 0f, 0.8f);
                c.Spawn(PH("Sofa_01"), new Vector3(hw - 1.2f, 0, d * 0.55f), -90f, 1.55f, FitAxis.Longest, true);
            }
        }

        // ---------------- Общепит ----------------

        static void Cafe(Ctx c)
        {
            var s = c.S;
            float hw = c.Hw, d = c.D;
            bool stall = s.Kind == PropertyKind.FoodStall;
            float cz = stall ? 1.0f : d - 2.2f;
            float len = Mathf.Min(hw * 1.6f, 2.2f + s.Equipment * 0.9f);
            float cx = stall ? 0f : -hw + len * 0.5f + 0.4f;
            Counter(c, cx, cz, len);
            int machines = Mathf.Clamp(s.Equipment, 1, 3);
            for (int i = 0; i < machines; i++) c.Spawn(F + "kitchenCoffeeMachine", new Vector3(cx - len * 0.4f + i * 0.7f, 1.0f, cz + 0.12f), 180f, 0.42f);
            Register(c, cx + len * 0.4f, cz, 160f);
            int pastries = 3 + s.Assortment * 3;
            for (int i = 0; i < pastries; i++) c.Product(Fd + Pastry[i % Pastry.Length], new Vector3(cx - len * 0.1f + (i % 6) * 0.18f, 1.03f, cz - 0.15f + (i / 6) * 0.15f), c.Rng.Range(0f, 360f), 0.14f);
            for (int i = 0; i < 4; i++) c.Product(Fd + "cup-coffee", new Vector3(cx + len * 0.2f + i * 0.14f, 1.03f, cz + 0.2f), 0f, 0.1f);
            c.Spawn(F + "kitchenFridge", new Vector3(cx + len * 0.5f + 0.3f > hw ? hw - 0.5f : cx + len * 0.5f + 0.4f, 0, cz + 0.9f), 180f, 1.8f);
            for (int i = 0; i < Mathf.Min(s.Equipment + 1, 4); i++) c.Work(cx - len * 0.3f + i * 0.8f, cz + 0.85f, 180f);
            c.Customer(cx, cz - 0.9f, 0f);
            c.Customer(cx + 0.8f, cz - 1.1f, 0f);
            if (stall)
            {
                c.Spawn(PH("standing_chalkboard_01"), new Vector3(hw - 0.5f, 0, -0.6f), 200f, 1.3f);
                return;
            }
            // Зал: столики, с ростом интерьера — больше мест и уютнее
            int rows = s.Interior >= 3 ? 2 : 1;
            for (int r = 0; r < rows; r++)
                for (float x = -hw + 1.3f; x < hw - 0.8f; x += 2.4f)
                {
                    var t = new Vector3(x, 0, 1.8f + r * 2.6f);
                    if (t.z > cz - 1.8f) continue;
                    c.Spawn(F + "tableRound", t, 0f, 0.75f, FitAxis.Height, true);
                    c.Spawn(F + "chairModernCushion", t + new Vector3(-0.7f, 0, 0), 90f, 0.85f);
                    c.Spawn(F + "chairModernCushion", t + new Vector3(0.7f, 0, 0), -90f, 0.85f);
                    c.Product(Fd + "cup-coffee", t + new Vector3(0.15f, 0.76f, 0), 0f, 0.1f);
                    c.Customer(t.x - 0.7f, t.z, 90f);
                }
            if (s.Interior >= 3) c.Spawn(PH("CoffeeCart_01"), new Vector3(hw - 1.4f, 0, d * 0.5f), -90f, 1.6f, FitAxis.Longest, true);
            c.Spawn(F + "stoolBar", new Vector3(cx - 0.6f, 0, cz - 0.75f), 0f, 0.8f);
        }

        static void Restaurant(Ctx c)
        {
            var s = c.S;
            float hw = c.Hw, d = c.D;
            // Кухня у задней стены: плиты, вытяжки, холодильник
            int stoves = Mathf.Clamp(s.Equipment, 1, 4);
            for (int i = 0; i < stoves; i++)
            {
                float x = -hw + 0.8f + i * 1.1f;
                c.Spawn(F + "kitchenStove", new Vector3(x, 0, d - 0.45f), 180f, 0.95f, FitAxis.Height, true);
                c.Spawn(F + "hoodModern", new Vector3(x, 1.8f, d - 0.35f), 180f, 0.6f);
                c.Work(x, d - 1.2f, 180f);
            }
            c.Spawn(F + "kitchenFridgeLarge", new Vector3(-hw + 0.8f + stoves * 1.1f + 0.3f, 0, d - 0.5f), 180f, 2.0f, FitAxis.Height, true);
            // Бар с вином
            float bx = hw - 1.6f;
            Counter(c, bx, d - 2.2f, 2.4f);
            for (int i = 0; i < 4 + s.Assortment * 2; i++)
                c.Product(Fd + (i % 2 == 0 ? "wine-red" : "wine-white"), new Vector3(bx - 1f + (i % 8) * 0.28f, 1.03f + (i / 8) * 0.01f, d - 1.9f), 0f, 0.3f);
            c.Work(bx, d - 1.5f, 180f);
            c.Work(0, d * 0.5f, 90f);
            // Зал
            for (float z = 1.8f; z < d - 3.5f; z += 2.8f)
                for (float x = -hw + 1.4f; x < hw - 2.6f; x += 2.8f)
                {
                    var t = new Vector3(x, 0, z);
                    c.Spawn(F + "tableCloth", t, 0f, 0.78f, FitAxis.Height, true);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * 90f;
                        var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                        c.Spawn(F + "chairCushion", t + dir * 0.8f, a + 180f, 0.9f);
                    }
                    c.Product(Fd + "plate-dinner", t + new Vector3(0.2f, 0.79f, 0.1f), 0f, 0.26f);
                    c.Product(Fd + (s.Assortment >= 3 ? "pizza" : "salad"), t + new Vector3(-0.15f, 0.8f, -0.1f), 0f, 0.3f);
                    c.Product(Fd + "glass-wine", t + new Vector3(0.25f, 0.79f, -0.25f), 0f, 0.18f, FitAxis.Height);
                    c.Customer(t.x + 0.8f, t.z, -90f);
                }
            if (s.Interior >= 3) c.Spawn(PH("Chandelier_01"), new Vector3(0, s.Height - 0.85f, d * 0.35f), 0f, 0.8f);
        }

        static void FastFood(Ctx c)
        {
            var s = c.S;
            float hw = c.Hw, d = c.D;
            float len = Counter(c, 0, 1.0f, hw * 2f - 0.4f);
            Register(c, -len * 0.3f, 1.0f);
            if (s.Equipment >= 3) Register(c, len * 0.3f, 1.0f);
            int stations = Mathf.Clamp(s.Equipment, 1, 4);
            for (int i = 0; i < stations; i++)
            {
                float x = -hw + 0.7f + i * (hw * 2f - 1.4f) / Mathf.Max(1, stations - 1 + (stations == 1 ? 1 : 0));
                c.Spawn(F + "kitchenStove", new Vector3(x, 0, d - 0.5f), 180f, 0.95f);
                c.Spawn(F + "hoodModern", new Vector3(x, 1.9f, d - 0.35f), 180f, 0.6f);
            }
            c.Spawn(F + "kitchenFridge", new Vector3(hw - 0.4f, 0, d * 0.55f), -90f, 1.8f);
            int items = 3 + s.Assortment * 2;
            for (int i = 0; i < items; i++)
                c.Product(Fd + FastFoodItems[i % FastFoodItems.Length], new Vector3(-len * 0.45f + (i + 0.5f) * len * 0.9f / items, 1.03f, 1.05f), 180f, 0.2f);
            // Меню-панель над кухней
            var menu = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(menu.GetComponent<Collider>());
            menu.transform.SetParent(c.Root, false);
            menu.transform.localPosition = new Vector3(0, 2.6f, d - 0.12f);
            menu.transform.localScale = new Vector3(hw * 1.6f, 0.8f, 0.05f);
            menu.GetComponent<MeshRenderer>().sharedMaterial = c.M.Emissive("menu_board", new Color(0.1f, 0.1f, 0.1f), new Color(0.6f, 0.35f, 0.1f));
            WorldText.Create(c.Root, "Menu", "КОМБО · БУРГЕРЫ · НАПИТКИ", new Vector3(0, 2.6f, d - 0.16f), 0.16f, Color.white, hw * 1.5f, TextAnchor.MiddleCenter, 180f);
            for (int i = 0; i < Mathf.Min(stations + 1, 5); i++) c.Work(-hw + 0.8f + i * 1.2f, 1.9f, 180f);
            c.Customer(-0.6f, -0.6f, 0f);
            c.Customer(0.7f, -0.7f, 0f);
        }

        static void IceCream(Ctx c)
        {
            var s = c.S;
            if (s.Kind == PropertyKind.Kiosk)
            {
                // Кольцевой прилавок вокруг продавца
                float r = s.Width * 0.5f - 0.5f;
                for (int side = 0; side < 4; side++)
                {
                    float yaw = side * 90f;
                    var p = Quaternion.Euler(0, yaw, 0) * new Vector3(0, 0, -r);
                    c.Spawn(F + "kitchenBar", p, yaw + 180f, 1.0f);
                    var size = Quaternion.Euler(0, yaw, 0) * new Vector3(2.2f, 1.1f, 0.6f);
                    c.Box(p + Vector3.up * 0.55f, new Vector3(Mathf.Abs(size.x), 1.1f, Mathf.Abs(size.z)));
                }
                string[] treats = { "ice-cream", "ice-cream-cup", "ice-cream-scoop-chocolate", "popsicle", "cupcake", "lollypop" };
                for (int i = 0; i < 4 + s.Assortment * 2; i++)
                {
                    var p = Quaternion.Euler(0, (i % 4) * 90f, 0) * new Vector3(-0.5f + (i / 4) * 0.25f, 1.03f, -r);
                    c.Product(Fd + treats[i % treats.Length], p, 0f, 0.16f);
                }
                c.Spawn(F + "kitchenFridge", new Vector3(0.6f, 0, 0.5f), 0f, 1.4f);
                c.Work(0, 0, 180f);
                if (s.Equipment >= 3) c.Work(-0.6f, 0.3f, 0f);
                c.Customer(0, -r - 0.9f, 0f);
                c.Customer(r + 0.9f, 0, -90f);
                return;
            }
            Cafe(c);
        }

        // ---------------- Розница ----------------

        static void Checkout(Ctx c, float x, float z, int count, float spacing = 1.8f)
        {
            for (int i = 0; i < count; i++)
            {
                float cx = x - i * spacing;
                Counter(c, cx, z, 1.4f, 180f);
                Register(c, cx + 0.3f, z, 200f);
                c.Work(cx, z + 0.8f, 180f);
                c.Customer(cx, z - 0.85f, 0f);
            }
        }

        static void Clothing(Ctx c)
        {
            var s = c.S;
            float hw = c.Hw, d = c.D;
            var palette = new[] { new Color(0.15f, 0.2f, 0.35f), new Color(0.85f, 0.82f, 0.75f), new Color(0.55f, 0.1f, 0.12f), new Color(0.1f, 0.1f, 0.1f), new Color(0.35f, 0.45f, 0.3f), new Color(0.85f, 0.6f, 0.55f), new Color(0.3f, 0.5f, 0.75f) };
            var mb = new MeshBuilder();
            var mats = new List<Material>();
            int Sub(Material m) { int i = mats.IndexOf(m); if (i >= 0) return i; mats.Add(m); return mats.Count - 1; }
            int alu = Sub(c.M.Aluminium);
            // Рейлы с одеждой: количество зависит от ассортимента
            int racks = 1 + s.Assortment;
            int placed = 0;
            for (float z = 2.2f; z < d - 3f && placed < racks; z += 2.6f)
                for (float x = -hw + 1.6f; x < hw - 1.2f && placed < racks; x += 3.2f, placed++)
                {
                    mb.AddBox(alu, new Vector3(x, 1.55f, z), new Vector3(1.8f, 0.03f, 0.03f));
                    mb.AddBox(alu, new Vector3(x - 0.9f, 0.78f, z), new Vector3(0.03f, 1.55f, 0.4f));
                    mb.AddBox(alu, new Vector3(x + 0.9f, 0.78f, z), new Vector3(0.03f, 1.55f, 0.4f));
                    for (float u = -0.8f; u < 0.8f; u += 0.1f)
                        mb.AddBox(Sub(c.M.Paint("cloth", palette[c.Rng.Range(0, palette.Length)], 0.05f)), new Vector3(x + u, 1.12f, z), new Vector3(0.05f, c.Rng.Range(0.7f, 0.95f), 0.42f));
                    c.Box(new Vector3(x, 0.8f, z), new Vector3(1.9f, 1.6f, 0.5f));
                    c.Customer(x, z - 0.8f, 0f);
                }
            // Полки вдоль стен со сложенной одеждой
            for (float z = 1.4f; z < d - 2f; z += 1.6f)
                foreach (float side in new[] { -1f, 1f })
                {
                    var p = new Vector3(side * (hw - 0.25f), 0, z);
                    c.Spawn(PH("wooden_display_shelves_01"), p, side > 0 ? -90f : 90f, 1.55f, FitAxis.Height, true);
                    for (int lv = 0; lv < 3; lv++)
                        mb.AddBox(Sub(c.M.Paint("cloth", palette[c.Rng.Range(0, palette.Length)], 0.05f)), p + new Vector3(-side * 0.05f, 0.14f + lv * 0.46f, 0), new Vector3(0.3f, 0.12f, 0.8f));
                }
            var go = new GameObject("Garments");
            go.transform.SetParent(c.Root, false);
            GeneratedMesh.Assign(go, mb.Build("Garments"));
            go.AddComponent<MeshRenderer>().sharedMaterials = mats.ToArray();
            // Манекены у витрины — персонажи Kenney в статичной позе
            for (int i = 0; i < 1 + s.Interior / 2; i++)
            {
                var rig = CharacterRig.Create(c.Root, CharacterRig.RandomModel(c.S.Seed + i * 13));
                rig.transform.localPosition = new Vector3(-hw + 1f + i * 1.2f, 0.06f, 0.8f);
                rig.transform.localRotation = Quaternion.Euler(0, 180f, 0);
                rig.Tick(0f);
                c.Spawn(F + "rugRound", new Vector3(-hw + 1f + i * 1.2f, 0.01f, 0.8f), 0f, 0.8f, FitAxis.Width);
            }
            c.Spawn(F + "bathroomMirror", new Vector3(hw - 0.15f, 0.4f, d - 2.2f), -90f, 1.5f);
            c.Spawn(F + "coatRackStanding", new Vector3(-hw + 0.5f, 0, d - 0.6f), 0f, 1.8f);
            Checkout(c, hw - 1.4f, d - 1.4f, Mathf.Clamp((s.Equipment + 1) / 2, 1, 3));
            c.Work(0, 1.4f, 180f);
        }

        static void Electronics(Ctx c)
        {
            var s = c.S;
            float hw = c.Hw, d = c.D;
            if (s.Kind == PropertyKind.Kiosk) { KioskRetail(c, new[] { "laptop", "computerScreen", "speakerSmall", "radio" }, true); return; }
            // Демо-столы с ноутбуками и мониторами
            int tables = 1 + s.Assortment;
            int placed = 0;
            for (float z = 2.2f; z < d - 3f && placed < tables; z += 2.6f)
                for (float x = -hw + 1.6f; x < hw - 1.4f && placed < tables; x += 3f, placed++)
                {
                    c.Spawn(F + "desk", new Vector3(x, 0, z), 0f, 0.78f, FitAxis.Height, true);
                    c.Spawn(F + "laptop", new Vector3(x - 0.35f, 0.78f, z), 180f, 0.32f, FitAxis.Width);
                    c.Spawn(F + "computerScreen", new Vector3(x + 0.35f, 0.78f, z + 0.1f), 180f, 0.45f, FitAxis.Width);
                    c.Customer(x, z - 0.8f, 0f);
                }
            // Стена телевизоров: ассортимент = число экранов
            int tvs = 2 + s.Assortment * 2;
            for (int i = 0; i < tvs; i++)
            {
                float x = -hw + 1f + (i % 6) * Mathf.Min(1.4f, (hw * 2f - 2f) / 6f);
                float y = 1.2f + (i / 6) * 0.9f;
                c.Spawn(F + "televisionModern", new Vector3(x, y, d - 0.2f), 180f, 1.1f, FitAxis.Width);
            }
            for (int i = 0; i < Mathf.Min(s.Equipment, 3); i++) c.Spawn(F + "speaker", new Vector3(hw - 0.4f, 0, 1.5f + i * 1.2f), -90f, 1.0f, FitAxis.Height, true);
            Shelf(c, new Vector3(-hw + 0.3f, 0, d - 2.4f), 90f, null, 0f);
            for (int i = 0; i < 8; i++) c.Product(F + "cardboardBoxClosed", new Vector3(-hw + 0.3f, 0.15f + (i / 2) * 0.5f, d - 2.6f + (i % 2) * 0.45f), 90f, 0.35f);
            Checkout(c, hw - 1.4f, d - 1.4f, Mathf.Clamp((s.Equipment + 1) / 2, 1, 3));
            c.Work(0, 1.5f, 180f);
        }

        static void Toys(Ctx c)
        {
            var s = c.S;
            float hw = c.Hw, d = c.D;
            int cases = 2 + s.Assortment;
            for (int i = 0; i < cases; i++)
            {
                float x = -hw + 0.8f + i * Mathf.Max(1.1f, (hw * 2f - 1.6f) / cases);
                if (x > hw - 0.6f) break;
                c.Spawn(F + "bookcaseOpen", new Vector3(x, 0, d - 0.35f), 180f, 1.9f, FitAxis.Height, true);
                for (int lv = 0; lv < 3; lv++)
                {
                    float y = 0.48f + lv * 0.46f;
                    c.Product(F + "bear", new Vector3(x - 0.2f, y, d - 0.4f), 180f, 0.26f, FitAxis.Height);
                    c.Product(PH("rubber_duck_toy"), new Vector3(x + 0.18f, y, d - 0.4f), 160f, 0.14f, FitAxis.Height);
                }
            }
            for (float x = -hw + 1.8f; x < hw - 1.5f; x += 3.4f)
            {
                var t = new Vector3(x, 0, d * 0.4f);
                c.Spawn(F + "tableCoffee", t, 0f, 0.45f, FitAxis.Height, true);
                for (int i = 0; i < 2 + s.Assortment; i++)
                    c.Product(i % 2 == 0 ? F + "bear" : PH("rubber_duck_toy"), t + new Vector3(-0.4f + i * 0.2f, 0.46f, 0), c.Rng.Range(0f, 360f), i % 2 == 0 ? 0.3f : 0.16f, FitAxis.Height);
                c.Customer(x, t.z - 0.9f, 0f);
            }
            c.Spawn(F + "rugRound", new Vector3(0, 0.01f, 2f), 0f, 2.4f, FitAxis.Width);
            Checkout(c, hw - 1.2f, 1.6f, Mathf.Clamp((s.Equipment + 1) / 2, 1, 2));
        }

        static void Cosmetics(Ctx c)
        {
            var s = c.S;
            float hw = c.Hw, d = c.D;
            string[] bottles = { "soda-bottle", "bottle-oil", "wine-white", "bottle-ketchup" };
            if (s.Kind == PropertyKind.Kiosk) { KioskRetail(c, bottles, false); return; }
            for (float z = 1.4f; z < d - 1.5f; z += 1.3f)
                foreach (float side in new[] { -1f, 1f })
                    Shelf(c, new Vector3(side * (hw - 0.25f), 0, z), side > 0 ? -90f : 90f, bottles, 0.5f + 0.15f * s.Assortment, true, 0.12f);
            int islands = 1 + s.Assortment / 2;
            for (int i = 0; i < islands; i++)
            {
                var t = new Vector3(-hw * 0.4f + i * 2.6f, 0, d * 0.45f);
                if (t.x > hw - 1.5f) break;
                c.Spawn(F + "tableGlass", t, 0f, 0.8f, FitAxis.Height, true);
                for (int k = 0; k < 6; k++) c.Product(Fd + bottles[k % bottles.Length], t + new Vector3(-0.4f + k * 0.16f, 0.82f, 0f), 0f, 0.12f, FitAxis.Height);
                c.Customer(t.x, t.z - 0.9f, 0f);
            }
            c.Spawn(F + "bathroomMirror", new Vector3(0, 0.6f, d - 0.15f), 180f, 1.2f);
            c.Spawn(F + "bathroomCabinet", new Vector3(-1.2f, 0, d - 0.35f), 180f, 0.9f, FitAxis.Height, true);
            Checkout(c, hw - 1.6f, d - 1.4f, Mathf.Clamp((s.Equipment + 1) / 2, 1, 2));
        }

        static void Bookstore(Ctx c)
        {
            var s = c.S;
            float hw = c.Hw, d = c.D;
            int rows = 1 + s.Assortment / 2;
            for (int r = 0; r < rows; r++)
                for (float x = -hw + 0.8f; x < hw - 0.6f; x += 1.0f)
                {
                    float z = d - 0.35f - r * 2.6f;
                    if (z < 2.5f) break;
                    c.Spawn(F + "bookcaseOpen", new Vector3(x, 0, z), 180f, 1.9f, FitAxis.Height, true);
                    for (int lv = 0; lv < 3; lv++) c.Product(F + "books", new Vector3(x, 0.48f + lv * 0.46f, z - 0.05f), 180f, 0.35f, FitAxis.Width);
                }
            c.Spawn(PH("modern_arm_chair_01"), new Vector3(-hw + 1f, 0, 1.6f), 150f, 0.95f, FitAxis.Height, true);
            c.Spawn(F + "lampRoundFloor", new Vector3(-hw + 0.4f, 0, 2.3f), 0f, 1.6f);
            var t = new Vector3(0.5f, 0, 2.2f);
            c.Spawn(F + "table", t, 0f, 0.75f, FitAxis.Height, true);
            for (int i = 0; i < 3 + s.Assortment; i++) c.Product(F + "books", t + new Vector3(-0.5f + i * 0.22f, 0.76f, 0), c.Rng.Range(-15f, 15f), 0.3f, FitAxis.Width);
            c.Customer(0.5f, 1.3f, 0f);
            Checkout(c, hw - 1.2f, 1.6f, 1);
        }

        static void Jewelry(Ctx c)
        {
            var s = c.S;
            float hw = c.Hw, d = c.D;
            var gold = c.M.Emissive("gold", new Color(1f, 0.8f, 0.35f), new Color(0.35f, 0.25f, 0.05f));
            int cases = 2 + s.Equipment;
            for (int i = 0; i < cases; i++)
            {
                float x = -hw + 1.2f + (i % 3) * 2.2f;
                float z = s.Kind == PropertyKind.Kiosk ? 0f : 2f + (i / 3) * 2.6f;
                if (x > hw - 0.8f || z > d - 1.5f) continue;
                var t = new Vector3(x, 0, z);
                c.Spawn(F + "tableGlass", t, 0f, 0.9f, FitAxis.Height, true);
                for (int k = 0; k < 2 + s.Assortment; k++)
                {
                    var ring = GameObject.CreatePrimitive(k % 2 == 0 ? PrimitiveType.Sphere : PrimitiveType.Cylinder);
                    Object.DestroyImmediate(ring.GetComponent<Collider>());
                    ring.transform.SetParent(c.Root, false);
                    ring.transform.localPosition = t + new Vector3(-0.3f + k * 0.12f, 0.93f, 0);
                    ring.transform.localScale = Vector3.one * 0.05f;
                    ring.GetComponent<MeshRenderer>().sharedMaterial = gold;
                }
                c.Spawn(F + "lampRoundTable", t + new Vector3(0.35f, 0.9f, 0.1f), 0f, 0.3f);
                c.Customer(x, z - 0.9f, 0f);
                c.Work(x, z + 0.8f, 180f);
            }
            if (s.Kind != PropertyKind.Kiosk)
            {
                c.Spawn(PH("Chandelier_01"), new Vector3(0, s.Height - 0.85f, d * 0.4f), 0f, 0.8f);
                c.Spawn(PH("modern_arm_chair_01"), new Vector3(hw - 1f, 0, d - 1.5f), -120f, 0.95f, FitAxis.Height, true);
            }
        }

        /// <summary>Островной киоск с витриной товаров (электроника, косметика).</summary>
        static void KioskRetail(Ctx c, string[] items, bool furniture)
        {
            var s = c.S;
            float r = s.Width * 0.5f - 0.5f;
            for (int side = 0; side < 4; side++)
            {
                float yaw = side * 90f;
                var p = Quaternion.Euler(0, yaw, 0) * new Vector3(0, 0, -r);
                c.Spawn(F + "tableGlass", p, yaw, 0.95f, FitAxis.Height);
                var size = Quaternion.Euler(0, yaw, 0) * new Vector3(2.4f, 1.1f, 0.6f);
                c.Box(p + Vector3.up * 0.55f, new Vector3(Mathf.Abs(size.x), 1.1f, Mathf.Abs(size.z)));
                for (int k = 0; k < 2 + s.Assortment; k++)
                {
                    var off = Quaternion.Euler(0, yaw, 0) * new Vector3(-0.5f + k * 0.22f, 0.97f, 0);
                    c.Product((furniture ? F : Fd) + items[k % items.Length], p + off, yaw + 180f, furniture ? 0.25f : 0.12f, furniture ? FitAxis.Width : FitAxis.Height);
                }
            }
            c.Work(0, 0, 180f);
            if (s.Equipment >= 3) c.Work(0.5f, 0.5f, 0f);
            c.Customer(0, -r - 0.9f, 0f);
            c.Customer(-r - 0.9f, 0, 90f);
        }

        static void Supermarket(Ctx c)
        {
            var s = c.S;
            float hw = c.Hw, d = c.D;
            // Кассовая линия у входа: число касс растёт с оборудованием
            Checkout(c, hw - 3f, 4.2f, 2 + s.Equipment, 2.6f);
            // Ряды стеллажей: число рядов растёт с ассортиментом
            int rows = 2 + s.Assortment;
            float density = 0.6f + 0.1f * s.Assortment;
            for (int r = 0; r < rows; r++)
            {
                float z = 8f + r * 4.2f;
                if (z > d - 5f) break;
                for (float x = -hw + 2f; x < hw - 2f; x += 1.15f)
                {
                    if (Mathf.Abs(x) < 1.4f) continue; // центральный проход
                    Shelf(c, new Vector3(x, 0, z), 180f, Groceries, density);
                    Shelf(c, new Vector3(x, 0, z + 0.55f), 0f, Groceries, density);
                }
                c.Customer(-hw * 0.5f, z - 1.2f, 0f);
                c.Customer(hw * 0.5f, z + 1.8f, 180f);
            }
            // Холодильники вдоль задней стены
            for (float x = -hw + 1f; x < hw - 1f; x += 1.2f)
                c.Spawn(F + "kitchenFridgeLarge", new Vector3(x, 0, d - 0.5f), 180f, 2.0f);
            c.Box(new Vector3(0, 1f, d - 0.5f), new Vector3(hw * 2f, 2f, 0.9f));
            // Овощи и фрукты у входа
            for (int i = 0; i < 2 + s.Assortment / 2; i++)
            {
                var t = new Vector3(-hw + 3f + i * 3.2f, 0, 6.2f);
                if (t.x > 0f) break;
                c.Spawn(F + "table", t, 0f, 0.8f, FitAxis.Height, true);
                for (int k = 0; k < 10; k++)
                    c.Product(Fd + Produce[(i * 3 + k) % Produce.Length], t + new Vector3(-0.6f + (k % 5) * 0.3f, 0.82f, -0.2f + (k / 5) * 0.35f), c.Rng.Range(0f, 360f), 0.2f);
                c.Customer(t.x, t.z - 0.9f, 0f);
            }
            // Тележки
            for (int i = 0; i < 3; i++) c.Spawn(PH("industrial_storage_cart"), new Vector3(-hw + 1.2f, 0, 1.6f + i * 1.2f), 90f, 0.9f, FitAxis.Height, true);
            for (int i = 0; i < 4; i++) c.Work(-hw + 4f + i * 4f, 9f + i * 3f, 90f);
        }

        static void Warehouse(Ctx c)
        {
            var s = c.S;
            float hw = c.Hw, d = c.D;
            for (float z = 1.5f; z < d - 0.8f; z += 2.2f)
            {
                Shelf(c, new Vector3(-hw + 0.3f, 0, z), 90f, null, 0f);
                for (int k = 0; k < 4 + s.Equipment; k++)
                    c.Product(F + (k % 2 == 0 ? "cardboardBoxClosed" : "cardboardBoxOpen"), new Vector3(-hw + 0.3f, 0.15f + (k % 4) * 0.5f, z - 0.2f + (k / 4) * 0.4f), 90f, 0.4f);
            }
            for (int i = 0; i < 6 + s.Equipment * 2; i++)
                c.Spawn(F + "cardboardBoxClosed", new Vector3(hw - 0.5f - (i % 2) * 0.6f, (i / 2 % 3) * 0.5f, 1.5f + i / 6 * 0.7f), c.Rng.Range(-10f, 10f), 0.5f, FitAxis.Longest);
            c.Spawn(PH("industrial_storage_cart"), new Vector3(0.4f, 0, d * 0.5f), 0f, 0.9f, FitAxis.Height, true);
            c.Work(0, 2f, 180f);
            c.Work(0.5f, d - 2f, 0f);
        }

        static void Vacant(Ctx c)
        {
            float hw = c.Hw, d = c.D;
            if (c.S.Kind == PropertyKind.Kiosk) return;
            for (int i = 0; i < 4; i++)
                c.Spawn(F + (i % 2 == 0 ? "cardboardBoxClosed" : "cardboardBoxOpen"), new Vector3(-hw + 0.6f + (i % 2) * 0.7f, (i / 2) * 0.5f, d - 0.6f), c.Rng.Range(-20f, 20f), 0.5f, FitAxis.Longest, i < 2);
            c.Spawn(PH("WetFloorSign_01"), new Vector3(hw * 0.3f, 0, d * 0.5f), 40f, 0.63f);
        }
    }
}

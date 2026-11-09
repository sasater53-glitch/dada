using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>Точки интерьера для персонала и покупателей (x, y, z, yaw) в координатах помещения.</summary>
    public sealed class InteriorInfo : MonoBehaviour
    {
        public List<Vector4> WorkPoints = new List<Vector4>();
        public List<Vector4> CustomerPoints = new List<Vector4>();
        public Light Light;
    }

    /// <summary>
    /// Интерьеры помещений под тип бизнеса: кафе, ресторан, магазин одежды, супермаркет, склад, пустое помещение.
    /// Вся мебель — один меш; коллайдеры — только у крупных объектов (стойки, стеллажи).
    /// Уровень интерьера (1–3) добавляет декор: растения, подвесные светильники, картины, лучший пол.
    /// </summary>
    public static class InteriorBuilder
    {
        sealed class Ctx
        {
            public readonly MeshBuilder Mb = new MeshBuilder();
            public readonly List<Material> Mats = new List<Material>();
            public GameObject Colliders;
            public int Sub(Material m) { int i = Mats.IndexOf(m); if (i >= 0) return i; Mats.Add(m); return Mats.Count - 1; }
            public void Collider(Vector3 center, Vector3 size)
            {
                var bc = Colliders.AddComponent<BoxCollider>();
                bc.center = center; bc.size = size;
            }
        }

        public static InteriorInfo Build(MaterialLibrary M, Transform parent, BusinessType type, int level, Vector3 size, float front, float openingTop, int seed)
        {
            var go = new GameObject("Content_" + type);
            go.transform.SetParent(parent, false);
            var info = go.AddComponent<InteriorInfo>();
            var c = new Ctx { Colliders = new GameObject("Colliders") };
            c.Colliders.transform.SetParent(go.transform, false);
            var rng = new DeterministicRandom((ulong)(seed * 31 + (int)type * 7 + level));

            float hw = size.x * 0.5f - 0.3f, d = size.z - 0.3f, h = size.y - 0.05f;
            var style = Style(M, type, level);
            Shell(c, style, hw, front, d, h, openingTop);

            switch (type)
            {
                case BusinessType.Cafe: Cafe(M, c, info, hw, front, d, level, rng); break;
                case BusinessType.Restaurant: Restaurant(M, c, info, hw, front, d, level, rng); break;
                case BusinessType.ClothingStore: Clothing(M, c, info, hw, front, d, level, rng); break;
                case BusinessType.Supermarket: Supermarket(M, c, info, hw, front, d, level, rng); break;
                case BusinessType.Warehouse: Warehouse(M, c, info, hw, front, d, rng); break;
                default: Vacant(M, c, hw, front, d, rng); break;
            }
            if (level >= 2 && type != BusinessType.Warehouse && type != BusinessType.None) Decor(M, c, hw, front, d, h, level, rng);
            CeilingLights(M, c, hw, front, d, h, type);

            var meshGo = new GameObject("Mesh");
            meshGo.transform.SetParent(go.transform, false);
            meshGo.AddComponent<MeshFilter>().sharedMesh = c.Mb.Build("Interior_" + type);
            var mr = meshGo.AddComponent<MeshRenderer>();
            mr.sharedMaterials = c.Mats.ToArray();
            mr.shadowCastingMode = ShadowCastingMode.Off; // интерьеры освещаются без теней — дешевле

            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(0, h - 0.4f, (front + d) * 0.5f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = Mathf.Max(hw * 2f, d) * 1.1f;
            light.intensity = type == BusinessType.Restaurant ? 1.2f : 1.8f;
            light.color = type == BusinessType.Restaurant || type == BusinessType.Cafe ? new Color(1f, 0.85f, 0.65f) : new Color(1f, 0.97f, 0.92f);
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            info.Light = light;
            return info;
        }

        struct RoomStyle { public Material Floor, Walls, Wainscot, Ceiling; }

        static RoomStyle Style(MaterialLibrary M, BusinessType type, int level)
        {
            var s = new RoomStyle { Ceiling = M.Paint("ceiling", new Color(0.92f, 0.92f, 0.9f), null, 1f, 0.1f) };
            switch (type)
            {
                case BusinessType.Cafe:
                    s.Floor = level >= 2 ? M.Paint("floor_parquet", new Color(0.75f, 0.55f, 0.38f), "parquet", 2f, 0.4f) : M.Paint("floor_tiles", new Color(0.8f, 0.78f, 0.74f), "tiles", 1.2f, 0.5f);
                    s.Walls = M.Paint("wall", new Color(0.93f, 0.87f, 0.76f), "plaster", 3f, 0.1f);
                    s.Wainscot = M.Paint("wainscot", new Color(0.18f, 0.32f, 0.24f), "wood", 1f, 0.3f);
                    break;
                case BusinessType.Restaurant:
                    s.Floor = M.Paint("floor_parquet", new Color(0.5f, 0.32f, 0.2f), "parquet", 2f, 0.45f);
                    s.Walls = M.Paint("wall", new Color(0.45f, 0.12f, 0.14f), "plaster", 3f, 0.1f);
                    s.Wainscot = M.Paint("wainscot", new Color(0.3f, 0.18f, 0.1f), "wood", 1f, 0.4f);
                    break;
                case BusinessType.ClothingStore:
                    s.Floor = level >= 2 ? M.Paint("floor_tiles", new Color(0.92f, 0.92f, 0.9f), "tiles", 1.5f, 0.75f) : M.Paint("floor_concrete", new Color(0.6f, 0.6f, 0.6f), "concrete", 3f, 0.5f);
                    s.Walls = M.Paint("wall", new Color(0.96f, 0.96f, 0.95f), "plaster", 3f, 0.1f);
                    s.Wainscot = M.Paint("wainscot", new Color(0.12f, 0.12f, 0.13f), null, 1f, 0.5f);
                    break;
                case BusinessType.Supermarket:
                    s.Floor = M.Paint("floor_tiles", new Color(0.86f, 0.86f, 0.84f), "tiles", 0.8f, 0.6f);
                    s.Walls = M.Paint("wall", new Color(0.95f, 0.96f, 0.94f), "plaster", 3f, 0.1f);
                    s.Wainscot = M.Paint("wainscot", new Color(0.15f, 0.5f, 0.25f), null, 1f, 0.4f);
                    break;
                case BusinessType.Warehouse:
                    s.Floor = M.Paint("floor_concrete", new Color(0.55f, 0.55f, 0.53f), "concrete", 4f, 0.35f);
                    s.Walls = M.Paint("wall_metal", new Color(0.75f, 0.77f, 0.78f), "corrugated", 3f, 0.3f);
                    s.Wainscot = M.RoadPaintYellow;
                    break;
                default:
                    s.Floor = M.Paint("floor_concrete", new Color(0.55f, 0.55f, 0.53f), "concrete", 3f, 0.2f);
                    s.Walls = M.Paint("wall_raw", new Color(0.72f, 0.71f, 0.69f), "concrete", 3f, 0.05f);
                    s.Wainscot = s.Walls;
                    break;
            }
            return s;
        }

        static void Shell(Ctx c, RoomStyle s, float hw, float front, float d, float h, float openingTop)
        {
            var mb = c.Mb;
            int floor = c.Sub(s.Floor), walls = c.Sub(s.Walls), wain = c.Sub(s.Wainscot), ceil = c.Sub(s.Ceiling);
            mb.AddQuad(floor, new Vector3(-hw, 0.01f, front), new Vector3(-hw, 0.01f, d), new Vector3(hw, 0.01f, d), new Vector3(hw, 0.01f, front));
            mb.AddQuad(ceil, new Vector3(-hw, h, d), new Vector3(-hw, h, front), new Vector3(hw, h, front), new Vector3(hw, h, d));
            // Стены изнутри (смотрят в помещение)
            mb.AddQuad(walls, new Vector3(-hw, 0, d), new Vector3(-hw, h, d), new Vector3(hw, h, d), new Vector3(hw, 0, d)); // задняя — лицом к -Z
            mb.AddQuad(walls, new Vector3(-hw, 0, front), new Vector3(-hw, h, front), new Vector3(-hw, h, d), new Vector3(-hw, 0, d)); // левая — лицом к +X
            mb.AddQuad(walls, new Vector3(hw, 0, d), new Vector3(hw, h, d), new Vector3(hw, h, front), new Vector3(hw, 0, front)); // правая — лицом к -X
            mb.AddQuad(walls, new Vector3(hw, openingTop, front), new Vector3(hw, h, front), new Vector3(-hw, h, front), new Vector3(-hw, openingTop, front)); // над витриной
            // Панели нижней части стен
            mb.AddBox(wain, new Vector3(0, 0.5f, d - 0.02f), new Vector3(hw * 2f, 1f, 0.04f));
            mb.AddBox(wain, new Vector3(-hw + 0.02f, 0.5f, (front + d) * 0.5f), new Vector3(0.04f, 1f, d - front));
            mb.AddBox(wain, new Vector3(hw - 0.02f, 0.5f, (front + d) * 0.5f), new Vector3(0.04f, 1f, d - front));
            c.Collider(new Vector3(-hw - 0.1f, h * 0.5f, (front + d) * 0.5f), new Vector3(0.2f, h, d - front));
            c.Collider(new Vector3(hw + 0.1f, h * 0.5f, (front + d) * 0.5f), new Vector3(0.2f, h, d - front));
            c.Collider(new Vector3(0, h * 0.5f, d + 0.1f), new Vector3(hw * 2f, h, 0.2f));
            c.Collider(new Vector3(0, -0.1f, (front + d) * 0.5f), new Vector3(hw * 2f, 0.2f, d - front));
        }

        // ---------- Типовые предметы ----------

        static void Counter(MaterialLibrary M, Ctx c, Vector3 center, float length, Color front, bool collider = true)
        {
            c.Mb.AddBox(c.Sub(M.Paint("counter", front, "wood", 1f, 0.35f)), center + new Vector3(0, 0.5f, 0), new Vector3(length, 1f, 0.7f));
            c.Mb.AddBox(c.Sub(M.Paint("countertop", new Color(0.85f, 0.85f, 0.83f), "tiles", 2f, 0.7f)), center + new Vector3(0, 1.03f, 0), new Vector3(length + 0.06f, 0.06f, 0.78f));
            if (collider) c.Collider(center + new Vector3(0, 0.55f, 0), new Vector3(length, 1.1f, 0.75f));
        }

        static void Table(MaterialLibrary M, Ctx c, Vector3 p, float radius, bool cloth, int chairs, DeterministicRandom rng)
        {
            var mb = c.Mb;
            int top = c.Sub(cloth ? M.Paint("tablecloth", new Color(0.95f, 0.94f, 0.9f), null, 1f, 0.1f) : M.Wood);
            int metal = c.Sub(M.MetalDark);
            mb.AddCylinder(metal, p, 0.04f, 0.72f, 6, false);
            mb.AddCylinder(top, p + new Vector3(0, 0.72f, 0), radius, cloth ? 0.06f : 0.04f, 14);
            if (cloth) mb.AddCylinder(top, p + new Vector3(0, 0.45f, 0), radius + 0.02f, 0.3f, 14, false);
            for (int i = 0; i < chairs; i++)
            {
                float a = i * Mathf.PI * 2f / chairs + rng.Range(-0.15f, 0.15f);
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                Chair(M, c, p + dir * (radius + 0.4f), Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg);
            }
            c.Collider(p + new Vector3(0, 0.4f, 0), new Vector3(radius * 2f, 0.8f, radius * 2f));
        }

        static void Chair(MaterialLibrary M, Ctx c, Vector3 p, float yaw)
        {
            var mb = c.Mb;
            var saved = mb.Transform;
            mb.Transform = saved * Matrix4x4.TRS(p, Quaternion.Euler(0, yaw, 0), Vector3.one);
            int wood = c.Sub(M.Wood), metal = c.Sub(M.MetalDark);
            mb.AddBox(wood, new Vector3(0, 0.45f, 0), new Vector3(0.42f, 0.05f, 0.42f));
            mb.AddBox(wood, new Vector3(0, 0.72f, -0.19f), new Vector3(0.42f, 0.5f, 0.04f));
            foreach (var x in new[] { -0.18f, 0.18f }) foreach (var z in new[] { -0.18f, 0.18f })
                mb.AddBox(metal, new Vector3(x, 0.22f, z), new Vector3(0.03f, 0.45f, 0.03f));
            mb.Transform = saved;
        }

        static void Plant(MaterialLibrary M, Ctx c, Vector3 p, float scale)
        {
            c.Mb.AddCylinder(c.Sub(M.Paint("pot", new Color(0.75f, 0.45f, 0.3f), null, 1f, 0.2f)), p, 0.22f * scale, 0.45f * scale, 10, true, 0.28f * scale);
            int leaves = c.Sub(M.Leaves(0));
            c.Mb.AddSphere(leaves, p + new Vector3(0, 0.85f * scale, 0), new Vector3(0.38f, 0.5f, 0.38f) * scale, 5, 7);
            c.Mb.AddSphere(leaves, p + new Vector3(0.12f, 1.2f * scale, 0.05f), new Vector3(0.25f, 0.3f, 0.25f) * scale, 4, 6);
        }

        static void Shelf(MaterialLibrary M, Ctx c, Vector3 center, float length, float height, float depth, bool doubleSided, DeterministicRandom rng, int levels)
        {
            var mb = c.Mb;
            int frame = c.Sub(M.Paint("shelf", new Color(0.88f, 0.88f, 0.86f), null, 1f, 0.4f));
            mb.AddBox(frame, center + new Vector3(0, height * 0.5f, 0), new Vector3(length, height, 0.05f));
            mb.AddBox(frame, center + new Vector3(-length * 0.5f, height * 0.5f, 0), new Vector3(0.05f, height, depth * (doubleSided ? 2f : 1f)));
            mb.AddBox(frame, center + new Vector3(length * 0.5f, height * 0.5f, 0), new Vector3(0.05f, height, depth * (doubleSided ? 2f : 1f)));
            var products = new[]
            {
                M.Paint("prod", new Color(0.85f, 0.2f, 0.15f)), M.Paint("prod", new Color(0.95f, 0.75f, 0.2f)), M.Paint("prod", new Color(0.2f, 0.45f, 0.8f)),
                M.Paint("prod", new Color(0.25f, 0.65f, 0.3f)), M.Paint("prod", new Color(0.95f, 0.95f, 0.92f)), M.Paint("prod", new Color(0.55f, 0.3f, 0.6f)),
            };
            foreach (float side in doubleSided ? new[] { -1f, 1f } : new[] { -1f })
            {
                for (int lv = 0; lv < levels; lv++)
                {
                    float y = 0.15f + lv * (height - 0.2f) / levels;
                    mb.AddBox(frame, center + new Vector3(0, y, side * depth * 0.5f), new Vector3(length, 0.03f, depth));
                    for (float x = -length * 0.5f + 0.15f; x < length * 0.5f - 0.1f;)
                    {
                        float w = rng.Range(0.12f, 0.3f), ph = rng.Range(0.15f, Mathf.Min(0.35f, (height - 0.2f) / levels - 0.05f));
                        if (rng.Chance(0.9)) mb.AddBox(c.Sub(products[rng.Range(0, products.Length)]), center + new Vector3(x + w * 0.5f, y + 0.015f + ph * 0.5f, side * depth * 0.5f), new Vector3(w * 0.9f, ph, depth * 0.8f));
                        x += w;
                    }
                }
            }
            c.Collider(center + new Vector3(0, height * 0.5f, 0), new Vector3(length, height, depth * (doubleSided ? 2f : 1f)));
        }

        // ---------- Типы помещений ----------

        static void Cafe(MaterialLibrary M, Ctx c, InteriorInfo info, float hw, float front, float d, int level, DeterministicRandom rng)
        {
            var mb = c.Mb;
            float cz = d - 1.6f;
            Counter(M, c, new Vector3(hw * 0.25f, 0, cz), hw * 1.1f, new Color(0.35f, 0.22f, 0.14f));
            // Кофемашина, витрина с выпечкой, меню
            int metal = c.Sub(M.Aluminium);
            mb.AddBox(metal, new Vector3(hw * 0.5f, 1.3f, d - 1.45f), new Vector3(0.7f, 0.45f, 0.45f));
            mb.AddBox(c.Sub(M.StoreGlass), new Vector3(-0.1f, 1.25f, cz), new Vector3(1.1f, 0.4f, 0.5f));
            int pastry = c.Sub(M.Paint("pastry", new Color(0.85f, 0.6f, 0.3f), null, 1f, 0.3f));
            for (int i = 0; i < 6; i++) mb.AddSphere(pastry, new Vector3(-0.5f + i * 0.16f, 1.12f, cz), new Vector3(0.07f, 0.04f, 0.07f), 3, 6);
            mb.AddBox(c.Sub(M.Paint("menu", new Color(0.1f, 0.1f, 0.1f), null, 1f, 0.6f)), new Vector3(hw * 0.25f, 2.3f, d - 0.05f), new Vector3(2.2f, 0.9f, 0.05f));
            // Пеналы с чашками
            int cup = c.Sub(M.Paint("cup", Color.white, null, 1f, 0.7f));
            for (int i = 0; i < 8; i++) mb.AddCylinder(cup, new Vector3(-hw + 0.6f + i * 0.18f, 1.6f, d - 0.15f), 0.05f, 0.09f, 6);
            mb.AddBox(c.Sub(M.Wood), new Vector3(-hw + 1.2f, 1.58f, d - 0.15f), new Vector3(1.8f, 0.03f, 0.25f));

            info.WorkPoints.Add(new Vector4(hw * 0.25f, 0, d - 0.85f, 180f));
            info.WorkPoints.Add(new Vector4(hw * 0.6f, 0, d - 0.85f, 180f));
            info.WorkPoints.Add(new Vector4(-hw * 0.2f, 0, d - 0.85f, 180f));
            info.CustomerPoints.Add(new Vector4(hw * 0.25f, 0, cz - 0.85f, 0f));
            info.CustomerPoints.Add(new Vector4(-0.1f, 0, cz - 0.9f, 0f));

            float tz = front + 1.6f;
            for (float x = -hw + 1.1f; x < hw - 0.8f; x += 2.2f)
            {
                Table(M, c, new Vector3(x, 0, tz), 0.38f, false, 2, rng);
                info.CustomerPoints.Add(new Vector4(x + 0.75f, 0, tz, -90f));
            }
            if (d - front > 7f)
                for (float x = -hw + 1.1f; x < -0.8f; x += 2.2f) Table(M, c, new Vector3(x, 0, tz + 2.2f), 0.38f, false, 3, rng);
            Plant(M, c, new Vector3(-hw + 0.4f, 0, d - 0.4f), 1f);
        }

        static void Restaurant(MaterialLibrary M, Ctx c, InteriorInfo info, float hw, float front, float d, int level, DeterministicRandom rng)
        {
            var mb = c.Mb;
            // Бар со стеной бутылок
            Counter(M, c, new Vector3(-hw + 2f, 0, d - 1.5f), 3.2f, new Color(0.25f, 0.14f, 0.08f));
            int shelf = c.Sub(M.Wood);
            for (int lv = 0; lv < 3; lv++)
            {
                mb.AddBox(shelf, new Vector3(-hw + 2f, 1.4f + lv * 0.45f, d - 0.15f), new Vector3(3f, 0.04f, 0.25f));
                for (int i = 0; i < 12; i++)
                    mb.AddCylinder(c.Sub(M.Paint("bottle", Color.HSVToRGB(rng.NextFloat(), 0.6f, 0.4f), null, 1f, 0.9f)), new Vector3(-hw + 0.7f + i * 0.22f, 1.42f + lv * 0.45f, d - 0.15f), 0.04f, 0.3f, 6);
            }
            // Раздача на кухню
            mb.AddBox(c.Sub(M.Aluminium), new Vector3(hw - 1.6f, 1.1f, d - 0.2f), new Vector3(2.4f, 0.08f, 0.4f));
            mb.AddBox(c.Sub(M.Dark), new Vector3(hw - 1.6f, 1.8f, d - 0.02f), new Vector3(2.4f, 1.2f, 0.02f));
            info.WorkPoints.Add(new Vector4(-hw + 2f, 0, d - 0.75f, 180f));
            info.WorkPoints.Add(new Vector4(hw - 1.6f, 0, d - 0.8f, 180f));
            info.WorkPoints.Add(new Vector4(0.5f, 0, (front + d) * 0.5f, 90f));
            info.WorkPoints.Add(new Vector4(hw - 2.5f, 0, front + 1.2f, -90f));

            for (float z = front + 1.5f; z < d - 2.6f; z += 2.4f)
                for (float x = -hw + 1.4f; x < hw - 1f; x += 2.6f)
                {
                    Table(M, c, new Vector3(x, 0, z), 0.5f, true, 4, rng);
                    info.CustomerPoints.Add(new Vector4(x + 0.9f, 0, z, -90f));
                    // Свеча на столе
                    mb.AddCylinder(c.Sub(M.Emission("candle", new Color(1f, 0.7f, 0.35f), 2f)), new Vector3(x, 0.78f, z), 0.025f, 0.08f, 6);
                }
            Plant(M, c, new Vector3(hw - 0.4f, 0, front + 0.6f), 1.2f);
            Plant(M, c, new Vector3(-hw + 0.4f, 0, front + 0.6f), 1.2f);
        }

        static void Clothing(MaterialLibrary M, Ctx c, InteriorInfo info, float hw, float front, float d, int level, DeterministicRandom rng)
        {
            var mb = c.Mb;
            int chrome = c.Sub(M.Aluminium);
            var palette = new[] { new Color(0.15f, 0.2f, 0.35f), new Color(0.8f, 0.78f, 0.72f), new Color(0.55f, 0.1f, 0.12f), new Color(0.1f, 0.1f, 0.1f), new Color(0.35f, 0.45f, 0.3f), new Color(0.85f, 0.6f, 0.55f) };
            // Рейлы с одеждой
            for (float x = -hw + 1.5f; x < hw - 1.2f; x += 2.6f)
            {
                float z = (front + d) * 0.5f;
                mb.AddBox(chrome, new Vector3(x, 1.55f, z), new Vector3(1.8f, 0.03f, 0.03f));
                mb.AddBox(chrome, new Vector3(x - 0.9f, 0.78f, z), new Vector3(0.03f, 1.55f, 0.03f));
                mb.AddBox(chrome, new Vector3(x + 0.9f, 0.78f, z), new Vector3(0.03f, 1.55f, 0.03f));
                for (float u = -0.8f; u < 0.8f; u += 0.11f)
                    mb.AddBox(c.Sub(M.Paint("cloth", palette[rng.Range(0, palette.Length)], "carpet", 0.5f, 0.05f)), new Vector3(x + u, 1.1f, z), new Vector3(0.06f, rng.Range(0.7f, 0.95f), 0.45f));
                c.Collider(new Vector3(x, 0.8f, z), new Vector3(1.9f, 1.6f, 0.5f));
                info.CustomerPoints.Add(new Vector4(x, 0, z - 0.8f, 0f));
            }
            // Стеллажи со сложенной одеждой вдоль стен
            for (float z = front + 1.2f; z < d - 1.5f; z += 1.6f)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    float x = side * (hw - 0.25f);
                    mb.AddBox(c.Sub(M.Wood), new Vector3(x, 1f, z), new Vector3(0.45f, 2f, 1.4f));
                    for (int lv = 0; lv < 4; lv++)
                        mb.AddBox(c.Sub(M.Paint("cloth", palette[rng.Range(0, palette.Length)], "carpet", 0.5f, 0.05f)), new Vector3(x - side * 0.05f, 0.35f + lv * 0.45f, z), new Vector3(0.4f, 0.15f, 1.2f));
                }
            }
            // Манекены и касса
            for (int i = 0; i < 2; i++)
            {
                var p = new Vector3(i == 0 ? -hw + 1f : hw - 1f, 0, front + 0.8f);
                mb.AddCylinder(chrome, p, 0.2f, 0.05f, 10);
                mb.AddCylinder(chrome, p, 0.02f, 0.9f, 4, false);
                mb.AddBox(c.Sub(M.Paint("mannequin", new Color(0.92f, 0.9f, 0.88f), null, 1f, 0.6f)), p + new Vector3(0, 1.25f, 0), new Vector3(0.42f, 0.6f, 0.24f));
                mb.AddSphere(c.Sub(M.Paint("mannequin", new Color(0.92f, 0.9f, 0.88f), null, 1f, 0.6f)), p + new Vector3(0, 1.7f, 0), new Vector3(0.11f, 0.13f, 0.11f), 5, 8);
                mb.AddBox(c.Sub(M.Paint("cloth", palette[rng.Range(0, palette.Length)], "carpet", 0.5f, 0.05f)), p + new Vector3(0, 1.15f, 0), new Vector3(0.46f, 0.7f, 0.27f));
            }
            Counter(M, c, new Vector3(hw - 1.6f, 0, d - 1.3f), 2.2f, new Color(0.1f, 0.1f, 0.1f));
            info.WorkPoints.Add(new Vector4(hw - 1.6f, 0, d - 0.6f, 180f));
            info.WorkPoints.Add(new Vector4(0, 0, front + 1.3f, 180f));
            info.CustomerPoints.Add(new Vector4(hw - 1.6f, 0, d - 2.1f, 0f));
            // Примерочные
            mb.AddBox(c.Sub(M.Paint("curtain", new Color(0.35f, 0.12f, 0.15f), "carpet", 0.5f, 0.05f)), new Vector3(-hw + 1.2f, 1.1f, d - 1.1f), new Vector3(2f, 2.2f, 0.05f));
            mb.AddBox(c.Sub(M.Paint("mirror", new Color(0.8f, 0.85f, 0.9f), null, 1f, 0.98f)), new Vector3(0, 1.2f, d - 0.03f), new Vector3(1f, 1.9f, 0.02f));
        }

        static void Supermarket(MaterialLibrary M, Ctx c, InteriorInfo info, float hw, float front, float d, int level, DeterministicRandom rng)
        {
            var mb = c.Mb;
            // Кассовая линия
            float checkoutZ = front + 2.2f;
            int belt = c.Sub(M.Dark);
            for (float x = -hw + 2f; x < -hw + 2f + 3 * 3f; x += 3f)
            {
                Counter(M, c, new Vector3(x, 0, checkoutZ), 1.8f, new Color(0.85f, 0.85f, 0.83f));
                mb.AddBox(belt, new Vector3(x, 1.07f, checkoutZ), new Vector3(1.6f, 0.02f, 0.45f));
                mb.AddBox(c.Sub(M.Emission("lane", new Color(0.2f, 0.9f, 0.3f), 1.5f)), new Vector3(x + 0.9f, 2.1f, checkoutZ), new Vector3(0.2f, 0.2f, 0.05f));
                info.WorkPoints.Add(new Vector4(x + 0.4f, 0, checkoutZ + 0.75f, -90f));
                info.CustomerPoints.Add(new Vector4(x - 0.3f, 0, checkoutZ - 0.75f, 90f));
            }
            // Сетка двусторонних стеллажей с проходами
            for (float x = -hw + 2f; x < hw - 1.8f; x += 3.4f)
                for (float z = checkoutZ + 2.6f; z < d - 2.2f; z += 2.6f)
                {
                    Shelf(M, c, new Vector3(x, 0, z), 2.4f, 1.9f, 0.45f, true, rng, 4);
                    info.CustomerPoints.Add(new Vector4(x + rng.Range(-0.8f, 0.8f), 0, z + 1.25f, 180f));
                }
            // Холодильники вдоль задней стены
            int fridge = c.Sub(M.Paint("fridge", new Color(0.9f, 0.92f, 0.94f), null, 1f, 0.6f));
            int glow = c.Sub(M.Emission("fridge_glow", new Color(0.75f, 0.9f, 1f), 1.2f));
            for (float x = -hw + 1f; x < hw - 0.8f; x += 1.6f)
            {
                mb.AddBox(fridge, new Vector3(x, 1f, d - 0.45f), new Vector3(1.5f, 2f, 0.8f));
                mb.AddBox(glow, new Vector3(x, 1.05f, d - 0.86f), new Vector3(1.3f, 1.6f, 0.02f));
            }
            c.Collider(new Vector3(0, 1f, d - 0.45f), new Vector3(hw * 2f, 2f, 0.8f));
            // Овощной прилавок и тележки
            mb.AddBox(c.Sub(M.Wood), new Vector3(hw - 2f, 0.45f, front + 1.4f), new Vector3(2.6f, 0.9f, 1.2f));
            var veg = new[] { new Color(0.9f, 0.3f, 0.15f), new Color(0.95f, 0.8f, 0.2f), new Color(0.3f, 0.7f, 0.25f) };
            for (int i = 0; i < 30; i++)
                mb.AddSphere(c.Sub(M.Paint("veg", veg[i % 3], null, 1f, 0.5f)), new Vector3(hw - 3.1f + (i % 10) * 0.24f, 0.97f, front + 1f + (i / 10) * 0.35f), Vector3.one * 0.09f, 3, 5);
            c.Collider(new Vector3(hw - 2f, 0.45f, front + 1.4f), new Vector3(2.6f, 0.9f, 1.2f));
            for (int i = 0; i < 4; i++)
                mb.AddBox(c.Sub(M.Aluminium), new Vector3(hw - 0.8f, 0.5f, front + 3f + i * 0.35f), new Vector3(0.55f, 0.55f, 0.3f));
        }

        static void Warehouse(MaterialLibrary M, Ctx c, InteriorInfo info, float hw, float front, float d, DeterministicRandom rng)
        {
            var mb = c.Mb;
            int rack = c.Sub(M.Paint("rack", new Color(0.15f, 0.3f, 0.6f), null, 1f, 0.4f));
            int beam = c.Sub(M.Paint("rackbeam", new Color(0.95f, 0.5f, 0.1f), null, 1f, 0.4f));
            int box = c.Sub(M.Paint("carton", new Color(0.7f, 0.55f, 0.35f), null, 1f, 0.1f));
            int pallet = c.Sub(M.Wood);
            for (float x = -hw + 2f; x < hw - 1.5f; x += 4.5f)
            {
                if (Mathf.Abs(x) < 2.6f) continue; // проход от ворот
                for (float z = front + 4f; z < d - 1f; z += 6f)
                {
                    for (int lv = 0; lv < 3; lv++)
                    {
                        float y = 0.1f + lv * 1.9f;
                        mb.AddBox(beam, new Vector3(x, y + 0.05f, z), new Vector3(1.4f, 0.1f, 5f));
                        for (float zz = z - 2f; zz <= z + 2f; zz += 1.3f)
                        {
                            if (!rng.Chance(0.8)) continue;
                            mb.AddBox(pallet, new Vector3(x, y + 0.17f, zz), new Vector3(1.1f, 0.14f, 1.1f));
                            mb.AddBox(box, new Vector3(x, y + 0.24f + 0.5f, zz), new Vector3(1f, rng.Range(0.7f, 1.2f), 1f));
                        }
                    }
                    foreach (float sx in new[] { -0.7f, 0.7f }) foreach (float sz in new[] { -2.5f, 2.5f })
                        mb.AddBox(rack, new Vector3(x + sx, 2.9f, z + sz), new Vector3(0.1f, 5.8f, 0.1f));
                    c.Collider(new Vector3(x, 2.9f, z), new Vector3(1.5f, 5.8f, 5.1f));
                }
            }
            // Погрузчик
            var fl = new Vector3(1.5f, 0, front + 3f);
            mb.AddBox(c.Sub(M.Paint("forklift", new Color(0.95f, 0.75f, 0.1f), null, 1f, 0.4f)), fl + new Vector3(0, 0.7f, 0), new Vector3(1.1f, 1f, 1.8f));
            mb.AddBox(c.Sub(M.MetalDark), fl + new Vector3(0, 1.4f, 1.2f), new Vector3(0.9f, 2.4f, 0.1f));
            mb.AddBox(c.Sub(M.MetalDark), fl + new Vector3(0, 0.1f, 1.6f), new Vector3(0.8f, 0.05f, 1f));
            c.Collider(fl + new Vector3(0, 0.7f, 0.3f), new Vector3(1.1f, 1.4f, 2.4f));
            info.WorkPoints.Add(new Vector4(-1.6f, 0, front + 2.5f, 180f));
            info.WorkPoints.Add(new Vector4(2.8f, 0, front + 5f, -90f));
            info.WorkPoints.Add(new Vector4(-3f, 0, front + 7f, 90f));
        }

        static void Vacant(MaterialLibrary M, Ctx c, float hw, float front, float d, DeterministicRandom rng)
        {
            var mb = c.Mb;
            // Ремонт: стремянка, вёдра краски, плёнка, коробки
            var p = new Vector3(rng.Range(-hw + 1f, hw - 1f), 0, (front + d) * 0.5f);
            int alu = c.Sub(M.Aluminium);
            mb.AddBox(alu, p + new Vector3(-0.25f, 0.9f, 0), new Vector3(0.04f, 1.8f, 0.05f), 0f);
            mb.AddBox(alu, p + new Vector3(0.25f, 0.9f, 0), new Vector3(0.04f, 1.8f, 0.05f), 0f);
            for (int i = 0; i < 5; i++) mb.AddBox(alu, p + new Vector3(0, 0.3f + i * 0.33f, 0), new Vector3(0.5f, 0.03f, 0.08f));
            for (int i = 0; i < 3; i++)
                mb.AddCylinder(c.Sub(M.Paint("bucket", Color.HSVToRGB(rng.NextFloat(), 0.5f, 0.85f), null, 1f, 0.4f)), new Vector3(rng.Range(-hw + 0.5f, hw - 0.5f), 0, d - rng.Range(0.5f, 2f)), 0.15f, 0.3f, 10);
            for (int i = 0; i < 4; i++)
                mb.AddBox(c.Sub(M.Paint("carton", new Color(0.7f, 0.55f, 0.35f), null, 1f, 0.1f)), new Vector3(-hw + 0.6f + i * 0.7f, 0.25f + (i % 2) * 0.5f, d - 0.6f), new Vector3(0.6f, 0.5f, 0.6f));
            mb.AddBox(c.Sub(M.Glass("plastic", new Color(0.85f, 0.9f, 0.9f, 0.35f), 0.7f)), new Vector3(0, 1.5f, d - 0.25f), new Vector3(hw * 1.5f, 3f, 0.02f));
        }

        static void Decor(MaterialLibrary M, Ctx c, float hw, float front, float d, float h, int level, DeterministicRandom rng)
        {
            var mb = c.Mb;
            for (int i = 0; i < level; i++)
            {
                float x = -hw + 1f + i * 1.8f;
                if (x > hw - 0.8f) break;
                mb.AddBox(c.Sub(M.Wood), new Vector3(x, 2.1f, d - 0.03f), new Vector3(0.9f, 0.65f, 0.03f));
                mb.AddBox(c.Sub(M.Paint("art", Color.HSVToRGB(rng.NextFloat(), 0.5f, 0.7f), null, 1f, 0.3f)), new Vector3(x, 2.1f, d - 0.05f), new Vector3(0.78f, 0.53f, 0.02f));
            }
            Plant(M, c, new Vector3(hw - 0.4f, 0, d - 0.4f), 1.1f);
            if (level >= 3)
            {
                // Подвесные светильники над залом
                int lamp = c.Sub(M.Emission("pendant", new Color(1f, 0.85f, 0.6f), 2.5f));
                for (float x = -hw + 1.5f; x < hw - 1f; x += 2.2f)
                {
                    mb.AddCylinder(c.Sub(M.MetalDark), new Vector3(x, h - 0.9f, front + 2f), 0.01f, 0.9f, 4, false);
                    mb.AddCylinder(lamp, new Vector3(x, h - 1.1f, front + 2f), 0.18f, 0.2f, 10, true, 0.05f);
                }
            }
        }

        static void CeilingLights(MaterialLibrary M, Ctx c, float hw, float front, float d, float h, BusinessType type)
        {
            int panel = c.Sub(M.Emission("ceiling_light", new Color(1f, 0.97f, 0.9f), type == BusinessType.None ? 0.6f : 2.2f));
            for (float z = front + 1.5f; z < d - 0.5f; z += 3f)
                for (float x = -hw + 1.5f; x < hw - 0.5f; x += 3f)
                    c.Mb.AddBox(panel, new Vector3(x, h - 0.02f, z), new Vector3(1.2f, 0.03f, 0.6f));
        }

        // ---------- Лобби бизнес-центра и летняя веранда ----------

        public static GameObject BuildLobby(MaterialLibrary M, Transform parent, float width, float height, float depth)
        {
            var go = new GameObject("LobbyInterior");
            go.transform.SetParent(parent, false);
            var c = new Ctx { Colliders = new GameObject("Colliders") };
            c.Colliders.transform.SetParent(go.transform, false);
            float hw = width * 0.5f - 0.3f, front = 0.42f, d = depth - 0.3f, h = height - 0.05f;
            var style = new RoomStyle
            {
                Floor = M.Paint("floor_marble", new Color(0.85f, 0.84f, 0.82f), "tiles", 2f, 0.85f),
                Walls = M.Paint("wall", new Color(0.82f, 0.8f, 0.76f), "plaster", 3f, 0.1f),
                Wainscot = M.Paint("granite", new Color(0.2f, 0.2f, 0.22f), "concrete", 2f, 0.6f),
                Ceiling = M.Paint("ceiling", new Color(0.92f, 0.92f, 0.9f), null, 1f, 0.1f),
            };
            Shell(c, style, hw, front, d, h, height - 0.5f);
            Counter(M, c, new Vector3(0, 0, 3.6f), 3.2f, new Color(0.2f, 0.2f, 0.22f));
            var mb = c.Mb;
            // Лифты и табло
            int steel = c.Sub(M.Aluminium);
            for (int i = -1; i <= 1; i += 2) mb.AddBox(steel, new Vector3(i * 1.6f, 1.2f, d - 0.05f), new Vector3(1.2f, 2.4f, 0.05f));
            mb.AddBox(c.Sub(M.Dark), new Vector3(-hw + 1.2f, 1.6f, 0.55f), new Vector3(2.4f, 1.6f, 0.05f));
            // Диваны и растения
            int sofa = c.Sub(M.Paint("sofa", new Color(0.25f, 0.3f, 0.38f), "carpet", 0.5f, 0.1f));
            mb.AddBox(sofa, new Vector3(hw - 1.4f, 0.25f, 2.2f), new Vector3(2f, 0.5f, 0.8f));
            mb.AddBox(sofa, new Vector3(hw - 1.4f, 0.6f, 2.55f), new Vector3(2f, 0.6f, 0.15f));
            c.Collider(new Vector3(hw - 1.4f, 0.4f, 2.3f), new Vector3(2f, 0.8f, 0.9f));
            Plant(M, c, new Vector3(-hw + 0.5f, 0, d - 0.5f), 1.4f);
            Plant(M, c, new Vector3(hw - 0.5f, 0, d - 0.5f), 1.4f);
            CeilingLights(M, c, hw, front, d, h, BusinessType.Office);
            var meshGo = new GameObject("Mesh");
            meshGo.transform.SetParent(go.transform, false);
            meshGo.AddComponent<MeshFilter>().sharedMesh = mb.Build("Lobby");
            meshGo.AddComponent<MeshRenderer>().sharedMaterials = c.Mats.ToArray();
            return go;
        }

        public static void BuildTerrace(MaterialLibrary M, Transform root, float width, BusinessType type)
        {
            var c = new Ctx { Colliders = new GameObject("Colliders") };
            c.Colliders.transform.SetParent(root, false);
            var rng = new DeterministicRandom((ulong)(width * 100));
            var mb = c.Mb;
            float zc = -1.6f;
            int fence = c.Sub(type == BusinessType.Restaurant ? M.Wood : M.MetalDark);
            float half = width * 0.5f - 0.4f;
            // Ограждение веранды с разрывом напротив двери
            float fz = zc - 0.75f;
            mb.AddBox(fence, new Vector3((-half - 1.2f) * 0.5f, 0.45f, fz), new Vector3(half - 1.2f, 0.9f, 0.05f));
            mb.AddBox(fence, new Vector3((half + 1.2f) * 0.5f, 0.45f, fz), new Vector3(half - 1.2f, 0.9f, 0.05f));
            c.Collider(new Vector3((-half - 1.2f) * 0.5f, 0.45f, fz), new Vector3(half - 1.2f, 0.9f, 0.1f));
            c.Collider(new Vector3((half + 1.2f) * 0.5f, 0.45f, fz), new Vector3(half - 1.2f, 0.9f, 0.1f));
            int umbrella = c.Sub(M.Paint("umbrella", type == BusinessType.Restaurant ? new Color(0.9f, 0.88f, 0.8f) : new Color(0.2f, 0.4f, 0.3f), null, 1f, 0.05f));
            for (float x = -half + 1f; x < -1.4f; x += 2.2f)
            {
                Table(M, c, new Vector3(x, 0, zc), 0.35f, type == BusinessType.Restaurant, 2, rng);
                mb.AddCylinder(c.Sub(M.Aluminium), new Vector3(x, 0.75f, zc), 0.02f, 1.6f, 4, false);
                mb.AddCylinder(umbrella, new Vector3(x, 2.1f, zc), 1.1f, 0.35f, 10, true, 0.02f);
            }
            for (float x = 1.6f; x < half - 0.6f; x += 2.2f)
            {
                Table(M, c, new Vector3(x, 0, zc), 0.35f, type == BusinessType.Restaurant, 2, rng);
                mb.AddCylinder(c.Sub(M.Aluminium), new Vector3(x, 0.75f, zc), 0.02f, 1.6f, 4, false);
                mb.AddCylinder(umbrella, new Vector3(x, 2.1f, zc), 1.1f, 0.35f, 10, true, 0.02f);
            }
            // Кашпо по краям веранды
            Plant(M, c, new Vector3(-half, 0, fz), 0.9f);
            Plant(M, c, new Vector3(half, 0, fz), 0.9f);
            var go = new GameObject("TerraceMesh");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mb.Build("Terrace");
            go.AddComponent<MeshRenderer>().sharedMaterials = c.Mats.ToArray();
        }
    }
}

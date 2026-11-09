using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>
    /// Строительный набор ТЦ: собирает статичную архитектуру в несколько крупных мешей (по этажам и участкам 36 м
    /// для отсечения по видимости), раздаёт упрощённые BoxCollider и создаёт типовые элементы — ограждения,
    /// колонны, эскалаторы, лифт, светильники.
    /// </summary>
    public sealed class MallKit
    {
        const float ChunkSize = 36f;

        sealed class Chunk
        {
            public readonly MeshBuilder Mb = new MeshBuilder();
            public readonly List<Material> Mats = new List<Material>();
            public int Sub(Material m) { int i = Mats.IndexOf(m); if (i >= 0) return i; Mats.Add(m); return Mats.Count - 1; }
        }

        public readonly MaterialLibrary M;
        public readonly Transform Root;
        public readonly Transform Static;
        public readonly Transform Colliders;
        public readonly InstancedRenderer Instanced;
        readonly Dictionary<(int, int, bool), Chunk> _chunks = new Dictionary<(int, int, bool), Chunk>();

        public MallKit(MaterialLibrary m, Transform root)
        {
            M = m;
            Root = root;
            Static = new GameObject("Static").transform;
            Static.SetParent(root, false);
            Colliders = new GameObject("Colliders").transform;
            Colliders.SetParent(Static, false);
            Instanced = root.gameObject.AddComponent<InstancedRenderer>();
        }

        Chunk C(int floor, float x, bool shadows)
        {
            var key = (floor, Mathf.FloorToInt(x / ChunkSize), shadows);
            if (!_chunks.TryGetValue(key, out var c)) _chunks[key] = c = new Chunk();
            return c;
        }

        static int FloorOf(float y) => Mathf.Clamp(Mathf.FloorToInt((y + 0.5f) / 6f), 0, 9);

        // ---------------- Примитивы ----------------

        /// <summary>Горизонтальная плита (верх на высоте top), UV в метрах.</summary>
        public void Slab(Rect r, float top, float thickness, Material topMat, Material bottomMat, Material edgeMat, bool collider = true)
        {
            var c = C(FloorOf(top), r.center.x, true);
            var mb = c.Mb;
            int t = c.Sub(topMat), b = c.Sub(bottomMat), e = c.Sub(edgeMat);
            float y0 = top - thickness;
            mb.AddQuad(t, new Vector3(r.xMin, top, r.yMin), new Vector3(r.xMin, top, r.yMax), new Vector3(r.xMax, top, r.yMax), new Vector3(r.xMax, top, r.yMin),
                new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin));
            if (thickness > 0f)
            {
                mb.AddQuad(b, new Vector3(r.xMin, y0, r.yMax), new Vector3(r.xMin, y0, r.yMin), new Vector3(r.xMax, y0, r.yMin), new Vector3(r.xMax, y0, r.yMax),
                    new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax));
                // Торцы плиты (видны со стороны атриума)
                mb.AddQuad(e, new Vector3(r.xMin, y0, r.yMin), new Vector3(r.xMin, top, r.yMin), new Vector3(r.xMax, top, r.yMin), new Vector3(r.xMax, y0, r.yMin));
                mb.AddQuad(e, new Vector3(r.xMax, y0, r.yMax), new Vector3(r.xMax, top, r.yMax), new Vector3(r.xMin, top, r.yMax), new Vector3(r.xMin, y0, r.yMax));
                mb.AddQuad(e, new Vector3(r.xMin, y0, r.yMax), new Vector3(r.xMin, top, r.yMax), new Vector3(r.xMin, top, r.yMin), new Vector3(r.xMin, y0, r.yMin));
                mb.AddQuad(e, new Vector3(r.xMax, y0, r.yMin), new Vector3(r.xMax, top, r.yMin), new Vector3(r.xMax, top, r.yMax), new Vector3(r.xMax, y0, r.yMax));
            }
            if (collider) Box(new Vector3(r.center.x, top - Mathf.Max(thickness, 0.3f) * 0.5f, r.center.y), new Vector3(r.width, Mathf.Max(thickness, 0.3f), r.height));
        }

        /// <summary>Потолок (смотрит вниз) с панелями света через каждые spacing метров.</summary>
        public void Ceiling(Rect r, float y, float lightSpacing, MallInstance inst, bool anchors = true)
        {
            var c = C(FloorOf(y - 1f), r.center.x, false);
            int ceil = c.Sub(M.Ceiling), light = c.Sub(M.CeilingLight);
            c.Mb.AddQuad(ceil, new Vector3(r.xMin, y, r.yMax), new Vector3(r.xMin, y, r.yMin), new Vector3(r.xMax, y, r.yMin), new Vector3(r.xMax, y, r.yMax),
                new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax));
            if (lightSpacing <= 0f) return;
            for (float x = r.xMin + lightSpacing * 0.5f; x < r.xMax; x += lightSpacing)
            {
                c.Mb.AddBox(light, new Vector3(x, y - 0.02f, r.center.y), new Vector3(1.6f, 0.04f, Mathf.Min(0.5f, r.height * 0.4f)));
                if (anchors && inst != null) inst.LightAnchors.Add(new Vector3(x, y - 0.3f, r.center.y));
            }
        }

        /// <summary>Вертикальная стена от a до b (смотрит вправо от направления a→b, т.е. normal = cross(up, b-a)).</summary>
        public void Wall(Vector3 a, Vector3 b, float height, Material mat, bool collider = true, float thickness = 0.3f, bool shadows = true)
        {
            var c = C(FloorOf(a.y), (a.x + b.x) * 0.5f, shadows);
            int s = c.Sub(mat);
            var up = Vector3.up * height;
            // Порядок a → a+up → b+up → b даёт лицевую грань, смотрящую вправо от направления a→b
            c.Mb.AddQuad(s, a, a + up, b + up, b, UvW(a, 0), UvW(a, height), UvW(b, height), UvW(b, 0));
            if (collider)
            {
                var dir = b - a;
                float len = dir.magnitude;
                if (len < 0.01f) return;
                var right = Vector3.Cross(Vector3.up, dir / len);
                var center = (a + b) * 0.5f + up * 0.5f - right * thickness * 0.5f;
                RotatedBox(center, new Vector3(thickness, height, len), Quaternion.LookRotation(dir / len, Vector3.up));
            }
        }

        static Vector2 UvW(Vector3 p, float v) => new Vector2(p.x + p.z, p.y + v);

        /// <summary>Двусторонняя стена — две встречные грани.</summary>
        public void WallBoth(Vector3 a, Vector3 b, float height, Material mat, bool collider = true)
        {
            Wall(a, b, height, mat, collider);
            Wall(b, a, height, mat, false);
        }

        public void Block(Vector3 center, Vector3 size, Material mat, float yaw = 0f, bool collider = true, bool shadows = true)
        {
            var c = C(FloorOf(center.y - size.y * 0.5f), center.x, shadows);
            c.Mb.AddBox(c.Sub(mat), center, size, yaw, true);
            if (collider) RotatedBox(center, size, Quaternion.Euler(0, yaw, 0));
        }

        public void Cylinder(Vector3 basePos, float radius, float height, Material mat, int segments = 16, bool collider = true)
        {
            var c = C(FloorOf(basePos.y), basePos.x, true);
            c.Mb.AddCylinder(c.Sub(mat), basePos, radius, height, segments);
            if (!collider) return;
            var go = new GameObject("Col");
            go.transform.SetParent(Colliders, false);
            var cc = go.AddComponent<CapsuleCollider>();
            cc.center = basePos + Vector3.up * height * 0.5f;
            cc.radius = radius;
            cc.height = height;
        }

        public void Quad(Material mat, Vector3 a, Vector3 b, Vector3 cc, Vector3 d, bool unitUv = false, bool shadows = false)
        {
            var c = C(FloorOf(Mathf.Min(a.y, cc.y)), (a.x + cc.x) * 0.5f, shadows);
            if (unitUv) c.Mb.AddQuadUnit(c.Sub(mat), a, b, cc, d);
            else c.Mb.AddQuad(c.Sub(mat), a, b, cc, d);
        }

        public void Box(Vector3 center, Vector3 size)
        {
            var go = new GameObject("Col");
            go.transform.SetParent(Colliders, false);
            go.transform.localPosition = center;
            go.AddComponent<BoxCollider>().size = size;
        }

        public void RotatedBox(Vector3 center, Vector3 size, Quaternion rot)
        {
            var go = new GameObject("Col");
            go.transform.SetParent(Colliders, false);
            go.transform.localPosition = center;
            go.transform.localRotation = rot;
            go.AddComponent<BoxCollider>().size = size;
        }

        // ---------------- Типовые элементы ----------------

        /// <summary>Стеклянное ограждение с поручнем вдоль отрезка (на краю атриума).</summary>
        public void Railing(Vector3 a, Vector3 b)
        {
            var dir = b - a;
            float len = dir.magnitude;
            if (len < 0.2f) return;
            var fwd = dir / len;
            float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            var mid = (a + b) * 0.5f;
            var c = C(FloorOf(a.y), mid.x, false);
            int glass = c.Sub(M.RailGlass), steel = c.Sub(M.Aluminium);
            c.Mb.AddBox(glass, mid + Vector3.up * 0.55f, new Vector3(0.02f, 0.95f, len), yaw);
            c.Mb.AddBox(steel, mid + Vector3.up * 1.08f, new Vector3(0.08f, 0.06f, len), yaw);
            c.Mb.AddBox(steel, mid + Vector3.up * 0.04f, new Vector3(0.06f, 0.08f, len), yaw);
            for (float t = 0f; t <= len + 0.01f; t += 1.5f)
                c.Mb.AddBox(steel, a + fwd * Mathf.Min(t, len) + Vector3.up * 0.55f, new Vector3(0.05f, 1.1f, 0.05f), yaw);
            // Невидимая преграда выше прыжка
            RotatedBox(mid + Vector3.up * 0.9f, new Vector3(0.15f, 1.8f, len), Quaternion.Euler(0, yaw, 0));
        }

        public void Column(Vector3 basePos, float height)
        {
            Cylinder(basePos, 0.45f, height, M.ColumnStone, 20);
            var c = C(FloorOf(basePos.y), basePos.x, true);
            for (float y = 0f; y < height - 1f; y += 6f)
            {
                c.Mb.AddCylinder(c.Sub(M.Aluminium), basePos + Vector3.up * y, 0.52f, 0.12f, 20);
                c.Mb.AddCylinder(c.Sub(M.Aluminium), basePos + Vector3.up * (y + 5.4f), 0.5f, 0.1f, 20);
            }
        }

        public GameObject MeshObject(Transform parent, string name, MeshBuilder mb, Material[] mats, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            GeneratedMesh.Assign(go, mb.Build(name));
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            // Стены и потолки — одиночные грани: тень от солнца через фонарь должна отбрасываться обеими сторонами
            mr.shadowCastingMode = shadows ? ShadowCastingMode.TwoSided : ShadowCastingMode.Off;
            return go;
        }

        /// <summary>Эскалатор от bottom до top (оба — точки на уровне пола у начала/конца наклона).</summary>
        public Escalator Escalator(Vector3 bottom, Vector3 top, bool goingUp, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Static, false);
            var esc = go.AddComponent<Escalator>();
            esc.Build(this, bottom, top, goingUp);
            return esc;
        }

        /// <summary>Сбрасывает накопленные меши в объекты сцены.</summary>
        public void Flush()
        {
            foreach (var kv in _chunks)
            {
                var (floor, chunk, shadows) = kv.Key;
                if (kv.Value.Mb.IsEmpty) continue;
                MeshObject(Static, $"Arch_F{floor}_C{chunk}{(shadows ? "" : "_ns")}", kv.Value.Mb, kv.Value.Mats.ToArray(), shadows);
            }
            _chunks.Clear();
        }
    }
}

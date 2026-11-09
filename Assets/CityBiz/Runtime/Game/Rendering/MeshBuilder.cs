using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>
    /// Собирает геометрию из примитивов в один меш с подмешами по материалам.
    /// Здание из сотен деталей становится одним рендерером — это главный приём против draw calls.
    /// UV проецируются по мировым метрам, поэтому текстуры кирпича/плитки имеют одинаковый масштаб везде.
    /// </summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> _v = new List<Vector3>(1024);
        readonly List<Vector3> _n = new List<Vector3>(1024);
        readonly List<Vector2> _uv = new List<Vector2>(1024);
        readonly List<List<int>> _tris = new List<List<int>>();

        public Matrix4x4 Transform = Matrix4x4.identity;
        public float UvScale = 1f; // метров на один повтор текстуры

        public int VertexCount => _v.Count;
        public bool IsEmpty => _v.Count == 0;

        List<int> Sub(int submesh)
        {
            while (_tris.Count <= submesh) _tris.Add(new List<int>());
            return _tris[submesh];
        }

        public void AddQuad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
        {
            a = Transform.MultiplyPoint3x4(a); b = Transform.MultiplyPoint3x4(b);
            c = Transform.MultiplyPoint3x4(c); d = Transform.MultiplyPoint3x4(d);
            var normal = Vector3.Cross(b - a, d - a).normalized;
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            _n.Add(normal); _n.Add(normal); _n.Add(normal); _n.Add(normal);
            _uv.Add(uvA); _uv.Add(uvB); _uv.Add(uvC); _uv.Add(uvD);
            var t = Sub(sub);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        /// <summary>Квад a→b→c→d по часовой стрелке, если смотреть с лицевой стороны; UV по мировым метрам.</summary>
        public void AddQuad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            float w = Vector3.Distance(a, d), h = Vector3.Distance(a, b);
            float s = 1f / UvScale;
            Vector2 o = PlanarOffset(a);
            AddQuad(sub, a, b, c, d, o, o + new Vector2(0, h * s), o + new Vector2(w * s, h * s), o + new Vector2(w * s, 0));
        }

        Vector2 PlanarOffset(Vector3 local)
        {
            var p = Transform.MultiplyPoint3x4(local);
            return new Vector2((p.x + p.z) / UvScale, p.y / UvScale);
        }

        /// <summary>Квад с UV 0..1 — для вывесок, витрин, экранов.</summary>
        public void AddQuadUnit(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d) =>
            AddQuad(sub, a, b, c, d, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));

        /// <summary>Треугольник a→b→c по часовой стрелке с лицевой стороны (фронтоны крыш).</summary>
        public void AddTriangle(int sub, Vector3 a, Vector3 b, Vector3 c)
        {
            var wa = Transform.MultiplyPoint3x4(a);
            var wb = Transform.MultiplyPoint3x4(b);
            var wc = Transform.MultiplyPoint3x4(c);
            var normal = Vector3.Cross(wb - wa, wc - wa).normalized;
            int i = _v.Count;
            _v.Add(wa); _v.Add(wb); _v.Add(wc);
            _n.Add(normal); _n.Add(normal); _n.Add(normal);
            float s = 1f / UvScale;
            _uv.Add(new Vector2((wa.x + wa.z) * s, wa.y * s));
            _uv.Add(new Vector2((wb.x + wb.z) * s, wb.y * s));
            _uv.Add(new Vector2((wc.x + wc.z) * s, wc.y * s));
            var t = Sub(sub);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        /// <summary>Коробка по центру и размеру (в локальных координатах Transform), с опциональным поворотом вокруг Y.</summary>
        public void AddBox(int sub, Vector3 center, Vector3 size, float yaw = 0f, bool bottom = false)
        {
            var saved = Transform;
            Transform = saved * Matrix4x4.TRS(center, Quaternion.Euler(0, yaw, 0), Vector3.one);
            Vector3 h = size * 0.5f;
            Vector3 p000 = new Vector3(-h.x, -h.y, -h.z), p100 = new Vector3(h.x, -h.y, -h.z);
            Vector3 p010 = new Vector3(-h.x, h.y, -h.z), p110 = new Vector3(h.x, h.y, -h.z);
            Vector3 p001 = new Vector3(-h.x, -h.y, h.z), p101 = new Vector3(h.x, -h.y, h.z);
            Vector3 p011 = new Vector3(-h.x, h.y, h.z), p111 = new Vector3(h.x, h.y, h.z);
            BoxFace(sub, p000, p010, p110, p100, size.x, size.y); // фронт (-Z)
            BoxFace(sub, p101, p111, p011, p001, size.x, size.y); // тыл (+Z)
            BoxFace(sub, p001, p011, p010, p000, size.z, size.y); // лево (-X)
            BoxFace(sub, p100, p110, p111, p101, size.z, size.y); // право (+X)
            BoxFace(sub, p010, p011, p111, p110, size.x, size.z); // верх
            if (bottom) BoxFace(sub, p001, p000, p100, p101, size.x, size.z);
            Transform = saved;
        }

        void BoxFace(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float w, float h)
        {
            var wa = Transform.MultiplyPoint3x4(a);
            float s = 1f / UvScale;
            // Проекция UV по доминирующей оси нормали — текстура «ложится» одинаково на соседние детали.
            var n = Vector3.Cross(Transform.MultiplyPoint3x4(b) - wa, Transform.MultiplyPoint3x4(d) - wa);
            Vector2 Project(Vector3 local)
            {
                var p = Transform.MultiplyPoint3x4(local);
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                if (ay >= ax && ay >= az) return new Vector2(p.x * s, p.z * s);
                if (ax >= az) return new Vector2(p.z * s, p.y * s);
                return new Vector2(p.x * s, p.y * s);
            }
            AddQuad(sub, a, b, c, d, Project(a), Project(b), Project(c), Project(d));
        }

        /// <summary>Цилиндр вдоль Y от base до base+height.</summary>
        public void AddCylinder(int sub, Vector3 basePos, float radius, float height, int segments = 10, bool caps = true, float topRadius = -1f)
        {
            if (topRadius < 0f) topRadius = radius;
            int start = _v.Count;
            var t = Sub(sub);
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var nrm = Transform.MultiplyVector(new Vector3(dir.x, (radius - topRadius) / Mathf.Max(0.01f, height), dir.z)).normalized;
                _v.Add(Transform.MultiplyPoint3x4(basePos + dir * radius));
                _v.Add(Transform.MultiplyPoint3x4(basePos + dir * topRadius + Vector3.up * height));
                _n.Add(nrm); _n.Add(nrm);
                float u = i / (float)segments * radius * Mathf.PI * 2f / UvScale;
                _uv.Add(new Vector2(u, 0)); _uv.Add(new Vector2(u, height / UvScale));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = start + i * 2;
                t.Add(a); t.Add(a + 1); t.Add(a + 3);
                t.Add(a); t.Add(a + 3); t.Add(a + 2);
            }
            if (!caps) return;
            AddDisc(sub, basePos + Vector3.up * height, topRadius, segments, true);
            AddDisc(sub, basePos, radius, segments, false);
        }

        public void AddDisc(int sub, Vector3 center, float radius, int segments, bool up)
        {
            int c = _v.Count;
            var nrm = Transform.MultiplyVector(up ? Vector3.up : Vector3.down).normalized;
            _v.Add(Transform.MultiplyPoint3x4(center)); _n.Add(nrm); _uv.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                _v.Add(Transform.MultiplyPoint3x4(center + dir * radius)); _n.Add(nrm);
                _uv.Add(new Vector2(0.5f + dir.x * 0.5f, 0.5f + dir.z * 0.5f));
            }
            var t = Sub(sub);
            for (int i = 0; i < segments; i++)
            {
                if (up) { t.Add(c); t.Add(c + i + 2); t.Add(c + i + 1); }
                else { t.Add(c); t.Add(c + i + 1); t.Add(c + i + 2); }
            }
        }

        /// <summary>Низкополигональная сфера (эллипсоид) — кроны деревьев, головы, кусты.</summary>
        public void AddSphere(int sub, Vector3 center, Vector3 radii, int rings = 6, int segments = 8)
        {
            int start = _v.Count;
            for (int r = 0; r <= rings; r++)
            {
                float phi = r / (float)rings * Mathf.PI;
                for (int s = 0; s <= segments; s++)
                {
                    float theta = s / (float)segments * Mathf.PI * 2f;
                    var unit = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                    var local = center + Vector3.Scale(unit, radii);
                    _v.Add(Transform.MultiplyPoint3x4(local));
                    _n.Add(Transform.MultiplyVector(new Vector3(unit.x / radii.x, unit.y / radii.y, unit.z / radii.z)).normalized);
                    _uv.Add(new Vector2(s / (float)segments * 2f, r / (float)rings));
                }
            }
            var t = Sub(sub);
            int stride = segments + 1;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = start + r * stride + s, b = a + stride;
                    t.Add(a); t.Add(a + 1); t.Add(b + 1);
                    t.Add(a); t.Add(b + 1); t.Add(b);
                }
        }

        /// <summary>Тонкая рама из четырёх брусков вокруг прямоугольника на плоскости XY (лицом к -Z).</summary>
        public void AddFrame(int sub, Vector3 center, float width, float height, float thickness, float depth)
        {
            AddBox(sub, center + new Vector3(0, height * 0.5f - thickness * 0.5f, 0), new Vector3(width, thickness, depth));
            AddBox(sub, center + new Vector3(0, -height * 0.5f + thickness * 0.5f, 0), new Vector3(width, thickness, depth));
            AddBox(sub, center + new Vector3(-width * 0.5f + thickness * 0.5f, 0, 0), new Vector3(thickness, height - thickness * 2f, depth));
            AddBox(sub, center + new Vector3(width * 0.5f - thickness * 0.5f, 0, 0), new Vector3(thickness, height - thickness * 2f, depth));
        }

        public int SubmeshCount => _tris.Count;

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            if (_v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(_v);
            mesh.SetNormals(_n);
            mesh.SetUVs(0, _uv);
            mesh.subMeshCount = _tris.Count;
            for (int i = 0; i < _tris.Count; i++) mesh.SetTriangles(_tris[i], i, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Clear()
        {
            _v.Clear(); _n.Clear(); _uv.Clear();
            foreach (var t in _tris) t.Clear();
            Transform = Matrix4x4.identity;
        }
    }
}

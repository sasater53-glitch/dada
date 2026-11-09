using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>
    /// Строит здание по BuildingSpec: фасады с настоящими нишами окон, витрины, вывески, входы, балконы,
    /// карнизы и крыши. Вся статичная геометрия здания — один меш (LOD0) + упрощённый LOD1 + LODGroup.
    /// Коллайдеры — несколько BoxCollider вместо MeshCollider.
    /// </summary>
    public sealed class BuildingFactory
    {
        readonly MaterialLibrary M;

        public BuildingFactory(MaterialLibrary materials) { M = materials; }

        // ---------- Контекст сборки одного меша ----------

        sealed class Ctx
        {
            public readonly MeshBuilder Mb = new MeshBuilder { UvScale = 1f };
            public readonly List<Material> Mats = new List<Material>();
            public int Sub(Material m)
            {
                int i = Mats.IndexOf(m);
                if (i >= 0) return i;
                Mats.Add(m);
                return Mats.Count - 1;
            }

            /// <summary>Отдельный подмеш даже для уже использованного материала — его можно переключать независимо.</summary>
            public int UniqueSub(Material m)
            {
                Mats.Add(m);
                return Mats.Count - 1;
            }
        }

        /// <summary>Система координат фасада: u — вправо (если смотреть снаружи), v — вверх, w — вглубь здания.</summary>
        struct Face
        {
            public Vector3 Origin, U, W;
            public Vector3 P(float u, float v, float w = 0f) => Origin + U * u + Vector3.up * v + W * w;
        }

        struct Opening
        {
            public float U0, U1, V0, V1, Depth;
            public int GlassSub;   // -1 — без стекла (дверной проём)
            public bool UnitUv;
            public Opening(float u0, float u1, float v0, float v1, float depth, int glassSub, bool unitUv = true)
            { U0 = u0; U1 = u1; V0 = v0; V1 = v1; Depth = depth; GlassSub = glassSub; UnitUv = unitUv; }
        }

        static Face FrontFace(BuildingSpec s) => new Face { Origin = Vector3.zero, U = Vector3.right, W = Vector3.forward };
        static Face BackFace(BuildingSpec s) => new Face { Origin = new Vector3(s.Width, 0, s.Depth), U = Vector3.left, W = Vector3.back };
        static Face LeftFace(BuildingSpec s) => new Face { Origin = new Vector3(0, 0, s.Depth), U = Vector3.back, W = Vector3.right };
        static Face RightFace(BuildingSpec s) => new Face { Origin = new Vector3(s.Width, 0, 0), U = Vector3.forward, W = Vector3.left };

        /// <summary>
        /// Стена с прямоугольными проёмами: плоскость режется на горизонтальные полосы, в каждой — участки между проёмами.
        /// Проёмы получают откосы глубиной Depth и стекло в глубине.
        /// </summary>
        void Wall(Ctx c, Face f, float width, float v0, float v1, List<Opening> openings, int wallSub, int revealSub)
        {
            var ys = new List<float> { v0, v1 };
            foreach (var o in openings) { ys.Add(Mathf.Clamp(o.V0, v0, v1)); ys.Add(Mathf.Clamp(o.V1, v0, v1)); }
            ys.Sort();
            for (int i = 0; i < ys.Count - 1; i++)
            {
                float y0 = ys[i], y1 = ys[i + 1];
                if (y1 - y0 < 0.001f) continue;
                float ym = (y0 + y1) * 0.5f;
                var gaps = new List<Vector2>();
                foreach (var o in openings) if (o.V0 < ym && o.V1 > ym) gaps.Add(new Vector2(o.U0, o.U1));
                gaps.Sort((a, b) => a.x.CompareTo(b.x));
                float x = 0f;
                foreach (var g in gaps)
                {
                    if (g.x > x + 0.001f) WallQuad(c, f, x, g.x, y0, y1, wallSub);
                    x = Mathf.Max(x, g.y);
                }
                if (x < width - 0.001f) WallQuad(c, f, x, width, y0, y1, wallSub);
            }
            foreach (var o in openings) Reveal(c, f, o, revealSub);
        }

        void WallQuad(Ctx c, Face f, float u0, float u1, float v0, float v1, int sub) =>
            c.Mb.AddQuad(sub, f.P(u0, v0), f.P(u0, v1), f.P(u1, v1), f.P(u1, v0));

        void Reveal(Ctx c, Face f, Opening o, int sub)
        {
            float r = o.Depth;
            var mb = c.Mb;
            mb.AddQuad(sub, f.P(o.U0, o.V0, 0), f.P(o.U0, o.V1, 0), f.P(o.U0, o.V1, r), f.P(o.U0, o.V0, r));
            mb.AddQuad(sub, f.P(o.U1, o.V0, r), f.P(o.U1, o.V1, r), f.P(o.U1, o.V1, 0), f.P(o.U1, o.V0, 0));
            mb.AddQuad(sub, f.P(o.U0, o.V1, r), f.P(o.U0, o.V1, 0), f.P(o.U1, o.V1, 0), f.P(o.U1, o.V1, r));
            mb.AddQuad(sub, f.P(o.U0, o.V0, 0), f.P(o.U0, o.V0, r), f.P(o.U1, o.V0, r), f.P(o.U1, o.V0, 0));
            if (o.GlassSub < 0) return;
            var a = f.P(o.U0, o.V0, r); var b = f.P(o.U0, o.V1, r); var cc = f.P(o.U1, o.V1, r); var d = f.P(o.U1, o.V0, r);
            if (o.UnitUv) mb.AddQuadUnit(o.GlassSub, a, b, cc, d); else mb.AddQuad(o.GlassSub, a, b, cc, d);
        }

        /// <summary>Брусок, выступающий из фасада (карнизы, подоконники, пилястры). w &lt; 0 — наружу.</summary>
        void FaceBox(Ctx c, Face f, int sub, float u, float v, float wCenter, Vector3 size)
        {
            var center = f.P(u, v, wCenter);
            float yaw = Quaternion.LookRotation(f.W, Vector3.up).eulerAngles.y;
            c.Mb.AddBox(sub, center, size, yaw, true);
        }

        // ---------- Публичный API ----------

        public GameObject Build(BuildingSpec s, Transform parent)
        {
            var root = new GameObject("Building_" + s.Key);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = s.Origin;
            root.transform.localRotation = Quaternion.Euler(0, s.Yaw, 0);

            var rng = new DeterministicRandom((ulong)(s.Seed + 1));
            var lod0 = new Ctx();
            var windowGroups = new int[3];
            for (int g = 0; g < 3; g++) windowGroups[g] = -1;

            BuildShell(s, lod0, rng, windowGroups, root.transform);
            var lod0Go = CreateRendererObject(root.transform, "LOD0", lod0, s.Key);

            var lod1 = new Ctx();
            BuildLod1(s, lod1);
            var lod1Go = CreateRendererObject(root.transform, "LOD1", lod1, s.Key + "_lod1");

            var lodGroup = root.AddComponent<LODGroup>();
            float lod0Height = s.Backdrop ? 0.35f : 0.12f;
            float cull = s.Backdrop ? 0.004f : 0.01f;
            lodGroup.SetLODs(new[]
            {
                new LOD(lod0Height, new Renderer[] { lod0Go.GetComponent<MeshRenderer>() }),
                new LOD(cull, new Renderer[] { lod1Go.GetComponent<MeshRenderer>() }),
            });
            lodGroup.RecalculateBounds();

            var lights = root.AddComponent<BuildingLights>();
            lights.Init(lod0Go.GetComponent<MeshRenderer>(), windowGroups, s.Seed, s.Units.Exists(u => u.Kind == GroundUnitKind.OfficeLobby) || s.Style == FacadeStyle.ModernGlass);

            BuildColliders(s, root.transform);
            return root;
        }

        GameObject CreateRendererObject(Transform parent, string name, Ctx c, string meshName)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = c.Mb.Build(meshName);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = c.Mats.ToArray();
            mr.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        // ---------- Стили ----------

        Material WallMaterial(BuildingSpec s)
        {
            switch (s.Style)
            {
                case FacadeStyle.Brick:
                case FacadeStyle.Townhouse: return M.Paint("brick", s.WallColor, "brick", 2.6f, 0.08f);
                case FacadeStyle.ClassicPlaster: return M.Paint("plaster", s.WallColor, "plaster", 4f, 0.1f);
                case FacadeStyle.ConcretePanel: return M.Paint("panel", s.WallColor, "concrete", 3.2f, 0.12f);
                case FacadeStyle.Industrial: return M.Paint("corrugated", s.WallColor, "corrugated", 3f, 0.35f);
                case FacadeStyle.ModernGlass: return M.Paint("modern", s.AccentColor, "concrete", 4f, 0.4f);
                default: return M.Paint("pavilion", s.WallColor, "concrete", 4f, 0.25f);
            }
        }

        Material BaseMaterial(BuildingSpec s)
        {
            switch (s.Style)
            {
                case FacadeStyle.Brick:
                case FacadeStyle.ClassicPlaster: return M.Paint("stone", new Color(0.45f, 0.43f, 0.42f), "concrete", 1.5f, 0.2f);
                case FacadeStyle.ModernGlass: return M.Paint("granite", new Color(0.2f, 0.2f, 0.22f), "concrete", 2f, 0.6f);
                default: return WallMaterial(s);
            }
        }

        float WindowSpacing(BuildingSpec s)
        {
            switch (s.Style)
            {
                case FacadeStyle.ClassicPlaster: return 3.3f;
                case FacadeStyle.ConcretePanel: return 3.0f;
                case FacadeStyle.ModernGlass: return 1.6f;
                case FacadeStyle.Townhouse: return 3.2f;
                default: return 2.9f;
            }
        }

        // ---------- Оболочка ----------

        void BuildShell(BuildingSpec s, Ctx c, DeterministicRandom rng, int[] windowGroups, Transform root)
        {
            var wall = c.Sub(WallMaterial(s));
            var trim = c.Sub(M.Paint("trim", s.TrimColor, "plaster", 2f, 0.2f));
            var baseMat = c.Sub(BaseMaterial(s));
            var glass = c.Sub(M.WindowGlass);
            for (int g = 0; g < 3; g++) windowGroups[g] = c.UniqueSub(M.WindowGlass);
            float gf = s.GroundFloorHeight, h = s.Height;
            bool singleStorey = s.Floors <= 1;

            // ---- Первый этаж: фасад с витринами, входами и проёмами ----
            var front = FrontFace(s);
            var groundOpenings = new List<Opening>();
            foreach (var unit in s.Units) GroundUnitOpenings(s, unit, c, groundOpenings);
            float groundTop = singleStorey ? h : gf;
            Wall(c, front, s.Width, 0, groundTop, groundOpenings, baseMat, baseMat);

            // ---- Верхние этажи ----
            if (!singleStorey)
            {
                UpperFacade(s, c, front, s.Width, rng, wall, trim, glass, windowGroups, true);
                UpperFacade(s, c, BackFace(s), s.Width, rng, wall, trim, glass, windowGroups, false);
                var left = LeftFace(s); var right = RightFace(s);
                if (s.Style == FacadeStyle.ModernGlass || s.Style == FacadeStyle.ConcretePanel || s.Backdrop)
                {
                    UpperFacade(s, c, left, s.Depth, rng, wall, trim, glass, windowGroups, false);
                    UpperFacade(s, c, right, s.Depth, rng, wall, trim, glass, windowGroups, false);
                }
                else
                {
                    Wall(c, left, s.Depth, gf, h, new List<Opening>(), wall, wall);
                    Wall(c, right, s.Depth, gf, h, new List<Opening>(), wall, wall);
                }
                // Межэтажный карниз над первым этажом
                FaceBox(c, front, trim, s.Width * 0.5f, gf, -0.12f, new Vector3(s.Width + 0.1f, 0.3f, 0.24f));
            }
            // Боковые и задняя стены первого этажа
            Wall(c, BackFace(s), s.Width, 0, groundTop, ServiceDoor(s), baseMat, baseMat);
            Wall(c, LeftFace(s), s.Depth, 0, groundTop, new List<Opening>(), baseMat, baseMat);
            Wall(c, RightFace(s), s.Depth, 0, groundTop, new List<Opening>(), baseMat, baseMat);

            // Цоколь
            if (s.Style == FacadeStyle.Brick || s.Style == FacadeStyle.ClassicPlaster || s.Style == FacadeStyle.Townhouse)
                FaceBox(c, front, c.Sub(M.Paint("plinth", new Color(0.32f, 0.31f, 0.3f), "concrete", 1.5f, 0.15f)), s.Width * 0.5f, 0.25f, -0.03f, new Vector3(s.Width, 0.5f, 0.08f));

            BuildRoof(s, c, rng, trim);
            BuildStyleDetails(s, c, rng, wall, trim);
            foreach (var unit in s.Units) BuildUnitExterior(s, unit, c, root);
            if (!string.IsNullOrEmpty(s.HouseNumber) && !s.Backdrop) HousePlate(s, root);
        }

        List<Opening> ServiceDoor(BuildingSpec s)
        {
            var list = new List<Opening>();
            if (s.Width > 10f) list.Add(new Opening(s.Width * 0.5f - 0.6f, s.Width * 0.5f + 0.6f, 0f, 2.2f, 0.15f, -1));
            return list;
        }

        // ---------- Верхние этажи ----------

        void UpperFacade(BuildingSpec s, Ctx c, Face f, float width, DeterministicRandom rng, int wall, int trim, int glass, int[] groups, bool isFront)
        {
            float gf = s.GroundFloorHeight, fh = s.FloorHeight;
            var openings = new List<Opening>();
            var mb = c.Mb;

            if (s.Style == FacadeStyle.ModernGlass)
            {
                // Навесной фасад: стеклянные ленты с алюминиевыми импостами и глухими поясами между этажами.
                int bays = Mathf.Max(1, Mathf.FloorToInt((width - 0.6f) / WindowSpacing(s)));
                float bay = (width - 0.6f) / bays;
                int frame = c.Sub(M.Aluminium);
                for (int fl = 1; fl < s.Floors; fl++)
                {
                    float y0 = gf + (fl - 1) * fh + 0.75f, y1 = gf + fl * fh - 0.15f;
                    for (int b = 0; b < bays; b++)
                    {
                        float u0 = 0.3f + b * bay + 0.04f, u1 = 0.3f + (b + 1) * bay - 0.04f;
                        openings.Add(new Opening(u0, u1, y0, y1, 0.12f, PickGlass(rng, glass, groups)));
                    }
                    // Горизонтальный ригель
                    FaceBox(c, f, frame, width * 0.5f, y1 + 0.04f, -0.03f, new Vector3(width - 0.4f, 0.08f, 0.1f));
                }
                Wall(c, f, width, gf, s.Height, openings, wall, frame);
                // Вертикальные ламели — дают ритм и тени
                for (int b = 0; b <= bays; b++)
                    FaceBox(c, f, frame, 0.3f + b * bay, (gf + s.Height) * 0.5f, -0.12f, new Vector3(0.07f, s.Height - gf, 0.3f));
                return;
            }

            int perFloor = s.WindowsPerFloor > 0 ? s.WindowsPerFloor : Mathf.Max(1, Mathf.FloorToInt((width - 1.2f) / WindowSpacing(s)));
            float spacing = (width - 1.2f) / perFloor;
            float winW = s.Style == FacadeStyle.ConcretePanel ? 1.6f : s.Style == FacadeStyle.ClassicPlaster ? 1.35f : 1.25f;
            winW = Mathf.Min(winW, spacing - 0.5f);
            float winH = s.Style == FacadeStyle.ClassicPlaster ? 2.0f : s.Style == FacadeStyle.ConcretePanel ? 1.5f : 1.75f;
            float sillH = s.Style == FacadeStyle.ConcretePanel ? 0.9f : 0.8f;
            int metal = c.Sub(M.MetalDark);
            bool detailed = !s.Backdrop;

            for (int fl = 1; fl < s.Floors; fl++)
            {
                float y0 = gf + (fl - 1) * fh;
                for (int i = 0; i < perFloor; i++)
                {
                    float uc = 0.6f + (i + 0.5f) * spacing;
                    bool balcony = isFront && s.Balconies && fl >= 1 && (i % 2 == (s.Seed % 2)) && perFloor >= 3;
                    float wb = balcony ? 0.05f : sillH;
                    float wt = sillH + winH;
                    var o = new Opening(uc - winW * 0.5f, uc + winW * 0.5f, y0 + wb, y0 + wt, 0.18f, PickGlass(rng, glass, groups));
                    openings.Add(o);
                    if (!detailed) continue;

                    // Рама окна и импост
                    FaceBox(c, f, trim, uc, y0 + wb + (wt - wb) * 0.5f, 0.15f, new Vector3(0.06f, wt - wb, 0.05f));
                    if (!balcony) FaceBox(c, f, trim, uc, y0 + wb - 0.04f, -0.06f, new Vector3(winW + 0.2f, 0.07f, 0.22f)); // подоконник

                    switch (s.Style)
                    {
                        case FacadeStyle.Brick:
                            FaceBox(c, f, trim, uc, y0 + wt + 0.12f, -0.02f, new Vector3(winW + 0.3f, 0.24f, 0.06f)); // перемычка
                            break;
                        case FacadeStyle.ClassicPlaster:
                            FaceBox(c, f, trim, uc, y0 + wt + 0.18f, -0.08f, new Vector3(winW + 0.5f, 0.12f, 0.2f)); // сандрик
                            FaceBox(c, f, trim, uc - winW * 0.5f - 0.08f, y0 + (wb + wt) * 0.5f, -0.03f, new Vector3(0.14f, wt - wb + 0.1f, 0.06f));
                            FaceBox(c, f, trim, uc + winW * 0.5f + 0.08f, y0 + (wb + wt) * 0.5f, -0.03f, new Vector3(0.14f, wt - wb + 0.1f, 0.06f));
                            if (!balcony && rng.Chance(0.35)) FlowerBox(c, f, uc, y0 + wb, winW);
                            break;
                        case FacadeStyle.ConcretePanel:
                            FaceBox(c, f, c.Sub(M.Paint("accent", s.AccentColor, "concrete", 2f, 0.2f)), uc, y0 + 0.35f, -0.02f, new Vector3(spacing - 0.2f, 0.5f, 0.05f));
                            break;
                        case FacadeStyle.Townhouse:
                            // Ставни
                            var shutter = c.Sub(M.Paint("shutter", s.AccentColor, "wood", 1f, 0.3f));
                            FaceBox(c, f, shutter, uc - winW * 0.5f - 0.32f, y0 + (wb + wt) * 0.5f, -0.04f, new Vector3(0.55f, wt - wb, 0.04f));
                            FaceBox(c, f, shutter, uc + winW * 0.5f + 0.32f, y0 + (wb + wt) * 0.5f, -0.04f, new Vector3(0.55f, wt - wb, 0.04f));
                            break;
                    }

                    if (balcony) Balcony(s, c, f, uc, y0, metal, trim);
                    else if (isFront && rng.Chance(0.12)) AirConditioner(c, f, uc + winW * 0.5f + 0.6f, y0 + 0.6f);
                }
            }
            Wall(c, f, width, s.GroundFloorHeight, s.Height, openings, wall, trim);
        }

        int PickGlass(DeterministicRandom rng, int glass, int[] groups) => rng.Chance(0.55) ? groups[rng.Range(0, 3)] : glass;

        void Balcony(BuildingSpec s, Ctx c, Face f, float uc, float y0, int metal, int trim)
        {
            float bw = 2.4f, depth = 1.1f;
            FaceBox(c, f, trim, uc, y0 - 0.05f, -depth * 0.5f, new Vector3(bw, 0.16f, depth)); // плита
            float railH = 1.0f;
            FaceBox(c, f, metal, uc, y0 + railH, -depth + 0.03f, new Vector3(bw, 0.05f, 0.05f));
            FaceBox(c, f, metal, uc - bw * 0.5f + 0.03f, y0 + railH, -depth * 0.5f, new Vector3(0.05f, 0.05f, depth));
            FaceBox(c, f, metal, uc + bw * 0.5f - 0.03f, y0 + railH, -depth * 0.5f, new Vector3(0.05f, 0.05f, depth));
            if (s.Style == FacadeStyle.ConcretePanel)
            {
                // Остеклённое/панельное ограждение в цвет акцента
                FaceBox(c, f, c.Sub(M.Paint("accent", s.AccentColor, "concrete", 2f, 0.2f)), uc, y0 + railH * 0.5f, -depth + 0.03f, new Vector3(bw, railH, 0.04f));
                return;
            }
            for (float u = -bw * 0.5f + 0.15f; u < bw * 0.5f - 0.1f; u += 0.16f)
                FaceBox(c, f, metal, uc + u, y0 + railH * 0.5f, -depth + 0.03f, new Vector3(0.025f, railH, 0.025f));
            for (float w = -depth + 0.15f; w < -0.05f; w += 0.16f)
            {
                FaceBox(c, f, metal, uc - bw * 0.5f + 0.03f, y0 + railH * 0.5f, w, new Vector3(0.025f, railH, 0.025f));
                FaceBox(c, f, metal, uc + bw * 0.5f - 0.03f, y0 + railH * 0.5f, w, new Vector3(0.025f, railH, 0.025f));
            }
        }

        void FlowerBox(Ctx c, Face f, float uc, float sillY, float width)
        {
            FaceBox(c, f, c.Sub(M.Wood), uc, sillY + 0.08f, -0.22f, new Vector3(width, 0.18f, 0.22f));
            var flowers = c.Sub(M.Paint("flowers", new Color(0.8f, 0.2f, 0.3f), "leaves", 0.5f, 0.1f));
            var leaves = c.Sub(M.Leaves(1));
            for (float u = -width * 0.5f + 0.15f; u < width * 0.5f; u += 0.3f)
                c.Mb.AddSphere((int)u % 2 == 0 ? flowers : leaves, f.P(uc + u, sillY + 0.22f, -0.22f), new Vector3(0.14f, 0.1f, 0.1f), 3, 5);
        }

        void AirConditioner(Ctx c, Face f, float u, float y)
        {
            int body = c.Sub(M.Paint("ac", new Color(0.86f, 0.86f, 0.84f), null, 1f, 0.3f));
            FaceBox(c, f, body, u, y, -0.18f, new Vector3(0.8f, 0.55f, 0.3f));
            FaceBox(c, f, c.Sub(M.Dark), u, y, -0.335f, new Vector3(0.45f, 0.45f, 0.01f));
        }

        // ---------- Первый этаж ----------

        void GroundUnitOpenings(BuildingSpec s, GroundUnit unit, Ctx c, List<Opening> list)
        {
            float u0 = unit.Start, u1 = unit.Start + unit.Width;
            switch (unit.Kind)
            {
                case GroundUnitKind.Shop:
                    if (unit.LotKey == null)
                    {
                        // Декоративная лавка: витрина без входа
                        list.Add(new Opening(u0, u1, 0.45f, 3.2f, 0.3f, c.Sub(M.Paint("decor_inside", new Color(0.25f, 0.22f, 0.2f), "litwindow", 1f, 0.6f))));
                    }
                    else
                    {
                        list.Add(new Opening(u0, u1, 0f, Mathf.Min(3.4f, s.GroundFloorHeight - 0.6f), 0.3f, -1));
                    }
                    break;
                case GroundUnitKind.ResidentialEntrance:
                    list.Add(new Opening(u0 + 0.6f, u1 - 0.6f, 0f, 2.6f, 0.6f, -1));
                    break;
                case GroundUnitKind.OfficeLobby:
                    list.Add(new Opening(u0, u1, 0f, s.GroundFloorHeight - 0.5f, 0.4f, -1));
                    break;
                case GroundUnitKind.LoadingDock:
                    // Проём за средними (подъёмными) воротами
                    float mid = unit.Start + unit.Width * 0.5f;
                    list.Add(new Opening(mid - 2f, mid + 2f, 1.1f, 5.4f, 0.3f, -1));
                    break;
            }

            // Окна первого этажа на свободных участках (кроме павильонов и складов)
            if (unit.Kind == GroundUnitKind.ResidentialEntrance && s.Style != FacadeStyle.Pavilion)
            {
                int glass = c.Sub(M.WindowGlass);
                foreach (float u in new[] { u0 - 1.6f, u1 + 1.6f })
                    if (u > 1f && u < s.Width - 1f && !Overlaps(s, u - 0.7f, u + 0.7f, unit))
                        list.Add(new Opening(u - 0.6f, u + 0.6f, 1.2f, 2.9f, 0.2f, glass));
            }
        }

        static bool Overlaps(BuildingSpec s, float a, float b, GroundUnit except)
        {
            foreach (var u in s.Units)
                if (u != except && a < u.Start + u.Width + 0.3f && b > u.Start - 0.3f) return true;
            return false;
        }

        void BuildUnitExterior(BuildingSpec s, GroundUnit unit, Ctx c, Transform root)
        {
            var front = FrontFace(s);
            float uc = unit.Start + unit.Width * 0.5f;
            int alu = c.Sub(M.Aluminium);
            switch (unit.Kind)
            {
                case GroundUnitKind.Shop:
                {
                    float top = Mathf.Min(3.4f, s.GroundFloorHeight - 0.6f);
                    if (unit.LotKey == null)
                    {
                        DecorShop(s, unit, c, root, top);
                        break;
                    }
                    // Витрина: цоколь, импосты, фрамуга; стекло и двери — отдельные объекты (дверь подвижна)
                    FaceBox(c, front, c.Sub(BaseMaterial(s)), uc, 0.22f, 0.15f, new Vector3(unit.Width - 0.02f, 0.44f, 0.3f));
                    float doorW = 1.9f;
                    float[] bars = { unit.Start + 0.05f, uc - doorW * 0.5f - 0.05f, uc + doorW * 0.5f + 0.05f, unit.Start + unit.Width - 0.05f };
                    foreach (float u in bars) FaceBox(c, front, alu, u, top * 0.5f, 0.28f, new Vector3(0.1f, top, 0.1f));
                    for (float u = unit.Start + 2.2f; u < uc - doorW * 0.5f - 0.6f; u += 2.2f) FaceBox(c, front, alu, u, top * 0.5f, 0.28f, new Vector3(0.06f, top, 0.08f));
                    for (float u = unit.Start + unit.Width - 2.2f; u > uc + doorW * 0.5f + 0.6f; u -= 2.2f) FaceBox(c, front, alu, u, top * 0.5f, 0.28f, new Vector3(0.06f, top, 0.08f));
                    FaceBox(c, front, alu, uc, 2.55f, 0.28f, new Vector3(unit.Width - 0.1f, 0.08f, 0.1f));
                    var unitGo = CreateShopUnit(s, unit, root, top, doorW);
                    if (s.Style != FacadeStyle.Pavilion && s.Style != FacadeStyle.ModernGlass && unit.Terrace)
                        Awning(c, front, unit, top + 0.25f);
                    break;
                }
                case GroundUnitKind.ResidentialEntrance:
                    ResidentialEntrance(s, unit, c, root);
                    break;
                case GroundUnitKind.OfficeLobby:
                    OfficeLobby(s, unit, c, root);
                    break;
                case GroundUnitKind.LoadingDock:
                    LoadingDock(s, unit, c, root);
                    break;
            }
        }

        void Awning(Ctx c, Face f, GroundUnit unit, float y)
        {
            int cloth = c.Sub(M.Paint("awning", unit.AwningColor, null, 1f, 0.05f));
            float u0 = unit.Start + 0.1f, u1 = unit.Start + unit.Width - 0.1f, out_ = 1.7f, drop = 0.7f;
            var mb = c.Mb;
            // Наклонная плоскость и фестон
            mb.AddQuad(cloth, f.P(u0, y - drop, -out_), f.P(u0, y, -0.05f), f.P(u1, y, -0.05f), f.P(u1, y - drop, -out_));
            mb.AddQuad(cloth, f.P(u1, y - drop, -out_), f.P(u1, y, -0.05f), f.P(u0, y, -0.05f), f.P(u0, y - drop, -out_));
            mb.AddQuad(cloth, f.P(u0, y - drop - 0.3f, -out_), f.P(u0, y - drop, -out_), f.P(u1, y - drop, -out_), f.P(u1, y - drop - 0.3f, -out_));
            mb.AddQuad(cloth, f.P(u1, y - drop - 0.3f, -out_ + 0.01f), f.P(u1, y - drop, -out_ + 0.01f), f.P(u0, y - drop, -out_ + 0.01f), f.P(u0, y - drop - 0.3f, -out_ + 0.01f));
        }

        GameObject CreateShopUnit(BuildingSpec s, GroundUnit unit, Transform root, float top, float doorW)
        {
            var go = new GameObject("Unit_" + unit.LotKey);
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(unit.Start + unit.Width * 0.5f, 0, 0);
            var view = go.AddComponent<PropertyView>();

            // Стекло витрины (две части слева и справа от двери + фрамуга над дверью)
            var glassGo = new GameObject("Glass");
            glassGo.transform.SetParent(go.transform, false);
            var gb = new MeshBuilder();
            float half = unit.Width * 0.5f;
            AddGlassPane(gb, -half + 0.05f, -doorW * 0.5f - 0.05f, 0.44f, top, 0.3f);
            AddGlassPane(gb, doorW * 0.5f + 0.05f, half - 0.05f, 0.44f, top, 0.3f);
            AddGlassPane(gb, -doorW * 0.5f, doorW * 0.5f, 2.6f, top, 0.3f);
            glassGo.AddComponent<MeshFilter>().sharedMesh = gb.Build("ShopGlass_" + unit.LotKey);
            var gr = glassGo.AddComponent<MeshRenderer>();
            gr.sharedMaterial = M.StoreGlass;
            gr.shadowCastingMode = ShadowCastingMode.Off;

            // Автоматические раздвижные двери
            var door = CreateSlidingDoor(go.transform, new Vector3(0, 0, 0.3f), doorW, 2.5f);

            // Вывеска над витриной (у павильона — на лицевой грани фриза-козырька)
            bool pavilion = s.Style == FacadeStyle.Pavilion;
            float signY = top + (s.GroundFloorHeight - top) * 0.5f + 0.05f;
            float signH = Mathf.Max(0.55f, (s.GroundFloorHeight - top) - 0.35f);
            float signZ = -0.14f;
            if (pavilion) { signY = s.Height - 0.7f; signH = 0.9f; signZ = -1.95f; }
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(board.GetComponent<Collider>());
            board.name = "SignBoard";
            board.transform.SetParent(go.transform, false);
            float boardW = Mathf.Min(unit.Width - 0.4f, pavilion ? unit.Width * 0.7f : 9f);
            board.transform.localPosition = new Vector3(0, signY, signZ);
            board.transform.localScale = new Vector3(boardW, signH, 0.22f);
            var boardRenderer = board.GetComponent<MeshRenderer>();
            boardRenderer.sharedMaterial = M.Paint("signboard", new Color(0.15f, 0.15f, 0.15f));
            var sign = WorldText.Create(go.transform, "SignText", "", new Vector3(0, signY, signZ - 0.12f), signH * 0.55f, Color.white, boardW - 0.4f);

            // Баннер «Сдаётся/Продаётся» — за стеклом, на белой подложке
            var bannerRoot = new GameObject("BannerRoot");
            bannerRoot.transform.SetParent(go.transform, false);
            bannerRoot.transform.localPosition = new Vector3(-half * 0.5f - 0.5f, 1.8f, 0.42f);
            var paper = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(paper.GetComponent<Collider>());
            paper.transform.SetParent(bannerRoot.transform, false);
            paper.transform.localPosition = new Vector3(0, 0, 0.03f);
            paper.transform.localScale = new Vector3(Mathf.Max(1.2f, half - 1.4f), 0.9f, 0.02f);
            paper.GetComponent<MeshRenderer>().sharedMaterial = M.Paint("paper", new Color(0.97f, 0.97f, 0.95f), null, 1f, 0.1f);
            var banner = WorldText.Create(bannerRoot.transform, "Banner", "", Vector3.zero, 0.28f, new Color(0.85f, 0.1f, 0.1f), Mathf.Max(1.0f, half - 1.6f));
            var hours = WorldText.Create(go.transform, "Hours", "", new Vector3(doorW * 0.5f + 0.45f, 1.6f, 0.24f), 0.09f, Color.white, 0.6f, TextAnchor.MiddleLeft);

            var terrace = new GameObject("Terrace");
            terrace.transform.SetParent(go.transform, false);
            terrace.SetActive(false);

            var interior = new GameObject("Interior");
            interior.transform.SetParent(go.transform, false);

            view.Configure(unit.LotKey, unit.Kind, new Vector3(unit.Width, s.GroundFloorHeight, unit.InteriorDepth), top, doorW,
                door, boardRenderer, sign, banner, hours, terrace.transform, interior.transform, unit.Terrace);
            return go;
        }

        static void AddGlassPane(MeshBuilder mb, float u0, float u1, float v0, float v1, float w)
        {
            mb.AddQuadUnit(0, new Vector3(u0, v0, w), new Vector3(u0, v1, w), new Vector3(u1, v1, w), new Vector3(u1, v0, w));
        }

        AutoDoor CreateSlidingDoor(Transform parent, Vector3 localPos, float width, float height)
        {
            var root = new GameObject("Door");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            var leaves = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                var leaf = new GameObject(i == 0 ? "Left" : "Right");
                leaf.transform.SetParent(root.transform, false);
                float sign = i == 0 ? -1f : 1f;
                leaf.transform.localPosition = new Vector3(sign * width * 0.25f, 0, 0);
                var mb = new MeshBuilder();
                mb.AddFrame(0, new Vector3(0, height * 0.5f, 0), width * 0.5f, height, 0.06f, 0.05f);
                mb.AddQuadUnit(1, new Vector3(-width * 0.25f + 0.06f, 0.06f, 0), new Vector3(-width * 0.25f + 0.06f, height - 0.06f, 0),
                    new Vector3(width * 0.25f - 0.06f, height - 0.06f, 0), new Vector3(width * 0.25f - 0.06f, 0.06f, 0));
                leaf.AddComponent<MeshFilter>().sharedMesh = mb.Build("DoorLeaf");
                var mr = leaf.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { M.Aluminium, M.StoreGlass };
                leaves[i] = leaf.transform;
            }
            var door = root.AddComponent<AutoDoor>();
            door.Configure(leaves[0], leaves[1], width * 0.5f - 0.05f);
            return door;
        }

        void DecorShop(BuildingSpec s, GroundUnit unit, Ctx c, Transform root, float top)
        {
            var front = FrontFace(s);
            float uc = unit.Start + unit.Width * 0.5f;
            FaceBox(c, front, c.Sub(M.Aluminium), uc, top * 0.5f + 0.2f, 0.27f, new Vector3(0.06f, top, 0.06f));
            Awning(c, front, unit, top + 0.25f);
            var boardColor = Color.Lerp(unit.AwningColor, Color.black, 0.3f);
            FaceBox(c, front, c.Sub(M.Paint("decorsign", boardColor)), uc, top + 0.55f, -0.1f, new Vector3(Mathf.Min(unit.Width - 0.4f, 6f), 0.6f, 0.2f));
            var parent = new GameObject("DecorSign");
            parent.transform.SetParent(root, false);
            WorldText.Create(parent.transform, "Text", unit.DecorName, new Vector3(uc, top + 0.55f, -0.22f), 0.34f, new Color(1f, 0.97f, 0.9f), Mathf.Min(unit.Width - 0.8f, 5.6f));
        }

        void ResidentialEntrance(BuildingSpec s, GroundUnit unit, Ctx c, Transform root)
        {
            var f = FrontFace(s);
            float uc = unit.Start + unit.Width * 0.5f;
            int wood = c.Sub(M.Paint("door", new Color(0.35f, 0.22f, 0.14f), "wood", 1f, 0.4f));
            int metal = c.Sub(M.MetalDark);
            int glass = c.Sub(M.WindowGlass);
            // Дверь в нише
            var o = new Opening(uc - 0.75f, uc + 0.75f, 0.12f, 2.35f, 0.58f, wood, false);
            Reveal(c, f, o, wood);
            FaceBox(c, f, glass, uc, 1.6f, 0.56f, new Vector3(0.9f, 0.9f, 0.02f));
            FaceBox(c, f, metal, uc + 0.5f, 1.1f, 0.52f, new Vector3(0.05f, 0.35f, 0.06f)); // ручка
            // Ступени и козырёк
            int stone = c.Sub(M.Concrete);
            FaceBox(c, f, stone, uc, 0.06f, -0.35f, new Vector3(unit.Width + 0.4f, 0.12f, 0.7f));
            FaceBox(c, f, stone, uc, 2.75f, -0.55f, new Vector3(unit.Width + 0.6f, 0.15f, 1.2f));
            FaceBox(c, f, metal, uc - unit.Width * 0.5f - 0.2f, 2.65f, -0.9f, new Vector3(0.04f, 0.04f, 0.8f));
            FaceBox(c, f, metal, uc + unit.Width * 0.5f + 0.2f, 2.65f, -0.9f, new Vector3(0.04f, 0.04f, 0.8f));
            // Светильник над дверью (переключается вместе с уличным освещением)
            FaceBox(c, f, c.Sub(M.LampLens), uc, 2.55f, -0.1f, new Vector3(0.3f, 0.12f, 0.12f));

            var go = new GameObject("Entrance");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(uc, 0, 0);
            // Домофон — точка взаимодействия с квартирами дома
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.name = "Intercom";
            panel.transform.SetParent(go.transform, false);
            panel.transform.localPosition = new Vector3(unit.Width * 0.5f - 0.15f, 1.35f, -0.03f);
            panel.transform.localScale = new Vector3(0.18f, 0.3f, 0.05f);
            panel.GetComponent<MeshRenderer>().sharedMaterial = M.Aluminium;
            var dir = go.AddComponent<DirectoryPoint>();
            dir.Configure(s.HouseNumber, "Домофон: квартиры", unit.ApartmentLots, new Vector3(0, 1f, -0.8f));
        }

        void OfficeLobby(BuildingSpec s, GroundUnit unit, Ctx c, Transform root)
        {
            var f = FrontFace(s);
            float uc = unit.Start + unit.Width * 0.5f;
            int alu = c.Sub(M.Aluminium);
            float top = s.GroundFloorHeight - 0.5f;
            // Стеклянный тамбур и козырёк с названием
            for (float u = unit.Start; u <= unit.Start + unit.Width + 0.01f; u += unit.Width / 4f)
                FaceBox(c, f, alu, u, top * 0.5f, 0.38f, new Vector3(0.08f, top, 0.08f));
            FaceBox(c, f, alu, uc, 2.6f, 0.38f, new Vector3(unit.Width, 0.08f, 0.08f));
            FaceBox(c, f, c.Sub(M.Paint("canopy", s.AccentColor, "concrete", 2f, 0.5f)), uc, top + 0.2f, -1.2f, new Vector3(unit.Width + 1f, 0.3f, 2.6f));

            var go = new GameObject("Lobby");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(uc, 0, 0);
            var glassGo = new GameObject("Glass");
            glassGo.transform.SetParent(go.transform, false);
            var gb = new MeshBuilder();
            float half = unit.Width * 0.5f;
            AddGlassPane(gb, -half, -1.1f, 0f, top, 0.4f);
            AddGlassPane(gb, 1.1f, half, 0f, top, 0.4f);
            AddGlassPane(gb, -1.1f, 1.1f, 2.6f, top, 0.4f);
            glassGo.AddComponent<MeshFilter>().sharedMesh = gb.Build("LobbyGlass");
            var gr = glassGo.AddComponent<MeshRenderer>();
            gr.sharedMaterial = M.StoreGlass;
            gr.shadowCastingMode = ShadowCastingMode.Off;
            CreateSlidingDoor(go.transform, new Vector3(0, 0, 0.4f), 2.2f, 2.55f);

            if (!string.IsNullOrEmpty(s.RoofSign))
                WorldText.Create(go.transform, "Name", s.RoofSign, new Vector3(0, top + 0.2f, -2.52f), 0.36f, new Color(0.95f, 0.95f, 0.92f), unit.Width - 1f);

            // Интерьер лобби: пол, стойка ресепшен, диваны, табло арендаторов
            var interior = InteriorBuilder.BuildLobby(M, go.transform, unit.Width, s.GroundFloorHeight, unit.InteriorDepth);
            var board = WorldText.Create(interior.transform, "Directory", "", new Vector3(-half + 1.2f, 1.8f, 0.6f), 0.12f, Color.white, 2.2f, TextAnchor.UpperLeft, 0f);
            var dir = go.AddComponent<DirectoryPoint>();
            dir.Configure(s.RoofSign ?? s.HouseNumber, "Ресепшен: офисы", unit.OfficeLots, new Vector3(0, 1f, 3.2f));
            dir.SetDirectoryBoard(board);
        }

        void LoadingDock(BuildingSpec s, GroundUnit unit, Ctx c, Transform root)
        {
            var f = FrontFace(s);
            int concrete = c.Sub(M.Concrete);
            int yellow = c.Sub(M.RoadPaintYellow);
            int dark = c.Sub(M.Dark);
            float uc = unit.Start + unit.Width * 0.5f;
            // Рампа
            FaceBox(c, f, concrete, uc, 0.55f, -1.4f, new Vector3(unit.Width, 1.1f, 2.8f));
            FaceBox(c, f, yellow, uc, 1.12f, -2.78f, new Vector3(unit.Width, 0.04f, 0.06f));
            // Трое ворот: двое закрыты всегда, средние — управляемые (вход в склад)
            int doors = 3;
            float doorW = 4f;
            var corr = c.Sub(M.Paint("rolldoor", new Color(0.7f, 0.72f, 0.74f), "corrugated", 1.2f, 0.4f));
            for (int i = 0; i < doors; i++)
            {
                float u = unit.Start + unit.Width * (i + 0.5f) / doors;
                FaceBox(c, f, yellow, u - doorW * 0.5f - 0.15f, 2.6f, -0.1f, new Vector3(0.3f, 5.2f, 0.2f));
                FaceBox(c, f, yellow, u + doorW * 0.5f + 0.15f, 2.6f, -0.1f, new Vector3(0.3f, 5.2f, 0.2f));
                FaceBox(c, f, dark, u - 1.2f, 1.25f, -2.85f, new Vector3(0.35f, 0.3f, 0.15f)); // отбойники
                FaceBox(c, f, dark, u + 1.2f, 1.25f, -2.85f, new Vector3(0.35f, 0.3f, 0.15f));
                if (i != 1) FaceBox(c, f, corr, u, 3.25f, -0.02f, new Vector3(doorW, 4.3f, 0.05f));
                FaceBox(c, f, c.Sub(M.LampLens), u, 5.6f, -0.3f, new Vector3(0.4f, 0.15f, 0.4f));
            }
            // Пандус сбоку от рампы — подъём на уровень пола склада
            float r0 = unit.Start - 4.4f, r1 = unit.Start;
            var mbd = c.Mb;
            mbd.AddQuad(concrete, f.P(r0, 0.02f, -0.2f), f.P(r0, 0.02f, -2.8f), f.P(r1, 1.1f, -2.8f), f.P(r1, 1.1f, -0.2f));
            mbd.AddTriangle(concrete, f.P(r0, 0.02f, -2.8f), f.P(r1, 0.02f, -2.8f), f.P(r1, 1.1f, -2.8f));
            FaceBox(c, f, c.Sub(M.MetalDark), (r0 + r1) * 0.5f, 1.0f, -2.75f, new Vector3(4.4f, 0.05f, 0.05f));

            var go = new GameObject("Unit_" + unit.LotKey);
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(uc, 1.1f, 0);
            var view = go.AddComponent<PropertyView>();
            // Средние ворота поднимаются, когда склад работает
            var gate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gate.name = "RollerGate";
            gate.transform.SetParent(go.transform, false);
            gate.transform.localPosition = new Vector3(0, 2.15f, 0.05f);
            gate.transform.localScale = new Vector3(doorW, 4.3f, 0.08f);
            gate.GetComponent<MeshRenderer>().sharedMaterial = M.Paint("rolldoor", new Color(0.7f, 0.72f, 0.74f), "corrugated", 1.2f, 0.4f);
            var sign = WorldText.Create(go.transform, "SignText", "", new Vector3(0, 5.6f, -0.12f), 0.6f, Color.white, unit.Width * 0.6f);
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(board.GetComponent<Collider>());
            board.name = "SignBoard";
            board.transform.SetParent(go.transform, false);
            board.transform.localPosition = new Vector3(0, 5.6f, 0f);
            board.transform.localScale = new Vector3(unit.Width * 0.65f, 1.0f, 0.2f);
            var boardR = board.GetComponent<MeshRenderer>();
            boardR.sharedMaterial = M.Paint("signboard", new Color(0.15f, 0.15f, 0.15f));
            var banner = WorldText.Create(go.transform, "Banner", "", new Vector3(-unit.Width / 3f, 2.5f, -0.1f), 0.45f, new Color(0.85f, 0.1f, 0.1f), 3.6f);
            var interior = new GameObject("Interior");
            interior.transform.SetParent(go.transform, false);
            view.Configure(unit.LotKey, unit.Kind, new Vector3(unit.Width, s.GroundFloorHeight - 1.1f, unit.InteriorDepth), 4.3f, doorW,
                null, boardR, sign, banner, null, null, interior.transform, false);
            view.SetGate(gate.transform);
        }

        void HousePlate(BuildingSpec s, Transform root)
        {
            float u = s.Width - 0.9f;
            foreach (var unit in s.Units)
                if (unit.Kind == GroundUnitKind.ResidentialEntrance) { u = unit.Start + unit.Width + 0.6f; break; }
            var go = new GameObject("HousePlate");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(u, 2.9f, -0.03f);
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(plate.GetComponent<Collider>());
            plate.transform.SetParent(go.transform, false);
            plate.transform.localScale = new Vector3(1.1f, 0.32f, 0.03f);
            plate.GetComponent<MeshRenderer>().sharedMaterial = M.Paint("plate", new Color(0.1f, 0.25f, 0.55f), null, 1f, 0.5f);
            WorldText.Create(go.transform, "Text", s.HouseNumber, new Vector3(0, 0, -0.02f), 0.12f, Color.white, 1.0f);
        }

        // ---------- Крыша ----------

        void BuildRoof(BuildingSpec s, Ctx c, DeterministicRandom rng, int trim)
        {
            float h = s.Height, w = s.Width, d = s.Depth;
            var mb = c.Mb;
            int roof = c.Sub(M.Roof);
            int metal = c.Sub(M.MetalDark);

            if (s.Roof == RoofStyle.Pitched)
            {
                int tiles = c.Sub(M.Paint("rooftiles", new Color(0.35f, 0.22f, 0.18f), "corrugated", 0.8f, 0.2f));
                float ridge = h + 3.2f, over = 0.4f;
                mb.AddQuad(tiles, new Vector3(-over, h - 0.2f, -over), new Vector3(-over, ridge, d * 0.5f), new Vector3(w + over, ridge, d * 0.5f), new Vector3(w + over, h - 0.2f, -over));
                mb.AddQuad(tiles, new Vector3(w + over, h - 0.2f, d + over), new Vector3(w + over, ridge, d * 0.5f), new Vector3(-over, ridge, d * 0.5f), new Vector3(-over, h - 0.2f, d + over));
                var wall = c.Sub(WallMaterial(s));
                mb.AddTriangle(wall, new Vector3(0, h, d), new Vector3(0, ridge - 0.1f, d * 0.5f), new Vector3(0, h, 0));
                mb.AddTriangle(wall, new Vector3(w, h, 0), new Vector3(w, ridge - 0.1f, d * 0.5f), new Vector3(w, h, d));
                // Каминная труба и слуховое окно
                mb.AddBox(c.Sub(M.Paint("brick", new Color(0.5f, 0.3f, 0.25f), "brick", 2.6f, 0.08f)), new Vector3(w * 0.75f, ridge, d * 0.62f), new Vector3(0.8f, 2.2f, 0.8f));
                FaceBox(c, FrontFace(s), trim, w * 0.5f, h + 0.25f, -0.15f, new Vector3(w + 0.8f, 0.2f, 0.3f));
                return;
            }

            // Плоская крыша с парапетом и инженерным оборудованием
            mb.AddQuad(roof, new Vector3(0, h, 0), new Vector3(0, h, d), new Vector3(w, h, d), new Vector3(w, h, 0));
            float p = s.Style == FacadeStyle.ModernGlass ? 1.1f : 0.8f, t = 0.3f;
            int parapet = c.Sub(s.Style == FacadeStyle.ModernGlass ? M.Aluminium : WallMaterial(s));
            mb.AddBox(parapet, new Vector3(w * 0.5f, h + p * 0.5f, t * 0.5f), new Vector3(w, p, t));
            mb.AddBox(parapet, new Vector3(w * 0.5f, h + p * 0.5f, d - t * 0.5f), new Vector3(w, p, t));
            mb.AddBox(parapet, new Vector3(t * 0.5f, h + p * 0.5f, d * 0.5f), new Vector3(t, p, d - 2 * t));
            mb.AddBox(parapet, new Vector3(w - t * 0.5f, h + p * 0.5f, d * 0.5f), new Vector3(t, p, d - 2 * t));
            if (s.Style == FacadeStyle.ClassicPlaster || s.Style == FacadeStyle.Brick)
                FaceBox(c, FrontFace(s), trim, w * 0.5f, h - 0.15f, -0.25f, new Vector3(w + 0.5f, 0.45f, 0.5f)); // венчающий карниз

            if (s.Backdrop && rng.Chance(0.5)) return;
            int units = Mathf.Clamp(Mathf.RoundToInt(w * d / 120f), 1, 5);
            int acMat = c.Sub(M.Paint("ac", new Color(0.86f, 0.86f, 0.84f), null, 1f, 0.3f));
            for (int i = 0; i < units; i++)
            {
                var pos = new Vector3(rng.Range(2f, w - 2f), h + 0.5f, rng.Range(3f, d - 2f));
                mb.AddBox(acMat, pos, new Vector3(1.6f, 1f, 1.1f), rng.Range(0, 2) * 90f);
                mb.AddCylinder(metal, pos + new Vector3(0, 0.5f, 0), 0.4f, 0.05f, 10);
            }
            // Выход на крышу и вентшахты
            mb.AddBox(c.Sub(WallMaterial(s)), new Vector3(w * 0.25f, h + 1.2f, d * 0.7f), new Vector3(2.4f, 2.4f, 2.4f));
            mb.AddBox(metal, new Vector3(w * 0.25f, h + 2.45f, d * 0.7f), new Vector3(2.6f, 0.1f, 2.6f));
            if (s.Style == FacadeStyle.Brick || s.Style == FacadeStyle.ClassicPlaster)
            {
                // Антенны и водяной бак — старый фонд
                mb.AddCylinder(c.Sub(M.Wood), new Vector3(w * 0.7f, h, d * 0.4f), 1.1f, 2.2f, 12);
                mb.AddCylinder(metal, new Vector3(w * 0.6f, h, d * 0.25f), 0.03f, 3.5f, 4, false);
                mb.AddBox(metal, new Vector3(w * 0.6f, h + 3.2f, d * 0.25f), new Vector3(1.2f, 0.03f, 0.03f));
            }
            if (s.Roof == RoofStyle.FlatWithBillboard)
            {
                // Вывеска супермаркета на крыше ставит PropertyView, здесь — опорная рама
                mb.AddBox(metal, new Vector3(w * 0.3f, h + 1.2f, 1.5f), new Vector3(0.15f, 2.4f, 0.15f));
                mb.AddBox(metal, new Vector3(w * 0.7f, h + 1.2f, 1.5f), new Vector3(0.15f, 2.4f, 0.15f));
            }
        }

        void BuildStyleDetails(BuildingSpec s, Ctx c, DeterministicRandom rng, int wall, int trim)
        {
            var f = FrontFace(s);
            int metal = c.Sub(M.MetalDark);
            float h = s.Height;
            if (s.Backdrop) return;
            // Водосточные трубы по углам фасада
            if (s.Style != FacadeStyle.ModernGlass && s.Style != FacadeStyle.Pavilion)
            {
                foreach (float u in new[] { 0.25f, s.Width - 0.25f })
                {
                    c.Mb.AddCylinder(metal, f.P(u, 0.3f, -0.12f), 0.07f, h - 0.3f, 8, false);
                    FaceBox(c, f, metal, u, h - 0.1f, -0.25f, new Vector3(0.3f, 0.2f, 0.35f));
                }
            }
            if (s.Style == FacadeStyle.ClassicPlaster && s.Floors > 1)
            {
                // Пилястры между осями окон
                int perFloor = Mathf.Max(1, Mathf.FloorToInt((s.Width - 1.2f) / WindowSpacing(s)));
                float spacing = (s.Width - 1.2f) / perFloor;
                for (int i = 0; i <= perFloor; i++)
                    FaceBox(c, f, trim, 0.6f + i * spacing, (s.GroundFloorHeight + h) * 0.5f, -0.06f, new Vector3(0.4f, h - s.GroundFloorHeight - 0.4f, 0.12f));
                for (int fl = 2; fl < s.Floors; fl++)
                    FaceBox(c, f, trim, s.Width * 0.5f, s.GroundFloorHeight + (fl - 1) * s.FloorHeight, -0.05f, new Vector3(s.Width, 0.12f, 0.1f));
            }
            if (s.Style == FacadeStyle.Industrial)
            {
                // Ленточные окна под карнизом и офисный блок
                var glass = c.Sub(M.WindowGlass);
                for (float u = 2f; u < s.Width - 2f; u += 3f)
                    FaceBox(c, f, glass, u + 1.2f, h - 1.2f, -0.02f, new Vector3(2.4f, 0.9f, 0.04f));
                foreach (var face in new[] { LeftFace(s), RightFace(s), BackFace(s) })
                    FaceBox(c, face, metal, (face.U == Vector3.left || face.U == Vector3.right ? s.Width : s.Depth) * 0.5f, h - 0.1f, -0.05f, new Vector3((face.U == Vector3.left || face.U == Vector3.right ? s.Width : s.Depth), 0.2f, 0.1f));
            }
            if (s.Style == FacadeStyle.Pavilion)
            {
                // Толстый фриз-козырёк по всей ширине и колонны
                int accent = c.Sub(M.Paint("fascia", s.AccentColor, "concrete", 2f, 0.4f));
                FaceBox(c, f, accent, s.Width * 0.5f, h - 0.7f, -0.9f, new Vector3(s.Width + 0.6f, 1.4f, 1.8f));
                if (s.Width > 20f)
                    for (float u = 3f; u < s.Width - 2f; u += 8f)
                        c.Mb.AddCylinder(c.Sub(M.Aluminium), f.P(u, 0, -1.6f), 0.15f, h - 1.4f, 10);
            }
        }

        // ---------- LOD1 и коллайдеры ----------

        void BuildLod1(BuildingSpec s, Ctx c)
        {
            var mb = c.Mb;
            float w = s.Width, d = s.Depth, h = s.Height, gf = s.GroundFloorHeight;
            int band = c.Sub(M.Lit("lod_" + MaterialLibrary.Hex(s.Style == FacadeStyle.ModernGlass ? s.AccentColor : s.WallColor),
                s.Style == FacadeStyle.ModernGlass ? s.AccentColor : s.WallColor, "windowband", 1f, 0.2f));
            int baseMat = c.Sub(BaseMaterial(s));
            int roof = c.Sub(M.Roof);
            float sp = WindowSpacing(s);
            // UV: одна клетка текстуры = одно окно (по ширине) и один этаж (по высоте)
            void Facade(Vector3 a, Vector3 dir, float len)
            {
                var b = a + dir * len;
                mb.AddQuad(band, a + Vector3.up * gf, a + Vector3.up * h, b + Vector3.up * h, b + Vector3.up * gf,
                    new Vector2(0, 0), new Vector2(0, (h - gf) / s.FloorHeight * 0.25f), new Vector2(len / sp * 0.25f, (h - gf) / s.FloorHeight * 0.25f), new Vector2(len / sp * 0.25f, 0));
                mb.AddQuad(baseMat, a, a + Vector3.up * gf, b + Vector3.up * gf, b);
            }
            Facade(new Vector3(0, 0, 0), Vector3.right, w);
            Facade(new Vector3(w, 0, d), Vector3.left, w);
            Facade(new Vector3(0, 0, d), Vector3.back, d);
            Facade(new Vector3(w, 0, 0), Vector3.forward, d);
            if (s.Roof == RoofStyle.Pitched)
            {
                float ridge = h + 3.2f;
                int tiles = c.Sub(M.Paint("rooftiles", new Color(0.35f, 0.22f, 0.18f), "corrugated", 0.8f, 0.2f));
                mb.AddQuad(tiles, new Vector3(0, h, 0), new Vector3(0, ridge, d * 0.5f), new Vector3(w, ridge, d * 0.5f), new Vector3(w, h, 0));
                mb.AddQuad(tiles, new Vector3(w, h, d), new Vector3(w, ridge, d * 0.5f), new Vector3(0, ridge, d * 0.5f), new Vector3(0, h, d));
            }
            else mb.AddQuad(roof, new Vector3(0, h, 0), new Vector3(0, h, d), new Vector3(w, h, d), new Vector3(w, h, 0));
        }

        void BuildColliders(BuildingSpec s, Transform root)
        {
            var go = new GameObject("Colliders");
            go.transform.SetParent(root, false);
            float w = s.Width, d = s.Depth, gf = s.GroundFloorHeight, h = s.Height, t = 0.3f;
            void Box(Vector3 center, Vector3 size)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.center = center;
                bc.size = size;
            }

            // Если входов в помещения нет — здание сплошной блок.
            var doors = new List<Vector2>();
            var lintels = new List<float>();
            foreach (var u in s.Units)
            {
                float mid = u.Start + u.Width * 0.5f;
                if (u.Kind == GroundUnitKind.Shop && u.LotKey != null) { doors.Add(new Vector2(mid - 0.95f, mid + 0.95f)); lintels.Add(2.55f); }
                if (u.Kind == GroundUnitKind.OfficeLobby) { doors.Add(new Vector2(mid - 1.1f, mid + 1.1f)); lintels.Add(2.55f); }
                if (u.Kind == GroundUnitKind.LoadingDock) { doors.Add(new Vector2(mid - 2f, mid + 2f)); lintels.Add(5.4f); }
            }
            if (doors.Count == 0)
            {
                Box(new Vector3(w * 0.5f, h * 0.5f, d * 0.5f), new Vector3(w, h, d));
                return;
            }

            Box(new Vector3(w * 0.5f, (gf + h) * 0.5f + 0.01f, d * 0.5f), new Vector3(w, h - gf, d));
            Box(new Vector3(w * 0.5f, gf * 0.5f, d - t * 0.5f), new Vector3(w, gf, t));
            Box(new Vector3(t * 0.5f, gf * 0.5f, d * 0.5f), new Vector3(t, gf, d));
            Box(new Vector3(w - t * 0.5f, gf * 0.5f, d * 0.5f), new Vector3(t, gf, d));
            float x = 0f;
            for (int i = 0; i < doors.Count; i++)
            {
                var g = doors[i];
                if (g.x > x) Box(new Vector3((x + g.x) * 0.5f, gf * 0.5f, 0.25f), new Vector3(g.x - x, gf, 0.5f));
                if (gf > lintels[i]) Box(new Vector3((g.x + g.y) * 0.5f, (lintels[i] + gf) * 0.5f, 0.25f), new Vector3(g.y - g.x, gf - lintels[i], 0.5f));
                x = g.y;
            }
            if (x < w) Box(new Vector3((x + w) * 0.5f, gf * 0.5f, 0.25f), new Vector3(w - x, gf, 0.5f));

            // Рампа склада и пандус (наклонный коллайдер)
            foreach (var u in s.Units)
                if (u.Kind == GroundUnitKind.LoadingDock)
                {
                    Box(new Vector3(u.Start + u.Width * 0.5f, 0.55f, -1.4f), new Vector3(u.Width, 1.1f, 2.8f));
                    var ramp = new GameObject("Ramp");
                    ramp.transform.SetParent(go.transform, false);
                    float angle = Mathf.Atan2(1.1f, 4.4f) * Mathf.Rad2Deg;
                    ramp.transform.localPosition = new Vector3(u.Start - 2.2f, 0.45f, -1.5f);
                    ramp.transform.localRotation = Quaternion.Euler(0, 0, angle);
                    var rc = ramp.AddComponent<BoxCollider>();
                    rc.size = new Vector3(4.6f, 0.2f, 2.6f);
                }
        }
    }
}

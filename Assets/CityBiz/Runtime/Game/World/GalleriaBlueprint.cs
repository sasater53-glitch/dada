using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;

namespace CityBiz.Game
{
    /// <summary>
    /// ТРЦ «Галерея»: галерея 140 м вдоль атриума со стеклянной крышей, три этажа, две пары эскалаторов,
    /// панорамный лифт, мосты, островные киоски, фонтан, фудкорт, якорный супермаркет и кинотеатр.
    /// Все размеры помещений берутся из ядра (MallContent) — экономика и геометрия всегда совпадают.
    /// </summary>
    public sealed class GalleriaBlueprint : MallBlueprint
    {
        public override string MallId => MallContent.MallId;
        public override string DisplayName => "ТРЦ «Галерея»";

        const float FH = MallContent.FloorHeight;           // 6 м
        const float Slab = 0.5f;
        const float XMin = MallContent.GalleryXMin, XMax = MallContent.GalleryXMax;
        const float Void = MallContent.VoidHalfWidth;       // 7
        const float Front = MallContent.ShopfrontZ;         // 14
        const float Back = MallContent.ShopfrontZ + MallContent.UnitDepth; // 27
        const float FoodX = MallContent.FoodCourtXMin;      // 40
        const float StallFront = Back - 6f;                 // 21
        const float SkyEave = 18f, SkyRidge = 21f;
        const float OuterZ = 10.5f, InnerZ = 3.8f;          // линии движения пешеходов

        static readonly float[] ColumnX = { -62f, -42f, -18f, 6f, 34f, 54f };
        static readonly float[] TreeX = { -60f, -40f, -21f, 17f, 29f, 47f, 62f };

        MallKit K;
        MallInstance I;
        PedestrianGraph G;
        WorldDefinition _world;

        public override void SetWorld(WorldDefinition world) => _world = world;

        public override void Build(MallKit kit, MallInstance inst)
        {
            K = kit; I = inst; G = inst.Pedestrians;
            inst.Floors = 3;
            inst.Bounds = Rect.MinMaxRect(XMin - 42f, -Back, XMax + 2f, Back);
            inst.PlayerSpawn = new Vector3(64f, 0.05f, 0f);
            inst.PlayerSpawnYaw = -90f;
            inst.FoodCourtCenter = new Vector3(55f, 2 * FH, 0f);

            Floors();
            Facades();
            Skylight();
            Columns();
            Escalators();
            Elevator();
            EastEnd();
            WestEnd();
            FoodCourt();
            Atrium();
            Pedestrians();
            Units();
            G.Weld();
            Exterior();
            Roof();
            K.Flush();
            MapShapes();
        }

        // ---------------- Перекрытия ----------------

        void Floors()
        {
            var M = K.M;
            // Первый этаж — вся галерея
            K.Slab(Rect.MinMaxRect(XMin, -Front, XMax, Front), 0f, 0f, M.MallFloor, M.Ceiling, M.TrimWhite);
            for (int f = 1; f <= 2; f++)
            {
                float y = f * FH;
                float xEnd = f == 2 ? FoodX : XMax;
                // Галереи вдоль витрин и мост через атриум
                K.Slab(Rect.MinMaxRect(XMin, Void, xEnd, Front), y, Slab, M.WalkwayFloor, M.Ceiling, M.TrimWhite);
                K.Slab(Rect.MinMaxRect(XMin, -Front, xEnd, -Void), y, Slab, M.WalkwayFloor, M.Ceiling, M.TrimWhite);
                K.Slab(Rect.MinMaxRect(-3f, -Void, 3f, Void), y, Slab, M.WalkwayFloor, M.Ceiling, M.TrimWhite);
                Lights(Rect.MinMaxRect(XMin, Void, xEnd, Front), y - Slab, true);
                Lights(Rect.MinMaxRect(XMin, -Front, xEnd, -Void), y - Slab, true);
            }
            // Фудкорт — сплошная плита на третьем этаже
            K.Slab(Rect.MinMaxRect(FoodX, -StallFront, XMax, StallFront), 2 * FH, Slab, M.FoodCourtFloor, M.Ceiling, M.TrimWhite);
            Lights(Rect.MinMaxRect(FoodX, -Void, XMax, Void), 2 * FH - Slab, true);
            // Потолки третьего этажа
            float top = 3 * FH - Slab;
            K.Ceiling(Rect.MinMaxRect(XMin, Void, FoodX, Front), top, 0f, null);
            K.Ceiling(Rect.MinMaxRect(XMin, -Front, FoodX, -Void), top, 0f, null);
            K.Ceiling(Rect.MinMaxRect(FoodX, Void, XMax, StallFront), top, 0f, null);
            K.Ceiling(Rect.MinMaxRect(FoodX, -StallFront, XMax, -Void), top, 0f, null);
            Lights(Rect.MinMaxRect(XMin, Void, FoodX, Front), top, true);
            Lights(Rect.MinMaxRect(XMin, -Front, FoodX, -Void), top, true);
            // Короб над краем атриума под фонарём крыши
            foreach (float s in new[] { 1f, -1f })
            {
                var a = new Vector3(s > 0 ? XMin : XMax, top, s * Void);
                var b = new Vector3(s > 0 ? XMax : XMin, top, s * Void);
                K.Wall(a, b, SkyEave - top + 0.3f, M.TrimWhite, false);
            }
        }

        /// <summary>Световые линии на потолке галерей + точки для реальных источников света.</summary>
        void Lights(Rect r, float y, bool anchors)
        {
            for (float x = r.xMin + 4f; x < r.xMax - 1f; x += 8f)
            {
                K.Block(new Vector3(x, y - 0.03f, r.center.y), new Vector3(3.2f, 0.06f, 0.35f), K.M.CeilingLight, 0f, false, false);
                if (anchors && Mathf.RoundToInt((x - r.xMin - 4f) / 8f) % 2 == 0) I.LightAnchors.Add(new Vector3(x, y - 0.4f, r.center.y));
            }
        }

        // ---------------- Фасады вдоль галерей ----------------

        void Facades()
        {
            var M = K.M;
            for (int f = 0; f < 3; f++)
            {
                float y = f * FH;
                float xEnd = f == 2 ? FoodX - 2f : XMax;
                // Короб над витринами (от верха витрины до перекрытия)
                K.Wall(new Vector3(XMin, y + 4.2f, Front), new Vector3(xEnd, y + 4.2f, Front), FH - Slab - 4.2f, M.Wall, false);
                K.Wall(new Vector3(xEnd, y + 4.2f, -Front), new Vector3(XMin, y + 4.2f, -Front), FH - Slab - 4.2f, M.Wall, false);
                // Карниз-подсветка под вывесками
                K.Block(new Vector3((XMin + xEnd) * 0.5f, y + 4.24f, Front - 0.08f), new Vector3(xEnd - XMin, 0.08f, 0.16f), M.Aluminium, 0f, false, false);
                K.Block(new Vector3((XMin + xEnd) * 0.5f, y + 4.24f, -Front + 0.08f), new Vector3(xEnd - XMin, 0.08f, 0.16f), M.Aluminium, 0f, false, false);
            }
            // Пилоны между помещениями
            foreach (var p in World.AllProperties)
            {
                if (p.Kind != PropertyKind.ShopUnit && p.Kind != PropertyKind.Storage) continue;
                float z = p.Side == Frontage.North ? Front : -Front;
                foreach (float x in new[] { p.PlanX - p.Width * 0.5f, p.PlanX + p.Width * 0.5f })
                    K.Block(new Vector3(x, p.Floor * FH + 2.1f, z - Mathf.Sign(z) * 0.12f), new Vector3(0.36f, 4.2f, 0.24f), M.MetalPanel, 0f, true);
            }
        }

        WorldDefinition World => _world ?? (_world = MallContent.CreateWorld());

        // ---------------- Стеклянная крыша ----------------

        void Skylight()
        {
            var M = K.M;
            var mb = new MeshBuilder();
            foreach (float s in new[] { 1f, -1f })
            {
                var e0 = new Vector3(XMin, SkyEave, s * (Void + 0.3f));
                var e1 = new Vector3(XMax, SkyEave, s * (Void + 0.3f));
                var r0 = new Vector3(XMin, SkyRidge, 0f);
                var r1 = new Vector3(XMax, SkyRidge, 0f);
                mb.AddQuadUnit(0, e0, r0, r1, e1);
                mb.AddQuadUnit(0, e1, r1, r0, e0);
            }
            // Торцевые фронтоны
            foreach (float x in new[] { XMin, XMax })
            {
                mb.AddTriangle(0, new Vector3(x, SkyEave, -Void - 0.3f), new Vector3(x, SkyRidge, 0), new Vector3(x, SkyEave, Void + 0.3f));
                mb.AddTriangle(0, new Vector3(x, SkyEave, Void + 0.3f), new Vector3(x, SkyRidge, 0), new Vector3(x, SkyEave, -Void - 0.3f));
            }
            K.MeshObject(K.Static, "SkylightGlass", mb, new[] { M.SkyGlass }, false);
            // Стальные рёбра (одним мешем) и конёк
            var rb = new MeshBuilder();
            for (float x = XMin; x <= XMax + 0.1f; x += 5f)
            {
                foreach (float s in new[] { 1f, -1f })
                {
                    var a = new Vector3(x, SkyEave, s * (Void + 0.3f));
                    var b = new Vector3(x, SkyRidge, 0f);
                    float len = Vector3.Distance(a, b);
                    rb.Transform = Matrix4x4.TRS((a + b) * 0.5f, Quaternion.LookRotation((b - a).normalized, Vector3.up), Vector3.one);
                    rb.AddBox(0, Vector3.zero, new Vector3(0.12f, 0.22f, len));
                }
            }
            rb.Transform = Matrix4x4.identity;
            K.MeshObject(K.Static, "SkylightRibs", rb, new[] { M.Aluminium }, false);
            K.Block(new Vector3(0, SkyRidge, 0), new Vector3(XMax - XMin, 0.25f, 0.3f), M.Aluminium, 0f, false, false);
            K.Block(new Vector3(0, SkyEave, Void + 0.3f), new Vector3(XMax - XMin, 0.3f, 0.4f), M.Aluminium, 0f, false, false);
            K.Block(new Vector3(0, SkyEave, -Void - 0.3f), new Vector3(XMax - XMin, 0.3f, 0.4f), M.Aluminium, 0f, false, false);
        }

        // ---------------- Колонны ----------------

        void Columns()
        {
            foreach (float x in ColumnX)
                foreach (float s in new[] { 1f, -1f })
                    K.Column(new Vector3(x, 0f, s * (Void + 0.5f)), SkyEave);
        }

        // ---------------- Эскалаторы и лифт ----------------

        void Escalators()
        {
            float a = MallContent.EscalatorX[0], b = MallContent.EscalatorX[1];
            K.Escalator(new Vector3(a - 1.2f, 0f, -Void), new Vector3(a - 1.2f, FH, Void), true, "Escalator_1to2_up");
            K.Escalator(new Vector3(a + 1.2f, 0f, -Void), new Vector3(a + 1.2f, FH, Void), false, "Escalator_1to2_down");
            K.Escalator(new Vector3(b - 1.2f, FH, -Void), new Vector3(b - 1.2f, 2 * FH, Void), true, "Escalator_2to3_up");
            K.Escalator(new Vector3(b + 1.2f, FH, -Void), new Vector3(b + 1.2f, 2 * FH, Void), false, "Escalator_2to3_down");
            EscalatorDivider(a, 0f);
            EscalatorDivider(b, FH);
            I.EscalatorSounds.Add(new Vector3(a, 3f, 0f));
            I.EscalatorSounds.Add(new Vector3(b, 9f, 0f));
            // Указатели над эскалаторами
            Sign(new Vector3(a, 4.6f, -Void - 1.2f), 0f, "↑ 2 этаж", 1.8f);
            Sign(new Vector3(b, FH + 4.6f, -Void - 1.2f), 0f, "↑ 3 этаж · Фудкорт", 2.6f);
        }

        /// <summary>Невидимая преграда между парой эскалаторов — иначе в щель над атриумом можно шагнуть с галереи.</summary>
        void EscalatorDivider(float x, float y0)
        {
            var bottom = new Vector3(x, y0, -Void);
            var top = new Vector3(x, y0 + FH, Void);
            var dir = (top - bottom).normalized;
            var rot = Quaternion.LookRotation(dir, Vector3.up);
            K.RotatedBox((bottom + top) * 0.5f + rot * Vector3.up * 1.0f, new Vector3(1.0f, 2.2f, Vector3.Distance(bottom, top) + 1.6f), rot);
        }

        void Sign(Vector3 pos, float yaw, string text, float width)
        {
            var go = new GameObject("Sign");
            go.transform.SetParent(K.Static, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(board.GetComponent<Collider>());
            board.transform.SetParent(go.transform, false);
            board.transform.localScale = new Vector3(width, 0.45f, 0.06f);
            board.GetComponent<MeshRenderer>().sharedMaterial = K.M.Emissive("wayfinding", new Color(0.08f, 0.12f, 0.18f), new Color(0.05f, 0.08f, 0.12f));
            WorldText.Create(go.transform, "Text", text, new Vector3(0, 0, -0.04f), 0.22f, Color.white, width - 0.2f);
            var back = WorldText.Create(go.transform, "TextBack", text, new Vector3(0, 0, 0.04f), 0.22f, Color.white, width - 0.2f);
            back.transform.localRotation = Quaternion.Euler(0, 180, 0);
        }

        void Elevator()
        {
            var M = K.M;
            float x = MallContent.ElevatorX, zc = -Void + 1.25f;
            // Стеклянная шахта
            var mb = new MeshBuilder();
            float hw = 1.25f;
            var corners = new[] { new Vector3(x - hw, 0, zc - hw), new Vector3(x + hw, 0, zc - hw), new Vector3(x + hw, 0, zc + hw), new Vector3(x - hw, 0, zc + hw) };
            for (int i = 0; i < 4; i++)
            {
                var a = corners[i]; var b = corners[(i + 1) % 4];
                var up = Vector3.up * SkyEave;
                mb.AddQuad(0, a, a + up, b + up, b);
                mb.AddQuad(0, b, b + up, a + up, a);
                K.Box((a + b) * 0.5f + up * 0.5f, new Vector3(Mathf.Abs(b.x - a.x) + 0.1f, SkyEave, Mathf.Abs(b.z - a.z) + 0.1f));
            }
            K.MeshObject(K.Static, "ElevatorShaft", mb, new[] { M.RailGlass }, false);
            foreach (var c in corners) K.Block(c + Vector3.up * SkyEave * 0.5f, new Vector3(0.14f, SkyEave, 0.14f), M.Aluminium, 0f, false);
            // Кабина (стоит на первом этаже, декоративная)
            K.Block(new Vector3(x, 0.1f, zc), new Vector3(2.2f, 0.2f, 2.2f), M.MetalPanel, 0f, false);
            K.Block(new Vector3(x, 2.6f, zc), new Vector3(2.2f, 0.12f, 2.2f), M.Aluminium, 0f, false);
            for (int f = 0; f < 3; f++)
            {
                float y = f * FH;
                var stop = new FloorTeleport { Floor = f, Position = new Vector3(x, y + 0.05f, -Void - 1.6f), Yaw = 180f };
                I.ElevatorStops.Add(stop);
                var panel = new GameObject("ElevatorPanel_" + f);
                panel.transform.SetParent(K.Static, false);
                panel.transform.localPosition = new Vector3(x + hw + 0.35f, y, -Void - 0.05f);
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(box.GetComponent<Collider>());
                box.transform.SetParent(panel.transform, false);
                box.transform.localPosition = new Vector3(0, 1.25f, 0);
                box.transform.localScale = new Vector3(0.25f, 0.45f, 0.06f);
                box.GetComponent<MeshRenderer>().sharedMaterial = M.Emissive("elevator_panel", new Color(0.2f, 0.25f, 0.3f), new Color(0.2f, 0.5f, 0.9f));
                WorldText.Create(panel.transform, "Label", "ЛИФТ", new Vector3(0, 1.62f, -0.04f), 0.08f, Color.white, 0.4f);
                panel.AddComponent<ElevatorPanel>().Configure(f, new Vector3(0, 1.2f, -0.6f));
            }
        }

        // ---------------- Торцы ----------------

        void EastEnd()
        {
            var M = K.M;
            // Первый этаж: стеклянный фасад с главным входом
            float x = XMax;
            GlassWall(new Vector3(x, 0, Front), new Vector3(x, 0, 2.2f), 5.5f);
            GlassWall(new Vector3(x, 0, -2.2f), new Vector3(x, 0, -Front), 5.5f);
            K.Block(new Vector3(x, 4.2f, 0), new Vector3(0.3f, 2.6f, 4.4f), M.MetalPanel, 0f, false);
            var door = StorefrontView.CreateSlidingDoor(K.M, K.Static, new Vector3(x, 0, 0), 4.2f, 2.9f, -90f);
            door.name = "MainEntranceDoor";
            // Вход открыт, но выйти из ТЦ нельзя — невидимая преграда в тамбуре
            K.Box(new Vector3(x + 2.2f, 2.5f, 0), new Vector3(0.4f, 5f, 4.6f));
            WorldText.Create(K.Static, "EntranceSign", "ТРЦ «ГАЛЕРЕЯ»", new Vector3(x - 0.3f, 4.25f, 0), 0.55f, new Color(0.95f, 0.85f, 0.55f), 4.0f, TextAnchor.MiddleCenter, 90f);
            // Второй этаж: стеклянный фасад
            GlassWall(new Vector3(x, FH, Front), new Vector3(x, FH, -Front), FH - Slab);
            // Над атриумом — глухая стена до крыши
            K.Wall(new Vector3(x, 2 * FH, Void), new Vector3(x, 2 * FH, -Void), SkyEave - 2 * FH, M.Wall, false);
            // Стены коридоров на 2 этаже у торца уже закрыты фасадом; на первом — над входом
            K.Wall(new Vector3(x, 5.5f, Front), new Vector3(x, 5.5f, -Front), 0.5f, M.Wall, false);
        }

        void GlassWall(Vector3 a, Vector3 b, float height)
        {
            var M = K.M;
            var mb = new MeshBuilder();
            var up = Vector3.up * height;
            mb.AddQuad(0, a, a + up, b + up, b);
            mb.AddQuad(0, b, b + up, a + up, a);
            K.MeshObject(K.Static, "GlassFacade", mb, new[] { M.StoreGlass }, false);
            float len = Vector3.Distance(a, b);
            var dir = (b - a).normalized;
            for (float t = 0; t <= len + 0.01f; t += 2.4f)
                K.Block(a + dir * Mathf.Min(t, len) + up * 0.5f, new Vector3(0.1f, height, 0.1f), M.Aluminium, 0f, false);
            K.Block((a + b) * 0.5f + up, new Vector3(Mathf.Abs(b.x - a.x) + 0.1f, 0.15f, Mathf.Abs(b.z - a.z) + 0.1f), M.Aluminium, 0f, false);
            K.Box((a + b) * 0.5f + up * 0.5f, new Vector3(Mathf.Abs(b.x - a.x) + 0.2f, height, Mathf.Abs(b.z - a.z) + 0.2f));
        }

        void WestEnd()
        {
            var M = K.M;
            float x = XMin;
            // Над якорным магазином и на верхних этажах — глухая стена (якорь строит свою витрину)
            K.Wall(new Vector3(x, 4.2f, -Front), new Vector3(x, 4.2f, Front), SkyEave - 4.2f, M.Wall, true);
            // 2 этаж: рекламный LED-экран
            var screen = new GameObject("LedScreen");
            screen.transform.SetParent(K.Static, false);
            screen.transform.localPosition = new Vector3(x + 0.12f, FH + 3f, 0);
            screen.transform.localRotation = Quaternion.Euler(0, 90, 0);
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.transform.SetParent(screen.transform, false);
            panel.transform.localScale = new Vector3(12f, 4f, 0.15f);
            panel.GetComponent<MeshRenderer>().sharedMaterial = M.Emissive("led_screen", new Color(0.1f, 0.15f, 0.35f), new Color(0.25f, 0.4f, 1.2f));
            WorldText.Create(screen.transform, "Text", "ЛЕТНЯЯ РАСПРОДАЖА\nскидки до −50%", new Vector3(0, 0, -0.1f), 0.7f, new Color(1f, 0.95f, 0.6f), 11f);
            // 3 этаж: фасад кинотеатра
            var cinema = new GameObject("Cinema");
            cinema.transform.SetParent(K.Static, false);
            cinema.transform.localPosition = new Vector3(x + 0.1f, 2 * FH, 0);
            cinema.transform.localRotation = Quaternion.Euler(0, 90, 0);
            K.Block(new Vector3(x + 0.2f, 2 * FH + 2.6f, 0), new Vector3(0.2f, 5.2f, 26f), M.WallDark, 0f, false);
            WorldText.Create(cinema.transform, "Title", "КИНО · IMAX", new Vector3(0, 4.3f, -0.2f), 0.9f, new Color(1f, 0.3f, 0.25f), 10f);
            for (int i = -2; i <= 2; i++)
            {
                if (i == 0) continue;
                var poster = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(poster.GetComponent<Collider>());
                poster.transform.SetParent(cinema.transform, false);
                poster.transform.localPosition = new Vector3(i * 5f, 2f, -0.25f);
                poster.transform.localScale = new Vector3(2.2f, 3.1f, 0.08f);
                poster.GetComponent<MeshRenderer>().sharedMaterial = M.Emission("poster_" + i, Color.HSVToRGB(Mathf.Repeat(i * 0.23f + 0.5f, 1f), 0.6f, 0.9f), 0.9f);
            }
            WorldText.Create(cinema.transform, "Soon", "Скоро открытие", new Vector3(0, 1.6f, -0.25f), 0.3f, Color.white, 3f);
        }

        // ---------------- Фудкорт ----------------

        void FoodCourt()
        {
            var M = K.M;
            float y = 2 * FH;
            // Ограждение края атриума у западной границы фудкорта
            K.Railing(new Vector3(FoodX, y, -Void), new Vector3(FoodX, y, Void));
            // Ряды помещений 3 этажа заканчиваются за 2 м до фудкорта — закрываем проём на линии витрин
            K.Wall(new Vector3(FoodX - 2f, y, Front), new Vector3(FoodX, y, Front), FH - Slab, M.Wall);
            K.Wall(new Vector3(FoodX, y, -Front), new Vector3(FoodX - 2f, y, -Front), FH - Slab, M.Wall);
            // Стены фудкорта
            K.Wall(new Vector3(FoodX, y, Front), new Vector3(FoodX, y, StallFront), FH - Slab, M.Wall);
            K.Wall(new Vector3(FoodX, y, -StallFront), new Vector3(FoodX, y, -Front), FH - Slab, M.Wall);
            // Простенки между стеной фудкорта и рядами точек питания (лицом внутрь фудкорта)
            K.Wall(new Vector3(FoodX, y, StallFront), new Vector3(42f, y, StallFront), FH - Slab, M.Wall);
            K.Wall(new Vector3(66f, y, StallFront), new Vector3(XMax, y, StallFront), FH - Slab, M.Wall);
            K.Wall(new Vector3(42f, y, -StallFront), new Vector3(FoodX, y, -StallFront), FH - Slab, M.Wall);
            K.Wall(new Vector3(XMax, y, -StallFront), new Vector3(66f, y, -StallFront), FH - Slab, M.Wall);
            // Восточная стена с панорамным остеклением
            GlassWall(new Vector3(XMax, y, StallFront), new Vector3(XMax, y, -StallFront), FH - Slab);
            K.Wall(new Vector3(XMax, 3 * FH - Slab, StallFront), new Vector3(XMax, 3 * FH - Slab, -StallFront), SkyEave - 3 * FH + Slab, M.Wall, false);

            // Столы со стульями (GPU Instancing)
            var ir = K.Instanced;
            int seatIndex = 0;
            for (float x = 45f; x <= 66f; x += 5f)
                for (float z = -15f; z <= 15.1f; z += 5f)
                {
                    if (Mathf.Abs(z) < 1f && x < 52f) continue; // проход
                    var c = new Vector3(x, y, z);
                    AssetCatalog.AddInstanced(ir, AssetCatalog.Furniture + "tableRound", K.Root.localToWorldMatrix * Matrix4x4.TRS(c, Quaternion.identity, Vector3.one), 0.76f, FitAxis.Height, true, 90f);
                    K.Box(c + Vector3.up * 0.38f, new Vector3(1.1f, 0.76f, 1.1f));
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * 90f + 45f;
                        var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                        var chairPos = c + dir * 0.95f;
                        AssetCatalog.AddInstanced(ir, AssetCatalog.Furniture + "chairModernCushion", K.Root.localToWorldMatrix * Matrix4x4.TRS(chairPos, Quaternion.Euler(0, a + 180f, 0), Vector3.one), 0.86f, FitAxis.Height, true, 70f);
                        if ((seatIndex++ % 3) == 0)
                        {
                            int seat = G.AddNode(chairPos, PedNodeKind.Seat, -1, a + 180f, false);
                            G.LinkToNearest(seat, 3f);
                        }
                    }
                }
            // Подвесные светильники и большие растения
            for (float x = 45f; x <= 66f; x += 5f)
                for (float z = -15f; z <= 15.1f; z += 10f)
                    AssetCatalog.AddInstanced(ir, AssetCatalog.Furniture + "lampSquareCeiling", K.Root.localToWorldMatrix * Matrix4x4.TRS(new Vector3(x, 3 * FH - Slab - 0.6f, z), Quaternion.identity, Vector3.one), 0.6f, FitAxis.Height, false, 90f);
            foreach (var p in new[] { new Vector3(42f, y, 18.5f), new Vector3(42f, y, -18.5f), new Vector3(68.5f, y, 18.5f), new Vector3(68.5f, y, -18.5f) })
                AssetCatalog.Spawn(AssetCatalog.PolyHaven("potted_plant_01"), K.Static, p, 0f, 1.7f, FitAxis.Height, true);
            Sign(new Vector3(54f, y + 4.6f, 0f), 90f, "ФУДКОРТ", 3.2f);
        }

        // ---------------- Атриум первого этажа ----------------

        void Atrium()
        {
            var M = K.M;
            // Ограждения краёв атриума на 2 и 3 этажах (с проёмами для мостов, эскалаторов и лифта)
            float a = MallContent.EscalatorX[0], b = MallContent.EscalatorX[1], e = MallContent.ElevatorX;
            for (int f = 1; f <= 2; f++)
            {
                float y = f * FH;
                float xEnd = f == 2 ? FoodX : XMax;
                var gapsNorth = new List<Vector2> { new Vector2(-3f, 3f) };
                var gapsSouth = new List<Vector2> { new Vector2(-3f, 3f), new Vector2(e - 1.3f, e + 1.3f) };
                if (f == 1) { gapsNorth.Add(new Vector2(a - 2.4f, a + 2.4f)); gapsSouth.Add(new Vector2(b - 2.4f, b + 2.4f)); }
                if (f == 2) gapsNorth.Add(new Vector2(b - 2.4f, b + 2.4f));
                RailRow(XMin, xEnd, Void, y, gapsNorth);
                RailRow(XMin, xEnd, -Void, y, gapsSouth);
                K.Railing(new Vector3(-3f, y, -Void), new Vector3(-3f, y, Void));
                K.Railing(new Vector3(3f, y, Void), new Vector3(3f, y, -Void));
            }

            // Фонтан в центре
            var fc = new Vector3(0f, 0f, 0f);
            K.Cylinder(fc, 3.0f, 0.5f, M.ColumnStone, 32);
            var water = new MeshBuilder();
            water.AddDisc(0, fc + Vector3.up * 0.42f, 2.8f, 32, true);
            K.MeshObject(K.Static, "FountainWater", water, new[] { M.Water }, false);
            K.Cylinder(fc, 0.5f, 1.6f, M.ColumnStone, 16, false);
            var bowl = new MeshBuilder();
            bowl.AddCylinder(0, fc + Vector3.up * 1.6f, 1.1f, 0.18f, 24, true, 0.8f);
            K.MeshObject(K.Static, "FountainBowl", bowl, new[] { M.ColumnStone });
            FountainSpray(fc + Vector3.up * 1.8f);
            WetFloor(new Vector3(3.6f, 0f, 2.8f));

            // Деревья в кадках и скамейки (Poly Haven, CC0)
            var ir = K.Instanced;
            foreach (float x in TreeX)
                foreach (float s in new[] { 1f, -1f })
                {
                    var p = new Vector3(x, 0f, s * 5.5f);
                    K.Block(p + Vector3.up * 0.35f, new Vector3(1.6f, 0.7f, 1.6f), M.ColumnStone, 0f, true);
                    AssetCatalog.Spawn(AssetCatalog.PolyHaven("pachira_aquatica_01"), K.Static, p + Vector3.up * 0.7f, x * 13f, 3.2f, FitAxis.Height);
                    var bench = p + new Vector3(4f, 0f, 0f);
                    AssetCatalog.AddInstanced(ir, AssetCatalog.PolyHaven("modular_street_seating"), K.Root.localToWorldMatrix * Matrix4x4.TRS(bench, Quaternion.Euler(0, 0, 0), Vector3.one), 3.6f, FitAxis.Longest, true, 110f);
                    K.Box(bench + Vector3.up * 0.4f, new Vector3(3.6f, 0.8f, 0.8f));
                    int seat = G.AddNode(bench + new Vector3(0.6f, 0f, -s * 0.55f), PedNodeKind.Seat, -1, s > 0 ? 180f : 0f, false);
                    G.LinkToNearest(seat, 4f);
                }
            // Кадки с растениями на верхних галереях
            for (int f = 1; f <= 2; f++)
                for (float x = -58f; x < (f == 2 ? FoodX - 4f : XMax - 4f); x += 24f)
                    foreach (float s in new[] { 1f, -1f })
                    {
                        var p = new Vector3(x, f * FH, s * (Void + 1.2f));
                        AssetCatalog.AddInstanced(ir, AssetCatalog.PolyHaven("planter_box_01"), K.Root.localToWorldMatrix * Matrix4x4.TRS(p, Quaternion.Euler(0, 0, 0), Vector3.one), 1.4f, FitAxis.Longest, true, 90f);
                        AssetCatalog.Spawn(AssetCatalog.PolyHaven("potted_plant_02"), K.Static, p + Vector3.up * 0.35f, x, 0.9f);
                        K.Box(p + Vector3.up * 0.3f, new Vector3(1.4f, 0.6f, 0.7f));
                    }

            // Навигационные стенды-карты
            Directory(new Vector3(60f, 0f, -4.6f), 90f);
            Directory(new Vector3(a + 4.5f, 0f, 4.6f), 90f);
            Directory(new Vector3(a + 4.5f, FH, Void + 1.6f), 90f);
            Directory(new Vector3(b - 4.5f, 2 * FH, Void + 1.6f), -90f);

            // Большая вывеска ТЦ над атриумом
            var banner = new GameObject("AtriumBanner");
            banner.transform.SetParent(K.Static, false);
            banner.transform.localPosition = new Vector3(XMax - 0.5f, 14.5f, 0f);
            banner.transform.localRotation = Quaternion.Euler(0, -90, 0);
            WorldText.Create(banner.transform, "Text", "ГАЛЕРЕЯ", new Vector3(0, 0, 0), 1.6f, new Color(1f, 0.85f, 0.5f), 12f);
        }

        void RailRow(float x0, float x1, float z, float y, List<Vector2> gaps)
        {
            gaps.Sort((p, q) => p.x.CompareTo(q.x));
            float x = x0;
            foreach (var g in gaps)
            {
                if (g.x > x) K.Railing(new Vector3(x, y, z), new Vector3(g.x, y, z));
                x = Mathf.Max(x, g.y);
            }
            if (x < x1) K.Railing(new Vector3(x, y, z), new Vector3(x1, y, z));
        }

        void FountainSpray(Vector3 pos)
        {
            var go = new GameObject("FountainSpray");
            go.transform.SetParent(K.Static, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startSpeed = 4.2f;
            main.startLifetime = 1.0f;
            main.startSize = 0.1f;
            main.gravityModifier = 0.9f;
            main.maxParticles = 500;
            main.startColor = new Color(0.85f, 0.92f, 1f, 0.55f);
            var emission = ps.emission;
            emission.rateOverTime = 320f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.1f;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = K.M.Particles("water_spray", new Color(0.85f, 0.92f, 1f, 0.55f));
        }

        void WetFloor(Vector3 p) => AssetCatalog.Spawn(AssetCatalog.PolyHaven("WetFloorSign_01"), K.Static, p, 30f, 0.63f, FitAxis.Height, true);

        void Directory(Vector3 pos, float yaw)
        {
            var go = new GameObject("MallDirectory");
            go.transform.SetParent(K.Static, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var M = K.M;
            var stand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stand.transform.SetParent(go.transform, false);
            stand.transform.localPosition = new Vector3(0, 1.1f, 0);
            stand.transform.localScale = new Vector3(1.1f, 2.2f, 0.25f);
            stand.GetComponent<MeshRenderer>().sharedMaterial = M.Aluminium;
            var screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(screen.GetComponent<Collider>());
            screen.transform.SetParent(go.transform, false);
            screen.transform.localPosition = new Vector3(0, 1.3f, -0.13f);
            screen.transform.localScale = new Vector3(0.9f, 1.4f, 0.02f);
            screen.GetComponent<MeshRenderer>().sharedMaterial = M.Emissive("directory_screen", new Color(0.1f, 0.2f, 0.3f), new Color(0.3f, 0.6f, 0.9f));
            WorldText.Create(go.transform, "Title", "СХЕМА ТЦ\n[E]", new Vector3(0, 1.75f, -0.15f), 0.11f, Color.white, 0.8f);
            go.AddComponent<MallDirectoryPoint>().Configure(new Vector3(0, 1.3f, -0.9f));
        }

        // ---------------- Пешеходный граф ----------------

        void Pedestrians()
        {
            float a = MallContent.EscalatorX[0], b = MallContent.EscalatorX[1];
            // Первый этаж: внешние линии вдоль витрин и внутренние вдоль атриума
            foreach (float s in new[] { 1f, -1f })
            {
                G.AddPolyline(new[] { new Vector3(XMin + 2f, 0, s * OuterZ), new Vector3(XMax - 2f, 0, s * OuterZ) }, 6f);
                G.AddPolyline(new[] { new Vector3(XMin + 2f, 0, s * InnerZ), new Vector3(a - 3.5f, 0, s * InnerZ) }, 6f);
                G.AddPolyline(new[] { new Vector3(a + 3.5f, 0, s * InnerZ), new Vector3(XMax - 4f, 0, s * InnerZ) }, 6f);
                for (float x = XMin + 4f; x < XMax - 3f; x += 12f)
                {
                    if (Mathf.Abs(x - a) < 4f || Mathf.Abs(x - MallContent.ElevatorX) < 2.5f) continue;
                    G.Link(new Vector3(x, 0, s * OuterZ), new Vector3(x, 0, s * InnerZ));
                }
                // Витрины — остановиться, посмотреть
                for (float x = XMin + 6f; x < XMax - 4f; x += 11f)
                {
                    int idle = G.AddNode(new Vector3(x, 0, s * (Front - 1.4f)), PedNodeKind.Idle, -1, s > 0 ? 0f : 180f);
                    G.LinkToNearest(idle, 5f);
                }
            }
            G.Link(new Vector3(a - 3.5f, 0, InnerZ), new Vector3(a - 3.5f, 0, -InnerZ));
            G.Link(new Vector3(a + 3.5f, 0, InnerZ), new Vector3(a + 3.5f, 0, -InnerZ));
            // Главный вход — здесь покупатели появляются и уходят
            foreach (float z in new[] { 1.4f, -1.4f })
            {
                int door = G.AddNode(new Vector3(XMax - 0.8f, 0, z), PedNodeKind.Door, -2, -90f, false);
                G.LinkToNearest(door, 6f);
            }

            // Верхние этажи
            for (int f = 1; f <= 2; f++)
            {
                float y = f * FH;
                float xEnd = f == 2 ? FoodX + 1f : XMax - 2f;
                foreach (float s in new[] { 1f, -1f })
                {
                    G.AddPolyline(new[] { new Vector3(XMin + 2f, y, s * OuterZ), new Vector3(xEnd, y, s * OuterZ) }, 6f);
                    for (float x = XMin + 6f; x < xEnd - 3f; x += 11f)
                    {
                        int idle = G.AddNode(new Vector3(x, y, s * (Front - 1.4f)), PedNodeKind.Idle, -1, s > 0 ? 0f : 180f);
                        G.LinkToNearest(idle, 5f);
                    }
                }
                G.AddPolyline(new[] { new Vector3(0, y, OuterZ), new Vector3(0, y, 0), new Vector3(0, y, -OuterZ) }, 5f);
            }

            // Эскалаторы: каждая пара связывает южную галерею нижнего этажа с северной верхнего
            foreach (var (x, y0) in new[] { (a - 1.2f, 0f), (a + 1.2f, 0f), (b - 1.2f, FH), (b + 1.2f, FH) })
            {
                G.AddPolyline(new[]
                {
                    new Vector3(x, y0, -OuterZ), new Vector3(x, y0, -Void - 1.4f), new Vector3(x, y0, -Void),
                    new Vector3(x, y0 + FH, Void), new Vector3(x, y0 + FH, Void + 1.4f), new Vector3(x, y0 + FH, OuterZ),
                }, 20f);
            }

            // Фудкорт: сетка проходов между столами
            float yf = 2 * FH;
            for (float z = -17.5f; z <= 17.6f; z += 5f)
                G.AddPolyline(new[] { new Vector3(FoodX + 1f, yf, z), new Vector3(68f, yf, z) }, 5f);
            for (float x = 42.5f; x <= 67.6f; x += 5f)
                G.AddPolyline(new[] { new Vector3(x, yf, -17.5f), new Vector3(x, yf, 17.5f) }, 5f);
            foreach (float s in new[] { 1f, -1f })
                G.Link(new Vector3(FoodX + 1f, yf, s * OuterZ), new Vector3(FoodX + 1f, yf, s * 12.5f));
        }

        // ---------------- Помещения ----------------

        void Units()
        {
            var shops = new GameObject("Units").transform;
            shops.SetParent(K.Root, false);
            foreach (var def in World.AllProperties)
            {
                Vector3 pos;
                float yaw;
                float y = def.Floor * FH;
                switch (def.Side)
                {
                    case Frontage.North: pos = new Vector3(def.PlanX, y, def.PlanZ); yaw = 0f; break;
                    case Frontage.South: pos = new Vector3(def.PlanX, y, def.PlanZ); yaw = 180f; break;
                    case Frontage.West: pos = new Vector3(def.PlanX, y, def.PlanZ); yaw = -90f; break;
                    case Frontage.East: pos = new Vector3(def.PlanX, y, def.PlanZ); yaw = 90f; break;
                    default: pos = new Vector3(def.PlanX, y, def.PlanZ); yaw = 0f; break;
                }
                var view = StorefrontView.Create(K, shops, def, pos, yaw);
                // Дверь помещения — узел графа пешеходов
                var door = K.Root.InverseTransformPoint(view.DoorOutside);
                int node = G.AddNode(door, PedNodeKind.Door, def.Id, 0f, false);
                G.LinkToNearest(node, def.Kind == PropertyKind.Anchor ? 12f : 7f);
            }
        }

        void Exterior()
        {
            var M = K.M;
            // Площадь перед входом и внешний короб здания (видны через стекло)
            K.Slab(Rect.MinMaxRect(XMax, -60f, XMax + 60f, 60f), -0.02f, 0f, K.M.Surface("concrete_floor_worn_001", 4f, new Color(0.7f, 0.7f, 0.68f), 0.2f), M.Ceiling, M.TrimWhite, false);
            K.Block(new Vector3(XMax + 30f, 0.3f, -18f), new Vector3(20f, 0.6f, 6f), M.ColumnStone, 0f, false);
            for (float z = -40f; z <= 40f; z += 10f)
                AssetCatalog.Spawn(AssetCatalog.PolyHaven("potted_plant_01"), K.Static, new Vector3(XMax + 8f, 0f, z), z * 7f, 1.6f);
            // Наружные стены (тёмный фасад) по периметру этажей 2–3
            K.Wall(new Vector3(XMax + 0.4f, 0, Back + 1f), new Vector3(XMax + 0.4f, 0, Front), SkyEave, M.WallDark, false);
            K.Wall(new Vector3(XMax + 0.4f, 0, -Front), new Vector3(XMax + 0.4f, 0, -Back - 1f), SkyEave, M.WallDark, false);
            K.Block(new Vector3(XMax + 0.4f, SkyEave + 0.3f, 0), new Vector3(0.8f, 0.6f, 2 * Back + 2f), M.MetalPanel, 0f, false);
        }

        /// <summary>
        /// Непрозрачная кровля вокруг стеклянного фонаря: солнце попадает в ТЦ только через фонарь над атриумом
        /// (потолки помещений односторонние и сами по себе тень не отбрасывают).
        /// </summary>
        void Roof()
        {
            var M = K.M;
            float y = SkyEave + 0.4f;
            K.Slab(Rect.MinMaxRect(XMin, Void + 0.3f, XMax + 0.4f, Back + 1f), y, 0.5f, M.MetalPanel, M.Ceiling, M.MetalPanel, false);
            K.Slab(Rect.MinMaxRect(XMin, -Back - 1f, XMax + 0.4f, -Void - 0.3f), y, 0.5f, M.MetalPanel, M.Ceiling, M.MetalPanel, false);
            // Над якорным магазином (западнее галереи)
            K.Slab(Rect.MinMaxRect(XMin - MallContent.AnchorDepth - 2f, -Back - 1f, XMin, Back + 1f), y, 0.5f, M.MetalPanel, M.Ceiling, M.MetalPanel, false);
        }

        void MapShapes()
        {
            for (int f = 0; f < 3; f++)
            {
                float xEnd = f == 2 ? FoodX : XMax;
                I.Map.Add(new MapShape { Floor = f, Kind = 0, Min = new Vector2(XMin, -Front), Max = new Vector2(xEnd, Front) });
                if (f > 0)
                {
                    I.Map.Add(new MapShape { Floor = f, Kind = 5, Min = new Vector2(XMin, -Void), Max = new Vector2(xEnd, Void), Label = "атриум" });
                    I.Map.Add(new MapShape { Floor = f, Kind = 0, Min = new Vector2(-3, -Void), Max = new Vector2(3, Void) });
                }
                foreach (float x in MallContent.EscalatorX)
                    I.Map.Add(new MapShape { Floor = f, Kind = 2, Min = new Vector2(x - 2.4f, -Void), Max = new Vector2(x + 2.4f, Void), Label = "эск." });
                I.Map.Add(new MapShape { Floor = f, Kind = 3, Min = new Vector2(MallContent.ElevatorX - 1.25f, -Void), Max = new Vector2(MallContent.ElevatorX + 1.25f, -Void + 2.5f), Label = "лифт" });
            }
            I.Map.Add(new MapShape { Floor = 2, Kind = 4, Min = new Vector2(FoodX, -StallFront), Max = new Vector2(XMax, StallFront), Label = "Фудкорт" });
            foreach (var def in World.AllProperties)
            {
                Vector2 min, max;
                switch (def.Side)
                {
                    case Frontage.North: min = new Vector2(def.PlanX - def.Width / 2, def.PlanZ); max = new Vector2(def.PlanX + def.Width / 2, def.PlanZ + def.Depth); break;
                    case Frontage.South: min = new Vector2(def.PlanX - def.Width / 2, def.PlanZ - def.Depth); max = new Vector2(def.PlanX + def.Width / 2, def.PlanZ); break;
                    case Frontage.West: min = new Vector2(def.PlanX - def.Depth, def.PlanZ - def.Width / 2); max = new Vector2(def.PlanX, def.PlanZ + def.Width / 2); break;
                    default: min = new Vector2(def.PlanX - def.Width / 2, def.PlanZ - def.Depth / 2); max = new Vector2(def.PlanX + def.Width / 2, def.PlanZ + def.Depth / 2); break;
                }
                I.Map.Add(new MapShape { Floor = def.Floor, Kind = 1, Min = min, Max = max, Label = def.LotKey, LotKey = def.LotKey });
            }
        }
    }
}

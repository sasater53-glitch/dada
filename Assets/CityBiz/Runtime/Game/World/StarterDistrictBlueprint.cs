using CityBiz.Core;
using UnityEngine;

namespace CityBiz.Game
{
    /// <summary>
    /// Стартовый район «Липовая улица»: главная улица с перекрёстком, 12 зданий с проработанными фасадами,
    /// сквер, две парковки, две остановки, склад. Координаты в метрах, X — восток, Z — север.
    /// </summary>
    public sealed class StarterDistrictBlueprint : DistrictBlueprint
    {
        public override string DistrictId => StarterDistrictContent.DistrictId;
        public override string DisplayName => "Липовая улица";

        const float Walk = 0.15f;   // высота тротуара
        const float MainHalf = 6.5f; // половина проезжей части Липовой
        const float CrossX = 20f;    // ось Садовой
        const float CrossHalf = 4f;

        static readonly string[] DecorShops = { "Аптека", "Цветы", "Пекарня", "Оптика", "Книги", "Химчистка", "Барбершоп", "Салон связи", "Ремонт обуви", "Зоомагазин", "Фото", "Ключи" };

        public override DistrictLayout CreateLayout()
        {
            var L = new DistrictLayout
            {
                DistrictId = DistrictId,
                DisplayName = DisplayName,
                Bounds = Rect.MinMaxRect(-110, -62, 110, 62),
                GroundBounds = Rect.MinMaxRect(-400, -400, 400, 400),
                PlayerSpawn = new Vector3(-40f, Walk + 0.05f, 9.5f),
                PlayerSpawnYaw = 90f,
            };

            AddRoads(L);
            AddBuildings(L);
            AddOpenSpaces(L);
            AddStreetFurniture(L);
            AddTraffic(L);
            AddPedestrianNetwork(L);
            AddBackdrop(L);
            return L;
        }

        // ---------------- Дороги ----------------

        static void AddRoads(DistrictLayout L)
        {
            var main = new RoadSpec { Name = "ул. Липовая", Start = new Vector3(-175, 0, 0), End = new Vector3(175, 0, 0), CarriageWidth = MainHalf * 2f, SidewalkWidth = 5.5f, ParkingLanes = true };
            main.SidewalkGaps.Add(new Vector2(175 + CrossX - CrossHalf, 175 + CrossX + CrossHalf));
            L.Roads.Add(main);

            var cross = new RoadSpec { Name = "ул. Садовая", Start = new Vector3(CrossX, 0, -135), End = new Vector3(CrossX, 0, 135), CarriageWidth = CrossHalf * 2f, SidewalkWidth = 5f };
            cross.CarriageGaps.Add(new Vector2(135 - MainHalf, 135 + MainHalf));
            cross.SidewalkGaps.Add(new Vector2(135 - 12f, 135 + 12f));
            L.Roads.Add(cross);

            L.Intersections.Add(new IntersectionSpec { Center = new Vector3(CrossX, 0, 0), Size = new Vector2(CrossHalf * 2f, MainHalf * 2f) });

            // Кварталы (плинты на уровне тротуара)
            L.PlainGround.Add(Rect.MinMaxRect(-175, 12, CrossX - 9, 95));
            L.PlainGround.Add(Rect.MinMaxRect(CrossX + 9, 12, 175, 95));
            L.PlainGround.Add(Rect.MinMaxRect(-175, -95, CrossX - 9, -12));
            L.PlainGround.Add(Rect.MinMaxRect(CrossX + 9, -95, 175, -12));
        }

        // ---------------- Здания ----------------

        static BuildingSpec North(string key, float x0, float width, float depth, int floors, FacadeStyle style, Color wall, int seed)
            => new BuildingSpec { Key = key, Origin = new Vector3(x0, Walk, 12f), Yaw = 0f, Width = width, Depth = depth, Floors = floors, Style = style, WallColor = wall, Seed = seed };

        static BuildingSpec South(string key, float x0, float width, float depth, int floors, FacadeStyle style, Color wall, int seed)
            => new BuildingSpec { Key = key, Origin = new Vector3(x0 + width, Walk, -12f), Yaw = 180f, Width = width, Depth = depth, Floors = floors, Style = style, WallColor = wall, Seed = seed };

        static GroundUnit Shop(string lot, float start, float width, bool terrace = false, float depth = 10f, Color? awning = null) =>
            new GroundUnit { Kind = GroundUnitKind.Shop, LotKey = lot, Start = start, Width = width, Terrace = terrace, InteriorDepth = depth, AwningColor = awning ?? new Color(0.55f, 0.12f, 0.12f) };

        static GroundUnit Entrance(float start, params string[] apartments) =>
            new GroundUnit { Kind = GroundUnitKind.ResidentialEntrance, Start = start, Width = 3f, ApartmentLots = apartments };

        static void AddBuildings(DistrictLayout L)
        {
            // N1 — кирпичный дом 1930-х с кафе на первом этаже
            var n1 = North("N1", -104, 24, 16, 5, FacadeStyle.Brick, new Color(0.62f, 0.32f, 0.24f), 11);
            n1.HouseNumber = "Липовая, 12"; n1.Balconies = true;
            n1.Units.Add(Shop("N1.shop", 1.5f, 12f, terrace: true, awning: new Color(0.18f, 0.35f, 0.25f)));
            n1.Units.Add(Entrance(16f, "N1.apt5", "N1.apt14"));
            L.Buildings.Add(n1);

            // N2 — современный трёхэтажный бутик-хаус со стеклянным фасадом
            var n2 = North("N2", -78, 18, 14, 3, FacadeStyle.ModernGlass, new Color(0.85f, 0.85f, 0.82f), 12);
            n2.HouseNumber = "Липовая, 14"; n2.AccentColor = new Color(0.15f, 0.15f, 0.16f);
            n2.Units.Add(Shop("N2.shop", 1f, 16f));
            L.Buildings.Add(n2);

            // N3 — классический оштукатуренный дом с карнизами и рестораном
            var n3 = North("N3", -58, 24, 16, 4, FacadeStyle.ClassicPlaster, new Color(0.93f, 0.84f, 0.66f), 13);
            n3.HouseNumber = "Липовая, 16"; n3.Balconies = true;
            n3.Units.Add(Shop("N3.shop", 2f, 14f, terrace: true, awning: new Color(0.45f, 0.1f, 0.12f)));
            n3.Units.Add(Entrance(18.5f, "N3.apt3"));
            L.Buildings.Add(n3);

            // N4 — бизнес-центр «Липа»
            var n4 = North("N4", -30, 36, 18, 7, FacadeStyle.ModernGlass, new Color(0.55f, 0.6f, 0.65f), 14);
            n4.HouseNumber = "Липовая, 18"; n4.RoofSign = "БЦ «ЛИПА»"; n4.AccentColor = new Color(0.3f, 0.33f, 0.36f);
            n4.Units.Add(new GroundUnit { Kind = GroundUnitKind.OfficeLobby, Start = 12f, Width = 12f, LotKey = "N4.lobby", OfficeLots = new[] { "N4.office2", "N4.office3", "N4.office5" } });
            L.Buildings.Add(n4);

            // N5 — угловой дом на Садовой
            var n5 = North("N5", 30, 22, 16, 4, FacadeStyle.ConcretePanel, new Color(0.86f, 0.78f, 0.55f), 15);
            n5.HouseNumber = "Садовая, 3"; n5.AccentColor = new Color(0.65f, 0.35f, 0.2f); n5.Balconies = true;
            n5.Units.Add(Shop("N5.shop", 1f, 11f, awning: new Color(0.15f, 0.25f, 0.45f)));
            n5.Units.Add(Entrance(15f, "N5.apt2", "N5.apt9"));
            L.Buildings.Add(n5);

            // N6 — супермаркет, отодвинут вглубь за парковку
            var n6 = new BuildingSpec
            {
                Key = "N6", Origin = new Vector3(64, Walk, 32), Yaw = 0, Width = 40, Depth = 26, Floors = 1, GroundFloorHeight = 7.5f,
                Style = FacadeStyle.Pavilion, WallColor = new Color(0.82f, 0.83f, 0.8f), AccentColor = new Color(0.1f, 0.45f, 0.25f), Seed = 16,
                HouseNumber = "Липовая, 22", Roof = RoofStyle.FlatWithBillboard,
            };
            n6.Units.Add(Shop("N6.shop", 6f, 28f, depth: 22f));
            L.Buildings.Add(n6);

            // S1 — шестиэтажный панельный дом с двумя помещениями
            var s1 = South("S1", -104, 30, 15, 6, FacadeStyle.ConcretePanel, new Color(0.78f, 0.8f, 0.82f), 21);
            s1.HouseNumber = "Липовая, 11"; s1.AccentColor = new Color(0.2f, 0.45f, 0.6f); s1.Balconies = true;
            s1.Units.Add(Shop("S1.shopA", 1f, 11f));
            s1.Units.Add(Entrance(13.5f, "S1.apt7", "S1.apt21"));
            s1.Units.Add(Shop("S1.shopB", 18f, 10.5f));
            L.Buildings.Add(s1);

            // S2 — павильон у остановки
            var s2 = South("S2", -66, 14, 10, 1, FacadeStyle.Pavilion, new Color(0.9f, 0.9f, 0.88f), 22);
            s2.HouseNumber = "Липовая, 13"; s2.GroundFloorHeight = 4.2f; s2.AccentColor = new Color(0.85f, 0.45f, 0.1f);
            s2.Units.Add(Shop("S2.shop", 0.8f, 12.4f, depth: 8.5f));
            L.Buildings.Add(s2);

            // S3 — таунхаусы
            var s3a = South("S3a", -40, 14, 12, 3, FacadeStyle.Townhouse, new Color(0.55f, 0.36f, 0.3f), 23);
            s3a.Roof = RoofStyle.Pitched; s3a.HouseNumber = "Липовая, 15/1";
            s3a.Units.Add(Entrance(5.5f, "S3.apt1"));
            L.Buildings.Add(s3a);
            var s3b = South("S3b", -26, 14, 12, 3, FacadeStyle.Townhouse, new Color(0.42f, 0.47f, 0.52f), 24);
            s3b.Roof = RoofStyle.Pitched; s3b.HouseNumber = "Липовая, 15/2";
            s3b.Units.Add(Entrance(5.5f, "S3.apt2"));
            L.Buildings.Add(s3b);

            // S5 — склад во дворе, фасадом к площадке
            var s5 = new BuildingSpec
            {
                Key = "S5", Origin = new Vector3(106, Walk, -30), Yaw = 180, Width = 34, Depth = 26, Floors = 1, GroundFloorHeight = 8f,
                Style = FacadeStyle.Industrial, WallColor = new Color(0.55f, 0.6f, 0.62f), AccentColor = new Color(0.9f, 0.6f, 0.1f), Seed = 25,
                HouseNumber = "Липовая, 21с1",
            };
            s5.Units.Add(new GroundUnit { Kind = GroundUnitKind.LoadingDock, LotKey = "S5.warehouse", Start = 4f, Width = 26f, InteriorDepth = 24f });
            L.Buildings.Add(s5);
        }

        // ---------------- Открытые пространства ----------------

        static void AddOpenSpaces(DistrictLayout L)
        {
            L.ParkingLots.Add(new ParkingLotSpec { LotKey = "P1", Area = Rect.MinMaxRect(58, 13, 106, 30.5f), BaysAlongX = true, Occupancy = 0.55f, Sign = "P  Парковка" });
            L.ParkingLots.Add(new ParkingLotSpec { LotKey = "P2", Area = Rect.MinMaxRect(70, -28.5f, 108, -13), BaysAlongX = true, Occupancy = 0.45f, Sign = "P" });
            L.Parks.Add(new ParkSpec { Name = "Сквер «Липки»", Area = Rect.MinMaxRect(30, -54, 66, -13), Fountain = true, Playground = true });
            L.Parks.Add(new ParkSpec { Name = "Площадка", Area = Rect.MinMaxRect(-10, -40, 10.5f, -13), Fountain = false });
        }

        // ---------------- Уличная мебель ----------------

        static bool NearIntersection(float x) => Mathf.Abs(x - CrossX) < 12f;

        static void AddStreetFurniture(DistrictLayout L)
        {
            var P = L.Props;
            // Фонари и деревья вдоль Липовой (чередуются)
            for (float x = -105; x <= 105; x += 18)
            {
                if (!NearIntersection(x))
                {
                    P.Add(new PropSpec(PropKind.StreetLamp, new Vector3(x, Walk, 7.0f), 180f));
                    P.Add(new PropSpec(PropKind.StreetLamp, new Vector3(x + 9, Walk, -7.0f), 0f));
                }
                float tx = x + 9;
                if (!NearIntersection(tx) && Mathf.Abs(tx - 45) > 6 && tx <= 106)
                    P.Add(new PropSpec(PropKind.Tree, new Vector3(tx, Walk, 7.6f), (tx * 37) % 360, (int)(Mathf.Abs(tx) / 9) % 3));
                if (!NearIntersection(x) && Mathf.Abs(x + 44) > 6)
                    P.Add(new PropSpec(PropKind.Tree, new Vector3(x, Walk, -7.6f), (x * 53) % 360, (int)(Mathf.Abs(x) / 9 + 1) % 3));
            }
            // Садовая
            for (float z = 22; z <= 62; z += 18)
            {
                P.Add(new PropSpec(PropKind.StreetLamp, new Vector3(CrossX - CrossHalf - 0.6f, Walk, z), 90f));
                P.Add(new PropSpec(PropKind.StreetLamp, new Vector3(CrossX + CrossHalf + 0.6f, Walk, -z), -90f));
                P.Add(new PropSpec(PropKind.Tree, new Vector3(CrossX - CrossHalf - 1.2f, Walk, -z + 9), 0, 1));
                P.Add(new PropSpec(PropKind.Tree, new Vector3(CrossX + CrossHalf + 1.2f, Walk, z + 9), 0, 2));
            }

            // Скамейки и урны (смотрят на витрины)
            foreach (float x in new[] { -87f, -51f, -15f, 77f })
            {
                P.Add(new PropSpec(PropKind.Bench, new Vector3(x, Walk, 8.1f), 180f));
                P.Add(new PropSpec(PropKind.TrashBin, new Vector3(x + 1.6f, Walk, 7.4f)));
            }
            foreach (float x in new[] { -96f, -24f, 41f, 95f })
            {
                P.Add(new PropSpec(PropKind.Bench, new Vector3(x, Walk, -8.1f), 0f));
                P.Add(new PropSpec(PropKind.TrashBin, new Vector3(x + 1.6f, Walk, -7.4f)));
            }

            // Столбики на углах перекрёстка
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    for (int i = 0; i < 3; i++)
                        P.Add(new PropSpec(PropKind.Bollard, new Vector3(CrossX + sx * (CrossHalf + 0.4f + i * 0.9f), Walk, sz * (MainHalf + 0.35f))));

            // Паркоматы, гидранты, почта, велопарковки, кашпо
            foreach (float x in new[] { -80f, -30f, 70f, 98f }) P.Add(new PropSpec(PropKind.ParkingMeter, new Vector3(x, Walk, 6.9f), 180f));
            foreach (float x in new[] { -70f, -5f, 60f }) P.Add(new PropSpec(PropKind.ParkingMeter, new Vector3(x, Walk, -6.9f), 0f));
            P.Add(new PropSpec(PropKind.Hydrant, new Vector3(-62f, Walk, 7.1f)));
            P.Add(new PropSpec(PropKind.Hydrant, new Vector3(56f, Walk, -7.1f)));
            P.Add(new PropSpec(PropKind.Mailbox, new Vector3(-81.5f, Walk, 11.5f), 180f));
            P.Add(new PropSpec(PropKind.BikeRack, new Vector3(8f, Walk, 10.6f), 90f));
            P.Add(new PropSpec(PropKind.BikeRack, new Vector3(60.5f, Walk, 14.5f), 0f));
            foreach (float x in new[] { -27.5f, -19f, -1f, 3.5f }) P.Add(new PropSpec(PropKind.Planter, new Vector3(x, Walk, 11.1f), 0f, 0));
            P.Add(new PropSpec(PropKind.Planter, new Vector3(-58.6f, Walk, 10.9f), 0f, 1));
            P.Add(new PropSpec(PropKind.Planter, new Vector3(-40.6f, Walk, 10.9f), 0f, 1));

            // Зелёная полоса между угловым домом и парковкой
            for (float z = 14; z <= 28; z += 4.5f)
            {
                P.Add(new PropSpec(PropKind.Hedge, new Vector3(55, Walk, z), 90f));
            }
            P.Add(new PropSpec(PropKind.Tree, new Vector3(55, Walk, 16f), 15f, 0));
            P.Add(new PropSpec(PropKind.Tree, new Vector3(55, Walk, 26f), 75f, 2));

            // Дорожные знаки с названиями улиц и рекламный щит
            P.Add(new PropSpec(PropKind.RoadSign, new Vector3(CrossX - CrossHalf - 0.8f, Walk, 12.6f), 90f, 0, "ул. Садовая"));
            P.Add(new PropSpec(PropKind.RoadSign, new Vector3(CrossX + CrossHalf + 0.8f, Walk, -12.6f), -90f, 0, "ул. Садовая"));
            P.Add(new PropSpec(PropKind.RoadSign, new Vector3(-104f, Walk, 6.9f), 180f, 0, "ул. Липовая"));
            P.Add(new PropSpec(PropKind.RoadSign, new Vector3(104f, Walk, -6.9f), 0f, 0, "ул. Липовая"));
            P.Add(new PropSpec(PropKind.RoadSign, new Vector3(57.5f, Walk, 7f), 180f, 1, "P"));
            P.Add(new PropSpec(PropKind.Billboard, new Vector3(0f, Walk, -33f), 180f, 0, "ТВОЙ БИЗНЕС\nНАЧИНАЕТСЯ ЗДЕСЬ"));
            P.Add(new PropSpec(PropKind.Kiosk, new Vector3(36f, Walk, -17f), 0f, 0, "Цветы"));
            P.Add(new PropSpec(PropKind.Kiosk, new Vector3(-6f, Walk, -16.5f), 90f, 1, "Мороженое"));
            P.Add(new PropSpec(PropKind.PhoneBooth, new Vector3(-108f, Walk, -10.8f), 0f));

            // Остановки общественного транспорта
            L.BusStops.Add(new BusStopSpec { Name = "Липовая улица", Position = new Vector3(-44f, Walk, -9.6f), Yaw = 180f, Lane = 0, LaneDistance = 175f - 44f });
            L.BusStops.Add(new BusStopSpec { Name = "Садовая улица", Position = new Vector3(45f, Walk, 9.6f), Yaw = 0f, Lane = 1, LaneDistance = 175f - 45f });

            // Светофоры
            L.TrafficLights.Add(new TrafficLightSpec { Position = new Vector3(11.0f, Walk, -7.2f), Yaw = -90f, VehicleGroup = 0 });
            L.TrafficLights.Add(new TrafficLightSpec { Position = new Vector3(29.0f, Walk, 7.2f), Yaw = 90f, VehicleGroup = 0 });
            L.TrafficLights.Add(new TrafficLightSpec { Position = new Vector3(25.0f, Walk, -12.8f), Yaw = 180f, VehicleGroup = 1 });
            L.TrafficLights.Add(new TrafficLightSpec { Position = new Vector3(15.0f, Walk, 12.8f), Yaw = 0f, VehicleGroup = 1 });
        }

        // ---------------- Транспорт ----------------

        static void AddTraffic(DistrictLayout L)
        {
            // Стоп-линии за 1 м до зебры (зебры шириной 4 м с центрами в 13.5 и 26.5 / ±9.3)
            var eb = new TrafficLane { BusRoute = true, NextLane = 1 };
            eb.Points.Add(new Vector3(-175, 0, -1.75f)); eb.Points.Add(new Vector3(175, 0, -1.75f));
            eb.Stops.Add(new LaneStop { Distance = 175f + 10.5f, SignalGroup = 0 });
            var wb = new TrafficLane { BusRoute = true, NextLane = 0 };
            wb.Points.Add(new Vector3(175, 0, 1.75f)); wb.Points.Add(new Vector3(-175, 0, 1.75f));
            wb.Stops.Add(new LaneStop { Distance = 175f - 29.5f, SignalGroup = 0 });
            var nb = new TrafficLane { NextLane = 3, SpeedLimit = 9f };
            nb.Points.Add(new Vector3(CrossX + 1.75f, 0, -135)); nb.Points.Add(new Vector3(CrossX + 1.75f, 0, 135));
            nb.Stops.Add(new LaneStop { Distance = 135f - 12.3f, SignalGroup = 1 });
            var sb = new TrafficLane { NextLane = 2, SpeedLimit = 9f };
            sb.Points.Add(new Vector3(CrossX - 1.75f, 0, 135)); sb.Points.Add(new Vector3(CrossX - 1.75f, 0, -135));
            sb.Stops.Add(new LaneStop { Distance = 135f - 12.3f, SignalGroup = 1 });
            L.Lanes.Add(eb); L.Lanes.Add(wb); L.Lanes.Add(nb); L.Lanes.Add(sb);

            // Места у тротуара для припаркованных машин (не у перекрёстка, переходов и остановок)
            void Row(float zMin, float zMax, float x0, float x1)
            {
                for (float x = x0; x + 5.6f <= x1; x += 6f) L.ParkingLaneSpots.Add(Rect.MinMaxRect(x, zMin, x + 5.6f, zMax));
            }
            Row(3.8f, 6.3f, -105, -60); Row(3.8f, 6.3f, -56, 4); Row(3.8f, 6.3f, 33, 36); Row(3.8f, 6.3f, 56, 105);
            Row(-6.3f, -3.8f, -105, -54); Row(-6.3f, -3.8f, -34, 4); Row(-6.3f, -3.8f, 33, 105);
        }

        // ---------------- Пешеходы ----------------

        static void AddPedestrianNetwork(DistrictLayout L)
        {
            var g = L.Pedestrians;
            const float lineZ = 9.3f;
            float west = CrossX - CrossHalf - 2.5f, east = CrossX + CrossHalf + 2.5f; // 13.5 и 26.5

            g.AddPolyline(new[] { new Vector3(-108, Walk, lineZ), new Vector3(west, Walk, lineZ) });
            // Узлы в 84 и 89 — развилки к супермаркету и складу
            g.AddPolyline(new[] { new Vector3(east, Walk, lineZ), new Vector3(84, Walk, lineZ), new Vector3(108, Walk, lineZ) });
            g.AddPolyline(new[] { new Vector3(-108, Walk, -lineZ), new Vector3(west, Walk, -lineZ) });
            g.AddPolyline(new[] { new Vector3(east, Walk, -lineZ), new Vector3(89, Walk, -lineZ), new Vector3(108, Walk, -lineZ) });
            g.AddPolyline(new[] { new Vector3(west, Walk, lineZ), new Vector3(west, Walk, 62) });
            g.AddPolyline(new[] { new Vector3(east, Walk, lineZ), new Vector3(east, Walk, 62) });
            g.AddPolyline(new[] { new Vector3(west, Walk, -lineZ), new Vector3(west, Walk, -62) });
            g.AddPolyline(new[] { new Vector3(east, Walk, -lineZ), new Vector3(east, Walk, -62) });

            // Переходы через Липовую (сигнал 1 — зелёный у Садовой) и через Садовую (сигнал 0)
            foreach (float x in new[] { west, east })
                Crossing(g, new Vector3(x, Walk, lineZ), new Vector3(x, Walk, -lineZ), Vector3.forward, MainHalf, 1);
            foreach (float z in new[] { lineZ, -lineZ })
                Crossing(g, new Vector3(west, Walk, z), new Vector3(east, Walk, z), Vector3.right, CrossHalf, 0, CrossX);

            // Дорожки к супермаркету и складу через парковки
            g.AddPolyline(new[] { new Vector3(84, Walk, lineZ), new Vector3(84, Walk, 30.5f) });
            g.AddPolyline(new[] { new Vector3(89, Walk, -lineZ), new Vector3(89, Walk, -27.5f) });

            // Точки ожидания: остановки и витрины
            int busS = g.AddNode(new Vector3(-44f, Walk, -8.4f), PedNodeKind.BusStop, -1, 0f);
            g.LinkToNearest(busS);
            int busN = g.AddNode(new Vector3(45f, Walk, 8.4f), PedNodeKind.BusStop, -1, 180f);
            g.LinkToNearest(busN);
            foreach (float x in new[] { -72f, -66f, -95f, 36f })
            {
                int n = g.AddNode(new Vector3(x, Walk, 10.9f), PedNodeKind.Idle, -1, 0f);
                g.LinkToNearest(n);
            }
            foreach (float x in new[] { -99f, -80f, -60f })
            {
                int n = g.AddNode(new Vector3(x, Walk, -10.9f), PedNodeKind.Idle, -1, 180f);
                g.LinkToNearest(n);
            }
        }

        /// <summary>Переход: ожидание на тротуаре → спуск на проезжую часть → другой тротуар.</summary>
        static void Crossing(PedestrianGraph g, Vector3 a, Vector3 b, Vector3 axis, float roadHalf, int signal, float axisCenter = 0f)
        {
            float Coord(Vector3 v) => Vector3.Dot(v, axis);
            Vector3 At(float c, float y) { var v = a + axis * (c - Coord(a)); v.y = y; return v; }
            int na = g.AddNode(a), nb = g.AddNode(b);
            int curbA = g.AddNode(At(axisCenter + Mathf.Sign(Coord(a) - axisCenter) * (roadHalf + 0.4f), Walk));
            int roadA = g.AddNode(At(axisCenter + Mathf.Sign(Coord(a) - axisCenter) * (roadHalf - 0.5f), 0.02f));
            int roadB = g.AddNode(At(axisCenter + Mathf.Sign(Coord(b) - axisCenter) * (roadHalf - 0.5f), 0.02f));
            int curbB = g.AddNode(At(axisCenter + Mathf.Sign(Coord(b) - axisCenter) * (roadHalf + 0.4f), Walk));
            g.AddEdge(na, curbA);
            g.AddEdge(curbA, roadA, signal);
            g.AddEdge(roadA, roadB);
            g.AddEdge(roadB, curbB, signal);
            g.AddEdge(curbB, nb);
        }

        // ---------------- Фон ----------------

        static void AddBackdrop(DistrictLayout L)
        {
            var rng = new DeterministicRandom(777);
            var styles = new[] { FacadeStyle.Brick, FacadeStyle.ClassicPlaster, FacadeStyle.ConcretePanel, FacadeStyle.Brick, FacadeStyle.ClassicPlaster };
            var walls = new[]
            {
                new Color(0.6f, 0.34f, 0.26f), new Color(0.88f, 0.8f, 0.62f), new Color(0.75f, 0.77f, 0.78f), new Color(0.48f, 0.3f, 0.25f),
                new Color(0.8f, 0.62f, 0.5f), new Color(0.68f, 0.72f, 0.62f), new Color(0.86f, 0.86f, 0.8f),
            };
            int decor = 0;

            BuildingSpec Make(Vector3 origin, float yaw, float width)
            {
                var s = new BuildingSpec
                {
                    Key = "bg" + L.BackdropBuildings.Count, Origin = origin, Yaw = yaw, Width = width, Depth = rng.Range(12f, 16f),
                    Floors = rng.Range(3, 8), Style = styles[rng.Range(0, styles.Length)], WallColor = walls[rng.Range(0, walls.Length)],
                    Seed = rng.Range(0, 100000), Backdrop = true, Balconies = rng.Chance(0.4),
                };
                if (rng.Chance(0.7))
                    s.Units.Add(new GroundUnit
                    {
                        Kind = GroundUnitKind.Shop, Start = 1f, Width = Mathf.Min(10f, width - 2f), DecorName = DecorShops[decor++ % DecorShops.Length],
                        AwningColor = Color.HSVToRGB(rng.NextFloat(), 0.55f, 0.5f),
                    });
                L.BackdropBuildings.Add(s);
                return s;
            }

            // Продолжение Липовой за пределами игровой зоны
            foreach (var side in new[] { 1f, -1f })
            {
                for (float x = 110; x < 172;)
                {
                    float w = rng.Range(14f, 22f);
                    if (side > 0) Make(new Vector3(x, Walk, 12f), 0f, w); else Make(new Vector3(x + w, Walk, -12f), 180f, w);
                    x += w + 0.01f;
                }
                for (float x = -172; x < -110;)
                {
                    float w = rng.Range(14f, 22f);
                    if (x + w > -108f) w = -108f - x;
                    if (w < 8f) break;
                    if (side > 0) Make(new Vector3(x, Walk, 12f), 0f, w); else Make(new Vector3(x + w, Walk, -12f), 180f, w);
                    x += w + 0.01f;
                }
            }

            // Застройка вдоль Садовой за пределами игровой зоны
            for (float z = 34; z < 128;)
            {
                float w = rng.Range(14f, 20f);
                Make(new Vector3(CrossX - CrossHalf - 5f, Walk, z), -90f, w);
                z += w + 0.01f;
            }
            for (float z = 34; z < 128;)
            {
                float w = rng.Range(14f, 20f);
                Make(new Vector3(CrossX + CrossHalf + 5f, Walk, z + w), 90f, w);
                z += w + 0.01f;
            }
            for (float z = -128; z < -42;)
            {
                float w = rng.Range(14f, 20f);
                if (z + w > -42f) break;
                Make(new Vector3(CrossX - CrossHalf - 5f, Walk, z), -90f, w);
                z += w + 0.01f;
            }
            for (float z = -128; z < -58;)
            {
                float w = rng.Range(14f, 20f);
                if (z + w > -58f) break;
                Make(new Vector3(CrossX + CrossHalf + 5f, Walk, z + w), 90f, w);
                z += w + 0.01f;
            }

            // Второй ряд домов позади кварталов — закрывает горизонт
            for (float x = -104; x < 104;)
            {
                float w = rng.Range(18f, 26f);
                // Не заходим на застройку вдоль Садовой (x от -8 до 48)
                if (x + w < -8f || x > 48f)
                {
                    var n = Make(new Vector3(x, Walk, 66f), 0f, w); n.Floors = rng.Range(5, 10); n.Units.Clear();
                    var s = Make(new Vector3(x + w, Walk, -66f), 180f, w); s.Floors = rng.Range(5, 10); s.Units.Clear();
                }
                x += w + 2f;
            }
        }
    }
}

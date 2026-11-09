using System.Collections.Generic;

namespace CityBiz.Core
{
    /// <summary>
    /// План и экономическое содержимое ТРЦ «Галерея»: три этажа вдоль атриума 140 м, якорный супермаркет,
    /// островные киоски, фудкорт на третьем этаже, складские помещения. LotKey связывает помещение с его
    /// визуальным представлением в Unity (MallBlueprint строит геометрию из этих же данных).
    /// </summary>
    public static class MallContent
    {
        public const string MallId = "galleria";
        public const int GromovId = 101, VetrovaId = 102, IndependentsId = 103;

        // Габариты плана, м
        public const float GalleryXMin = -70f, GalleryXMax = 70f;
        public const float VoidHalfWidth = 7f;       // атриум (пустота над первым этажом)
        public const float ShopfrontZ = 14f;         // линия витрин
        public const float UnitDepth = 13f;
        public const float FloorHeight = 6f;
        public const float FoodCourtXMin = 40f;      // на 3 этаже восточнее — фудкорт
        public const float AnchorDepth = 40f;
        public static readonly float[] EscalatorX = { -30f, 22f };   // 1→2 и 2→3 этажи
        public const float ElevatorX = -50f;

        public static WorldDefinition CreateWorld()
        {
            var world = new WorldDefinition();
            world.AddMall(CreateMall());
            world.Rivals.Add(new RivalProfile
            {
                PlayerId = GromovId, Name = "Алексей Громов", StartingCash = 260_000, Aggression = 0.6f, Expansion = 0.6f, PriceBias = 0.9f, UpgradeDrive = 0.4f,
                Preferred = new[] { BusinessType.FastFood, BusinessType.ClothingStore, BusinessType.Electronics, BusinessType.Toys }, Color = 0xD9534Fu,
            });
            world.Rivals.Add(new RivalProfile
            {
                PlayerId = VetrovaId, Name = "Марина Ветрова", StartingCash = 420_000, Aggression = 0.3f, Expansion = 0.5f, PriceBias = 1.12f, UpgradeDrive = 0.8f,
                Preferred = new[] { BusinessType.Cafe, BusinessType.Jewelry, BusinessType.Cosmetics, BusinessType.Restaurant }, Color = 0x9B59B6u,
            });
            world.Rivals.Add(new RivalProfile
            {
                PlayerId = IndependentsId, Name = "Независимые арендаторы", StartingCash = 300_000, Aggression = 0f, Expansion = 0f, PriceBias = 1f, UpgradeDrive = 0.15f,
                Preferred = new BusinessType[0], Color = 0x7F8C8Du,
            });
            return world;
        }

        static readonly float[][] Rows =
        {
            // этаж 0: север, юг (юг начинается с двух складских помещений по 6 м)
            new float[] { 14, 12, 12, 16, 12, 14, 12, 12, 18, 18 },
            new float[] { 6, 6, 12, 14, 12, 12, 16, 12, 14, 14, 22 },
            // этаж 1
            new float[] { 16, 12, 12, 14, 12, 12, 16, 14, 12, 20 },
            new float[] { 12, 14, 16, 12, 12, 14, 12, 16, 12, 20 },
            // этаж 2 (до фудкорта)
            new float[] { 14, 12, 16, 12, 14, 12, 14, 14 },
            new float[] { 16, 12, 12, 14, 12, 16, 12, 14 },
        };

        static readonly float[] KioskX = { -14f, 12f, 40f, 54f };
        static readonly float[] FoodStallX = { 46f, 54f, 62f };

        static float Traffic(int floor, float x, PropertyKind kind)
        {
            float t = floor == 0 ? 1.15f : floor == 1 ? 1.0f : 0.92f;
            foreach (float ex in EscalatorX) if (System.Math.Abs(x - ex) < 12f) t += 0.15f;
            if (floor == 0 && x > 52f) t += 0.15f; // главный вход
            if (kind == PropertyKind.Kiosk) t += 0.15f;
            if (kind == PropertyKind.FoodStall) t += 0.2f;
            return t;
        }

        static long RentRate(int floor, PropertyKind kind)
        {
            switch (kind)
            {
                case PropertyKind.Kiosk: return 32;
                case PropertyKind.FoodStall: return 14;
                case PropertyKind.Anchor: return 2;
                case PropertyKind.Storage: return 4;
                default: return floor == 0 ? 8 : floor == 1 ? 7 : 6;
            }
        }

        static int _nextId;

        static PropertyDef Unit(MallDef mall, string lot, string name, PropertyKind kind, int floor, Frontage side, float x, float z, float width, float depth)
        {
            var def = new PropertyDef
            {
                Id = ++_nextId, LotKey = lot, Name = name, Kind = kind, Floor = floor, Side = side, PlanX = x, PlanZ = z, Width = width, Depth = depth,
                FootTraffic = Traffic(floor, x, kind),
            };
            def.BaseRent = (long)(def.Area * RentRate(floor, kind) * def.FootTraffic);
            def.BasePrice = def.BaseRent * 230;
            def.Address = $"{EconomyCatalog.FloorName(floor)}, {lot}";
            mall.Properties.Add(def);
            return def;
        }

        public static MallDef CreateMall()
        {
            _nextId = 0;
            var mall = new MallDef { Id = MallId, Name = "ТРЦ «Галерея»", Floors = 3, BaseVisitorsPerHour = 900f };

            // Якорный супермаркет в западном торце первого этажа
            Unit(mall, "A-1", "Якорное помещение (супермаркет)", PropertyKind.Anchor, 0, Frontage.West, GalleryXMin, 0f, 28f, AnchorDepth);

            for (int floor = 0; floor < 3; floor++)
            {
                for (int s = 0; s < 2; s++)
                {
                    var side = s == 0 ? Frontage.North : Frontage.South;
                    float x = GalleryXMin;
                    int n = 0;
                    foreach (float w in Rows[floor * 2 + s])
                    {
                        n++;
                        bool storage = floor == 0 && side == Frontage.South && n <= 2;
                        string prefix = storage ? "S" : (floor + 1) + (side == Frontage.North ? "N" : "S");
                        string lot = storage ? $"S-{n}" : $"{prefix}-{n:00}";
                        string name = storage ? $"Склад №{n}" : $"Помещение {lot}";
                        float z = side == Frontage.North ? ShopfrontZ : -ShopfrontZ;
                        Unit(mall, lot, name, storage ? PropertyKind.Storage : PropertyKind.ShopUnit, floor, side, x + w * 0.5f, z, w, UnitDepth);
                        x += w;
                    }
                }
            }

            for (int i = 0; i < KioskX.Length; i++)
                Unit(mall, $"K-{i + 1}", $"Островной киоск К-{i + 1}", PropertyKind.Kiosk, 0, Frontage.Island, KioskX[i], 0f, 4f, 4f);

            float stallFront = ShopfrontZ + UnitDepth - 6f;
            int stall = 0;
            foreach (var side in new[] { Frontage.North, Frontage.South })
                foreach (float x in FoodStallX)
                {
                    stall++;
                    Unit(mall, $"F-{stall}", $"Фудкорт, точка F-{stall}", PropertyKind.FoodStall, 2, side, x, side == Frontage.North ? stallFront : -stallFront, 8f, 6f);
                }

            AddInitialTenants(mall);
            return mall;
        }

        // ---------- Стартовые арендаторы ----------

        static readonly BusinessType[] ShopMix =
        {
            BusinessType.ClothingStore, BusinessType.Cosmetics, BusinessType.Electronics, BusinessType.ClothingStore, BusinessType.Toys,
            BusinessType.Cafe, BusinessType.Bookstore, BusinessType.ClothingStore, BusinessType.Jewelry, BusinessType.Restaurant,
        };

        static void AddInitialTenants(MallDef mall)
        {
            var rng = new DeterministicRandom(4711);
            int mix = 0;
            // Свободные точки с хорошей проходимостью оставляем для игрока
            var reservedForPlayer = new HashSet<string> { "1N-09", "1N-10", "1S-05", "1S-11", "2N-02", "2S-07", "3N-03", "K-2", "K-4", "F-2", "F-5", "S-2" };

            foreach (var p in mall.Properties)
            {
                if (reservedForPlayer.Contains(p.LotKey)) continue;
                BusinessType type;
                int owner;
                switch (p.Kind)
                {
                    case PropertyKind.Anchor:
                        mall.Initial.Add(new InitialTenancy { PropertyId = p.Id, OwnerPlayerId = VetrovaId, Business = BusinessType.Supermarket, BusinessName = "Гипермаркет «Корзина»", Staff = 6, Interior = 2, EquipmentLevel = 2, Assortment = 3, Marketing = 1, Security = 1 });
                        continue;
                    case PropertyKind.Storage:
                        mall.Initial.Add(new InitialTenancy { PropertyId = p.Id, OwnerPlayerId = 0, TenantPlayerId = VetrovaId, Business = BusinessType.Warehouse, Staff = 1 });
                        continue;
                    case PropertyKind.Kiosk:
                        type = rng.Chance(0.5) ? BusinessType.IceCream : BusinessType.Cosmetics;
                        owner = GromovId;
                        break;
                    case PropertyKind.FoodStall:
                        type = rng.Chance(0.75) ? BusinessType.FastFood : BusinessType.Cafe;
                        owner = rng.Chance(0.5) ? GromovId : IndependentsId;
                        break;
                    default:
                        if (rng.Chance(0.22)) continue; // часть помещений сдаётся
                        type = ShopMix[mix++ % ShopMix.Length];
                        if (type == BusinessType.Restaurant && p.Width < 14f) type = BusinessType.Cafe;
                        float roll = rng.NextFloat();
                        owner = roll < 0.2f ? GromovId : roll < 0.38f ? VetrovaId : IndependentsId;
                        break;
                }
                var def = EconomyCatalog.Get(type);
                mall.Initial.Add(new InitialTenancy
                {
                    PropertyId = p.Id, OwnerPlayerId = 0, TenantPlayerId = owner, Business = type,
                    BusinessName = NameGenerator.Brand(type, rng), PriceLevel = rng.Range(0.92f, 1.1f), Staff = def.MinStaff + rng.Range(0, 2),
                    Interior = rng.Range(1, 4), Marketing = rng.Range(0, 3), EquipmentLevel = rng.Range(1, 3), Assortment = rng.Range(1, 4), Security = rng.Range(0, 2),
                });
            }
        }
    }
}

namespace CityBiz.Core
{
    /// <summary>
    /// Экономическое содержимое стартового района «Липовая улица».
    /// LotKey связывает объект с участком из Unity-чертежа StarterDistrictBlueprint.
    /// </summary>
    public static class StarterDistrictContent
    {
        public const string DistrictId = "starter_linden";

        public const int GromovId = 101, VetrovaId = 102, IndependentsId = 103;

        public static WorldDefinition CreateWorld()
        {
            var world = new WorldDefinition();
            world.AddDistrict(CreateDistrict());
            world.Rivals.Add(new RivalProfile { PlayerId = GromovId, Name = "Алексей Громов", StartingCash = 220_000, Aggression = 0.6f, Expansion = 0.6f, PriceBias = 0.92f, Color = 0xD9534Fu });
            world.Rivals.Add(new RivalProfile { PlayerId = VetrovaId, Name = "Марина Ветрова", StartingCash = 350_000, Aggression = 0.3f, Expansion = 0.5f, PriceBias = 1.1f, Color = 0x9B59B6u });
            world.Rivals.Add(new RivalProfile { PlayerId = IndependentsId, Name = "Местные предприниматели", StartingCash = 150_000, Aggression = 0f, Expansion = 0f, PriceBias = 1f, Color = 0x7F8C8Du });
            return world;
        }

        static PropertyDef P(int id, string lot, string name, string address, PropertyKind kind, float area, long price, long rent, float traffic = 1f, int spots = 0) =>
            new PropertyDef { Id = id, LotKey = lot, Name = name, Address = address, Kind = kind, Area = area, BasePrice = price, BaseRent = rent, FootTraffic = traffic, ParkingSpots = spots };

        public static DistrictDef CreateDistrict()
        {
            var d = new DistrictDef { Id = DistrictId, Name = "Липовая улица", RentMultiplier = 1f, TrafficMultiplier = 1f, Wealth = 1f };
            var props = d.Properties;

            // Северная сторона Липовой улицы (с запада на восток)
            props.Add(P(101, "N1.shop", "Помещение на Липовой, 12", "ул. Липовая, 12", PropertyKind.CommercialUnit, 95, 210_000, 1_150, 1.1f));
            props.Add(P(102, "N1.apt5", "Квартира 5, Липовая 12", "ул. Липовая, 12, кв. 5", PropertyKind.Apartment, 54, 92_000, 290));
            props.Add(P(103, "N1.apt14", "Квартира 14, Липовая 12", "ул. Липовая, 12, кв. 14", PropertyKind.Apartment, 71, 118_000, 360));
            props.Add(P(104, "N2.shop", "Бутик на Липовой, 14", "ул. Липовая, 14", PropertyKind.CommercialUnit, 140, 260_000, 1_400, 1.0f));
            props.Add(P(105, "N3.shop", "Помещение на Липовой, 16", "ул. Липовая, 16", PropertyKind.CommercialUnit, 160, 280_000, 1_500, 1.05f));
            props.Add(P(106, "N3.apt3", "Квартира 3, Липовая 16", "ул. Липовая, 16, кв. 3", PropertyKind.Apartment, 88, 140_000, 420));
            props.Add(P(107, "N4.office2", "БЦ «Липа», офис 201", "ул. Липовая, 18, оф. 201", PropertyKind.Office, 120, 190_000, 950, 1f));
            props.Add(P(108, "N4.office3", "БЦ «Липа», офис 305", "ул. Липовая, 18, оф. 305", PropertyKind.Office, 85, 150_000, 720, 1f));
            props.Add(P(109, "N4.office5", "БЦ «Липа», офис 512", "ул. Липовая, 18, оф. 512", PropertyKind.Office, 140, 220_000, 1_100, 1f));
            props.Add(P(110, "N5.shop", "Угловое помещение, Садовая 3", "ул. Садовая, 3", PropertyKind.CommercialUnit, 110, 250_000, 1_300, 1.3f));
            props.Add(P(111, "N5.apt2", "Квартира 2, Садовая 3", "ул. Садовая, 3, кв. 2", PropertyKind.Apartment, 48, 85_000, 260));
            props.Add(P(112, "N5.apt9", "Квартира 9, Садовая 3", "ул. Садовая, 3, кв. 9", PropertyKind.Apartment, 63, 104_000, 320));
            props.Add(P(113, "N6.shop", "Здание супермаркета", "ул. Липовая, 22", PropertyKind.CommercialUnit, 620, 420_000, 2_300, 1.15f));
            props.Add(P(114, "P1", "Парковка у супермаркета", "ул. Липовая, 22А", PropertyKind.Parking, 900, 120_000, 520, 1f, 36));

            // Южная сторона
            props.Add(P(115, "S1.shopA", "Помещение А, Липовая 11", "ул. Липовая, 11", PropertyKind.CommercialUnit, 90, 190_000, 1_000, 1.0f));
            props.Add(P(116, "S1.shopB", "Помещение Б, Липовая 11", "ул. Липовая, 11Б", PropertyKind.CommercialUnit, 70, 160_000, 850, 0.95f));
            props.Add(P(117, "S1.apt7", "Квартира 7, Липовая 11", "ул. Липовая, 11, кв. 7", PropertyKind.Apartment, 58, 96_000, 300));
            props.Add(P(118, "S1.apt21", "Квартира 21, Липовая 11", "ул. Липовая, 11, кв. 21", PropertyKind.Apartment, 76, 124_000, 380));
            props.Add(P(119, "S2.shop", "Павильон у остановки", "ул. Липовая, 13", PropertyKind.CommercialUnit, 60, 150_000, 950, 1.35f));
            props.Add(P(120, "S3.apt1", "Таунхаус 1, Липовая 15", "ул. Липовая, 15/1", PropertyKind.Apartment, 110, 165_000, 480));
            props.Add(P(121, "S3.apt2", "Таунхаус 2, Липовая 15", "ул. Липовая, 15/2", PropertyKind.Apartment, 110, 165_000, 480));
            props.Add(P(122, "S5.warehouse", "Склад на Садовой", "ул. Садовая, 8", PropertyKind.Warehouse, 800, 260_000, 1_150, 1f));
            props.Add(P(123, "P2", "Парковка на Садовой", "ул. Садовая, 6", PropertyKind.Parking, 650, 95_000, 420, 1f, 24));

            var init = d.Initial;
            init.Add(new InitialTenancy { PropertyId = 101, OwnerPlayerId = 0, TenantPlayerId = GromovId, Business = BusinessType.Cafe, BusinessName = "Кофейня «Зерно»", PriceLevel = 0.95f, Staff = 2 });
            init.Add(new InitialTenancy { PropertyId = 104, OwnerPlayerId = IndependentsId, Business = BusinessType.ClothingStore, BusinessName = "Urban Wear", PriceLevel = 1.05f, Staff = 2 });
            init.Add(new InitialTenancy { PropertyId = 105, OwnerPlayerId = 0, TenantPlayerId = IndependentsId, Business = BusinessType.Restaurant, BusinessName = "Ресторан «Терраса»", PriceLevel = 1.1f, Staff = 3 });
            init.Add(new InitialTenancy { PropertyId = 107, OwnerPlayerId = 0, TenantPlayerId = VetrovaId, Business = BusinessType.Office, BusinessName = "Консалтинг «Вектор»", Staff = 3 });
            init.Add(new InitialTenancy { PropertyId = 113, OwnerPlayerId = VetrovaId, Business = BusinessType.Supermarket, BusinessName = "Маркет «Соседи»", PriceLevel = 1.05f, Staff = 3 });
            init.Add(new InitialTenancy { PropertyId = 114, OwnerPlayerId = VetrovaId });
            init.Add(new InitialTenancy { PropertyId = 102, OwnerPlayerId = 0, ApartmentOccupied = true });
            init.Add(new InitialTenancy { PropertyId = 106, OwnerPlayerId = IndependentsId, ApartmentOccupied = true, ForSale = true });
            init.Add(new InitialTenancy { PropertyId = 111, OwnerPlayerId = 0, ApartmentOccupied = true });
            init.Add(new InitialTenancy { PropertyId = 117, OwnerPlayerId = 0, ApartmentOccupied = true });
            init.Add(new InitialTenancy { PropertyId = 120, OwnerPlayerId = GromovId, ApartmentOccupied = true });
            return d;
        }
    }
}

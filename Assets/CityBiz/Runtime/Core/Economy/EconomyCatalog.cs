using System;
using System.Collections.Generic;

namespace CityBiz.Core
{
    /// <summary>Тип помещения в торговом центре.</summary>
    public enum PropertyKind : byte { ShopUnit, Kiosk, FoodStall, Anchor, Storage }

    public enum BusinessType : byte
    {
        None, Cafe, Restaurant, FastFood, IceCream, ClothingStore, Electronics, Toys, Cosmetics, Bookstore, Jewelry, Supermarket, Warehouse,
    }

    public enum StaffRole : byte { Barista, Cook, Waiter, Seller, Cashier, Manager, Consultant, Storekeeper }

    /// <summary>Направления развития предприятия.</summary>
    public enum UpgradeKind : byte { Interior, Marketing, Equipment, Assortment, Security }

    public sealed class UpgradeDef
    {
        public UpgradeKind Kind;
        public string Name;
        public string Description;
        public int MinLevel, MaxLevel;
        public float BaseCost;          // за переход на уровень 2 при площади 100 м²
        public int RenovationHours;     // на сколько игровых часов закрывается точка
        public long DailyCostPerLevel;  // постоянные расходы (реклама, охрана)
    }

    /// <summary>Параметры типа бизнеса. Баланс задаётся здесь, а не разбросан по коду.</summary>
    public sealed class BusinessTypeDef
    {
        public BusinessType Type;
        public string Name;
        public string Description;
        public PropertyKind[] AllowedKinds;
        public long SetupCost;
        public float AvgTicket;
        public float CogsRatio;          // себестоимость как доля выручки (общепит)
        public int OpenHour, CloseHour;  // CloseHour может быть > 24
        public float VisitShare;         // доля посетителей ТЦ, которым нужна эта категория
        public float[] HourCurve;        // 24 множителя спроса по часам
        public int MinStaff, MaxStaff;
        public StaffRole[] Roles;
        public float CustomersPerStaffHour;
        public bool UsesStock;
        public float StockUnitCost;
        public float PriceElasticity;
        public float RainMultiplier = 1f;
        public float WeekendMultiplier = 1f;
        public bool FootTraffic = true;
        public float StorageIncomePerDay;

        public bool IsOpenAt(int hour)
        {
            if (CloseHour <= 24) return hour >= OpenHour && hour < CloseHour;
            return hour >= OpenHour || hour < CloseHour - 24;
        }

        public bool Allows(PropertyKind kind) => Array.IndexOf(AllowedKinds, kind) >= 0;
        public bool IsFood => CogsRatio > 0f;
    }

    public static class EconomyCatalog
    {
        public const long StartingCash = 160_000;
        public const float TaxRate = 0.06f;
        public const float PropertyTaxPerDay = 0.0004f;
        public const float SellBackRatio = 0.8f;
        public const int EvictionDebtDays = 3;
        public const float RepairCostPerPoint = 6_000;
        public const int MallOpenHour = 10, MallCloseHour = 22;
        public const string Currency = "$";

        static readonly Dictionary<BusinessType, BusinessTypeDef> Defs = new Dictionary<BusinessType, BusinessTypeDef>();
        static readonly UpgradeDef[] Upgrades =
        {
            new UpgradeDef { Kind = UpgradeKind.Interior, Name = "Интерьер", MinLevel = 1, MaxLevel = 5, BaseCost = 9_000, RenovationHours = 2,
                Description = "Дизайн зала, мебель, освещение. Повышает качество, удовлетворённость и репутацию." },
            new UpgradeDef { Kind = UpgradeKind.Marketing, Name = "Витрина и реклама", MinLevel = 0, MaxLevel = 4, BaseCost = 6_000, DailyCostPerLevel = 90,
                Description = "Световая вывеска, экраны, промо-стойки, реклама в ТЦ. Больше посетителей заходит именно к вам." },
            new UpgradeDef { Kind = UpgradeKind.Equipment, Name = "Оборудование", MinLevel = 1, MaxLevel = 5, BaseCost = 8_000, RenovationHours = 1,
                Description = "Кассы, кофемашины, кухня, стеллажи. Больше клиентов обслуживается в час, можно нанять больше персонала." },
            new UpgradeDef { Kind = UpgradeKind.Assortment, Name = "Ассортимент", MinLevel = 1, MaxLevel = 5, BaseCost = 7_000,
                Description = "Больше товаров и блюд — выше средний чек и шире круг покупателей." },
            new UpgradeDef { Kind = UpgradeKind.Security, Name = "Охрана", MinLevel = 0, MaxLevel = 3, BaseCost = 5_000, DailyCostPerLevel = 160,
                Description = "Видеонаблюдение и охранник. Защищает от вредительства конкурентов." },
        };

        static float[] Curve(params float[] v) => v;

        // Типовой профиль посещаемости магазинов ТЦ (по часам)
        static readonly float[] RetailCurve = Curve(0,0,0,0,0,0,0,0,0,0,0.5f,0.7f,0.9f,1.0f,1.0f,1.0f,1.1f,1.3f,1.45f,1.35f,1.0f,0.6f,0,0);

        static BusinessTypeDef Retail(BusinessType type, string name, string description, long setup, float ticket, float share, float stockCost,
            float elasticity, float weekend, PropertyKind[] kinds, StaffRole[] roles, float perStaff = 8f, int maxStaff = 6)
            => new BusinessTypeDef
            {
                Type = type, Name = name, Description = description, AllowedKinds = kinds, SetupCost = setup, AvgTicket = ticket,
                OpenHour = MallOpenHour, CloseHour = MallCloseHour, VisitShare = share, HourCurve = RetailCurve, MinStaff = 1, MaxStaff = maxStaff,
                Roles = roles, CustomersPerStaffHour = perStaff, UsesStock = true, StockUnitCost = stockCost, PriceElasticity = elasticity,
                RainMultiplier = 1.1f, WeekendMultiplier = weekend,
            };

        static EconomyCatalog()
        {
            var shop = new[] { PropertyKind.ShopUnit };
            var shopOrKiosk = new[] { PropertyKind.ShopUnit, PropertyKind.Kiosk };
            var sellers = new[] { StaffRole.Seller, StaffRole.Cashier, StaffRole.Consultant };

            Add(new BusinessTypeDef
            {
                Type = BusinessType.Cafe, Name = "Кофейня", Description = "Кофе и десерты. Пик утром и после обеда, любит дождливую погоду.",
                AllowedKinds = new[] { PropertyKind.ShopUnit, PropertyKind.FoodStall }, SetupCost = 28_000, AvgTicket = 8f, CogsRatio = 0.3f,
                OpenHour = 9, CloseHour = 22, VisitShare = 0.09f,
                HourCurve = Curve(0,0,0,0,0,0,0,0,0,0.7f,1.0f,1.2f,1.1f,1.0f,1.0f,1.2f,1.3f,1.2f,1.1f,0.9f,0.7f,0.4f,0,0),
                MinStaff = 1, MaxStaff = 5, Roles = new[] { StaffRole.Barista, StaffRole.Cashier }, CustomersPerStaffHour = 16f,
                PriceElasticity = 1.5f, RainMultiplier = 1.2f, WeekendMultiplier = 1.15f,
            });
            Add(new BusinessTypeDef
            {
                Type = BusinessType.Restaurant, Name = "Ресторан", Description = "Высокий средний чек, обеды и семейные ужины.",
                AllowedKinds = shop, SetupCost = 70_000, AvgTicket = 30f, CogsRatio = 0.35f,
                OpenHour = 11, CloseHour = 23, VisitShare = 0.035f,
                HourCurve = Curve(0,0,0,0,0,0,0,0,0,0,0,0.7f,1.5f,1.4f,0.8f,0.6f,0.7f,1.0f,1.6f,1.8f,1.5f,1.0f,0.5f,0),
                MinStaff = 2, MaxStaff = 8, Roles = new[] { StaffRole.Cook, StaffRole.Waiter }, CustomersPerStaffHour = 6f,
                PriceElasticity = 1.2f, RainMultiplier = 1.1f, WeekendMultiplier = 1.35f,
            });
            Add(new BusinessTypeDef
            {
                Type = BusinessType.FastFood, Name = "Фастфуд", Description = "Бургеры, пицца, суши на фудкорте. Огромный поток в обед и вечером.",
                AllowedKinds = new[] { PropertyKind.FoodStall }, SetupCost = 32_000, AvgTicket = 10f, CogsRatio = 0.38f,
                OpenHour = MallOpenHour, CloseHour = MallCloseHour, VisitShare = 0.11f,
                HourCurve = Curve(0,0,0,0,0,0,0,0,0,0,0.4f,0.8f,1.6f,1.6f,1.0f,0.8f,0.9f,1.2f,1.6f,1.5f,1.1f,0.6f,0,0),
                MinStaff = 1, MaxStaff = 6, Roles = new[] { StaffRole.Cook, StaffRole.Cashier }, CustomersPerStaffHour = 18f,
                PriceElasticity = 1.7f, RainMultiplier = 1.15f, WeekendMultiplier = 1.3f,
            });
            Add(new BusinessTypeDef
            {
                Type = BusinessType.IceCream, Name = "Мороженое и десерты", Description = "Островной киоск: дёшево открыть, импульсные покупки.",
                AllowedKinds = new[] { PropertyKind.Kiosk, PropertyKind.FoodStall }, SetupCost = 12_000, AvgTicket = 4.5f, CogsRatio = 0.3f,
                OpenHour = MallOpenHour, CloseHour = MallCloseHour, VisitShare = 0.05f, HourCurve = RetailCurve,
                MinStaff = 1, MaxStaff = 3, Roles = new[] { StaffRole.Seller, StaffRole.Cashier }, CustomersPerStaffHour = 22f,
                PriceElasticity = 1.6f, RainMultiplier = 0.9f, WeekendMultiplier = 1.4f,
            });
            Add(Retail(BusinessType.ClothingStore, "Магазин одежды", "Мода — главный магнит торгового центра. Выходные — лучшие дни.",
                45_000, 46f, 0.06f, 20f, 1.4f, 1.6f, shop, sellers, 7f, 7));
            Add(Retail(BusinessType.Electronics, "Электроника", "Телефоны, ноутбуки, аксессуары. Высокий чек, дорогой товар.",
                65_000, 95f, 0.025f, 62f, 1.3f, 1.4f, shopOrKiosk, new[] { StaffRole.Consultant, StaffRole.Cashier }, 5f, 6));
            Add(Retail(BusinessType.Toys, "Игрушки", "Семьи с детьми, особенно в выходные и праздники.",
                38_000, 28f, 0.025f, 12f, 1.3f, 1.8f, shop, sellers, 8f, 5));
            Add(Retail(BusinessType.Cosmetics, "Косметика и парфюмерия", "Стабильный спрос, хорошая маржа.",
                40_000, 32f, 0.04f, 13f, 1.4f, 1.3f, shopOrKiosk, sellers, 9f, 5));
            Add(Retail(BusinessType.Bookstore, "Книжный", "Небольшой, но лояльный поток покупателей.",
                30_000, 18f, 0.02f, 8f, 1.2f, 1.3f, shop, sellers, 10f, 4));
            Add(Retail(BusinessType.Jewelry, "Ювелирный салон", "Мало покупателей, огромный чек. Любимая цель вредителей.",
                90_000, 240f, 0.007f, 150f, 1.0f, 1.4f, shopOrKiosk, new[] { StaffRole.Consultant, StaffRole.Manager }, 3f, 4));
            Add(new BusinessTypeDef
            {
                Type = BusinessType.Supermarket, Name = "Супермаркет", Description = "Якорный арендатор: стабильный поток весь день, низкая маржа, нужен склад товара.",
                AllowedKinds = new[] { PropertyKind.Anchor }, SetupCost = 150_000, AvgTicket = 24f, CogsRatio = 0f,
                OpenHour = 8, CloseHour = 23, VisitShare = 0.12f,
                HourCurve = Curve(0,0,0,0,0,0,0,0,0.6f,0.8f,0.9f,1.0f,1.0f,1.0f,0.9f,0.9f,1.1f,1.5f,1.7f,1.5f,1.1f,0.8f,0.5f,0),
                MinStaff = 2, MaxStaff = 12, Roles = new[] { StaffRole.Cashier, StaffRole.Seller }, CustomersPerStaffHour = 16f,
                UsesStock = true, StockUnitCost = 16.5f, PriceElasticity = 2.4f, RainMultiplier = 1.0f, WeekendMultiplier = 1.25f,
            });
            Add(new BusinessTypeDef
            {
                Type = BusinessType.Warehouse, Name = "Склад", Description = "Снижает закупочные цены ваших магазинов на 30% и сдаёт место соседям по ТЦ.",
                AllowedKinds = new[] { PropertyKind.Storage }, SetupCost = 35_000, AvgTicket = 0f,
                OpenHour = 7, CloseHour = 22, VisitShare = 0f, HourCurve = new float[24],
                MinStaff = 1, MaxStaff = 4, Roles = new[] { StaffRole.Storekeeper, StaffRole.Manager },
                PriceElasticity = 1f, FootTraffic = false, StorageIncomePerDay = 900f,
            });
        }

        static void Add(BusinessTypeDef def) => Defs[def.Type] = def;

        public static BusinessTypeDef Get(BusinessType type) => Defs.TryGetValue(type, out var d) ? d : null;
        public static IEnumerable<BusinessTypeDef> All => Defs.Values;
        public static UpgradeDef Upgrade(UpgradeKind kind) => Upgrades[(int)kind];
        public static UpgradeDef[] AllUpgrades => Upgrades;

        public static string RoleName(StaffRole role)
        {
            switch (role)
            {
                case StaffRole.Barista: return "Бариста";
                case StaffRole.Cook: return "Повар";
                case StaffRole.Waiter: return "Официант";
                case StaffRole.Seller: return "Продавец";
                case StaffRole.Cashier: return "Кассир";
                case StaffRole.Manager: return "Менеджер";
                case StaffRole.Consultant: return "Консультант";
                case StaffRole.Storekeeper: return "Кладовщик";
                default: return role.ToString();
            }
        }

        public static string KindName(PropertyKind kind)
        {
            switch (kind)
            {
                case PropertyKind.ShopUnit: return "Торговое помещение";
                case PropertyKind.Kiosk: return "Островной киоск";
                case PropertyKind.FoodStall: return "Точка на фудкорте";
                case PropertyKind.Anchor: return "Якорное помещение";
                case PropertyKind.Storage: return "Складское помещение";
                default: return kind.ToString();
            }
        }

        public static string FloorName(int floor) => floor == 0 ? "1 этаж" : floor == 1 ? "2 этаж" : floor == 2 ? "3 этаж" : (floor + 1) + " этаж";

        public static string Money(long amount) => (amount < 0 ? "-" : "") + Currency + Math.Abs(amount).ToString("N0").Replace('\u00A0', ' ');
        public static string Money(float amount) => Money((long)Math.Round(amount));
    }
}

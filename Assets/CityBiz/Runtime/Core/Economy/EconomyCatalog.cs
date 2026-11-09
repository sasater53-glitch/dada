using System;
using System.Collections.Generic;

namespace CityBiz.Core
{
    public enum PropertyKind : byte { Apartment, CommercialUnit, Office, Warehouse, Parking }

    public enum BusinessType : byte { None, Cafe, Restaurant, ClothingStore, Supermarket, Office, Warehouse }

    public enum StaffRole : byte { Barista, Cook, Waiter, Seller, Cashier, Manager, Analyst, Storekeeper }

    /// <summary>Параметры типа бизнеса. Баланс задаётся здесь, а не разбросан по коду.</summary>
    public sealed class BusinessTypeDef
    {
        public BusinessType Type;
        public string Name;
        public string Description;
        public PropertyKind[] AllowedKinds;
        public long SetupCost;
        public float AvgTicket;
        public float CogsRatio;          // себестоимость как доля выручки (без склада)
        public int OpenHour, CloseHour;  // CloseHour может быть > 24 для ночных заведений
        public float BaseHourlyDemand;   // покупателей в час на весь район при трафике 1.0
        public float[] HourCurve;        // 24 множителя спроса по часам
        public int MinStaff, MaxStaff;
        public StaffRole[] Roles;
        public float CustomersPerStaffHour;
        public bool UsesStock;
        public float StockUnitCost;       // закупка единицы товара
        public float PriceElasticity;
        public float RainMultiplier;
        public float WeekendMultiplier;
        public bool FootTraffic = true;
        public float ContractIncomePerStaffHour; // офисы
        public float StorageIncomePerDay;        // склады

        public bool IsOpenAt(int hour)
        {
            if (CloseHour <= 24) return hour >= OpenHour && hour < CloseHour;
            return hour >= OpenHour || hour < CloseHour - 24;
        }

        public bool Allows(PropertyKind kind) => Array.IndexOf(AllowedKinds, kind) >= 0;
    }

    public static class EconomyCatalog
    {
        public const long StartingCash = 300_000;
        public const float TaxRate = 0.06f;
        public const float PropertyTaxPerDay = 0.0004f;
        public const float SellBackRatio = 0.8f;
        public const int EvictionDebtDays = 3;
        public const long SecurityUpgradeCost = 6_000;
        public const long SecurityDailyCostPerLevel = 180;
        public const long InteriorUpgradeCost = 15_000;
        public const float RepairCostPerPoint = 9_000; // за полный ремонт с 0 до 1
        public const string Currency = "$";

        static readonly Dictionary<BusinessType, BusinessTypeDef> Defs = new Dictionary<BusinessType, BusinessTypeDef>();

        static float[] Curve(params float[] v) => v;

        static EconomyCatalog()
        {
            var commercial = new[] { PropertyKind.CommercialUnit };
            Add(new BusinessTypeDef
            {
                Type = BusinessType.Cafe, Name = "Кафе", Description = "Кофе и выпечка. Пик утром и в обед, любит дождливую погоду.",
                AllowedKinds = commercial, SetupCost = 35_000, AvgTicket = 11f, CogsRatio = 0.3f,
                OpenHour = 7, CloseHour = 22, BaseHourlyDemand = 52f,
                HourCurve = Curve(0,0,0,0,0,0,0.3f,1.4f,1.8f,1.2f,0.9f,1.0f,1.3f,1.1f,0.9f,0.9f,1.0f,1.1f,0.9f,0.7f,0.5f,0.3f,0,0),
                MinStaff = 1, MaxStaff = 5, Roles = new[] { StaffRole.Barista, StaffRole.Cashier }, CustomersPerStaffHour = 14f,
                PriceElasticity = 1.5f, RainMultiplier = 1.25f, WeekendMultiplier = 1.15f,
            });
            Add(new BusinessTypeDef
            {
                Type = BusinessType.Restaurant, Name = "Ресторан", Description = "Высокий средний чек, пик в обед и вечером.",
                AllowedKinds = commercial, SetupCost = 80_000, AvgTicket = 38f, CogsRatio = 0.35f,
                OpenHour = 11, CloseHour = 24, BaseHourlyDemand = 17f,
                HourCurve = Curve(0,0,0,0,0,0,0,0,0,0,0,0.6f,1.4f,1.3f,0.7f,0.5f,0.6f,0.9f,1.5f,1.8f,1.6f,1.1f,0.6f,0.3f),
                MinStaff = 2, MaxStaff = 7, Roles = new[] { StaffRole.Cook, StaffRole.Waiter }, CustomersPerStaffHour = 6f,
                PriceElasticity = 1.2f, RainMultiplier = 0.9f, WeekendMultiplier = 1.35f,
            });
            Add(new BusinessTypeDef
            {
                Type = BusinessType.ClothingStore, Name = "Магазин одежды", Description = "Товар закупается заранее. Выходные — главный день.",
                AllowedKinds = commercial, SetupCost = 60_000, AvgTicket = 52f, CogsRatio = 0f,
                OpenHour = 10, CloseHour = 21, BaseHourlyDemand = 16f,
                HourCurve = Curve(0,0,0,0,0,0,0,0,0,0,0.6f,0.8f,1.0f,1.1f,1.1f,1.1f,1.2f,1.4f,1.4f,1.1f,0.7f,0,0,0),
                MinStaff = 1, MaxStaff = 5, Roles = new[] { StaffRole.Seller, StaffRole.Cashier }, CustomersPerStaffHour = 7f,
                UsesStock = true, StockUnitCost = 21f, PriceElasticity = 1.4f, RainMultiplier = 0.7f, WeekendMultiplier = 1.6f,
            });
            Add(new BusinessTypeDef
            {
                Type = BusinessType.Supermarket, Name = "Супермаркет", Description = "Стабильный поток весь день, низкая маржа, нужен склад товара.",
                AllowedKinds = commercial, SetupCost = 120_000, AvgTicket = 24f, CogsRatio = 0f,
                OpenHour = 7, CloseHour = 23, BaseHourlyDemand = 52f,
                HourCurve = Curve(0,0,0,0,0,0,0,0.6f,0.9f,0.8f,0.8f,0.9f,1.0f,1.0f,0.9f,0.9f,1.1f,1.5f,1.7f,1.5f,1.1f,0.8f,0.5f,0),
                MinStaff = 2, MaxStaff = 9, Roles = new[] { StaffRole.Cashier, StaffRole.Seller }, CustomersPerStaffHour = 16f,
                UsesStock = true, StockUnitCost = 16.5f, PriceElasticity = 2.6f, RainMultiplier = 0.95f, WeekendMultiplier = 1.25f,
            });
            Add(new BusinessTypeDef
            {
                Type = BusinessType.Office, Name = "Офис (консалтинг)", Description = "Доход от контрактов зависит от числа и навыков сотрудников. Работает по будням.",
                AllowedKinds = new[] { PropertyKind.Office }, SetupCost = 45_000, AvgTicket = 0f,
                OpenHour = 9, CloseHour = 18, BaseHourlyDemand = 0f, HourCurve = new float[24],
                MinStaff = 1, MaxStaff = 10, Roles = new[] { StaffRole.Analyst, StaffRole.Manager },
                PriceElasticity = 0.8f, RainMultiplier = 1f, WeekendMultiplier = 0f, FootTraffic = false,
                ContractIncomePerStaffHour = 55f,
            });
            Add(new BusinessTypeDef
            {
                Type = BusinessType.Warehouse, Name = "Склад и логистика", Description = "Снижает закупочные цены ваших магазинов на 30% и сдаёт место в аренду.",
                AllowedKinds = new[] { PropertyKind.Warehouse }, SetupCost = 50_000, AvgTicket = 0f,
                OpenHour = 6, CloseHour = 22, BaseHourlyDemand = 0f, HourCurve = new float[24],
                MinStaff = 1, MaxStaff = 6, Roles = new[] { StaffRole.Storekeeper, StaffRole.Manager },
                PriceElasticity = 1f, RainMultiplier = 1f, WeekendMultiplier = 1f, FootTraffic = false,
                StorageIncomePerDay = 1_400f,
            });
        }

        static void Add(BusinessTypeDef def) => Defs[def.Type] = def;

        public static BusinessTypeDef Get(BusinessType type) => Defs.TryGetValue(type, out var d) ? d : null;

        public static IEnumerable<BusinessTypeDef> All => Defs.Values;

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
                case StaffRole.Analyst: return "Аналитик";
                case StaffRole.Storekeeper: return "Кладовщик";
                default: return role.ToString();
            }
        }

        public static string KindName(PropertyKind kind)
        {
            switch (kind)
            {
                case PropertyKind.Apartment: return "Квартира";
                case PropertyKind.CommercialUnit: return "Торговое помещение";
                case PropertyKind.Office: return "Офис";
                case PropertyKind.Warehouse: return "Склад";
                case PropertyKind.Parking: return "Парковка";
                default: return kind.ToString();
            }
        }

        public static string Money(long amount) => (amount < 0 ? "-" : "") + Currency + Math.Abs(amount).ToString("N0").Replace('\u00A0', ' ');
        public static string Money(float amount) => Money((long)Math.Round(amount));
    }
}

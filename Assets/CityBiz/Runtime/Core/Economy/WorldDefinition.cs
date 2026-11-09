using System.Collections.Generic;

namespace CityBiz.Core
{
    public enum Frontage : byte { North, South, West, East, Island }

    /// <summary>
    /// Статическое описание помещения. Одинаково на сервере и клиентах, по сети не передаётся.
    /// Координаты плана (м): X — вдоль галереи, Z — поперёк; Floor — этаж (0 — первый).
    /// </summary>
    public sealed class PropertyDef
    {
        public int Id;
        public string MallId;
        public string LotKey;      // связь с визуальным помещением в Unity
        public string Name;
        public string Address;
        public PropertyKind Kind;
        public int Floor;
        public Frontage Side;      // куда «смотрит» витрина: North — помещение на северной стороне галереи
        public float PlanX, PlanZ; // центр линии витрины
        public float Width, Depth;
        public float Area => Width * Depth;
        public long BasePrice;
        public long BaseRent;      // в день
        public float FootTraffic;  // проходимость места (у эскалаторов, входа, фудкорта выше)
    }

    /// <summary>Начальное состояние помещения при создании мира.</summary>
    public sealed class InitialTenancy
    {
        public int PropertyId;
        public int OwnerPlayerId;   // 0 — управляющая компания ТЦ
        public int TenantPlayerId;
        public BusinessType Business;
        public string BusinessName;
        public float PriceLevel = 1f;
        public int Staff;
        public int Interior = 1, Marketing, EquipmentLevel = 1, Assortment = 1, Security;
        public bool ForSale;
    }

    /// <summary>
    /// Торговый центр (или его отдельное крыло). Новый ТЦ/крыло = новый MallDef с собственными помещениями
    /// и множителями; симуляция работает со всеми зарегистрированными одинаково.
    /// </summary>
    public sealed class MallDef
    {
        public string Id;
        public string Name;
        public int Floors = 3;
        public float BaseVisitorsPerHour = 900f;
        public float RentMultiplier = 1f;
        public float Wealth = 1f;
        public readonly List<PropertyDef> Properties = new List<PropertyDef>();
        public readonly List<InitialTenancy> Initial = new List<InitialTenancy>();
    }

    public sealed class RivalProfile
    {
        public int PlayerId;
        public string Name;
        public long StartingCash;
        public float Aggression;
        public float Expansion;
        public float PriceBias;
        public float UpgradeDrive;
        public BusinessType[] Preferred;
        public uint Color;
    }

    /// <summary>Реестр всех торговых центров мира. Добавление ТЦ не требует правок в симуляции.</summary>
    public sealed class WorldDefinition
    {
        public readonly List<MallDef> Malls = new List<MallDef>();
        public readonly List<RivalProfile> Rivals = new List<RivalProfile>();
        readonly Dictionary<int, PropertyDef> _properties = new Dictionary<int, PropertyDef>();
        readonly Dictionary<string, MallDef> _malls = new Dictionary<string, MallDef>();

        public void AddMall(MallDef mall)
        {
            Malls.Add(mall);
            _malls[mall.Id] = mall;
            foreach (var p in mall.Properties)
            {
                p.MallId = mall.Id;
                _properties.Add(p.Id, p);
            }
        }

        public PropertyDef GetProperty(int id) => _properties.TryGetValue(id, out var p) ? p : null;
        public MallDef GetMall(string id) => id != null && _malls.TryGetValue(id, out var m) ? m : null;
        public IEnumerable<PropertyDef> AllProperties => _properties.Values;

        public PropertyDef FindByLot(string mallId, string lotKey)
        {
            foreach (var p in _properties.Values)
                if (p.MallId == mallId && p.LotKey == lotKey) return p;
            return null;
        }
    }
}

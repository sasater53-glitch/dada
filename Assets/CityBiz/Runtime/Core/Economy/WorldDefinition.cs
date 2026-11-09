using System.Collections.Generic;

namespace CityBiz.Core
{
    /// <summary>Статическое описание объекта недвижимости. Одинаково на сервере и клиентах, по сети не передаётся.</summary>
    public sealed class PropertyDef
    {
        public int Id;
        public string DistrictId;
        public string LotKey;      // связь с визуальным участком в Unity-чертеже района
        public string Name;
        public string Address;
        public PropertyKind Kind;
        public float Area;
        public long BasePrice;
        public long BaseRent;      // в день
        public float FootTraffic;  // множитель проходимости точки (угол, остановка, парковка рядом)
        public int ParkingSpots;
    }

    /// <summary>Начальное состояние объекта при создании мира.</summary>
    public sealed class InitialTenancy
    {
        public int PropertyId;
        public int OwnerPlayerId;   // 0 — городской фонд
        public int TenantPlayerId;  // 0 — нет арендатора
        public BusinessType Business;
        public string BusinessName;
        public float PriceLevel = 1f;
        public int Staff;
        public bool ForSale;
        public bool ApartmentOccupied;
    }

    /// <summary>
    /// Экономический профиль района. Новый район = новый DistrictDef с собственным набором объектов
    /// и множителями; симуляция работает со всеми зарегистрированными районами одинаково.
    /// </summary>
    public sealed class DistrictDef
    {
        public string Id;
        public string Name;
        public float RentMultiplier = 1f;
        public float TrafficMultiplier = 1f;
        public float Wealth = 1f; // влияет на средний чек и чувствительность к цене
        public readonly List<PropertyDef> Properties = new List<PropertyDef>();
        public readonly List<InitialTenancy> Initial = new List<InitialTenancy>();
    }

    /// <summary>Описание ИИ-конкурента.</summary>
    public sealed class RivalProfile
    {
        public int PlayerId;
        public string Name;
        public long StartingCash;
        public float Aggression;   // склонность к вредительству
        public float Expansion;    // склонность открывать новые точки
        public float PriceBias;    // <1 демпинг, >1 премиум
        public uint Color;
    }

    /// <summary>Реестр всех районов мира. Добавление района не требует правок в симуляции.</summary>
    public sealed class WorldDefinition
    {
        public readonly List<DistrictDef> Districts = new List<DistrictDef>();
        public readonly List<RivalProfile> Rivals = new List<RivalProfile>();
        readonly Dictionary<int, PropertyDef> _properties = new Dictionary<int, PropertyDef>();
        readonly Dictionary<string, DistrictDef> _districts = new Dictionary<string, DistrictDef>();

        public void AddDistrict(DistrictDef district)
        {
            Districts.Add(district);
            _districts[district.Id] = district;
            foreach (var p in district.Properties)
            {
                p.DistrictId = district.Id;
                _properties.Add(p.Id, p);
            }
        }

        public PropertyDef GetProperty(int id) => _properties.TryGetValue(id, out var p) ? p : null;
        public DistrictDef GetDistrict(string id) => id != null && _districts.TryGetValue(id, out var d) ? d : null;
        public IEnumerable<PropertyDef> AllProperties => _properties.Values;

        public PropertyDef FindByLot(string districtId, string lotKey)
        {
            foreach (var p in _properties.Values)
                if (p.DistrictId == districtId && p.LotKey == lotKey) return p;
            return null;
        }
    }
}

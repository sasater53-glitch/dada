using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBiz.Game
{
    public enum FacadeStyle { Brick, ClassicPlaster, ModernGlass, ConcretePanel, Industrial, Townhouse, Pavilion }
    public enum RoofStyle { FlatParapet, Pitched, FlatWithBillboard }
    public enum GroundUnitKind { Shop, ResidentialEntrance, OfficeLobby, Blank, LoadingDock }

    [Serializable]
    public sealed class GroundUnit
    {
        public GroundUnitKind Kind;
        public float Start;        // от левого края фасада, м
        public float Width;
        public string LotKey;      // связь с PropertyDef (для Shop / Lobby / Dock)
        public string[] ApartmentLots = Array.Empty<string>(); // квартиры, доступные через домофон
        public string[] OfficeLots = Array.Empty<string>();    // офисы, доступные через ресепшен
        public bool Terrace;       // летняя веранда перед входом
        public float InteriorDepth = 10f;
        public string DecorName;   // декоративная лавка без экономики (фон района)
        public Color AwningColor = new Color(0.55f, 0.12f, 0.12f);
    }

    /// <summary>Здание в локальных координатах: фасад вдоль +X от 0 до Width, глубина вдоль +Z, улица со стороны -Z.</summary>
    public sealed class BuildingSpec
    {
        public string Key;
        public Vector3 Origin;     // мировая позиция левого переднего угла
        public float Yaw;          // 0 — фасад смотрит на юг (-Z), 180 — на север
        public float Width, Depth;
        public int Floors = 4;
        public float GroundFloorHeight = 4.4f;
        public float FloorHeight = 3.2f;
        public FacadeStyle Style;
        public RoofStyle Roof = RoofStyle.FlatParapet;
        public Color WallColor = new Color(0.7f, 0.4f, 0.3f);
        public Color TrimColor = new Color(0.92f, 0.9f, 0.86f);
        public Color AccentColor = new Color(0.2f, 0.3f, 0.35f);
        public bool Balconies;
        public int WindowsPerFloor = 0; // 0 — авто
        public string HouseNumber;
        public string RoofSign;    // крупная надпись на крыше / козырьке
        public int Seed;
        public bool Backdrop;      // фоновое здание: упрощённая детализация, без интерьеров
        public readonly List<GroundUnit> Units = new List<GroundUnit>();

        public float Height => GroundFloorHeight + (Floors - 1) * FloorHeight;
        public Matrix4x4 LocalToWorld => Matrix4x4.TRS(Origin, Quaternion.Euler(0, Yaw, 0), Vector3.one);
    }

    public sealed class RoadSpec
    {
        public string Name;
        public Vector3 Start, End;       // ось дороги
        public float CarriageWidth = 12f;
        public float SidewalkWidth = 6f;
        public bool ParkingLanes;
        public bool CenterLineSolid;
        public readonly List<Vector2> CarriageGaps = new List<Vector2>(); // участки оси без асфальта (уже покрыты другой дорогой)
        public float TrimStart, TrimEnd; // где тротуар прерывается перекрёстком (расстояния от концов)
        public bool SidewalkLeft = true, SidewalkRight = true;
        public readonly List<Vector2> SidewalkGaps = new List<Vector2>(); // участки вдоль оси без бордюра (въезды)

        public Vector3 Direction => (End - Start).normalized;
        public Vector3 Right => Vector3.Cross(Vector3.up, Direction).normalized;
        public float Length => Vector3.Distance(Start, End);
    }

    public sealed class IntersectionSpec
    {
        public Vector3 Center;
        public Vector2 Size;            // по X и Z (проезжая часть)
        public float CrosswalkOffset = 2.5f;
        public float CrosswalkWidth = 4f;
    }

    public sealed class ParkingLotSpec
    {
        public string LotKey;
        public Rect Area;              // XZ
        public bool BaysAlongX = true; // направление рядов
        public float Occupancy = 0.6f;
        public string Sign;
    }

    public sealed class ParkSpec
    {
        public Rect Area;
        public string Name;
        public bool Fountain = true;
        public bool Playground;
    }

    public enum PropKind
    {
        StreetLamp, Tree, Bench, TrashBin, Bollard, Planter, Hydrant, BikeRack, Kiosk, ParkingMeter,
        Hedge, FlowerBed, RoadSign, Billboard, Mailbox, PhoneBooth
    }

    public struct PropSpec
    {
        public PropKind Kind;
        public Vector3 Position;
        public float Yaw;
        public int Variant;
        public Vector3 Scale;
        public string Text;

        public PropSpec(PropKind kind, Vector3 pos, float yaw = 0f, int variant = 0, string text = null)
        {
            Kind = kind; Position = pos; Yaw = yaw; Variant = variant; Scale = Vector3.one; Text = text;
        }
    }

    public sealed class BusStopSpec
    {
        public string Name;
        public Vector3 Position;  // центр павильона на тротуаре
        public float Yaw;         // павильон смотрит на дорогу
        public int Lane;          // индекс полосы, где останавливается автобус
        public float LaneDistance;
    }

    public sealed class TrafficLightSpec
    {
        public Vector3 Position;
        public float Yaw;          // направление «лица» светофора (навстречу машинам)
        public int VehicleGroup;   // 0 — главная улица, 1 — поперечная
    }

    [Serializable]
    public sealed class LaneStop
    {
        public float Distance;      // дистанция вдоль полосы до стоп-линии
        public int SignalGroup;
    }

    [Serializable]
    public sealed class TrafficLane
    {
        public List<Vector3> Points = new List<Vector3>();
        public float SpeedLimit = 11f; // м/с ≈ 40 км/ч
        public List<LaneStop> Stops = new List<LaneStop>();
        public int NextLane = -1;      // куда «перетекает» машина в конце полосы (разворот за пределами района)
        public bool BusRoute;

        [NonSerialized] float[] _cumulative;

        public float Length { get { EnsureCache(); return _cumulative[_cumulative.Length - 1]; } }

        void EnsureCache()
        {
            if (_cumulative != null && _cumulative.Length == Points.Count) return;
            _cumulative = new float[Points.Count];
            for (int i = 1; i < Points.Count; i++) _cumulative[i] = _cumulative[i - 1] + Vector3.Distance(Points[i - 1], Points[i]);
        }

        public Vector3 Evaluate(float distance, out Vector3 forward)
        {
            EnsureCache();
            distance = Mathf.Clamp(distance, 0f, _cumulative[_cumulative.Length - 1]);
            int i = 1;
            while (i < Points.Count - 1 && _cumulative[i] < distance) i++;
            float segLen = _cumulative[i] - _cumulative[i - 1];
            float t = segLen > 0.001f ? (distance - _cumulative[i - 1]) / segLen : 0f;
            forward = (Points[i] - Points[i - 1]).normalized;
            return Vector3.Lerp(Points[i - 1], Points[i], t);
        }
    }

    /// <summary>Полное описание района, из которого DistrictBuilder строит сцену.</summary>
    public sealed class DistrictLayout
    {
        public string DistrictId;
        public string DisplayName;
        public Rect Bounds;               // игровая зона
        public Rect GroundBounds;         // с запасом под фон
        public Vector3 PlayerSpawn;
        public float PlayerSpawnYaw;

        public readonly List<RoadSpec> Roads = new List<RoadSpec>();
        public readonly List<IntersectionSpec> Intersections = new List<IntersectionSpec>();
        public readonly List<BuildingSpec> Buildings = new List<BuildingSpec>();
        public readonly List<BuildingSpec> BackdropBuildings = new List<BuildingSpec>();
        public readonly List<ParkingLotSpec> ParkingLots = new List<ParkingLotSpec>();
        public readonly List<ParkSpec> Parks = new List<ParkSpec>();
        public readonly List<PropSpec> Props = new List<PropSpec>();
        public readonly List<BusStopSpec> BusStops = new List<BusStopSpec>();
        public readonly List<TrafficLightSpec> TrafficLights = new List<TrafficLightSpec>();
        public readonly List<TrafficLane> Lanes = new List<TrafficLane>();
        public readonly List<Rect> ParkingLaneSpots = new List<Rect>(); // места для припаркованных машин у тротуара
        public readonly List<Rect> PlainGround = new List<Rect>();      // дворы, газоны за домами
        public readonly PedestrianGraph Pedestrians = new PedestrianGraph();
    }
}

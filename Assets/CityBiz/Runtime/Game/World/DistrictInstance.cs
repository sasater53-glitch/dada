using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBiz.Game
{
    [Serializable]
    public sealed class MapShape
    {
        public Vector2 Min, Max;      // XZ
        public int Kind;              // 0 дорога, 1 тротуар, 2 здание, 3 сквер, 4 парковка, 5 фон
        public string Label;
        public string[] LotKeys = Array.Empty<string>();
    }

    [Serializable]
    public sealed class BusStopRef
    {
        public int Lane;
        public float Distance;
        public Vector3 Position;
        public string Name;
    }

    [Serializable]
    public sealed class MaterialEntry
    {
        public string Key;
        public Material Material;
    }

    /// <summary>
    /// Корень построенного района. Хранит всё, что нужно системам во время игры (граф пешеходов, полосы,
    /// фонари, остановки, данные карты) в сериализуемом виде — работает и для запечённой сцены.
    /// </summary>
    public sealed class DistrictInstance : MonoBehaviour
    {
        public static readonly List<DistrictInstance> All = new List<DistrictInstance>();

        public string DistrictId;
        public string DisplayName;
        public Rect Bounds;
        public Vector3 PlayerSpawn;
        public float PlayerSpawnYaw;
        public PedestrianGraph Pedestrians = new PedestrianGraph();
        public List<TrafficLane> Lanes = new List<TrafficLane>();
        public List<Vector3> LampHeads = new List<Vector3>();
        public List<BusStopRef> BusStops = new List<BusStopRef>();
        public List<TrafficLightView> TrafficLights = new List<TrafficLightView>();
        public List<MapShape> Map = new List<MapShape>();
        public List<MaterialEntry> Materials = new List<MaterialEntry>();
        public bool Baked;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>Для запечённой сцены: материалы переключения день/ночь должны быть именно теми, что в сцене.</summary>
        public void AdoptMaterials(MaterialLibrary lib)
        {
            foreach (var e in Materials) lib.Adopt(e.Key, e.Material);
        }

        public static DistrictInstance Find(string id)
        {
            foreach (var d in All) if (d.DistrictId == id) return d;
            return null;
        }
    }
}

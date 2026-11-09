using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;

namespace CityBiz.Game
{
    /// <summary>
    /// Чертёж торгового центра (или крыла). Новый ТЦ = новый MallDef в ядре + наследник MallBlueprint,
    /// зарегистрированный в MallRegistry. Существующий код при этом не меняется.
    /// </summary>
    public abstract class MallBlueprint
    {
        public abstract string MallId { get; }
        public abstract string DisplayName { get; }
        /// <summary>Смещение в мире — несколько ТЦ/крыльев не пересекаются.</summary>
        public virtual Vector3 WorldOffset => Vector3.zero;
        /// <summary>Экономическое описание мира — чертёж берёт из него размеры помещений.</summary>
        public virtual void SetWorld(WorldDefinition world) { }
        public abstract void Build(MallKit kit, MallInstance instance);
    }

    public static class MallRegistry
    {
        static readonly List<MallBlueprint> Blueprints = new List<MallBlueprint> { new GalleriaBlueprint() };

        public static IReadOnlyList<MallBlueprint> All => Blueprints;

        public static void Register(MallBlueprint blueprint)
        {
            if (Find(blueprint.MallId) == null) Blueprints.Add(blueprint);
        }

        public static MallBlueprint Find(string id)
        {
            foreach (var b in Blueprints) if (b.MallId == id) return b;
            return null;
        }

        /// <summary>Экономическое описание мира: все ТЦ, известные ядру.</summary>
        public static WorldDefinition CreateWorld() => MallContent.CreateWorld();
    }

    [System.Serializable]
    public sealed class MapShape
    {
        public int Floor;
        public Vector2 Min, Max;      // XZ
        public int Kind;              // 0 галерея, 1 помещение, 2 эскалатор, 3 лифт, 4 фудкорт, 5 атриум (пустота)
        public string Label;
        public string LotKey;
    }

    [System.Serializable]
    public sealed class FloorTeleport
    {
        public int Floor;
        public Vector3 Position;
        public float Yaw;
    }

    /// <summary>
    /// Корень построенного ТЦ. Хранит всё, что нужно системам во время игры (граф пешеходов, план этажей,
    /// точки света, лифт) в сериализуемом виде — работает и для запечённой сцены.
    /// </summary>
    public sealed class MallInstance : MonoBehaviour
    {
        public static readonly List<MallInstance> All = new List<MallInstance>();

        public string MallId;
        public string DisplayName;
        public int Floors = 3;
        public Rect Bounds;
        public Vector3 PlayerSpawn;
        public float PlayerSpawnYaw;
        public PedestrianGraph Pedestrians = new PedestrianGraph();
        public List<Vector3> LightAnchors = new List<Vector3>();
        public List<MapShape> Map = new List<MapShape>();
        public List<FloorTeleport> ElevatorStops = new List<FloorTeleport>();
        public List<Vector3> EscalatorSounds = new List<Vector3>();
        public Vector3 FoodCourtCenter;
        public List<MaterialEntry> Materials = new List<MaterialEntry>();
        public bool Baked;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public void AdoptMaterials(MaterialLibrary lib)
        {
            foreach (var e in Materials) lib.Adopt(e.Key, e.Material);
        }

        /// <summary>Этаж по высоте точки (в локальных координатах ТЦ).</summary>
        public static int FloorAt(float localY) => Mathf.Clamp(Mathf.FloorToInt((localY + 1.5f) / MallContent.FloorHeight), 0, 9);

        public int FloorOfWorld(Vector3 world) => Mathf.Min(FloorAt(transform.InverseTransformPoint(world).y), Mathf.Max(0, Floors - 1));

        /// <summary>Точка внутри плана ТЦ (по XZ, с запасом).</summary>
        public bool Contains(Vector3 world, float margin = 2f)
        {
            var p = transform.InverseTransformPoint(world);
            return p.x > Bounds.xMin - margin && p.x < Bounds.xMax + margin && p.z > Bounds.yMin - margin && p.z < Bounds.yMax + margin &&
                   p.y > -5f && p.y < Floors * MallContent.FloorHeight + 8f;
        }
    }

    [System.Serializable]
    public sealed class MaterialEntry
    {
        public string Key;
        public Material Material;
    }
}

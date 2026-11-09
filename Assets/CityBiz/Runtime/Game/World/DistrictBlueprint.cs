using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;

namespace CityBiz.Game
{
    /// <summary>
    /// Чертёж района. Новый район (деловой центр, промзона…) = новый наследник + DistrictDef в ядре,
    /// зарегистрированные в DistrictRegistry. Существующий код при этом не меняется.
    /// </summary>
    public abstract class DistrictBlueprint
    {
        public abstract string DistrictId { get; }
        public abstract string DisplayName { get; }
        /// <summary>Смещение района в мире — районы стыкуются по дорогам.</summary>
        public virtual Vector3 WorldOffset => Vector3.zero;
        public abstract DistrictLayout CreateLayout();
    }

    public static class DistrictRegistry
    {
        static readonly List<DistrictBlueprint> Blueprints = new List<DistrictBlueprint> { new StarterDistrictBlueprint() };

        public static IReadOnlyList<DistrictBlueprint> All => Blueprints;

        public static void Register(DistrictBlueprint blueprint)
        {
            if (Find(blueprint.DistrictId) == null) Blueprints.Add(blueprint);
        }

        public static DistrictBlueprint Find(string id)
        {
            foreach (var b in Blueprints) if (b.DistrictId == id) return b;
            return null;
        }

        /// <summary>Экономическое описание мира: все районы, известные ядру.</summary>
        public static WorldDefinition CreateWorld() => StarterDistrictContent.CreateWorld();
    }
}

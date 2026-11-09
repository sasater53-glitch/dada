using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;

namespace CityBiz.Game
{
    /// <summary>
    /// Строит ТЦ по чертежам из MallRegistry: корень с MallInstance, строительный набор MallKit, запуск Build.
    /// Запечённые в сцену ТЦ (MallInstance.Baked) не перестраиваются — у них только «усыновляются» материалы.
    /// </summary>
    public sealed class MallBuilder
    {
        readonly MaterialLibrary _lib;
        readonly WorldDefinition _world;

        public MallBuilder(MaterialLibrary lib, WorldDefinition world)
        {
            _lib = lib;
            _world = world;
        }

        public MallInstance Build(MallBlueprint blueprint, Transform parent)
        {
            var go = new GameObject("Mall_" + blueprint.MallId);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = blueprint.WorldOffset;
            var inst = go.AddComponent<MallInstance>();
            inst.MallId = blueprint.MallId;
            inst.DisplayName = blueprint.DisplayName;
            blueprint.SetWorld(_world);
            var kit = new MallKit(_lib, go.transform);
            blueprint.Build(kit, inst);
            if (inst.Floors <= 0) inst.Floors = 1;
            // Список материалов нужен запечённой сцене: по ключам их найдут системы (ночь, погода)
            inst.Materials.Clear();
            foreach (var kv in _lib.AllMaterials)
                if (kv.Value != null) inst.Materials.Add(new MaterialEntry { Key = kv.Key, Material = kv.Value });
            return inst;
        }

        /// <summary>Все ТЦ мира: запечённые берутся из сцены, недостающие строятся.</summary>
        public List<MallInstance> BuildAll(Transform parent, IEnumerable<MallInstance> baked)
        {
            var result = new List<MallInstance>();
            var bakedList = baked != null ? new List<MallInstance>(baked) : new List<MallInstance>();
            foreach (var blueprint in MallRegistry.All)
            {
                MallInstance existing = null;
                foreach (var b in bakedList)
                    if (b != null && b.MallId == blueprint.MallId) { existing = b; break; }
                if (existing != null)
                {
                    existing.AdoptMaterials(_lib);
                    result.Add(existing);
                    continue;
                }
                float started = Time.realtimeSinceStartup;
                var inst = Build(blueprint, parent);
                Debug.Log($"[CityBiz] «{blueprint.DisplayName}» построен за {(Time.realtimeSinceStartup - started) * 1000f:0} мс");
                result.Add(inst);
            }
            return result;
        }
    }
}

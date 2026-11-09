using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>
    /// Свет в окнах: три группы окон здания включаются в разное время вечером,
    /// офисы светятся в рабочие утренние/вечерние часы. Переключение — заменой материала подмеша.
    /// </summary>
    public sealed class BuildingLights : MonoBehaviour
    {
        public static readonly List<BuildingLights> All = new List<BuildingLights>();

        [SerializeField] MeshRenderer _renderer;
        [SerializeField] int[] _groups;
        [SerializeField] int _seed;
        [SerializeField] bool _office;
        int _state = -1;

        public void Init(MeshRenderer renderer, int[] groups, int seed, bool office)
        {
            _renderer = renderer; _groups = groups; _seed = seed; _office = office;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public void Apply(float hour, bool weekend)
        {
            if (_renderer == null || _groups == null) return;
            int state = 0;
            float jitter = (_seed % 7) * 0.12f;
            for (int g = 0; g < _groups.Length; g++)
            {
                bool lit;
                if (_office)
                    lit = !weekend && ((hour >= 6.5f + g * 0.4f && hour < 9f) || (hour >= 16.5f && hour < 19.5f + g * 0.8f + jitter));
                else
                    lit = (hour >= 17.5f + g * 0.9f + jitter && hour < 23.8f - g * 1.1f) || (g == 0 && hour >= 6f && hour < 7.8f);
                if (lit) state |= 1 << g;
            }
            if (state == _state) return;
            _state = state;
            var mats = _renderer.sharedMaterials;
            var lib = MaterialLibrary.Instance;
            for (int g = 0; g < _groups.Length; g++)
                if (_groups[g] >= 0 && _groups[g] < mats.Length)
                    mats[_groups[g]] = (state & (1 << g)) != 0 ? lib.WindowLit(g) : lib.WindowGlass;
            _renderer.sharedMaterials = mats;
        }
    }
}

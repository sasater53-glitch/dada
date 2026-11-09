using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    public sealed class CarVisual : MonoBehaviour
    {
        [SerializeField] Transform[] _wheels;
        [SerializeField] MeshRenderer _renderer;
        public CarFactory.Dimensions Size;
        float _spin;

        public void Init(Transform[] wheels, CarFactory.Dimensions size, MeshRenderer renderer)
        {
            _wheels = wheels; Size = size; _renderer = renderer;
        }

        public void Spin(float speed, float dt)
        {
            _spin += speed * dt / Mathf.Max(0.1f, Size.WheelRadius) * Mathf.Rad2Deg;
            var rot = Quaternion.Euler(_spin, 0, 0);
            foreach (var w in _wheels) w.localRotation = rot;
        }

        public void SetVisible(bool v)
        {
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = v;
        }
    }
}

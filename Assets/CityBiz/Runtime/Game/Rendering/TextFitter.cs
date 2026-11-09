using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>
    /// Ужимает TextMesh по ширине по фактическим границам рендерера (они доступны после генерации меша),
    /// поэтому не зависит от внутренних метрик шрифта.
    /// </summary>
    public sealed class TextFitter : MonoBehaviour
    {
        public float MaxWidth = 2f;
        [SerializeField] float _baseScale = -1f;
        int _passes;
        MeshRenderer _renderer;

        void OnEnable() => Refit();

        public void Refit()
        {
            if (_baseScale < 0f) _baseScale = transform.localScale.x;
            transform.localScale = Vector3.one * _baseScale;
            _passes = 3;
            enabled = true;
        }

        void LateUpdate()
        {
            if (_passes-- <= 0) { enabled = false; return; }
            if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
            var size = _renderer.bounds.size;
            var right = transform.right;
            float width = Mathf.Abs(right.x) * size.x + Mathf.Abs(right.z) * size.z;
            float parentScale = transform.parent != null ? transform.parent.lossyScale.x : 1f;
            float limit = MaxWidth * parentScale;
            if (width > limit * 1.01f && width > 0.0001f)
                transform.localScale *= limit / width;
        }
    }
}

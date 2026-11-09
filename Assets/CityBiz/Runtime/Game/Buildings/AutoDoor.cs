using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>Раздвижные двери магазинов: открываются, когда рядом человек и заведение работает.</summary>
    public sealed class AutoDoor : MonoBehaviour
    {
        [SerializeField] Transform _left, _right;
        [SerializeField] float _slide = 0.9f;
        [SerializeField] Vector3 _leftClosed, _rightClosed;
        public bool Locked;
        float _open, _checkTimer;
        bool _wantOpen;

        public void Configure(Transform left, Transform right, float slide)
        {
            _left = left; _right = right; _slide = slide;
            _leftClosed = left.localPosition; _rightClosed = right.localPosition;
        }

        void Update()
        {
            _checkTimer -= Time.deltaTime;
            if (_checkTimer <= 0f)
            {
                _checkTimer = 0.15f;
                bool want = !Locked && Agents.AnyWithin(transform.position, 2.8f);
                if (want && !_wantOpen && _open < 0.05f) MallAudio.Instance?.Chime(transform.position);
                _wantOpen = want;
            }
            float target = _wantOpen ? 1f : 0f;
            if (Mathf.Approximately(_open, target)) return;
            _open = Mathf.MoveTowards(_open, target, Time.deltaTime * 2.2f);
            float e = Mathf.SmoothStep(0f, 1f, _open);
            _left.localPosition = _leftClosed + Vector3.left * _slide * e;
            _right.localPosition = _rightClosed + Vector3.right * _slide * e;
        }
    }
}

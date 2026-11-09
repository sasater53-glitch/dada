using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CityBiz.Game
{
    public enum CharacterAnim { Idle, Walk, Sprint, Sit, Interact, PickUp, EmoteYes, HoldRight }

    /// <summary>
    /// Персонаж Kenney Mini Characters (CC0) с встроенными анимациями. Анимации проигрываются через Playables
    /// без Animator Controller: плавный переход между двумя клипами, зацикливание — вручную (не зависит от настроек импорта).
    /// </summary>
    public sealed class CharacterRig : MonoBehaviour
    {
        public static readonly string[] Female = { "character-female-a", "character-female-b", "character-female-c", "character-female-d", "character-female-e", "character-female-f" };
        public static readonly string[] Male = { "character-male-a", "character-male-b", "character-male-c", "character-male-d", "character-male-e", "character-male-f", "character-male-g" };
        public const string Worker = "character-worker";
        public const float Height = 1.72f;

        static readonly Dictionary<CharacterAnim, AnimationClip> Clips = new Dictionary<CharacterAnim, AnimationClip>();
        static bool _clipsLoaded;

        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        AnimationClipPlayable _a, _b;
        CharacterAnim _current = (CharacterAnim)(-1);
        float _blend = 1f, _timeA, _timeB, _speedScale = 1f;
        Renderer[] _renderers;
        bool _visible = true;
        GameObject _bag;

        public static void ResetCache()
        {
            Clips.Clear();
            _clipsLoaded = false;
        }

        static void LoadClips()
        {
            if (_clipsLoaded) return;
            _clipsLoaded = true;
            var all = Resources.LoadAll<AnimationClip>(AssetCatalog.Characters + Male[0]);
            foreach (var clip in all)
            {
                if (clip.name.StartsWith("__preview__")) continue;
                switch (clip.name)
                {
                    case "idle": Clips[CharacterAnim.Idle] = clip; break;
                    case "walk": Clips[CharacterAnim.Walk] = clip; break;
                    case "sprint": Clips[CharacterAnim.Sprint] = clip; break;
                    case "sit": Clips[CharacterAnim.Sit] = clip; break;
                    case "interact-right": Clips[CharacterAnim.Interact] = clip; break;
                    case "pick-up": Clips[CharacterAnim.PickUp] = clip; break;
                    case "emote-yes": Clips[CharacterAnim.EmoteYes] = clip; break;
                    case "holding-right": Clips[CharacterAnim.HoldRight] = clip; break;
                }
            }
            if (Clips.Count == 0) Debug.LogWarning("[CityBiz] Анимации персонажей не найдены — персонажи будут статичными.");
        }

        public static CharacterRig Create(Transform parent, string model, float heightScale = 1f)
        {
            LoadClips();
            var go = AssetCatalog.Spawn(AssetCatalog.Characters + model, parent, Vector3.zero, 0f, Height * heightScale);
            go.name = "Character_" + model;
            var rig = go.AddComponent<CharacterRig>();
            rig.Init();
            return rig;
        }

        public static string RandomModel(int seed, bool? female = null)
        {
            bool f = female ?? (seed & 1) == 0;
            var list = f ? Female : Male;
            return list[(int)((uint)seed / 2 % (uint)list.Length)];
        }

        void Init()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            var animator = GetComponentInChildren<Animator>();
            if (animator == null)
            {
                var model = transform.childCount > 0 ? transform.GetChild(0).gameObject : gameObject;
                animator = model.AddComponent<Animator>();
            }
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullCompletely;
            if (Clips.Count == 0) return;
            _graph = PlayableGraph.Create("CharacterRig");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(_graph, "Anim", animator);
            _mixer = AnimationMixerPlayable.Create(_graph, 2);
            output.SetSourcePlayable(_mixer);
            Play(CharacterAnim.Idle, 0f);
        }

        void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }

        public void Play(CharacterAnim anim, float fade = 0.2f, float speed = 1f)
        {
            _speedScale = speed;
            if (anim == _current || !_graph.IsValid()) return;
            if (!Clips.TryGetValue(anim, out var clip) && !Clips.TryGetValue(CharacterAnim.Idle, out clip)) return;
            _current = anim;
            if (_b.IsValid()) { _graph.Disconnect(_mixer, 1); _b.Destroy(); }
            if (_a.IsValid())
            {
                _graph.Disconnect(_mixer, 0);
                _b = _a;
                _timeB = _timeA;
                _graph.Connect(_b, 0, _mixer, 1);
            }
            _a = AnimationClipPlayable.Create(_graph, clip);
            _a.SetApplyFootIK(false);
            _timeA = 0f;
            _graph.Connect(_a, 0, _mixer, 0);
            _blend = fade <= 0f ? 1f : 0f;
            ApplyWeights();
        }

        public CharacterAnim Current => _current;

        void ApplyWeights()
        {
            _mixer.SetInputWeight(0, _blend);
            _mixer.SetInputWeight(1, _b.IsValid() ? 1f - _blend : 0f);
        }

        /// <summary>Продвигает анимацию. Вызывается системами NPC (позволяет обновлять дальних реже).</summary>
        public void Tick(float dt)
        {
            if (!_graph.IsValid() || !_visible) return;
            _blend = Mathf.MoveTowards(_blend, 1f, dt / 0.2f);
            ApplyWeights();
            Advance(_a, ref _timeA, dt * _speedScale, _current != CharacterAnim.PickUp && _current != CharacterAnim.Interact);
            if (_b.IsValid()) Advance(_b, ref _timeB, dt * _speedScale, true);
            _graph.Evaluate(0f);
        }

        static void Advance(AnimationClipPlayable p, ref float time, float dt, bool loop)
        {
            if (!p.IsValid()) return;
            var clip = p.GetAnimationClip();
            float len = clip != null ? Mathf.Max(0.01f, clip.length) : 1f;
            time += dt;
            time = loop ? Mathf.Repeat(time, len) : Mathf.Min(time, len);
            p.SetTime(time);
        }

        public bool OneShotFinished
        {
            get
            {
                if (!_a.IsValid()) return true;
                var clip = _a.GetAnimationClip();
                return clip == null || _timeA >= clip.length - 0.01f;
            }
        }

        public void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            foreach (var r in _renderers) if (r != null) r.enabled = visible;
        }

        public void SetShadows(bool on)
        {
            var mode = on ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var r in _renderers) if (r != null && r.shadowCastingMode != mode) r.shadowCastingMode = mode;
        }

        /// <summary>Пакет с покупками в руке.</summary>
        public void SetBag(bool on, Color color)
        {
            if (on && _bag == null)
            {
                var hand = FindDeep(transform, "arm-right") ?? transform;
                _bag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(_bag.GetComponent<Collider>());
                _bag.name = "Bag";
                _bag.transform.SetParent(hand, true);
                _bag.transform.position = transform.position + transform.right * 0.28f + Vector3.up * 0.45f;
                _bag.transform.rotation = transform.rotation;
                _bag.transform.localScale = Vector3.one;
                var s = _bag.transform.lossyScale;
                _bag.transform.localScale = new Vector3(0.28f / Mathf.Max(0.001f, s.x), 0.34f / Mathf.Max(0.001f, s.y), 0.1f / Mathf.Max(0.001f, s.z));
                _renderers = GetComponentsInChildren<Renderer>(true);
            }
            if (_bag != null)
            {
                _bag.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Instance.Paint("bag", color, 0.2f);
                _bag.SetActive(on);
            }
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }
    }

    /// <summary>Персонал за стойкой: стоит, иногда «работает» руками.</summary>
    public sealed class StaffAnimator : MonoBehaviour
    {
        public CharacterRig Rig;
        float _timer;

        void Start() => _timer = Random.Range(2f, 6f);

        void Update()
        {
            if (Rig == null) return;
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _timer = Random.Range(4f, 9f);
                Rig.Play(Random.value < 0.6f ? CharacterAnim.Interact : CharacterAnim.Idle, 0.25f);
            }
            if (Rig.Current == CharacterAnim.Interact && Rig.OneShotFinished) Rig.Play(CharacterAnim.Idle, 0.25f);
            Rig.Tick(Time.deltaTime);
        }
    }
}

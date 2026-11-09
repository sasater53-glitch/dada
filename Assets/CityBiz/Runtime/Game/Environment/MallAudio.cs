using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;

namespace CityBiz.Game
{
    public enum UiSound { Click, Open, Close, Confirm, Error, Select, Coins }

    /// <summary>
    /// Звуки из Resources/Audio (записи с Wikimedia Commons, Kenney, Kevin MacLeod — см. THIRD_PARTY_ASSETS.md).
    /// Кеширует клипы; отсутствующий файл даёт одно предупреждение и null — игра работает без звука.
    /// </summary>
    public static class AudioLibrary
    {
        public const string Root = "Audio/";
        static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();
        static readonly HashSet<string> Warned = new HashSet<string>();

        public static AudioClip Get(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (Cache.TryGetValue(path, out var clip)) return clip;
            clip = Resources.Load<AudioClip>(Root + path);
            if (clip == null && Warned.Add(path)) Debug.LogWarning("[CityBiz] Звук не найден в Resources: " + Root + path);
            Cache[path] = clip;
            return clip;
        }

        /// <summary>Шаги Kenney: surface = "concrete" или "carpet".</summary>
        public static AudioClip[] Footsteps(string surface)
        {
            var list = new List<AudioClip>(5);
            for (int i = 0; i < 5; i++)
            {
                var c = Get($"Footsteps/footstep_{surface}_{i:000}");
                if (c != null) list.Add(c);
            }
            return list.ToArray();
        }

        public static void Reset() => Cache.Clear();
    }

    /// <summary>
    /// Звуковая атмосфера ТЦ: гомон толпы (громче, когда покупателей много), тихий фон утром и ночью,
    /// фудкорт и гул эскалаторов в 3D, фоновая музыка «радио ТЦ» в часы работы, дождь по стеклянной крыше,
    /// объявления, колокольчики дверей, кассы, звуки интерфейса.
    /// </summary>
    public sealed class MallAudio : MonoBehaviour
    {
        public static MallAudio Instance { get; private set; }

        public float MusicVolume = 0.6f;
        public float MasterVolume
        {
            get => AudioListener.volume;
            set => AudioListener.volume = Mathf.Clamp01(value);
        }

        AudioSource _crowd, _quiet, _music, _rain, _ui, _announce;
        readonly List<AudioSource> _spatialLoops = new List<AudioSource>();
        readonly List<AudioSource> _escalators = new List<AudioSource>();
        AudioSource _food;
        readonly List<AudioSource> _pool = new List<AudioSource>();
        int _poolNext, _track = -1, _lastHour = -1;
        AudioClip[] _tracks;
        AudioClip _chime, _cash, _cloth, _doorOpen, _coins;
        AudioClip _uiClick, _uiOpen, _uiClose, _uiConfirm, _uiError, _uiSelect;
        float _announceTimer = 150f, _mallBaseY;

        public void Init(IList<MallInstance> malls)
        {
            Instance = this;
            _crowd = Loop2D("Crowd", AudioLibrary.Get("Ambience/mall_crowd"));
            _quiet = Loop2D("Quiet", AudioLibrary.Get("Ambience/mall_quiet"));
            _rain = Loop2D("RainOnRoof", AudioLibrary.Get("Ambience/rain"));
            _music = Source("Music", false);
            _music.loop = false;
            _ui = Source("UI", false);
            _announce = Source("Announcements", false);
            _tracks = new[] { AudioLibrary.Get("Music/casa_bossa_nova"), AudioLibrary.Get("Music/local_forecast_elevator") };

            _chime = AudioLibrary.Get("Sfx/announcement_chime");
            _cash = AudioLibrary.Get("Sfx/cash_register");
            _cloth = AudioLibrary.Get("Sfx/cloth1");
            _doorOpen = AudioLibrary.Get("Sfx/doorOpen_1");
            _coins = AudioLibrary.Get("Sfx/handleCoins");
            _uiClick = AudioLibrary.Get("UI/click_001");
            _uiOpen = AudioLibrary.Get("UI/open_001");
            _uiClose = AudioLibrary.Get("UI/close_001");
            _uiConfirm = AudioLibrary.Get("UI/confirmation_001");
            _uiError = AudioLibrary.Get("UI/error_001");
            _uiSelect = AudioLibrary.Get("UI/select_001");

            var escalatorClip = AudioLibrary.Get("Ambience/escalator");
            var foodClip = AudioLibrary.Get("Ambience/foodcourt");
            if (malls != null)
                foreach (var mall in malls)
                {
                    if (mall == null) continue;
                    _mallBaseY = mall.transform.position.y;
                    foreach (var p in mall.EscalatorSounds)
                    {
                        var s = Loop3D("EscalatorHum", escalatorClip, mall.transform.TransformPoint(p), 2f, 16f);
                        if (s != null) _escalators.Add(s);
                    }
                    if (_food == null) _food = Loop3D("FoodCourt", foodClip, mall.transform.TransformPoint(mall.FoodCourtCenter + Vector3.up * 1.5f), 6f, 38f);
                }

            for (int i = 0; i < 10; i++)
            {
                var s = Source("Sfx" + i, true);
                s.minDistance = 1.5f;
                s.maxDistance = 22f;
                _pool.Add(s);
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        AudioSource Source(string name, bool spatial)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = spatial ? 1f : 0f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.dopplerLevel = 0f;
            return s;
        }

        AudioSource Loop2D(string name, AudioClip clip)
        {
            var s = Source(name, false);
            s.loop = true;
            s.volume = 0f;
            s.clip = clip;
            if (clip != null) s.Play();
            return s;
        }

        AudioSource Loop3D(string name, AudioClip clip, Vector3 position, float minD, float maxD)
        {
            if (clip == null) return null;
            var s = Source(name, true);
            s.transform.position = position;
            s.loop = true;
            s.clip = clip;
            s.minDistance = minD;
            s.maxDistance = maxD;
            s.volume = 0f;
            s.time = Random.Range(0f, clip.length * 0.9f);
            s.Play();
            _spatialLoops.Add(s);
            return s;
        }

        static void Fade(AudioSource s, float target, float dt, float speed = 0.5f)
        {
            if (s == null) return;
            s.volume = Mathf.MoveTowards(s.volume, target, dt * speed);
        }

        void Update()
        {
            var game = CityGame.Instance;
            if (game == null) return;
            float dt = Time.deltaTime;
            var cam = game.ViewPosition;
            bool ready = game.Ready;
            bool open = !ready || DayNightCycle.MallOpen;
            int npc = PedestrianSystem.Instance != null ? PedestrianSystem.Instance.ActiveCount : 0;
            float busy = ready ? Mathf.Clamp01(npc / 45f) : 0.6f;

            Fade(_crowd, open ? Mathf.Lerp(0.06f, 0.4f, busy) : 0f, dt);
            Fade(_quiet, open ? Mathf.Lerp(0.25f, 0.06f, busy) : 0.16f, dt);
            float height = Mathf.Clamp01((cam.y - _mallBaseY) / 14f);
            Fade(_rain, WeatherController.Rain * Mathf.Lerp(0.12f, 0.55f, height), dt, 0.2f);
            Fade(_food, ready && open ? 0.7f : 0f, dt);
            foreach (var e in _escalators) Fade(e, open ? 0.5f : 0f, dt);

            // Фоновая музыка ТЦ — две композиции по очереди, только в часы работы
            float musicTarget = ready && open ? 0.13f * MusicVolume : 0f;
            if (_music != null)
            {
                Fade(_music, musicTarget, dt, 0.1f);
                if (musicTarget > 0.001f && !_music.isPlaying && _tracks != null && _tracks.Length > 0)
                {
                    for (int i = 0; i < _tracks.Length; i++)
                    {
                        _track = (_track + 1) % _tracks.Length;
                        if (_tracks[_track] != null) { _music.clip = _tracks[_track]; _music.Play(); break; }
                    }
                }
            }

            if (!ready) return;
            // Объявления: открытие, за час до закрытия и изредка в течение дня
            int hour = game.State.Time.Hour;
            if (hour != _lastHour)
            {
                if (_lastHour >= 0 && (hour == EconomyCatalog.MallOpenHour || hour == EconomyCatalog.MallCloseHour - 1 || hour == EconomyCatalog.MallCloseHour)) Announce();
                _lastHour = hour;
            }
            if (open)
            {
                _announceTimer -= dt;
                if (_announceTimer <= 0f) { _announceTimer = Random.Range(180f, 360f); Announce(); }
            }
        }

        public void Announce()
        {
            if (_announce != null && _chime != null) _announce.PlayOneShot(_chime, 0.35f);
        }

        public void PlayAt(AudioClip clip, Vector3 position, float volume)
        {
            if (clip == null || _pool.Count == 0) return;
            var game = CityGame.Instance;
            if (game != null && (game.ViewPosition - position).sqrMagnitude > 26f * 26f) return;
            var s = _pool[_poolNext];
            _poolNext = (_poolNext + 1) % _pool.Count;
            s.transform.position = position;
            s.pitch = Random.Range(0.94f, 1.06f);
            s.PlayOneShot(clip, volume);
        }

        /// <summary>Автоматическая дверь открылась.</summary>
        public void Chime(Vector3 position) => PlayAt(_doorOpen, position, 0.3f);
        public void CashRegister(Vector3 position) => PlayAt(_cash, position, 0.45f);
        public void Rustle(Vector3 position) => PlayAt(_cloth, position, 0.35f);

        public void Ui(UiSound sound)
        {
            if (_ui == null) return;
            AudioClip clip;
            switch (sound)
            {
                case UiSound.Open: clip = _uiOpen; break;
                case UiSound.Close: clip = _uiClose; break;
                case UiSound.Confirm: clip = _uiConfirm; break;
                case UiSound.Error: clip = _uiError; break;
                case UiSound.Select: clip = _uiSelect; break;
                case UiSound.Coins: clip = _coins; break;
                default: clip = _uiClick; break;
            }
            if (clip != null) _ui.PlayOneShot(clip, 0.5f);
        }
    }
}

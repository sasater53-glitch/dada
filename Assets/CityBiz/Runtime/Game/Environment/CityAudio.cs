using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBiz.Game
{
    /// <summary>
    /// Звуки улицы без внешних ассетов — синтезируются при старте: гул города, дождь, гомон прохожих,
    /// птицы днём, сверчки ночью, двигатели ближайших машин (3D), гром, колокольчик дверей магазинов.
    /// </summary>
    public sealed class CityAudio : MonoBehaviour
    {
        public static CityAudio Instance { get; private set; }
        const int Rate = 22050;

        AudioSource _hum, _rain, _crowd, _night, _birds;
        readonly List<AudioSource> _engines = new List<AudioSource>();
        readonly List<Transform> _engineTargets = new List<Transform>();
        readonly List<Vector3> _lastPos = new List<Vector3>();
        AudioClip _birdClip, _thunderClip, _chimeClip;
        float _birdTimer = 3f, _engineTimer;
        System.Random _rnd = new System.Random(7);
        TrafficSystem _traffic;
        public float MasterVolume = 1f;

        public void Init(TrafficSystem traffic)
        {
            Instance = this;
            _traffic = traffic;
            var rnd = new System.Random(1);
            float white() => (float)(rnd.NextDouble() * 2.0 - 1.0);

            // Гул города: коричневый шум + низкочастотная пульсация
            float brown = 0f, lp = 0f;
            var hum = Make("CityHum", 6f, i =>
            {
                brown = (brown + 0.02f * white()) / 1.02f;
                lp += 0.05f * (brown - lp);
                return lp * 6f * (0.85f + 0.15f * Mathf.Sin(i * 0.0007f));
            });
            // Дождь: высокочастотный шум с каплями
            float hp = 0f, prev = 0f;
            var rain = Make("Rain", 4f, i =>
            {
                float w = white();
                hp = 0.9f * (hp + w - prev);
                prev = w;
                float drop = rnd.NextDouble() < 0.0015 ? white() * 2f : 0f;
                return hp * 0.35f + drop * 0.3f;
            });
            // Гомон: полосовой шум, промодулированный «слогами»
            float b1 = 0f, b2 = 0f, env = 0f;
            var crowd = Make("Crowd", 5f, i =>
            {
                float w = white();
                b1 += 0.08f * (w - b1);
                b2 += 0.02f * (b1 - b2);
                float band = b1 - b2;
                if (i % 2200 == 0) env = (float)rnd.NextDouble();
                float syll = 0.5f + 0.5f * Mathf.Sin(i * 2f * Mathf.PI * 4.3f / Rate) * env;
                return band * 3f * syll;
            });
            // Сверчки ночью
            var night = Make("Crickets", 3f, i =>
            {
                float t = i / (float)Rate;
                float pulse = Mathf.Sin(t * 2f * Mathf.PI * 30f) > 0.3f && (t * 2.2f % 1f) < 0.35f ? 1f : 0f;
                return Mathf.Sin(t * 2f * Mathf.PI * 4400f) * pulse * 0.12f;
            });
            _birdClip = MakeOneShot("Bird", 0.7f, i =>
            {
                float t = i / (float)Rate;
                float chirp = 0f;
                for (int k = 0; k < 3; k++)
                {
                    float s = t - k * 0.2f;
                    if (s < 0f || s > 0.12f) continue;
                    float f = 3200f + 2400f * (s / 0.12f) + k * 300f;
                    chirp += Mathf.Sin(2f * Mathf.PI * f * s) * Mathf.Sin(Mathf.PI * s / 0.12f);
                }
                return chirp * 0.4f;
            });
            float tb = 0f, tl = 0f;
            _thunderClip = MakeOneShot("Thunder", 3.5f, i =>
            {
                float t = i / (float)Rate;
                tb = (tb + 0.02f * white()) / 1.02f;
                tl += 0.03f * (tb - tl);
                float envelope = Mathf.Min(1f, t * 20f) * Mathf.Exp(-t * 1.2f) * (1f + 0.5f * Mathf.Sin(t * 9f));
                return tl * 14f * envelope;
            });
            _chimeClip = MakeOneShot("Chime", 1.2f, i =>
            {
                float t = i / (float)Rate;
                float a = Mathf.Sin(2f * Mathf.PI * 1318.5f * t) * Mathf.Exp(-t * 5f);
                float b = t > 0.18f ? Mathf.Sin(2f * Mathf.PI * 1046.5f * (t - 0.18f)) * Mathf.Exp(-(t - 0.18f) * 4f) : 0f;
                return (a + b) * 0.25f;
            });
            // Двигатель: 42 Гц укладывается ровно в секунду — бесшовная петля
            float en = 0f;
            var engine = Make("Engine", 1f, i =>
            {
                float t = i / (float)Rate;
                en += 0.1f * (white() - en);
                float ph = 2f * Mathf.PI * 42f * t;
                return (Mathf.Sin(ph) * 0.5f + Mathf.Sin(ph * 2f) * 0.3f + Mathf.Sin(ph * 3f) * 0.15f + en * 0.3f) * 0.6f;
            }, crossfade: false);

            _hum = Source("Hum", hum, true, 0f);
            _rain = Source("Rain", rain, true, 0f);
            _crowd = Source("Crowd", crowd, true, 0f);
            _night = Source("Night", night, true, 0f);
            _birds = Source("Birds", null, false, 1f);
            _birds.maxDistance = 60f;
            for (int i = 0; i < 4; i++)
            {
                var s = Source("Engine" + i, engine, true, 1f);
                s.minDistance = 3f;
                s.maxDistance = 45f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.volume = 0f;
                s.Play();
                _engines.Add(s);
                _engineTargets.Add(null);
                _lastPos.Add(Vector3.zero);
            }
            foreach (var s in new[] { _hum, _rain, _crowd, _night }) s.Play();
        }

        static AudioClip Make(string name, float seconds, Func<int, float> sample, bool crossfade = true)
        {
            int n = (int)(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = sample(i);
            if (crossfade)
            {
                // Склейка конца с началом — без щелчка на петле
                int fade = Rate / 5;
                for (int i = 0; i < fade; i++)
                {
                    float t = i / (float)fade;
                    data[n - fade + i] = Mathf.Lerp(data[n - fade + i], data[i], t);
                }
            }
            Normalize(data, 0.8f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip MakeOneShot(string name, float seconds, Func<int, float> sample) => Make(name, seconds, sample, false);

        static void Normalize(float[] data, float peak)
        {
            float max = 0.0001f;
            foreach (float v in data) max = Mathf.Max(max, Mathf.Abs(v));
            float k = peak / max;
            for (int i = 0; i < data.Length; i++) data[i] *= k;
        }

        AudioSource Source(string name, AudioClip clip, bool loop, float spatial)
        {
            var go = new GameObject("Audio_" + name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = loop;
            s.spatialBlend = spatial;
            s.playOnAwake = false;
            s.dopplerLevel = 0.3f;
            return s;
        }

        void Update()
        {
            var game = CityGame.Instance;
            if (game == null || !game.Ready) return;
            float hour = DayNightCycle.Hour;
            bool day = hour > 6f && hour < 21f;
            float rain = WeatherController.Rain;
            float dt = Time.deltaTime;
            var listener = game.ViewPosition;

            _hum.volume = Mathf.MoveTowards(_hum.volume, MasterVolume * (day ? 0.35f : 0.18f), dt * 0.2f);
            _rain.volume = Mathf.MoveTowards(_rain.volume, MasterVolume * rain * 0.55f, dt * 0.2f);
            _night.volume = Mathf.MoveTowards(_night.volume, MasterVolume * (!day && rain < 0.1f ? 0.12f : 0f), dt * 0.1f);
            int nearby = PedestrianSystem.Instance != null ? PedestrianSystem.Instance.CountNear(listener, 25f) : 0;
            _crowd.volume = Mathf.MoveTowards(_crowd.volume, MasterVolume * Mathf.Clamp01(nearby / 14f) * 0.3f, dt * 0.3f);

            _birdTimer -= dt;
            if (_birdTimer <= 0f)
            {
                _birdTimer = (float)(2.0 + _rnd.NextDouble() * 6.0);
                if (day && rain < 0.1f && hour < 19f)
                {
                    _birds.transform.position = listener + new Vector3((float)_rnd.NextDouble() * 30f - 15f, 6f, (float)_rnd.NextDouble() * 30f - 15f);
                    _birds.pitch = 0.85f + (float)_rnd.NextDouble() * 0.4f;
                    _birds.PlayOneShot(_birdClip, MasterVolume * 0.5f);
                }
            }

            // Звук двигателей «прикрепляется» к четырём ближайшим машинам
            _engineTimer -= dt;
            if (_engineTimer <= 0f && _traffic != null)
            {
                _engineTimer = 0.5f;
                var cars = new List<Transform>(_traffic.CarTransforms);
                cars.Sort((a, b) => (a.position - listener).sqrMagnitude.CompareTo((b.position - listener).sqrMagnitude));
                for (int i = 0; i < _engines.Count; i++)
                {
                    var target = i < cars.Count ? cars[i] : null;
                    if (_engineTargets[i] != target) { _engineTargets[i] = target; if (target != null) _lastPos[i] = target.position; }
                }
            }
            for (int i = 0; i < _engines.Count; i++)
            {
                var t = _engineTargets[i];
                var s = _engines[i];
                if (t == null) { s.volume = Mathf.MoveTowards(s.volume, 0f, dt); continue; }
                float speed = dt > 0f ? (t.position - _lastPos[i]).magnitude / dt : 0f;
                _lastPos[i] = t.position;
                s.transform.position = t.position;
                s.pitch = Mathf.Lerp(s.pitch, 0.7f + speed * 0.07f, dt * 3f);
                s.volume = Mathf.MoveTowards(s.volume, MasterVolume * (0.25f + Mathf.Clamp01(speed / 12f) * 0.35f), dt * 2f);
            }
        }

        public void Thunder(float delay) => StartCoroutine(PlayLater(_thunderClip, delay, 0.9f));

        System.Collections.IEnumerator PlayLater(AudioClip clip, float delay, float volume)
        {
            yield return new WaitForSeconds(delay);
            _hum.PlayOneShot(clip, volume * MasterVolume);
        }

        public void Chime(Vector3 position)
        {
            if (_chimeClip == null || CityGame.Instance == null) return;
            if ((position - CityGame.Instance.ViewPosition).sqrMagnitude > 20f * 20f) return;
            AudioSource.PlayClipAtPoint(_chimeClip, position, 0.5f * MasterVolume);
        }
    }
}

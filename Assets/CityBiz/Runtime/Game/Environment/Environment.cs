using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>
    /// Смена дня и ночи по реплицированному игровому времени: солнце/луна, процедурное небо, трёхцветный ambient,
    /// туман, уличные фонари, фары, окна домов, постобработка. Отражения обновляются realtime-пробой по частям.
    /// </summary>
    public sealed class DayNightCycle : MonoBehaviour
    {
        public static float Hour { get; private set; } = 12f;
        public static bool IsDark { get; private set; }
        public static bool LampsOn { get; private set; }
        public static float SunElevation { get; private set; }

        Light _sun;
        Material _sky;
        ReflectionProbe _probe;
        UrpBridge _post;
        float _windowsTimer, _probeTimer;
        bool _lampsApplied = true;

        public void Init(Light sun, UrpBridge post)
        {
            _sun = sun;
            _post = post;
            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                _sky = new Material(skyShader) { name = "CB_Sky" };
                _sky.SetFloat("_SunSize", 0.035f);
                _sky.SetFloat("_SunSizeConvergence", 6f);
                RenderSettings.skybox = _sky;
            }
            RenderSettings.sun = _sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;

            var probeGo = new GameObject("CityReflections");
            probeGo.transform.SetParent(transform, false);
            probeGo.transform.position = new Vector3(0, 12f, 0);
            _probe = probeGo.AddComponent<ReflectionProbe>();
            _probe.mode = ReflectionProbeMode.Realtime;
            _probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            _probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            _probe.resolution = 128;
            _probe.size = new Vector3(400, 120, 300);
            _probe.farClipPlane = 300f;
            _probe.hdr = true;
            _probe.importance = 1;
        }

        static Color Lerp3(Color night, Color dusk, Color day, float t) => t < 0.5f ? Color.Lerp(night, dusk, t * 2f) : Color.Lerp(dusk, day, (t - 0.5f) * 2f);

        void LateUpdate()
        {
            var game = CityGame.Instance;
            if (game == null || !game.Ready || _sun == null) return;
            var time = game.State.Time;
            Hour = time.HourOfDay;
            float cloud = WeatherController.Cloudiness;

            // Солнце: восход ~6:00, закат ~20:30
            float dayT = (Hour - 6f) / 14.5f;
            float elevation = Mathf.Sin(dayT * Mathf.PI) * 58f;
            SunElevation = elevation;
            float azimuth = Mathf.Lerp(-100f, 100f, Mathf.Clamp01(dayT));
            bool sunUp = elevation > -2f;
            // Ночью тот же источник играет роль луны
            float lightElevation = sunUp ? Mathf.Max(elevation, 2f) : 35f;
            float lightAzimuth = sunUp ? azimuth : azimuth + 180f;
            _sun.transform.rotation = Quaternion.Euler(lightElevation, lightAzimuth + 30f, 0);

            float daylight = Mathf.Clamp01((elevation + 4f) / 22f); // 0 — ночь, 1 — день
            float golden = Mathf.Clamp01(1f - Mathf.Abs(elevation - 6f) / 14f) * (sunUp ? 1f : 0f);
            var sunColor = Color.Lerp(new Color(1f, 0.96f, 0.9f), new Color(1f, 0.62f, 0.35f), golden);
            var moonColor = new Color(0.55f, 0.65f, 0.9f);
            _sun.color = sunUp ? sunColor : moonColor;
            _sun.intensity = sunUp ? Mathf.Lerp(0.15f, 1.35f, daylight) * (1f - cloud * 0.65f) : 0.12f * (1f - cloud * 0.8f);
            _sun.intensity += WeatherController.Flash * 2.5f;
            _sun.shadowStrength = Mathf.Lerp(0.35f, 0.85f, daylight) * (1f - cloud * 0.6f);

            var sky = Lerp3(new Color(0.06f, 0.08f, 0.16f), new Color(0.75f, 0.5f, 0.45f), new Color(0.55f, 0.7f, 0.95f), daylight);
            var equator = Lerp3(new Color(0.05f, 0.06f, 0.1f), new Color(0.6f, 0.45f, 0.4f), new Color(0.6f, 0.65f, 0.7f), daylight);
            var ground = Lerp3(new Color(0.03f, 0.03f, 0.04f), new Color(0.25f, 0.2f, 0.18f), new Color(0.32f, 0.3f, 0.27f), daylight);
            var grey = new Color(0.5f, 0.52f, 0.55f) * Mathf.Lerp(0.15f, 1f, daylight);
            RenderSettings.ambientSkyColor = Color.Lerp(sky, grey, cloud * 0.7f);
            RenderSettings.ambientEquatorColor = Color.Lerp(equator, grey, cloud * 0.6f);
            RenderSettings.ambientGroundColor = ground;
            var fogColor = Color.Lerp(Lerp3(new Color(0.05f, 0.06f, 0.1f), new Color(0.7f, 0.5f, 0.42f), new Color(0.7f, 0.78f, 0.88f), daylight), grey, cloud * 0.8f);
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = 0.0028f + WeatherController.FogAmount * 0.022f + WeatherController.Rain * 0.004f;
            if (_sky != null)
            {
                _sky.SetFloat("_AtmosphereThickness", Mathf.Lerp(0.9f, 2.2f, cloud) + golden * 0.4f);
                _sky.SetFloat("_Exposure", Mathf.Lerp(0.18f, 1.25f, daylight) * (1f - cloud * 0.45f));
                _sky.SetColor("_SkyTint", Color.Lerp(new Color(0.5f, 0.5f, 0.5f), new Color(0.6f, 0.6f, 0.62f), cloud));
                _sky.SetColor("_GroundColor", fogColor);
            }

            IsDark = daylight < 0.35f || (cloud > 0.8f && daylight < 0.6f);
            bool lamps = elevation < 5f || (cloud > 0.85f && elevation < 15f);
            LampsOn = lamps;
            if (lamps != _lampsApplied)
            {
                _lampsApplied = lamps;
                var M = MaterialLibrary.Instance;
                M.SetEmission(M.LampLens, lamps ? new Color(3.2f, 2.7f, 2.0f) : Color.black);
                M.SetEmission(M.Headlight, lamps ? new Color(2.5f, 2.4f, 2.1f) : new Color(0.15f, 0.15f, 0.14f));
                _probeTimer = 0f;
            }

            _post?.Apply(Mathf.Lerp(0.35f, 0f, daylight), Mathf.Lerp(-15f, 6f, daylight) - cloud * 18f, 8f, Color.Lerp(new Color(0.85f, 0.9f, 1.05f), Color.white, daylight), Mathf.Lerp(1.1f, 0.45f, daylight));

            _windowsTimer -= Time.deltaTime;
            if (_windowsTimer <= 0f)
            {
                _windowsTimer = 1f;
                foreach (var b in BuildingLights.All) b.Apply(Hour, time.IsWeekend);
            }

            _probeTimer -= Time.deltaTime;
            if (_probeTimer <= 0f && _probe != null)
            {
                _probeTimer = 20f;
                _probe.RenderProbe();
            }
        }
    }

    /// <summary>
    /// Плавные переходы погоды по реплицированному состоянию: облачность, дождь со «стрейч»-частицами вокруг камеры,
    /// мокрый асфальт (гладкость и цвет материалов), туман, молнии в грозу.
    /// </summary>
    public sealed class WeatherController : MonoBehaviour
    {
        public static WeatherKind CurrentKind { get; private set; }
        public static float Cloudiness { get; private set; }
        public static float Rain { get; private set; }
        public static float FogAmount { get; private set; }
        public static float Wetness { get; private set; }
        public static float Flash { get; private set; }

        ParticleSystem _rain;
        Light _sun;
        float _lightningTimer = 8f, _flash;
        readonly List<(Material mat, Color color, float smooth)> _wetSurfaces = new List<(Material, Color, float)>();
        float _appliedWetness = -1f;

        public void Init(Light sun)
        {
            _sun = sun;
            var M = MaterialLibrary.Instance;
            foreach (var m in new[] { M.Asphalt, M.AsphaltParking, M.Sidewalk, M.Pavers, M.Curb })
            {
                var color = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
                float smooth = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : m.GetFloat("_Glossiness");
                _wetSurfaces.Add((m, color, smooth));
            }

            var go = new GameObject("Rain");
            go.transform.SetParent(transform, false);
            _rain = go.AddComponent<ParticleSystem>();
            var main = _rain.main;
            main.startSpeed = 0f;
            main.startLifetime = 1.4f;
            main.startSize = 0.025f;
            main.maxParticles = 6000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(0.75f, 0.8f, 0.9f, 0.45f);
            var vel = _rain.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.6f);
            vel.y = new ParticleSystem.MinMaxCurve(-16f);
            vel.z = new ParticleSystem.MinMaxCurve(0.4f);
            var shape = _rain.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(44f, 1f, 44f);
            var emission = _rain.emission;
            emission.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.06f;
            r.lengthScale = 1f;
            r.sharedMaterial = M.Particles("rain", new Color(0.75f, 0.8f, 0.9f, 0.45f));
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        void Update()
        {
            var game = CityGame.Instance;
            if (game == null || !game.Ready) return;
            var kind = game.State.Weather.Kind;
            CurrentKind = kind;
            float targetCloud = kind == WeatherKind.Clear ? 0.05f : kind == WeatherKind.Cloudy ? 0.55f : kind == WeatherKind.Fog ? 0.6f : kind == WeatherKind.Rain ? 0.85f : 1f;
            float targetRain = kind == WeatherKind.Rain ? 0.6f : kind == WeatherKind.Storm ? 1f : 0f;
            float targetFog = kind == WeatherKind.Fog ? 1f : kind == WeatherKind.Storm ? 0.25f : 0f;
            float dt = Time.deltaTime;
            Cloudiness = Mathf.MoveTowards(Cloudiness, targetCloud, dt * 0.05f);
            Rain = Mathf.MoveTowards(Rain, targetRain, dt * 0.08f);
            FogAmount = Mathf.MoveTowards(FogAmount, targetFog, dt * 0.04f);
            Wetness = Mathf.MoveTowards(Wetness, Rain > 0.05f ? 1f : 0f, dt * (Rain > 0.05f ? 0.04f : 0.008f));

            var cam = game.ViewPosition;
            _rain.transform.position = cam + new Vector3(0, 14f, 0);
            var emission = _rain.emission;
            emission.rateOverTime = Rain * 4200f;
            if (Rain > 0.01f && !_rain.isPlaying) _rain.Play();
            if (Rain <= 0.01f && _rain.isPlaying) _rain.Stop();

            if (Mathf.Abs(Wetness - _appliedWetness) > 0.01f)
            {
                _appliedWetness = Wetness;
                var M = MaterialLibrary.Instance;
                foreach (var (mat, color, smooth) in _wetSurfaces)
                {
                    M.SetSmoothness(mat, Mathf.Lerp(smooth, 0.78f, Wetness));
                    M.SetColor(mat, Color.Lerp(color, color * 0.62f, Wetness));
                }
            }

            // Молнии
            if (kind == WeatherKind.Storm)
            {
                _lightningTimer -= dt;
                if (_lightningTimer <= 0f)
                {
                    _lightningTimer = Random.Range(6f, 18f);
                    _flash = 1f;
                    CityAudio.Instance?.Thunder(Random.Range(0.6f, 2.2f));
                }
            }
            if (_flash > 0f)
            {
                _flash = Mathf.MoveTowards(_flash, 0f, dt * 4f);
                Flash = _flash;
            }
        }
    }

    /// <summary>
    /// Реальные источники света только у ближайших к камере фонарей (по умолчанию 10),
    /// остальные фонари светятся эмиссией материала. Это и есть «разумное количество источников света».
    /// </summary>
    public sealed class LightBudget : MonoBehaviour
    {
        public int MaxLights = 10;
        readonly List<Light> _pool = new List<Light>();
        readonly List<Vector3> _heads = new List<Vector3>();
        float _timer;

        public void Init(IEnumerable<DistrictInstance> districts)
        {
            _heads.Clear();
            foreach (var d in districts)
                foreach (var p in d.LampHeads) _heads.Add(d.transform.TransformPoint(p));
            for (int i = 0; i < MaxLights; i++)
            {
                var go = new GameObject("LampLight" + i);
                go.transform.SetParent(transform, false);
                go.transform.rotation = Quaternion.Euler(90, 0, 0);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.spotAngle = 115f;
                l.innerSpotAngle = 60f;
                l.range = 16f;
                l.intensity = 9f;
                l.color = new Color(1f, 0.86f, 0.66f);
                l.shadows = LightShadows.None;
                l.enabled = false;
                _pool.Add(l);
            }
        }

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = 0.4f;
            var game = CityGame.Instance;
            if (game == null) return;
            if (!DayNightCycle.LampsOn)
            {
                foreach (var l in _pool) l.enabled = false;
                return;
            }
            var cam = game.ViewPosition;
            _heads.Sort((a, b) => (a - cam).sqrMagnitude.CompareTo((b - cam).sqrMagnitude));
            for (int i = 0; i < _pool.Count; i++)
            {
                bool on = i < _heads.Count;
                _pool[i].enabled = on;
                if (on) _pool[i].transform.position = _heads[i] - new Vector3(0, 0.15f, 0);
            }
        }
    }
}

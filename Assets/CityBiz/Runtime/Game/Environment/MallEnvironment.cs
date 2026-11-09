using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>
    /// Время суток в ТЦ. Дневной свет попадает внутрь только через стеклянный фонарь над атриумом (солнце + небо),
    /// а основной свет галерей — искусственный и зависит от часов работы ТЦ: утром включается, после закрытия
    /// остаётся дежурное освещение, рольставни закрытых точек опущены (это делает StorefrontView).
    /// </summary>
    public sealed class DayNightCycle : MonoBehaviour
    {
        public static float Hour { get; private set; } = 12f;
        /// <summary>0 — ночь, 1 — день (за стеклянной крышей).</summary>
        public static float Daylight { get; private set; } = 1f;
        public static bool MallOpen { get; private set; } = true;
        /// <summary>Вывески и витрины подсвечены — ТЦ работает или вот-вот откроется/только закрылся.</summary>
        public static bool SignsLit { get; private set; } = true;
        /// <summary>Яркость общего освещения галерей: 1 — ТЦ работает, ~0.2 — дежурный свет.</summary>
        public static float MallLighting { get; private set; } = 1f;

        Light _sun;
        Material _sky;
        ReflectionProbe _probe;
        UrpBridge _post;
        float _probeTimer, _appliedLighting = -1f;

        public void Init(Light sun, UrpBridge post, IList<MallInstance> malls)
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
            if (_sun != null) RenderSettings.sun = _sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = false;

            // Одна реалтайм-проба отражений на ТЦ: глянцевый мрамор и стекло витрин
            if (malls != null && malls.Count > 0 && malls[0] != null)
            {
                var mall = malls[0];
                var probeGo = new GameObject("MallReflections");
                probeGo.transform.SetParent(transform, false);
                var b = mall.Bounds;
                float h = mall.Floors * MallContent.FloorHeight;
                probeGo.transform.position = mall.transform.TransformPoint(new Vector3(b.center.x, h * 0.5f, b.center.y));
                _probe = probeGo.AddComponent<ReflectionProbe>();
                _probe.mode = ReflectionProbeMode.Realtime;
                _probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                _probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
                _probe.resolution = 128;
                _probe.boxProjection = true;
                _probe.size = new Vector3(b.width, h + 6f, b.height);
                _probe.farClipPlane = Mathf.Max(b.width, b.height);
                _probe.hdr = true;
                _probe.importance = 1;
            }
        }

        static Color Lerp3(Color night, Color dusk, Color day, float t) => t < 0.5f ? Color.Lerp(night, dusk, t * 2f) : Color.Lerp(dusk, day, (t - 0.5f) * 2f);

        void LateUpdate()
        {
            var game = CityGame.Instance;
            if (game == null || !game.Ready) return;
            var time = game.State.Time;
            Hour = time.HourOfDay;
            float open = EconomyCatalog.MallOpenHour, close = EconomyCatalog.MallCloseHour;
            MallOpen = Hour >= open && Hour < close;
            bool serviceLights = Hour >= open - 0.5f && Hour < close + 0.5f;
            SignsLit = serviceLights;
            MallLighting = Mathf.MoveTowards(MallLighting, serviceLights ? 1f : 0.2f, Time.deltaTime * 0.4f);
            float cloud = WeatherController.Cloudiness;

            // Солнце: восход ~6:00, закат ~21:00
            float dayT = (Hour - 6f) / 15f;
            float elevation = Mathf.Sin(dayT * Mathf.PI) * 64f;
            bool sunUp = elevation > 0f;
            Daylight = Mathf.Clamp01((elevation + 3f) / 22f);
            if (_sun != null)
            {
                // Не ниже 32°: низкое солнце иначе «подсвечивало» бы помещения под кровлей
                float lightElevation = sunUp ? Mathf.Max(elevation, 32f) : 55f;
                float azimuth = Mathf.Lerp(-70f, 70f, Mathf.Clamp01(dayT));
                _sun.transform.rotation = Quaternion.Euler(lightElevation, azimuth, 0f);
                float golden = sunUp ? Mathf.Clamp01(1f - Mathf.Abs(elevation - 6f) / 16f) : 0f;
                _sun.color = sunUp ? Color.Lerp(new Color(1f, 0.96f, 0.9f), new Color(1f, 0.66f, 0.4f), golden) : new Color(0.55f, 0.65f, 0.9f);
                _sun.intensity = (sunUp ? Mathf.Lerp(0.25f, 1.5f, Daylight) * (1f - cloud * 0.7f) : 0.05f) + WeatherController.Flash * 2.5f;
                _sun.shadows = sunUp ? LightShadows.Soft : LightShadows.None;
                _sun.shadowStrength = Mathf.Lerp(0.4f, 0.85f, Daylight) * (1f - cloud * 0.6f);
            }

            // Рассеянный свет: искусственный (часы работы ТЦ) + дневной через фонарь
            float art = MallLighting;
            float day = Daylight * (1f - cloud * 0.5f);
            RenderSettings.ambientSkyColor = new Color(0.72f, 0.7f, 0.66f) * art + new Color(0.32f, 0.4f, 0.5f) * day;
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.6f, 0.56f) * art + new Color(0.18f, 0.2f, 0.22f) * day + new Color(0.03f, 0.03f, 0.04f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.34f, 0.31f) * art + new Color(0.06f, 0.06f, 0.06f) * day;

            if (_sky != null)
            {
                _sky.SetFloat("_AtmosphereThickness", Mathf.Lerp(0.9f, 2.2f, cloud));
                _sky.SetFloat("_Exposure", Mathf.Lerp(0.12f, 1.25f, Daylight) * (1f - cloud * 0.45f));
                _sky.SetColor("_SkyTint", Color.Lerp(new Color(0.5f, 0.5f, 0.5f), new Color(0.6f, 0.6f, 0.62f), cloud));
                _sky.SetColor("_GroundColor", Lerp3(new Color(0.05f, 0.06f, 0.1f), new Color(0.6f, 0.45f, 0.4f), new Color(0.55f, 0.58f, 0.6f), Daylight));
            }

            if (Mathf.Abs(_appliedLighting - art) > 0.02f)
            {
                _appliedLighting = art;
                var M = MaterialLibrary.Instance;
                M.SetEmission(M.CeilingLight, new Color(2.4f, 2.3f, 2.1f) * Mathf.Lerp(0.12f, 1f, art));
                _probeTimer = Mathf.Min(_probeTimer, 1f);
            }

            float bright = Mathf.Max(art, day);
            _post?.Apply(Mathf.Lerp(-0.35f, 0.1f, bright), 6f - cloud * 10f, 8f, Color.Lerp(new Color(0.82f, 0.88f, 1.05f), Color.white, bright), Mathf.Lerp(1.0f, 0.55f, bright));

            _probeTimer -= Time.deltaTime;
            if (_probeTimer <= 0f && _probe != null)
            {
                _probeTimer = 30f;
                _probe.RenderProbe();
            }
        }
    }

    /// <summary>
    /// Погода снаружи ТЦ: облачность приглушает солнце в фонаре, дождь виден на стеклянной крыше и слышен
    /// (громче на верхнем этаже — это делает MallAudio), в грозу — вспышки молний через фонарь.
    /// </summary>
    public sealed class WeatherController : MonoBehaviour
    {
        public static WeatherKind CurrentKind { get; private set; }
        public static float Cloudiness { get; private set; }
        public static float Rain { get; private set; }
        public static float Flash { get; private set; }

        ParticleSystem _rain;
        float _lightningTimer = 8f;

        public void Init(IList<MallInstance> malls)
        {
            if (malls == null || malls.Count == 0 || malls[0] == null) return;
            var mall = malls[0];
            var M = MaterialLibrary.Instance;
            // Капли падают на стеклянную крышу и исчезают на её высоте — изнутри видны через фонарь
            var go = new GameObject("RainOnRoof");
            go.transform.SetParent(transform, false);
            float roof = mall.Floors * MallContent.FloorHeight + 1.5f;
            go.transform.position = mall.transform.TransformPoint(new Vector3(0f, roof + 12f, 0f));
            _rain = go.AddComponent<ParticleSystem>();
            var main = _rain.main;
            main.startSpeed = 0f;
            main.startLifetime = 12f / 16f;
            main.startSize = 0.03f;
            main.maxParticles = 4000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(0.75f, 0.8f, 0.9f, 0.4f);
            var vel = _rain.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.4f);
            vel.y = new ParticleSystem.MinMaxCurve(-16f);
            vel.z = new ParticleSystem.MinMaxCurve(0.3f);
            var shape = _rain.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(mall.Bounds.width * 0.8f, 1f, 18f);
            var emission = _rain.emission;
            emission.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            r.lengthScale = 1f;
            r.sharedMaterial = M.Particles("rain", new Color(0.75f, 0.8f, 0.9f, 0.4f));
            r.shadowCastingMode = ShadowCastingMode.Off;
            _rain.Stop();
        }

        void Update()
        {
            var game = CityGame.Instance;
            if (game == null || !game.Ready) return;
            var kind = game.State.Weather.Kind;
            CurrentKind = kind;
            float targetCloud = kind == WeatherKind.Clear ? 0.05f : kind == WeatherKind.Cloudy ? 0.55f : kind == WeatherKind.Fog ? 0.6f : kind == WeatherKind.Rain ? 0.85f : 1f;
            float targetRain = kind == WeatherKind.Rain ? 0.6f : kind == WeatherKind.Storm ? 1f : 0f;
            float dt = Time.deltaTime;
            Cloudiness = Mathf.MoveTowards(Cloudiness, targetCloud, dt * 0.05f);
            Rain = Mathf.MoveTowards(Rain, targetRain, dt * 0.08f);

            if (_rain != null)
            {
                var emission = _rain.emission;
                emission.rateOverTime = Rain * 3500f;
                if (Rain > 0.01f && !_rain.isPlaying) _rain.Play();
                if (Rain <= 0.01f && _rain.isPlaying) _rain.Stop();
            }

            if (kind == WeatherKind.Storm)
            {
                _lightningTimer -= dt;
                if (_lightningTimer <= 0f)
                {
                    _lightningTimer = Random.Range(7f, 20f);
                    Flash = 1f;
                }
            }
            if (Flash > 0f) Flash = Mathf.MoveTowards(Flash, 0f, dt * 4f);
        }
    }

    /// <summary>
    /// Бюджет источников света: реальные точечные источники только у N ближайших к камере светильников галерей
    /// (с приоритетом своего этажа), остальные светятся эмиссией. Свет в помещениях тоже ограничен: включают его
    /// только ближайшие магазины. Это и есть «разумное количество источников света» для URP Forward.
    /// </summary>
    public sealed class LightBudget : MonoBehaviour
    {
        public static LightBudget Instance { get; private set; }
        static readonly HashSet<StorefrontView> AllowedShops = new HashSet<StorefrontView>();

        public int MaxLights = 12;
        public int MaxShopLights = 6;
        public int ActiveLights { get; private set; }

        readonly List<Light> _pool = new List<Light>();
        readonly List<Vector3> _anchors = new List<Vector3>();
        readonly List<StorefrontView> _shopScratch = new List<StorefrontView>();
        float _timer;

        public static bool AllowShopLight(StorefrontView view) => Instance == null || AllowedShops.Contains(view);

        public void Init(IList<MallInstance> malls)
        {
            Instance = this;
            AllowedShops.Clear();
            _anchors.Clear();
            if (malls != null)
                foreach (var m in malls)
                    if (m != null)
                        foreach (var a in m.LightAnchors) _anchors.Add(m.transform.TransformPoint(a));
            for (int i = 0; i < MaxLights; i++)
            {
                var go = new GameObject("GalleryLight" + i);
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 14f;
                l.intensity = 2.2f;
                l.color = new Color(1f, 0.95f, 0.86f);
                l.shadows = LightShadows.None;
                l.enabled = false;
                _pool.Add(l);
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            AllowedShops.Clear();
        }

        static float WeightedDistance(Vector3 a, Vector3 cam)
        {
            var d = a - cam;
            d.y *= 3f; // светильники чужого этажа менее важны
            return d.sqrMagnitude;
        }

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = 0.35f;
            var game = CityGame.Instance;
            if (game == null) return;
            var cam = game.ViewPosition;
            float level = DayNightCycle.MallLighting;

            _anchors.Sort((a, b) => WeightedDistance(a, cam).CompareTo(WeightedDistance(b, cam)));
            int active = 0;
            for (int i = 0; i < _pool.Count; i++)
            {
                bool on = i < _anchors.Count && level > 0.25f;
                _pool[i].enabled = on;
                if (!on) continue;
                _pool[i].transform.position = _anchors[i];
                _pool[i].intensity = 2.2f * level;
                active++;
            }

            _shopScratch.Clear();
            foreach (var v in StorefrontView.All)
            {
                if (v == null) continue;
                var d = v.transform.position - cam;
                // Камера на высоте глаз (~1.7 м над полом), этаж выше — уже ~4.3 м
                if (Mathf.Abs(d.y) < 3.5f && d.sqrMagnitude < 45f * 45f) _shopScratch.Add(v);
            }
            _shopScratch.Sort((a, b) => (a.transform.position - cam).sqrMagnitude.CompareTo((b.transform.position - cam).sqrMagnitude));
            AllowedShops.Clear();
            for (int i = 0; i < _shopScratch.Count && i < MaxShopLights; i++) AllowedShops.Add(_shopScratch[i]);
            ActiveLights = active + AllowedShops.Count;
        }
    }
}

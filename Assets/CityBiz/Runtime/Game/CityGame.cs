using System;
using System.Collections.Generic;
using CityBiz.Core;
using CityBiz.Core.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>
    /// Точка входа игры. Достаточно одного объекта с этим компонентом в сцене:
    /// он строит (или находит запечённые) районы, поднимает окружение, NPC, транспорт, UI и сетевую сессию.
    /// </summary>
    public sealed class CityGame : MonoBehaviour
    {
        public static CityGame Instance { get; private set; }

        [Tooltip("Seed мира: одинаковый у всех клиентов, определяет экономику и косметику.")]
        public ulong WorldSeed = 20261109;
        [Tooltip("Стартовая скорость: игровых минут за реальную секунду (24 мин реального времени = сутки).")]
        public float StartTimeScale = 1f;

        public WorldDefinition World { get; private set; }
        public SimClient Client { get; private set; }
        public SimServer Server { get; private set; }
        public GameUI UI { get; private set; }
        public TrafficSystem Traffic { get; private set; }
        public InstancedRenderer Instanced { get; private set; }
        public DistrictInstance CurrentDistrict { get; private set; }
        public bool SessionStarted { get; private set; }
        public string SessionInfo { get; private set; } = "";

        public bool Ready => Client != null && Client.HasSnapshot;
        public CityState State => Client?.State;
        public int LocalPlayerId => Client != null ? Client.LocalPlayerId : 0;
        public Vector3 ViewPosition => _camera != null ? _camera.transform.position : Vector3.zero;

        Camera _camera;
        Light _sun;
        LoopbackHub _hub;
        TcpServerTransport _tcpServer;
        TcpClientTransport _tcpClient;
        PlayerController _player;
        bool _worldSystemsStarted;
        readonly List<DistrictInstance> _districts = new List<DistrictInstance>();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Application.runInBackground = true;
            Application.targetFrameRate = -1;
            MaterialLibrary.Reset();
            World = DistrictRegistry.CreateWorld();

            SetupCameraAndLight();
            BuildDistricts();
            SetupSystems();
            if (_districts.Count > 0) PlaceCameraOverview(_districts[0]);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            _tcpServer?.Dispose();
            _tcpClient?.Dispose();
            MaterialLibrary.Reset();
        }

        // ---------------- Сцена ----------------

        void SetupCameraAndLight()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                _camera = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 650f;
            _camera.fieldOfView = 62f;
            _camera.useOcclusionCulling = true; // работает при запечённой сцене (меню CityBiz → Запечь район)
            _camera.allowHDR = true;
            UrpBridge.ConfigureCamera(_camera);

#if UNITY_6000_6_OR_NEWER
            foreach (var l in FindObjectsByType<Light>())
#else
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
#endif
                if (l.type == LightType.Directional) { _sun = l; break; }
            if (_sun == null)
            {
                var sunGo = new GameObject("Sun");
                _sun = sunGo.AddComponent<Light>();
                _sun.type = LightType.Directional;
            }
            _sun.shadows = LightShadows.Soft;
            _sun.shadowBias = 0.04f;

            // Разумные настройки производительности
            QualitySettings.shadowDistance = Mathf.Min(QualitySettings.shadowDistance > 0 ? QualitySettings.shadowDistance : 90f, 110f);
            QualitySettings.lodBias = Mathf.Max(QualitySettings.lodBias, 1f);
        }

        void BuildDistricts()
        {
            var builder = new DistrictBuilder(MaterialLibrary.Instance, World);
            var worldRoot = new GameObject("City").transform;
            // Запечённые районы ищем напрямую: порядок OnEnable/Awake между объектами сцены не гарантирован.
#if UNITY_6000_6_OR_NEWER
            var baked = FindObjectsByType<DistrictInstance>();
#else
            var baked = FindObjectsByType<DistrictInstance>(FindObjectsSortMode.None);
#endif
            foreach (var blueprint in DistrictRegistry.All)
            {
                DistrictInstance existing = null;
                foreach (var d in baked) if (d.DistrictId == blueprint.DistrictId) existing = d;
                if (existing != null)
                {
                    existing.AdoptMaterials(MaterialLibrary.Instance);
                    _districts.Add(existing);
                    continue;
                }
                var started = Time.realtimeSinceStartup;
                var inst = builder.Build(blueprint, worldRoot);
                Debug.Log($"[CityBiz] Район «{blueprint.DisplayName}» построен за {(Time.realtimeSinceStartup - started) * 1000f:0} мс");
                _districts.Add(inst);
            }
            CurrentDistrict = _districts.Count > 0 ? _districts[0] : null;
            if (CurrentDistrict != null) Instanced = CurrentDistrict.GetComponent<InstancedRenderer>();
        }

        void SetupSystems()
        {
            var systems = new GameObject("Systems").transform;
            systems.SetParent(transform, false);
            T Add<T>(string name) where T : Component
            {
                var go = new GameObject(name);
                go.transform.SetParent(systems, false);
                return go.AddComponent<T>();
            }

            var post = UrpBridge.CreateGlobalVolume(systems);
            Add<DayNightCycle>("DayNight").Init(_sun, post);
            Add<WeatherController>("Weather").Init(_sun);
            Add<LightBudget>("LightBudget").Init(_districts);
            Traffic = Add<TrafficSystem>("Traffic");
            Add<CityAudio>("Audio").Init(Traffic);
            Add<PedestrianSystem>("Pedestrians");
            UI = Add<GameUI>("UI");
        }

        void PlaceCameraOverview(DistrictInstance d)
        {
            // Пока открыто главное меню — панорама района
            var target = d.transform.TransformPoint(new Vector3(0, 0, 0));
            _camera.transform.position = target + new Vector3(-70f, 38f, -55f);
            _camera.transform.LookAt(target + new Vector3(10f, 0, 10f));
        }

        // ---------------- Сессии ----------------

        static uint ColorFromName(string name)
        {
            float hue = (Mathf.Abs((name ?? "").GetHashCode()) % 360) / 360f;
            var c = Color.HSVToRGB(hue, 0.65f, 0.95f);
            return ((uint)(c.r * 255) << 16) | ((uint)(c.g * 255) << 8) | (uint)(c.b * 255);
        }

        CitySimulation CreateSimulation()
        {
            var sim = new CitySimulation(World, WorldSeed);
            sim.State.TimeScale = StartTimeScale;
            return sim;
        }

        public void StartSolo(string playerName)
        {
            _hub = new LoopbackHub();
            Server = new SimServer(CreateSimulation(), _hub);
            AttachClient(new SimClient(World, _hub.CreateClient(), playerName, ColorFromName(playerName)));
            SessionInfo = "Одиночная игра · ИИ-конкуренты: 3";
        }

        public void StartHost(string playerName, int port)
        {
            try
            {
                _hub = new LoopbackHub();
                _tcpServer = new TcpServerTransport(port);
                Server = new SimServer(CreateSimulation(), new CompositeServerTransport(_hub, _tcpServer));
                AttachClient(new SimClient(World, _hub.CreateClient(), playerName, ColorFromName(playerName)));
                SessionInfo = $"Сервер запущен: порт {_tcpServer.Port}. Друзья подключаются по IP этого компьютера.";
            }
            catch (Exception e)
            {
                SessionInfo = "Не удалось запустить сервер: " + e.Message;
                Debug.LogException(e);
                _tcpServer?.Dispose();
                _tcpServer = null;
                Server = null;
            }
        }

        public void Join(string playerName, string host, int port)
        {
            _tcpClient = new TcpClientTransport(host, port);
            AttachClient(new SimClient(World, _tcpClient, playerName, ColorFromName(playerName)));
            SessionInfo = $"Подключение к {host}:{port}…";
        }

        void AttachClient(SimClient client)
        {
            Client = client;
            SessionStarted = true;
            client.NewsReceived += OnNews;
            client.SnapshotReceived += () => SessionInfo = Server != null ? SessionInfo : $"Подключено к серверу · игрок #{client.LocalPlayerId}";
        }

        public void Send(Command cmd)
        {
            if (Client == null) return;
            Client.Send(cmd, r =>
            {
                if (!string.IsNullOrEmpty(r.Message) && cmd.Type != CommandType.Chat) UI?.Toast(r.Message, r.Success);
            });
        }

        void OnNews(NewsItem n)
        {
            if (UI == null || State == null) return;
            // Важные события для игрока выводятся всплывающими уведомлениями
            var prop = State.GetProperty(n.PropertyId);
            bool mine = prop != null && (prop.OwnerId == LocalPlayerId || prop.TenantId == LocalPlayerId);
            if (mine && (n.Category == NewsCategory.Sabotage || n.Category == NewsCategory.Police || n.Category == NewsCategory.Business || n.Category == NewsCategory.Property))
                UI.Toast(n.Text, n.Category != NewsCategory.Sabotage);
            var me = Client.LocalPlayer;
            if (me != null && n.Category == NewsCategory.Property && n.Text.Contains("предлагает " + me.Name))
                UI.Toast("Вам предложили сделку — откройте Портфель → Сделки", true);
        }

        public string NetworkStats
        {
            get
            {
                if (_hub != null && Server != null)
                    return $"Сеть: дельт {Server.DeltasSent}, клиентам {_hub.BytesToClients / 1024f:0.0} КБ, серверу {_hub.BytesToServer / 1024f:0.0} КБ";
                return _tcpClient != null ? (_tcpClient.IsConnected ? "Сеть: подключено (TCP)" : "Сеть: нет соединения " + _tcpClient.Error) : "Сеть: —";
            }
        }

        // ---------------- Цикл ----------------

        void Update()
        {
            float dt = Time.deltaTime;
            Server?.Update(dt);
            Client?.Update(dt);

            if (Ready && !_worldSystemsStarted && CurrentDistrict != null)
            {
                _worldSystemsStarted = true;
                Traffic.Init(CurrentDistrict);
                GetComponentInChildren<PedestrianSystem>().Init(CurrentDistrict);
                SpawnPlayer();
                var remote = new GameObject("RemotePlayers").AddComponent<RemotePlayers>();
                remote.transform.SetParent(transform, false);
                remote.Init(Client);
            }
        }

        void SpawnPlayer()
        {
            var d = CurrentDistrict;
            var go = new GameObject("Player");
            _player = go.AddComponent<PlayerController>();
            var me = Client.LocalPlayer;
            var color = me != null ? RemotePlayers.ColorFrom(me.Color) : new Color(0.2f, 0.5f, 0.9f);
            // Игроки по сети появляются чуть в стороне друг от друга
            var offset = new Vector3((LocalPlayerId % 5) * 1.2f, 0, 0);
            _player.Init(_camera, d.transform.TransformPoint(d.PlayerSpawn) + offset, d.PlayerSpawnYaw, color);
            UI.Toast("Добро пожаловать на Липовую улицу! F1 — справка, M — карта.", true);
        }
    }
}

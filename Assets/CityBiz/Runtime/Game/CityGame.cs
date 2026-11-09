using System;
using System.Collections.Generic;
using CityBiz.Core;
using CityBiz.Core.Net;
using UnityEngine;

namespace CityBiz.Game
{
    /// <summary>
    /// Точка входа игры. Достаточно одного объекта с этим компонентом в сцене:
    /// он строит (или находит запечённые) торговые центры, поднимает окружение, посетителей, звук, UI
    /// и сетевую сессию (одиночная игра, сервер или подключение), затем ставит игрока от первого лица у входа.
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
        public InstancedRenderer Instanced { get; private set; }
        public IReadOnlyList<MallInstance> Malls => _malls;
        public FirstPersonController Player => _player;
        public bool SessionStarted { get; private set; }
        public string SessionInfo { get; private set; } = "";

        public bool Ready => Client != null && Client.HasSnapshot;
        public CityState State => Client?.State;
        public int LocalPlayerId => Client != null ? Client.LocalPlayerId : 0;
        public Vector3 ViewPosition => _camera != null ? _camera.transform.position : Vector3.zero;

        /// <summary>ТЦ, в котором сейчас находится игрок (или первый).</summary>
        public MallInstance CurrentMall
        {
            get
            {
                if (_malls.Count == 0) return null;
                var pos = ViewPosition;
                foreach (var m in _malls) if (m != null && m.Contains(pos)) return m;
                return _malls[0];
            }
        }

        Camera _camera;
        Light _sun;
        LoopbackHub _hub;
        TcpServerTransport _tcpServer;
        TcpClientTransport _tcpClient;
        FirstPersonController _player;
        PedestrianSystem _pedestrians;
        bool _worldSystemsStarted;
        float _menuOrbit;
        readonly List<MallInstance> _malls = new List<MallInstance>();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Application.runInBackground = true;
            Application.targetFrameRate = -1;
            // Статические кеши переживают Play Mode при выключенной перезагрузке домена — сбрасываем
            MaterialLibrary.Reset();
            AssetCatalog.Reset();
            CharacterRig.ResetCache();
            AudioLibrary.Reset();
            World = MallRegistry.CreateWorld();

            SetupCameraAndLight();
            BuildMalls();
            SetupSystems();
            PlaceMenuCamera(0f);
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            _tcpServer?.Dispose();
            _tcpClient?.Dispose();
            AssetCatalog.DestroyTemplates();
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
            else if (_camera.GetComponent<AudioListener>() == null) _camera.gameObject.AddComponent<AudioListener>();
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 400f;
            _camera.fieldOfView = 70f;
            _camera.useOcclusionCulling = true; // работает в запечённой сцене (CityBiz → Запечь ТЦ)
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

            // Внутри ТЦ тени нужны только вблизи (солнечные пятна под фонарём)
            QualitySettings.shadowDistance = 70f;
            QualitySettings.lodBias = Mathf.Max(QualitySettings.lodBias, 1f);
        }

        void BuildMalls()
        {
            var builder = new MallBuilder(MaterialLibrary.Instance, World);
            // Запечённые ТЦ ищем напрямую: порядок OnEnable/Awake между объектами сцены не гарантирован
#if UNITY_6000_6_OR_NEWER
            var baked = FindObjectsByType<MallInstance>();
#else
            var baked = FindObjectsByType<MallInstance>(FindObjectsSortMode.None);
#endif
            Transform root = null;
            foreach (var b in baked) if (b != null && b.transform.parent != null) { root = b.transform.parent; break; }
            if (root == null) root = new GameObject("Malls").transform;
            _malls.Clear();
            _malls.AddRange(builder.BuildAll(root, baked));
            if (_malls.Count > 0 && _malls[0] != null) Instanced = _malls[0].GetComponent<InstancedRenderer>();
            if (_malls.Count == 0) Debug.LogError("[CityBiz] В MallRegistry нет ни одного ТЦ.");
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
            Add<WeatherController>("Weather").Init(_malls);
            Add<DayNightCycle>("DayNight").Init(_sun, post, _malls);
            Add<LightBudget>("LightBudget").Init(_malls);
            Add<MallAudio>("Audio").Init(_malls);
            _pedestrians = Add<PedestrianSystem>("Shoppers");
            UI = Add<GameUI>("UI");
        }

        /// <summary>Пока открыто главное меню — медленный облёт атриума.</summary>
        void PlaceMenuCamera(float t)
        {
            var mall = _malls.Count > 0 ? _malls[0] : null;
            if (mall == null || _camera == null) return;
            var b = mall.Bounds;
            float x = Mathf.Lerp(b.xMax - 12f, b.center.x + 10f, Mathf.PingPong(t * 0.01f, 1f));
            var pos = mall.transform.TransformPoint(new Vector3(x, 8.5f, Mathf.Sin(t * 0.05f) * 3f));
            var look = mall.transform.TransformPoint(new Vector3(x - 30f, 4f, 0f));
            _camera.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
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
            if (SessionStarted) return;
            _hub = new LoopbackHub();
            Server = new SimServer(CreateSimulation(), _hub);
            AttachClient(new SimClient(World, _hub.CreateClient(), playerName, ColorFromName(playerName)));
            SessionInfo = "Одиночная игра · ИИ-конкуренты: 3";
        }

        public void StartHost(string playerName, int port)
        {
            if (SessionStarted) return;
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
                _hub = null;
            }
        }

        public void Join(string playerName, string host, int port)
        {
            if (SessionStarted) return;
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
                if (cmd.Type == CommandType.Chat) return;
                if (!string.IsNullOrEmpty(r.Message)) UI?.Toast(r.Message, r.Success);
                MallAudio.Instance?.Ui(r.Success ? (cmd.Type == CommandType.UpgradeBusiness ? UiSound.Coins : UiSound.Confirm) : UiSound.Error);
            });
        }

        void OnNews(NewsItem n)
        {
            if (UI == null || State == null || Client == null) return;
            // Важные события для игрока выводятся всплывающими уведомлениями
            var prop = State.GetProperty(n.PropertyId);
            bool mine = prop != null && (prop.OwnerId == LocalPlayerId || prop.TenantId == LocalPlayerId);
            if (mine && (n.Category == NewsCategory.Sabotage || n.Category == NewsCategory.Police || n.Category == NewsCategory.Business || n.Category == NewsCategory.Property))
                UI.Toast(n.Text, n.Category != NewsCategory.Sabotage);
            var me = Client.LocalPlayer;
            if (me != null && n.Category == NewsCategory.Property && n.Text != null && n.Text.Contains("предлагает " + me.Name))
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

            if (!_worldSystemsStarted)
            {
                _menuOrbit += dt;
                if (!Ready) PlaceMenuCamera(_menuOrbit);
            }

            if (Ready && !_worldSystemsStarted && _malls.Count > 0)
            {
                _worldSystemsStarted = true;
                if (_pedestrians != null) _pedestrians.Init(_malls);
                SpawnPlayer();
                var remote = new GameObject("RemotePlayers").AddComponent<RemotePlayers>();
                remote.transform.SetParent(transform, false);
                remote.Init(Client);
            }
        }

        void SpawnPlayer()
        {
            var mall = _malls[0];
            var go = new GameObject("Player");
            _player = go.AddComponent<FirstPersonController>();
            // Игроки по сети появляются чуть в стороне друг от друга (поперёк галереи)
            var offset = mall.transform.rotation * new Vector3(0f, 0f, ((LocalPlayerId % 5) - 2) * 0.9f);
            _player.Init(_camera, mall.transform.TransformPoint(mall.PlayerSpawn) + offset, mall.PlayerSpawnYaw + mall.transform.eulerAngles.y);
            UI.Toast($"Добро пожаловать в {mall.DisplayName}! Найдите свободное помещение и развивайте бизнес. F1 — справка, M — схема ТЦ.", true);
        }
    }
}

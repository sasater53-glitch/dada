using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;

namespace CityBiz.Game
{
    /// <summary>
    /// Посетители ТЦ (персонажи Kenney). Гуляющие ходят по графу галерей между этажами (через эскалаторы),
    /// разглядывают витрины, отдыхают на скамейках и за столиками фудкорта. Покупатели появляются с частотой,
    /// пропорциональной реальному числу клиентов бизнеса в симуляции, заходят в магазин к полкам/кассе
    /// (у киосков и точек фудкорта — обслуживаются у стойки) и выходят с пакетами.
    /// Персонал за стойками = нанятые сотрудники (его расставляет StorefrontView).
    /// Оптимизация: пул персонажей, скрытие дальних и «чужих» этажей, реже обновляемая анимация вдали.
    /// Косметические NPC не синхронизируются по сети — каждый клиент генерирует их сам.
    /// </summary>
    public sealed class PedestrianSystem : MonoBehaviour
    {
        public static PedestrianSystem Instance { get; private set; }

        enum State { Walking, Idle, Sitting, GoIn, GoInside, Inside, Service, Hidden, GoOut, GoOutside }

        sealed class Ped
        {
            public CharacterRig Rig;
            public Transform T;
            public MallInstance Mall;
            public PedestrianGraph G;
            public bool[] Narrow;
            public State State;
            public List<int> Path;
            public int PathIndex;
            public int Node, Prev = -1;
            public Vector3 From, To, InsideTarget;
            public float Progress, SegLength, Speed, Lateral, Timer, AnimAccum;
            public bool Customer, LeaveAfter;
            public StorefrontView Shop;
            public int Phase;
        }

        sealed class Graph
        {
            public MallInstance Mall;
            public PedestrianGraph G;
            public bool[] Narrow;            // узлы на эскалаторах — без бокового смещения
            public readonly List<int> Spawn = new List<int>();
            public readonly List<int> Exits = new List<int>();
        }

        /// <summary>Сколько видимых покупателей показывать на одного покупателя из экономики.</summary>
        public float CustomerVisualFactor = 0.2f;
        public float DensityMultiplier = 1f;
        public int MaxPeds = 72;
        public int MaxCustomers = 30;

        readonly List<Graph> _graphs = new List<Graph>();
        readonly List<Ped> _active = new List<Ped>();
        readonly Stack<Ped> _pool = new Stack<Ped>();
        readonly Dictionary<StorefrontView, float> _customerAccum = new Dictionary<StorefrontView, float>();
        readonly Dictionary<StorefrontView, Graph> _shopGraph = new Dictionary<StorefrontView, Graph>();
        readonly DeterministicRandom _rng = new DeterministicRandom(2026);
        int _seed = 1, _frame, _pathBudget;
        double _lastGameMinutes = -1;
        float _spawnTimer;

        public int ActiveCount => _active.Count;
        public int VisibleCount { get; private set; }

        public void Init(IList<MallInstance> malls)
        {
            Instance = this;
            _graphs.Clear();
            if (malls == null) return;
            foreach (var mall in malls)
            {
                if (mall == null || mall.Pedestrians == null || mall.Pedestrians.Count == 0) continue;
                var gr = new Graph { Mall = mall, G = mall.Pedestrians };
                var g = gr.G;
                gr.Narrow = new bool[g.Count];
                for (int e = 0; e < g.EdgeA.Count; e++)
                {
                    int a = g.EdgeA[e], b = g.EdgeB[e];
                    if (a < 0 || b < 0 || a >= g.Count || b >= g.Count) continue;
                    if (Mathf.Abs(g.Nodes[a].y - g.Nodes[b].y) > 0.3f) gr.Narrow[a] = gr.Narrow[b] = true;
                }
                for (int i = 0; i < g.Count; i++)
                {
                    if (g.Kinds[i] == PedNodeKind.Walk && g.Neighbors(i).Count > 0) gr.Spawn.Add(i);
                    if (g.Kinds[i] == PedNodeKind.Door && g.NodeProperty[i] == -2) gr.Exits.Add(i);
                }
                if (gr.Spawn.Count > 0) _graphs.Add(gr);
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var p in _active) if (p.T != null) Agents.All.Remove(p.T);
        }

        static Vector3 W(Ped p, int node) => p.Mall.transform.TransformPoint(p.G.Nodes[node]);
        static Vector3 W(Graph g, int node) => g.Mall.transform.TransformPoint(g.G.Nodes[node]);

        int TargetWalkers(CityGame game)
        {
            float h = DayNightCycle.Hour;
            float open = EconomyCatalog.MallOpenHour, close = EconomyCatalog.MallCloseHour;
            if (h < open - 0.2f || h >= close + 0.2f) return 0; // ночью ТЦ пуст
            float curve = h < open + 1f ? 0.45f : h < 12f ? 0.7f : h < 17f ? 0.85f : h < 20f ? 1f : 0.55f;
            float weather = game.State.Weather.MallVisitMultiplier;
            float weekend = game.State.Time.IsWeekend ? 1.2f : 1f;
            return Mathf.Clamp(Mathf.RoundToInt(46f * curve * weather * weekend * DensityMultiplier), 0, MaxPeds - 10);
        }

        // ---------------- Пул ----------------

        Ped Acquire(Graph g, Vector3 position)
        {
            Ped p;
            if (_pool.Count > 0)
            {
                p = _pool.Pop();
                p.T.gameObject.SetActive(true);
            }
            else
            {
                var rig = CharacterRig.Create(transform, CharacterRig.RandomModel(_seed++ * 7919), _rng.Range(0.93f, 1.05f));
                p = new Ped { Rig = rig, T = rig.transform };
            }
            p.Mall = g.Mall; p.G = g.G; p.Narrow = g.Narrow;
            p.T.position = position;
            p.Rig.SetBag(false, Color.white);
            p.Rig.SetVisible(true);
            p.Rig.Play(CharacterAnim.Walk, 0f);
            p.Customer = false; p.Shop = null; p.LeaveAfter = false; p.Timer = 0f; p.Prev = -1; p.Path = null; p.PathIndex = 0;
            p.Speed = _rng.Range(1.1f, 1.5f);
            p.Lateral = _rng.Range(-0.8f, 0.8f);
            p.Phase = _rng.Range(0, 4);
            p.AnimAccum = 0f;
            _active.Add(p);
            Agents.All.Add(p.T);
            return p;
        }

        void Release(Ped p)
        {
            _active.Remove(p);
            Agents.All.Remove(p.T);
            p.Shop = null;
            p.Path = null;
            p.T.gameObject.SetActive(false);
            _pool.Push(p);
        }

        Graph RandomGraph()
        {
            if (_graphs.Count == 1) return _graphs[0];
            int total = 0;
            foreach (var g in _graphs) total += g.Spawn.Count;
            int r = _rng.Range(0, total);
            foreach (var g in _graphs) { if (r < g.Spawn.Count) return g; r -= g.Spawn.Count; }
            return _graphs[0];
        }

        int PickSpawnNode(Graph g, Vector3 cam, bool anywhere)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                // Чаще всего люди приходят через главный вход
                if (!anywhere && g.Exits.Count > 0 && _rng.Chance(0.35)) return g.Exits[_rng.Range(0, g.Exits.Count)];
                int n = g.Spawn[_rng.Range(0, g.Spawn.Count)];
                if (anywhere) return n;
                var d = W(g, n) - cam;
                // Новые люди появляются вне поля зрения: далеко или на другом этаже
                if (d.magnitude > 35f || Mathf.Abs(d.y) > 3f) return n;
            }
            return g.Spawn[_rng.Range(0, g.Spawn.Count)];
        }

        void SpawnWalker(Vector3 cam, bool anywhere)
        {
            if (_graphs.Count == 0) return;
            var g = RandomGraph();
            int node = PickSpawnNode(g, cam, anywhere);
            var p = Acquire(g, W(g, node));
            p.Node = node;
            StartNextRandomEdge(p);
        }

        Graph GraphOf(StorefrontView shop)
        {
            if (_shopGraph.TryGetValue(shop, out var g)) return g;
            var mall = shop.GetComponentInParent<MallInstance>();
            g = null;
            foreach (var gr in _graphs) if (gr.Mall == mall) g = gr;
            _shopGraph[shop] = g;
            return g;
        }

        bool SpawnCustomer(StorefrontView shop, Vector3 cam)
        {
            var g = GraphOf(shop);
            if (g == null) return false;
            int door = g.G.FindDoor(shop.PropertyId);
            if (door < 0) return false;
            int start = -1;
            var doorPos = W(g, door);
            for (int attempt = 0; attempt < 10; attempt++)
            {
                int n = PickSpawnNode(g, cam, false);
                float d = (W(g, n) - doorPos).magnitude;
                if (d > 12f && d < 150f) { start = n; break; }
            }
            if (start < 0) return false;
            var path = g.G.FindPath(start, door);
            if (path == null || path.Count < 2) return false;
            var p = Acquire(g, W(g, start));
            p.Customer = true;
            p.Shop = shop;
            p.Node = start;
            p.Path = path;
            p.PathIndex = 0;
            StartEdgeTo(p, path[1]);
            return true;
        }

        // ---------------- Движение по графу ----------------

        void StartEdgeTo(Ped p, int next)
        {
            p.From = p.T.position;
            var target = W(p, next);
            var dir = target - W(p, p.Node);
            dir.y = 0f;
            var side = dir.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, dir.normalized) : Vector3.zero;
            bool exact = p.G.Kinds[next] != PedNodeKind.Walk || p.Narrow[next] || p.Narrow[p.Node];
            p.To = target + (exact ? Vector3.zero : side * p.Lateral);
            p.Prev = p.Node;
            p.Node = next;
            BeginLeg(p, State.Walking, p.To);
        }

        void BeginLeg(Ped p, State state, Vector3 to)
        {
            p.State = state;
            p.From = p.T.position;
            p.To = to;
            p.Progress = 0f;
            p.SegLength = Mathf.Max(0.05f, Vector3.Distance(p.From, p.To));
        }

        void StartNextRandomEdge(Ped p)
        {
            var g = p.G;
            var edges = g.Neighbors(p.Node);
            if (edges.Count == 0) { Release(p); return; }
            int choice = -1;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int n = g.Other(edges[_rng.Range(0, edges.Count)], p.Node);
                if (n == p.Prev && edges.Count > 1) continue;
                // В магазины без цели не заходим; к выходу идут уходящие (и изредка просто так)
                if (g.Kinds[n] == PedNodeKind.Door && (g.NodeProperty[n] != -2 || !(p.LeaveAfter || _rng.Chance(0.08)))) continue;
                choice = n;
                break;
            }
            if (choice < 0) choice = p.Prev >= 0 ? p.Prev : g.Other(edges[0], p.Node);
            StartEdgeTo(p, choice);
        }

        /// <summary>Уходящий посетитель прокладывает путь к ближайшему выходу (не больше пары путей за кадр).</summary>
        bool TryHeadToExit(Ped p)
        {
            if (_pathBudget <= 0) return false;
            Graph gr = null;
            foreach (var x in _graphs) if (x.G == p.G) gr = x;
            if (gr == null || gr.Exits.Count == 0) return false;
            _pathBudget--;
            int best = -1;
            float bestD = float.MaxValue;
            var pos = p.G.Nodes[p.Node];
            foreach (int e in gr.Exits)
            {
                float d = (p.G.Nodes[e] - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = e; }
            }
            var path = p.G.FindPath(p.Node, best);
            if (path == null || path.Count < 2) return false;
            p.Path = path;
            p.PathIndex = 0;
            StartEdgeTo(p, path[1]);
            return true;
        }

        void ArriveAtNode(Ped p, Vector3 cam)
        {
            var g = p.G;
            var kind = g.Kinds[p.Node];
            if (p.Path != null)
            {
                p.PathIndex++;
                if (p.PathIndex < p.Path.Count - 1) { StartEdgeTo(p, p.Path[p.PathIndex + 1]); return; }
                p.Path = null;
                if (p.Customer)
                {
                    ArriveAtShop(p);
                    return;
                }
            }
            if (kind == PedNodeKind.Door && g.NodeProperty[p.Node] == -2 && (p.LeaveAfter || _rng.Chance(0.5))) { Release(p); return; }
            if (p.LeaveAfter)
            {
                var d = W(p, p.Node) - cam;
                if ((d.sqrMagnitude > 35f * 35f || Mathf.Abs(d.y) > 3f) && _rng.Chance(0.3)) { Release(p); return; }
                if (!DayNightCycle.MallOpen && TryHeadToExit(p)) return;
            }
            if (!p.LeaveAfter)
            {
                if (kind == PedNodeKind.Idle && _rng.Chance(0.6))
                {
                    p.State = State.Idle;
                    p.Timer = _rng.Range(4f, 12f);
                    p.T.rotation = p.Mall.transform.rotation * Quaternion.Euler(0, g.NodeYaw[p.Node], 0);
                    return;
                }
                if (kind == PedNodeKind.Seat && _rng.Chance(0.55))
                {
                    p.State = State.Sitting;
                    p.Timer = _rng.Range(12f, 40f);
                    p.T.position = W(p, p.Node);
                    p.T.rotation = p.Mall.transform.rotation * Quaternion.Euler(0, g.NodeYaw[p.Node], 0);
                    return;
                }
            }
            StartNextRandomEdge(p);
        }

        void ArriveAtShop(Ped p)
        {
            var shop = p.Shop;
            if (shop == null || !shop.AcceptsCustomers)
            {
                p.Customer = false;
                p.Shop = null;
                StartNextRandomEdge(p);
                return;
            }
            BeginLeg(p, State.GoIn, shop.DoorInside);
        }

        void EnterShop(Ped p)
        {
            var shop = p.Shop;
            if (shop == null) { FinishShop(p); return; }
            if (shop.CustomersStayOutside)
            {
                // Киоск или точка фудкорта: обслуживание у стойки
                p.State = State.Service;
                p.Timer = _rng.Range(5f, 12f);
                p.T.rotation = shop.transform.rotation;
                p.Rig.Play(CharacterAnim.Interact, 0.2f);
                return;
            }
            if (shop.InteriorActive && shop.Interior.CustomerPoints.Count > 0)
            {
                var cp = shop.Interior.CustomerPoints[_rng.Range(0, shop.Interior.CustomerPoints.Count)];
                p.InsideTarget = shop.Interior.transform.TransformPoint(new Vector3(cp.x, 0, cp.z));
                p.Timer = cp.w; // запоминаем направление взгляда у полки
                BeginLeg(p, State.GoInside, p.InsideTarget);
                return;
            }
            // Интерьер не активен (игрок далеко) — человек «внутри», невидим
            p.State = State.Hidden;
            p.Timer = _rng.Range(6f, 14f);
            p.Rig.SetVisible(false);
        }

        void FinishShop(Ped p)
        {
            var shop = p.Shop;
            var type = shop != null ? shop.BusinessType : BusinessType.None;
            var def = type != BusinessType.None ? EconomyCatalog.Get(type) : null;
            if (def != null && !def.IsFood && type != BusinessType.Warehouse)
            {
                p.Rig.SetBag(true, type == BusinessType.Supermarket ? new Color(0.95f, 0.95f, 0.9f) : Color.HSVToRGB(_rng.NextFloat(), 0.45f, 0.9f));
                if (type == BusinessType.ClothingStore) MallAudio.Instance?.Rustle(p.T.position);
            }
            p.Customer = false;
            p.LeaveAfter = true;
            int door = shop != null ? p.G.FindDoor(shop.PropertyId) : -1;
            p.Shop = null;
            if (door < 0) { Release(p); return; }
            p.Node = door;
            p.Prev = -1;
            StartNextRandomEdge(p);
        }

        // ---------------- Цикл ----------------

        void Update()
        {
            var game = CityGame.Instance;
            if (game == null || !game.Ready || _graphs.Count == 0) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            _frame++;
            _pathBudget = 2;
            var cam = game.ViewPosition;

            // Поток посетителей
            _spawnTimer -= dt;
            if (_spawnTimer <= 0f)
            {
                _spawnTimer = 0.6f;
                int walkers = 0;
                foreach (var p in _active) if (!p.Customer && !p.LeaveAfter) walkers++;
                int target = TargetWalkers(game);
                if (walkers < target && _active.Count < MaxPeds) SpawnWalker(cam, _active.Count == 0 && target > 6);
                else if (walkers > target + 4)
                    foreach (var p in _active) if (!p.Customer && !p.LeaveAfter) { p.LeaveAfter = true; break; }
            }

            // Покупатели — по статистике бизнеса из реплицированного состояния
            double now = game.State.TimeMinutes;
            if (_lastGameMinutes < 0) _lastGameMinutes = now;
            double gameDelta = now - _lastGameMinutes;
            _lastGameMinutes = now;
            if (gameDelta > 0 && gameDelta < 60)
            {
                int customers = 0;
                foreach (var p in _active) if (p.Customer) customers++;
                bool spawned = false;
                foreach (var view in StorefrontView.All)
                {
                    if (view == null || !view.AcceptsCustomers) continue;
                    var b = game.State.BusinessAt(view.PropertyId);
                    if (b == null) continue;
                    _customerAccum.TryGetValue(view, out float acc);
                    acc += (float)(b.LastHourCustomers * CustomerVisualFactor * gameDelta / 60.0);
                    // Не больше одного нового покупателя за кадр — поиск пути не создаёт пиков
                    if (acc >= 1f && !spawned && customers < MaxCustomers && _active.Count < MaxPeds && SpawnCustomer(view, cam))
                    {
                        acc -= 1f;
                        customers++;
                        spawned = true;
                    }
                    _customerAccum[view] = Mathf.Min(acc, 3f);
                }
            }

            int visibleCount = 0;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var p = _active[i];
                var d = p.T.position - cam;
                float dist = d.magnitude;
                bool sameFloor = Mathf.Abs(d.y) < 3f;
                // LOD: дальние и NPC других этажей (кроме видимых через атриум вблизи) скрыты
                bool visible = p.State != State.Hidden && (sameFloor ? dist < 110f : dist < 55f);
                p.Rig.SetVisible(visible);
                if (visible) visibleCount++;
                if ((_frame + p.Phase) % 8 == 0) p.Rig.SetShadows(sameFloor && dist < 22f);
                int every = dist < 20f ? 1 : dist < 50f ? 2 : 4;
                Step(p, dt, cam);
                if (i >= _active.Count || _active[i] != p) continue; // освобождён в Step
                p.AnimAccum = Mathf.Min(p.AnimAccum + dt, 0.5f);
                if (visible && (_frame + p.Phase) % every == 0)
                {
                    p.Rig.Tick(p.AnimAccum);
                    p.AnimAccum = 0f;
                }
            }
            VisibleCount = visibleCount;
        }

        void Step(Ped p, float dt, Vector3 cam)
        {
            var t = p.T;
            switch (p.State)
            {
                case State.Walking:
                case State.GoIn:
                case State.GoInside:
                case State.GoOut:
                case State.GoOutside:
                {
                    p.Progress += p.Speed * dt / p.SegLength;
                    t.position = Vector3.Lerp(p.From, p.To, Mathf.Clamp01(p.Progress));
                    var dir = p.To - p.From;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.001f) t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(dir), dt * 8f);
                    p.Rig.Play(CharacterAnim.Walk, 0.2f, p.Speed / 1.25f);
                    if (p.Progress < 1f) return;
                    switch (p.State)
                    {
                        case State.Walking: ArriveAtNode(p, cam); break;
                        case State.GoIn: EnterShop(p); break;
                        case State.GoInside:
                            p.State = State.Inside;
                            t.rotation = p.Shop != null && p.Shop.Interior != null ? p.Shop.Interior.transform.rotation * Quaternion.Euler(0, p.Timer, 0) : t.rotation;
                            p.Timer = _rng.Range(6f, 16f);
                            p.Rig.Play(_rng.Chance(0.5) ? CharacterAnim.Interact : CharacterAnim.Idle, 0.2f);
                            break;
                        case State.GoOut:
                            if (p.Shop != null) BeginLeg(p, State.GoOutside, p.Shop.DoorOutside); else FinishShop(p);
                            break;
                        default:
                            if (p.Shop != null && (t.position - cam).sqrMagnitude < 15f * 15f) MallAudio.Instance?.CashRegister(t.position);
                            FinishShop(p);
                            break;
                    }
                    return;
                }
                case State.Idle:
                    p.Rig.Play(CharacterAnim.Idle, 0.25f);
                    p.Timer -= dt;
                    if (p.Timer <= 0f) StartNextRandomEdge(p);
                    return;
                case State.Sitting:
                    p.Rig.Play(CharacterAnim.Sit, 0.3f);
                    p.Timer -= dt;
                    if (p.Timer <= 0f || !DayNightCycle.MallOpen) StartNextRandomEdge(p);
                    return;
                case State.Inside:
                case State.Service:
                    if (p.Rig.Current == CharacterAnim.Interact && p.Rig.OneShotFinished) p.Rig.Play(CharacterAnim.Idle, 0.25f);
                    p.Timer -= dt;
                    if (p.Timer > 0f) return;
                    if (p.State == State.Service || p.Shop == null) { BeginLeg(p, State.GoOutside, p.Shop != null ? p.Shop.DoorOutside : t.position); return; }
                    BeginLeg(p, State.GoOut, p.Shop.DoorInside);
                    return;
                case State.Hidden:
                    p.Timer -= dt;
                    if (p.Timer > 0f) return;
                    if (p.Shop == null) { FinishShop(p); return; }
                    t.position = p.Shop.DoorInside;
                    p.Rig.SetVisible(true);
                    BeginLeg(p, State.GoOutside, p.Shop.DoorOutside);
                    return;
            }
        }
    }
}

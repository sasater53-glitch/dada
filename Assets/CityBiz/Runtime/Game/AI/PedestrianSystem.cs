using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;

namespace CityBiz.Game
{
    /// <summary>
    /// Прохожие и покупатели. Прохожие гуляют по графу тротуаров, ждут зелёный на переходах, задерживаются
    /// у витрин, остановок и скамеек. Покупатели появляются с частотой, пропорциональной реальной статистике
    /// бизнеса из симуляции, заходят внутрь и выходят (из магазинов — с пакетами).
    /// Косметические NPC не синхронизируются по сети — каждый клиент генерирует их сам.
    /// </summary>
    public sealed class PedestrianSystem : MonoBehaviour
    {
        public static PedestrianSystem Instance { get; private set; }

        enum State { Walking, WaitSignal, Idle, Entering, Inside, Exiting, Hidden }

        sealed class Ped
        {
            public NpcBody Body;
            public State State;
            public List<int> Path;
            public int PathIndex;
            public int Node, Prev = -1;
            public Vector3 From, To;
            public float T, SegLength, Speed, Lateral, Timer;
            public bool Customer;
            public PropertyView Shop;
            public Vector3 InsideTarget;
            public bool LeaveAfter;
            public int Frame;
        }

        /// <summary>Сколько визуальных покупателей показывать на одного покупателя из экономики.</summary>
        public float CustomerVisualFactor = 0.18f;
        public float DensityMultiplier = 1f;

        DistrictInstance _district;
        PedestrianGraph _g;
        readonly List<Ped> _active = new List<Ped>();
        readonly Stack<Ped> _pool = new Stack<Ped>();
        readonly Dictionary<PropertyView, float> _customerAccum = new Dictionary<PropertyView, float>();
        readonly DeterministicRandom _rng = new DeterministicRandom(2025);
        readonly List<int> _spawnNodes = new List<int>();
        int _seed = 1, _frame;
        double _lastGameMinutes = -1;
        float _spawnTimer;

        public int ActiveCount => _active.Count;

        public void Init(DistrictInstance district)
        {
            Instance = this;
            _district = district;
            _g = district.Pedestrians;
            for (int i = 0; i < _g.Count; i++)
                if (_g.Kinds[i] == PedNodeKind.Walk || (_g.Kinds[i] == PedNodeKind.Door && _g.NodeProperty[i] == -2) || _g.Kinds[i] == PedNodeKind.BusStop)
                    _spawnNodes.Add(i);
            // Стартовая толпа, чтобы улица не была пустой с первого кадра
            for (int i = 0; i < 22; i++) SpawnWalker(true);
        }

        Vector3 W(int node) => _district.transform.TransformPoint(_g.Nodes[node]);

        public int CountNear(Vector3 p, float radius)
        {
            int n = 0;
            float r2 = radius * radius;
            foreach (var ped in _active)
                if (ped.State != State.Hidden && (ped.Body.transform.position - p).sqrMagnitude < r2) n++;
            return n;
        }

        int TargetWalkers()
        {
            float h = DayNightCycle.Hour;
            float density = h < 6f ? 0.15f : h < 8f ? 0.6f : h < 12f ? 0.9f : h < 19f ? 1f : h < 22f ? 0.7f : 0.3f;
            float weather = CityGame.Instance != null ? CityGame.Instance.State.Weather.FootTrafficMultiplier : 1f;
            return Mathf.RoundToInt(42 * density * weather * DensityMultiplier);
        }

        Ped Acquire(Vector3 position)
        {
            Ped p;
            if (_pool.Count > 0)
            {
                p = _pool.Pop();
                p.Body.gameObject.SetActive(true);
            }
            else
            {
                p = new Ped { Body = NpcBody.Create(transform, NpcLook.FromSeed(_seed++ * 7919)) };
            }
            p.Body.transform.position = position;
            p.Body.SetBag(false, Color.white);
            p.Body.SetVisible(true);
            p.Customer = false; p.Shop = null; p.LeaveAfter = false; p.Timer = 0f; p.Prev = -1; p.Path = null;
            p.Speed = _rng.Range(1.15f, 1.55f);
            p.Lateral = _rng.Range(-0.9f, 0.9f);
            p.Frame = _rng.Range(0, 4);
            _active.Add(p);
            Agents.All.Add(p.Body.transform);
            return p;
        }

        void Release(Ped p)
        {
            _active.Remove(p);
            Agents.All.Remove(p.Body.transform);
            p.Body.gameObject.SetActive(false);
            _pool.Push(p);
        }

        int PickSpawnNode(bool anywhere)
        {
            var cam = CityGame.Instance.ViewPosition;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                int n = _spawnNodes[_rng.Range(0, _spawnNodes.Count)];
                if (anywhere) return n;
                float d = (W(n) - cam).magnitude;
                // Новые люди появляются из подъездов/с остановок рядом или на тротуаре вдали от камеры
                if (_g.Kinds[n] != PedNodeKind.Walk || d > 30f) return n;
            }
            return _spawnNodes[_rng.Range(0, _spawnNodes.Count)];
        }

        void SpawnWalker(bool anywhere)
        {
            if (_spawnNodes.Count == 0) return;
            int node = PickSpawnNode(anywhere);
            var p = Acquire(W(node));
            p.Node = node;
            StartNextRandomEdge(p);
        }

        void SpawnCustomer(PropertyView shop)
        {
            int door = _g.FindDoor(shop.PropertyId);
            if (door < 0 || _spawnNodes.Count == 0) return;
            int start = -1;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                int n = PickSpawnNode(false);
                float d = (W(n) - W(door)).magnitude;
                if (d > 15f && d < 90f) { start = n; break; }
            }
            if (start < 0) return;
            var path = _g.FindPath(start, door);
            if (path == null || path.Count < 2) return;
            var p = Acquire(W(start));
            p.Customer = true;
            p.Shop = shop;
            p.Node = start;
            p.Path = path;
            p.PathIndex = 0;
            StartEdgeTo(p, path[1]);
        }

        // ---------- Перемещение по графу ----------

        void StartEdgeTo(Ped p, int next)
        {
            p.From = p.Body.transform.position;
            var target = W(next);
            var dir = target - W(p.Node);
            dir.y = 0f;
            var side = dir.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, dir.normalized) : Vector3.zero;
            bool door = _g.Kinds[next] != PedNodeKind.Walk;
            p.To = target + (door ? Vector3.zero : side * p.Lateral);
            p.Prev = p.Node;
            p.Node = next;
            p.T = 0f;
            p.SegLength = Mathf.Max(0.05f, Vector3.Distance(p.From, p.To));

            // Переход регулируется светофором: ждём на бордюре
            int signal = _g.SignalBetween(p.Prev, next);
            if (signal != PedestrianGraph.NoSignal && _g.Nodes[p.Prev].y > 0.07f && !TrafficSignals.PedestrianWalk(signal))
            {
                p.State = State.WaitSignal;
                p.Timer = 0f;
                return;
            }
            p.State = State.Walking;
        }

        void StartNextRandomEdge(Ped p)
        {
            var edges = _g.Neighbors(p.Node);
            if (edges.Count == 0) { Release(p); return; }
            int choice = -1;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int e = edges[_rng.Range(0, edges.Count)];
                int n = _g.Other(e, p.Node);
                if (n == p.Prev && edges.Count > 1) continue;
                // В магазины без цели не заходим; в подъезд — иногда (человек «пришёл домой»)
                if (_g.Kinds[n] == PedNodeKind.Door && (_g.NodeProperty[n] != -2 || !_rng.Chance(0.25))) continue;
                choice = n;
                break;
            }
            if (choice < 0) choice = p.Prev >= 0 ? p.Prev : _g.Other(edges[0], p.Node);
            StartEdgeTo(p, choice);
        }

        void ArriveAtNode(Ped p)
        {
            var kind = _g.Kinds[p.Node];
            if (p.Customer && p.Path != null)
            {
                p.PathIndex++;
                if (p.PathIndex < p.Path.Count - 1) { StartEdgeTo(p, p.Path[p.PathIndex + 1]); return; }
                // У двери магазина
                if (p.Shop != null && p.Shop.AcceptsCustomers)
                {
                    p.State = State.Entering;
                    p.From = p.Body.transform.position;
                    p.To = p.Shop.DoorInside;
                    p.T = 0f;
                    p.SegLength = Mathf.Max(0.1f, Vector3.Distance(p.From, p.To));
                    return;
                }
                p.Customer = false;
                p.Path = null;
            }
            if (kind == PedNodeKind.Door && _g.NodeProperty[p.Node] == -2) { Release(p); return; } // ушёл в подъезд
            if ((kind == PedNodeKind.Idle || kind == PedNodeKind.BusStop) && _rng.Chance(0.7))
            {
                p.State = State.Idle;
                p.Timer = kind == PedNodeKind.BusStop ? _rng.Range(10f, 35f) : _rng.Range(4f, 14f);
                p.Body.transform.rotation = _district.transform.rotation * Quaternion.Euler(0, _g.NodeYaw[p.Node], 0);
                return;
            }
            if (p.LeaveAfter && _rng.Chance(0.15) && (W(p.Node) - CityGame.Instance.ViewPosition).sqrMagnitude > 900f) { Release(p); return; }
            StartNextRandomEdge(p);
        }

        void Update()
        {
            var game = CityGame.Instance;
            if (game == null || !game.Ready || _district == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            _frame++;
            var cam = game.ViewPosition;

            // Поток прохожих
            _spawnTimer -= dt;
            if (_spawnTimer <= 0f)
            {
                _spawnTimer = 0.7f;
                int walkers = 0;
                foreach (var p in _active) if (!p.Customer) walkers++;
                if (walkers < TargetWalkers()) SpawnWalker(false);
                else if (walkers > TargetWalkers() + 6) foreach (var p in _active) if (!p.Customer) { p.LeaveAfter = true; }
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
                foreach (var view in PropertyView.All)
                {
                    if (!view.AcceptsCustomers) continue;
                    var b = game.State.BusinessAt(view.PropertyId);
                    if (b == null) continue;
                    _customerAccum.TryGetValue(view, out float acc);
                    acc += (float)(b.LastHourCustomers * CustomerVisualFactor * gameDelta / 60.0);
                    if (acc >= 1f && customers < 28)
                    {
                        acc -= 1f;
                        SpawnCustomer(view);
                        customers++;
                    }
                    _customerAccum[view] = Mathf.Min(acc, 3f);
                }
            }

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var p = _active[i];
                var t = p.Body.transform;
                float dist = (t.position - cam).magnitude;
                // LOD: дальние NPC скрыты и обновляются реже
                bool visible = dist < 170f && p.State != State.Hidden;
                p.Body.SetVisible(visible);
                p.Body.SetShadows(dist < 45f);
                bool animate = visible && (dist < 60f || (_frame + p.Frame) % 3 == 0);
                Step(p, dt, animate);
            }
        }

        void Step(Ped p, float dt, bool animate)
        {
            var t = p.Body.transform;
            switch (p.State)
            {
                case State.Walking:
                case State.Entering:
                case State.Exiting:
                {
                    p.T += p.Speed * dt / p.SegLength;
                    var pos = Vector3.Lerp(p.From, p.To, Mathf.Clamp01(p.T));
                    var dir = p.To - p.From;
                    dir.y = 0f;
                    t.position = pos;
                    if (dir.sqrMagnitude > 0.001f) t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(dir), dt * 8f);
                    if (animate) p.Body.Walk(p.Speed, dt);
                    if (p.T < 1f) return;
                    if (p.State == State.Walking) ArriveAtNode(p);
                    else if (p.State == State.Entering) EnterShop(p);
                    else ExitShop(p);
                    return;
                }
                case State.WaitSignal:
                {
                    if (animate) p.Body.Idle(dt);
                    var dir = p.To - p.From; dir.y = 0f;
                    if (dir.sqrMagnitude > 0.001f) t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(dir), dt * 4f);
                    int signal = _g.SignalBetween(p.Prev, p.Node);
                    p.Timer += dt;
                    if (signal == PedestrianGraph.NoSignal || TrafficSignals.PedestrianWalk(signal) || p.Timer > 90f) p.State = State.Walking;
                    return;
                }
                case State.Idle:
                {
                    if (animate) p.Body.Idle(dt);
                    p.Timer -= dt;
                    if (p.Timer <= 0f) StartNextRandomEdge(p);
                    return;
                }
                case State.Inside:
                case State.Hidden:
                {
                    if (animate && p.State == State.Inside) p.Body.Idle(dt);
                    p.Timer -= dt;
                    if (p.Timer > 0f) return;
                    p.State = State.Exiting;
                    p.Body.SetVisible(true);
                    p.From = t.position;
                    p.To = p.Shop != null ? p.Shop.DoorOutside : t.position;
                    p.T = 0f;
                    p.SegLength = Mathf.Max(0.1f, Vector3.Distance(p.From, p.To));
                    return;
                }
            }
        }

        void EnterShop(Ped p)
        {
            var shop = p.Shop;
            if (shop != null && shop.InteriorActive && shop.Interior.CustomerPoints.Count > 0)
            {
                var cp = shop.Interior.CustomerPoints[_rng.Range(0, shop.Interior.CustomerPoints.Count)];
                var target = shop.Interior.transform.TransformPoint(new Vector3(cp.x, 0, cp.z));
                p.Body.transform.position = Vector3.MoveTowards(p.Body.transform.position, target, 0.01f);
                p.State = State.Inside;
                p.Timer = _rng.Range(6f, 16f);
                p.InsideTarget = target;
                p.Body.transform.position = target;
                p.Body.transform.rotation = shop.Interior.transform.rotation * Quaternion.Euler(0, cp.w, 0);
            }
            else
            {
                p.State = State.Hidden;
                p.Timer = _rng.Range(6f, 14f);
                p.Body.SetVisible(false);
            }
        }

        void ExitShop(Ped p)
        {
            var type = p.Shop != null ? p.Shop.BusinessType : BusinessType.None;
            if (type == BusinessType.ClothingStore) p.Body.SetBag(true, Color.HSVToRGB(_rng.NextFloat(), 0.4f, 0.9f));
            if (type == BusinessType.Supermarket) p.Body.SetBag(true, new Color(0.95f, 0.95f, 0.9f));
            p.Customer = false;
            p.Path = null;
            p.LeaveAfter = true;
            int door = p.Shop != null ? _g.FindDoor(p.Shop.PropertyId) : -1;
            p.Shop = null;
            if (door < 0) { Release(p); return; }
            p.Node = door;
            p.Prev = -1;
            StartNextRandomEdge(p);
        }
    }
}

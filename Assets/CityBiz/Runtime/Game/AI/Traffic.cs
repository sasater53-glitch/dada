using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    public enum CarKind { Sedan, Hatchback, Suv, Van, Taxi, Bus }

    /// <summary>Процедурные автомобили: кузов одним мешем (краска, стёкла, фары, отделка) + 4 вращающихся колеса.</summary>
    public static class CarFactory
    {
        static readonly Dictionary<CarKind, Mesh> Bodies = new Dictionary<CarKind, Mesh>();
        static Mesh _wheel;

        static readonly Color[] Paints =
        {
            new Color(0.92f, 0.92f, 0.9f), new Color(0.65f, 0.67f, 0.7f), new Color(0.08f, 0.08f, 0.09f), new Color(0.12f, 0.2f, 0.38f),
            new Color(0.6f, 0.08f, 0.08f), new Color(0.35f, 0.36f, 0.38f), new Color(0.75f, 0.68f, 0.55f), new Color(0.15f, 0.3f, 0.22f),
        };

        public struct Dimensions { public float Length, Width, Height, WheelRadius, WheelBase; }

        public static Dimensions Size(CarKind k)
        {
            switch (k)
            {
                case CarKind.Hatchback: return new Dimensions { Length = 4.0f, Width = 1.75f, Height = 1.5f, WheelRadius = 0.31f, WheelBase = 2.5f };
                case CarKind.Suv: return new Dimensions { Length = 4.7f, Width = 1.9f, Height = 1.75f, WheelRadius = 0.37f, WheelBase = 2.8f };
                case CarKind.Van: return new Dimensions { Length = 5.2f, Width = 2.0f, Height = 2.3f, WheelRadius = 0.36f, WheelBase = 3.2f };
                case CarKind.Bus: return new Dimensions { Length = 11.5f, Width = 2.5f, Height = 3.1f, WheelRadius = 0.5f, WheelBase = 6.0f };
                default: return new Dimensions { Length = 4.6f, Width = 1.8f, Height = 1.45f, WheelRadius = 0.32f, WheelBase = 2.75f };
            }
        }

        static Mesh Body(CarKind kind)
        {
            if (Bodies.TryGetValue(kind, out var mesh)) return mesh;
            var d = Size(kind);
            var mb = new MeshBuilder();
            const int paint = 0, glass = 1, trim = 2, head = 3, tail = 4;
            float L = d.Length, W = d.Width, H = d.Height, r = d.WheelRadius;
            float sill = r * 0.9f;
            if (kind == CarKind.Bus)
            {
                mb.AddBox(paint, new Vector3(0, sill + (H - sill) * 0.5f, 0), new Vector3(W, H - sill, L));
                mb.AddBox(glass, new Vector3(0, H * 0.62f, 0.4f), new Vector3(W + 0.02f, H * 0.38f, L - 2.2f));
                mb.AddBox(glass, new Vector3(0, H * 0.55f, L * 0.5f + 0.01f), new Vector3(W - 0.3f, H * 0.55f, 0.02f));
                mb.AddBox(trim, new Vector3(0, sill + 0.35f, 0), new Vector3(W + 0.04f, 0.18f, L - 0.1f));
                mb.AddBox(head, new Vector3(0, H - 0.25f, L * 0.5f + 0.02f), new Vector3(1.6f, 0.3f, 0.02f)); // маршрутное табло
                foreach (float x in new[] { -W * 0.5f + 0.25f, W * 0.5f - 0.25f })
                {
                    mb.AddBox(head, new Vector3(x, sill + 0.35f, L * 0.5f + 0.01f), new Vector3(0.3f, 0.15f, 0.04f));
                    mb.AddBox(tail, new Vector3(x, sill + 0.5f, -L * 0.5f - 0.01f), new Vector3(0.25f, 0.4f, 0.04f));
                }
            }
            else
            {
                float bodyH = kind == CarKind.Van ? H - sill : (H - sill) * 0.52f;
                mb.AddBox(paint, new Vector3(0, sill + bodyH * 0.5f, 0), new Vector3(W, bodyH, L));
                if (kind != CarKind.Van)
                {
                    float cabinH = H - sill - bodyH;
                    float cabinL = kind == CarKind.Hatchback || kind == CarKind.Suv ? L * 0.58f : L * 0.48f;
                    float cabinZ = kind == CarKind.Hatchback || kind == CarKind.Suv ? -L * 0.12f : -L * 0.04f;
                    mb.AddBox(glass, new Vector3(0, sill + bodyH + cabinH * 0.45f, cabinZ), new Vector3(W - 0.14f, cabinH * 0.9f, cabinL));
                    mb.AddBox(paint, new Vector3(0, H - 0.04f, cabinZ - 0.05f), new Vector3(W - 0.12f, 0.08f, cabinL - 0.3f));
                    foreach (float x in new[] { -W * 0.5f + 0.09f, W * 0.5f - 0.09f })
                    {
                        mb.AddBox(paint, new Vector3(x, sill + bodyH + cabinH * 0.45f, cabinZ + cabinL * 0.5f - 0.05f), new Vector3(0.06f, cabinH * 0.9f, 0.1f));
                        mb.AddBox(paint, new Vector3(x, sill + bodyH + cabinH * 0.45f, cabinZ - cabinL * 0.5f + 0.05f), new Vector3(0.06f, cabinH * 0.9f, 0.1f));
                        mb.AddBox(trim, new Vector3(x + Mathf.Sign(x) * 0.08f, sill + bodyH + 0.1f, cabinZ + cabinL * 0.5f - 0.2f), new Vector3(0.1f, 0.1f, 0.15f)); // зеркала
                    }
                    if (kind == CarKind.Taxi) mb.AddBox(head, new Vector3(0, H + 0.08f, cabinZ), new Vector3(0.5f, 0.15f, 0.25f));
                }
                else
                {
                    mb.AddBox(glass, new Vector3(0, H * 0.72f, L * 0.5f - 0.02f), new Vector3(W - 0.2f, H * 0.32f, 0.06f));
                    foreach (float x in new[] { -W * 0.5f, W * 0.5f })
                        mb.AddBox(glass, new Vector3(x, H * 0.72f, L * 0.32f), new Vector3(0.02f, H * 0.3f, 0.9f));
                }
                // Бамперы, фары, фонари, решётка радиатора
                mb.AddBox(trim, new Vector3(0, sill + 0.08f, L * 0.5f + 0.03f), new Vector3(W, 0.22f, 0.12f));
                mb.AddBox(trim, new Vector3(0, sill + 0.08f, -L * 0.5f - 0.03f), new Vector3(W, 0.22f, 0.12f));
                mb.AddBox(trim, new Vector3(0, sill + bodyH * 0.55f, L * 0.5f + 0.01f), new Vector3(W * 0.4f, 0.16f, 0.03f));
                foreach (float x in new[] { -W * 0.5f + 0.25f, W * 0.5f - 0.25f })
                {
                    mb.AddBox(head, new Vector3(x, sill + bodyH * 0.6f, L * 0.5f + 0.01f), new Vector3(0.36f, 0.14f, 0.04f));
                    mb.AddBox(tail, new Vector3(x, sill + bodyH * 0.65f, -L * 0.5f - 0.01f), new Vector3(0.34f, 0.14f, 0.04f));
                }
            }
            // Колёсные арки (тёмные)
            foreach (float z in new[] { -d.WheelBase * 0.5f, d.WheelBase * 0.5f })
                foreach (float x in new[] { -W * 0.5f + 0.005f, W * 0.5f - 0.005f })
                    mb.AddBox(trim, new Vector3(x, r, z), new Vector3(0.02f, r * 1.6f, r * 2.3f));
            mesh = mb.Build("Car_" + kind);
            Bodies[kind] = mesh;
            return mesh;
        }

        static Mesh Wheel()
        {
            if (_wheel != null) return _wheel;
            var mb = new MeshBuilder();
            mb.AddCylinder(0, new Vector3(0, -0.11f, 0), 1f, 0.22f, 14);
            mb.AddCylinder(1, new Vector3(0, 0.111f, 0), 0.55f, 0.01f, 10);
            mb.AddCylinder(1, new Vector3(0, -0.121f, 0), 0.55f, 0.01f, 10);
            _wheel = mb.Build("Wheel");
            return _wheel;
        }

        public static Material[] BodyMaterials(CarKind kind, Color paint)
        {
            var M = MaterialLibrary.Instance;
            var paintMat = kind == CarKind.Taxi ? M.Paint("carpaint", new Color(0.95f, 0.78f, 0.1f), null, 1f, 0.75f)
                : M.Lit("carpaint_" + MaterialLibrary.Hex(paint), paint, null, 1f, 0.75f, 0.35f);
            return new[] { paintMat, M.CarGlass, M.Dark, M.Headlight, M.Taillight };
        }

        public static CarVisual Create(Transform parent, CarKind kind, Color paint)
        {
            var d = Size(kind);
            var go = new GameObject("Car_" + kind);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Body(kind);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = BodyMaterials(kind, paint);
            var wheels = new Transform[4];
            var M = MaterialLibrary.Instance;
            int i = 0;
            foreach (float z in new[] { -d.WheelBase * 0.5f, d.WheelBase * 0.5f })
                foreach (float x in new[] { -d.Width * 0.5f + 0.15f, d.Width * 0.5f - 0.15f })
                {
                    var pivot = new GameObject("Wheel").transform;
                    pivot.SetParent(go.transform, false);
                    pivot.localPosition = new Vector3(x, d.WheelRadius, z);
                    var w = new GameObject("Mesh");
                    w.transform.SetParent(pivot, false);
                    w.transform.localRotation = Quaternion.Euler(0, 0, 90);
                    w.transform.localScale = new Vector3(d.WheelRadius, 1f, d.WheelRadius);
                    w.AddComponent<MeshFilter>().sharedMesh = Wheel();
                    var wr = w.AddComponent<MeshRenderer>();
                    wr.sharedMaterials = new[] { M.Tire, M.Aluminium };
                    wheels[i++] = pivot;
                }
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0, d.Height * 0.5f, 0);
            col.size = new Vector3(d.Width, d.Height, d.Length);
            var visual = go.AddComponent<CarVisual>();
            visual.Init(wheels, d, mr);
            return visual;
        }

        public static void CreateParked(Transform parent, Vector3 localPos, float yaw, int seed)
        {
            var rng = new DeterministicRandom((ulong)seed + 99);
            var kind = (CarKind)rng.Range(0, 4);
            var car = Create(parent, kind, Paints[rng.Range(0, Paints.Length)]);
            car.transform.localPosition = localPos;
            car.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            car.enabled = false;
        }

        public static Color RandomPaint(DeterministicRandom rng) => Paints[rng.Range(0, Paints.Length)];
    }

    public enum SignalState { Red, Yellow, Green }

    /// <summary>
    /// Цикл светофорного объекта. Работает в реальном времени (не ускоряется вместе с игровыми часами).
    /// Пешеходный сигнал 0 — переход через поперечную улицу, 1 — через главную.
    /// </summary>
    public static class TrafficSignals
    {
        static readonly float[] Phases = { 22f, 3f, 2f, 14f, 3f, 2f };
        static float _t;
        static int _phase;

        public static void Tick(float dt)
        {
            _t += dt;
            while (_t >= Phases[_phase]) { _t -= Phases[_phase]; _phase = (_phase + 1) % Phases.Length; }
        }

        public static SignalState Vehicle(int group)
        {
            int green = group == 0 ? 0 : 3;
            if (_phase == green) return SignalState.Green;
            if (_phase == green + 1) return SignalState.Yellow;
            return SignalState.Red;
        }

        public static bool PedestrianWalk(int signal) => signal == 0 ? _phase == 0 : _phase == 3;
    }

    /// <summary>
    /// Дорожное движение по полосам района: держат дистанцию, останавливаются на красный, тормозят перед игроком,
    /// автобус стоит на остановках. Количество машин зависит от времени суток и погоды.
    /// </summary>
    public sealed class TrafficSystem : MonoBehaviour
    {
        sealed class Agent
        {
            public CarVisual Car;
            public int Lane;
            public float Distance, Speed, MaxSpeed, Length;
            public bool IsBus;
            public float DwellTimer;
            public int LastStopServed = -1;
            public bool Despawn;
        }

        DistrictInstance _district;
        readonly List<Agent> _agents = new List<Agent>();
        readonly DeterministicRandom _rng = new DeterministicRandom(31337);
        float _spawnTimer;
        public int ActiveCars => _agents.Count;
        public IReadOnlyList<Transform> CarTransforms => _carTransforms;
        readonly List<Transform> _carTransforms = new List<Transform>();

        public void Init(DistrictInstance district)
        {
            _district = district;
            // Автобус на маршруте и стартовый поток
            Spawn(0, 60f, CarKind.Bus);
            for (int i = 0; i < 8; i++) TrySpawnRandom(true);
        }

        int TargetCount()
        {
            float hour = DayNightCycle.Hour;
            float density = hour < 6f ? 0.25f : hour < 8f ? 0.8f : hour < 10f ? 1f : hour < 17f ? 0.75f : hour < 20f ? 1f : hour < 23f ? 0.55f : 0.3f;
            if (WeatherController.CurrentKind == WeatherKind.Storm) density *= 0.7f;
            return Mathf.RoundToInt(18 * density);
        }

        void TrySpawnRandom(bool anywhere)
        {
            if (_district == null || _district.Lanes.Count == 0) return;
            int lane = _rng.Range(0, _district.Lanes.Count);
            float dist = anywhere ? _rng.Range(10f, _district.Lanes[lane].Length - 20f) : 0f;
            if (!IsClear(lane, dist, 14f)) return;
            float roll = _rng.NextFloat();
            var kind = roll < 0.45f ? CarKind.Sedan : roll < 0.7f ? CarKind.Hatchback : roll < 0.85f ? CarKind.Suv : roll < 0.93f ? CarKind.Van : CarKind.Taxi;
            Spawn(lane, dist, kind);
        }

        bool IsClear(int lane, float dist, float margin)
        {
            foreach (var a in _agents)
                if (a.Lane == lane && Mathf.Abs(a.Distance - dist) < margin) return false;
            return true;
        }

        void Spawn(int lane, float dist, CarKind kind)
        {
            var car = CarFactory.Create(transform, kind, CarFactory.RandomPaint(_rng));
            var rb = car.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            var a = new Agent
            {
                Car = car, Lane = lane, Distance = dist, Length = car.Size.Length, IsBus = kind == CarKind.Bus,
                MaxSpeed = _district.Lanes[lane].SpeedLimit * (kind == CarKind.Bus ? 0.8f : _rng.Range(0.85f, 1.1f)),
            };
            a.Speed = a.MaxSpeed * 0.7f;
            if (a.IsBus)
                WorldText.Create(car.transform, "Route", "14  Липовая — Садовая", new Vector3(0, car.Size.Height - 0.25f, car.Size.Length * 0.5f + 0.04f), 0.18f, new Color(1f, 0.6f, 0.1f), 1.5f, TextAnchor.MiddleCenter, 180f);
            _agents.Add(a);
            _carTransforms.Add(car.transform);
            Place(a);
        }

        void Place(Agent a)
        {
            var lane = _district.Lanes[a.Lane];
            var p = lane.Evaluate(a.Distance, out var fwd);
            a.Car.transform.SetPositionAndRotation(_district.transform.TransformPoint(p), _district.transform.rotation * Quaternion.LookRotation(fwd, Vector3.up));
        }

        void Update()
        {
            if (_district == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            TrafficSignals.Tick(dt);
            foreach (var tl in _district.TrafficLights)
                if (tl != null) tl.Show(TrafficSignals.Vehicle(tl.VehicleGroup), TrafficSignals.PedestrianWalk(1 - tl.VehicleGroup));

            _spawnTimer -= dt;
            if (_spawnTimer <= 0f)
            {
                _spawnTimer = 1.5f;
                if (_agents.Count < TargetCount()) TrySpawnRandom(false);
            }

            var player = PlayerController.Instance != null ? PlayerController.Instance.transform.position : new Vector3(9999, 0, 9999);
            for (int i = _agents.Count - 1; i >= 0; i--)
            {
                var a = _agents[i];
                var lane = _district.Lanes[a.Lane];
                float gap = 999f;
                foreach (var o in _agents)
                {
                    if (o == a || o.Lane != a.Lane || o.Distance <= a.Distance) continue;
                    gap = Mathf.Min(gap, o.Distance - a.Distance - (o.Length + a.Length) * 0.5f);
                }
                // Стоп-линии
                foreach (var stop in lane.Stops)
                {
                    float toStop = stop.Distance - (a.Distance + a.Length * 0.5f);
                    if (toStop < -0.5f || toStop > 40f) continue;
                    var signal = TrafficSignals.Vehicle(stop.SignalGroup);
                    bool mustStop = signal == SignalState.Red || (signal == SignalState.Yellow && toStop > a.Speed * 0.9f);
                    if (mustStop) gap = Mathf.Min(gap, toStop);
                }
                // Остановки автобуса
                if (a.IsBus)
                {
                    for (int s = 0; s < _district.BusStops.Count; s++)
                    {
                        var stop = _district.BusStops[s];
                        if (stop.Lane != a.Lane || s == a.LastStopServed) continue;
                        float toStop = stop.Distance - a.Distance;
                        if (toStop < 0f || toStop > 35f) continue;
                        gap = Mathf.Min(gap, toStop);
                        if (toStop < 0.6f && a.Speed < 0.3f)
                        {
                            a.DwellTimer += dt;
                            if (a.DwellTimer > 8f) { a.LastStopServed = s; a.DwellTimer = 0f; }
                        }
                    }
                }
                // Игрок на проезжей части впереди
                var pos = a.Car.transform.position;
                var fwd = a.Car.transform.forward;
                var toPlayer = player - pos;
                float ahead = Vector3.Dot(toPlayer, fwd);
                if (ahead > 0f && ahead < 18f && Mathf.Abs(Vector3.Dot(toPlayer, a.Car.transform.right)) < a.Car.Size.Width * 0.5f + 0.8f)
                    gap = Mathf.Min(gap, ahead - a.Length * 0.5f - 1.5f);

                float safe = gap < 998f ? Mathf.Sqrt(Mathf.Max(0f, 2f * 5f * (gap - 2f))) : a.MaxSpeed;
                float target = Mathf.Min(a.MaxSpeed, safe);
                a.Speed = target > a.Speed ? Mathf.MoveTowards(a.Speed, target, 2.2f * dt) : Mathf.MoveTowards(a.Speed, target, 7f * dt);
                if (gap < 0.5f) a.Speed = Mathf.Min(a.Speed, 0f);
                a.Distance += a.Speed * dt;

                if (a.Distance >= lane.Length)
                {
                    // Конец полосы далеко за пределами игровой зоны: разворот на встречную или удаление
                    if (!a.IsBus && _agents.Count > TargetCount()) { Remove(i); continue; }
                    a.Lane = lane.NextLane >= 0 ? lane.NextLane : a.Lane;
                    a.Distance = 0f;
                    a.LastStopServed = -1;
                    if (!IsClear(a.Lane, 0f, a.Length + 2f)) a.Distance = -a.Length - 4f;
                }
                if (a.Distance >= 0f) Place(a);
                a.Car.Spin(a.Speed, dt);
            }
        }

        void Remove(int index)
        {
            var a = _agents[index];
            _carTransforms.Remove(a.Car.transform);
            Destroy(a.Car.gameObject);
            _agents.RemoveAt(index);
        }
    }
}

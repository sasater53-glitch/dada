using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBiz.Game
{
    public enum PedNodeKind : byte { Walk, Door, BusStop, Idle }

    /// <summary>
    /// Навигационный граф пешеходов (тротуары, переходы, дорожки сквера, входы в здания).
    /// Сериализуется в сцену при запекании. Рёбра-переходы привязаны к фазе светофора.
    /// </summary>
    [Serializable]
    public sealed class PedestrianGraph
    {
        public const int NoSignal = -1;

        public List<Vector3> Nodes = new List<Vector3>();
        public List<PedNodeKind> Kinds = new List<PedNodeKind>();
        public List<int> NodeProperty = new List<int>();   // id объекта недвижимости для дверей
        public List<float> NodeYaw = new List<float>();     // направление «взгляда» для точек ожидания
        public List<int> EdgeA = new List<int>(), EdgeB = new List<int>(), EdgeSignal = new List<int>();

        [NonSerialized] List<int>[] _adjacency;

        public int Count => Nodes.Count;

        public int AddNode(Vector3 p, PedNodeKind kind = PedNodeKind.Walk, int property = -1, float yaw = 0f, bool merge = true)
        {
            if (merge && kind == PedNodeKind.Walk)
                for (int i = 0; i < Nodes.Count; i++)
                    if (Kinds[i] == PedNodeKind.Walk && (Nodes[i] - p).sqrMagnitude < 0.5f) return i;
            Nodes.Add(p); Kinds.Add(kind); NodeProperty.Add(property); NodeYaw.Add(yaw);
            _adjacency = null;
            return Nodes.Count - 1;
        }

        public void AddEdge(int a, int b, int signal = NoSignal)
        {
            if (a == b || a < 0 || b < 0) return;
            for (int i = 0; i < EdgeA.Count; i++)
                if ((EdgeA[i] == a && EdgeB[i] == b) || (EdgeA[i] == b && EdgeB[i] == a)) return;
            EdgeA.Add(a); EdgeB.Add(b); EdgeSignal.Add(signal);
            _adjacency = null;
        }

        /// <summary>Ломаная с разбиением на узлы через spacing метров. Возвращает индексы узлов.</summary>
        public List<int> AddPolyline(IList<Vector3> points, float spacing = 7f)
        {
            var result = new List<int>();
            int prev = -1;
            for (int s = 0; s < points.Count - 1; s++)
            {
                var a = points[s];
                var b = points[s + 1];
                int steps = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(a, b) / spacing));
                for (int i = (s == 0 ? 0 : 1); i <= steps; i++)
                {
                    int n = AddNode(Vector3.Lerp(a, b, i / (float)steps));
                    if (prev >= 0) AddEdge(prev, n);
                    result.Add(n);
                    prev = n;
                }
            }
            return result;
        }

        public void Link(Vector3 a, Vector3 b, int signal = NoSignal) => AddEdge(AddNode(a), AddNode(b), signal);

        /// <summary>Соединяет узел с ближайшим обычным узлом графа, с которым он ещё не связан.</summary>
        public void LinkToNearest(int node, float maxDistance = 12f)
        {
            var neighbors = new HashSet<int>();
            for (int e = 0; e < EdgeA.Count; e++)
            {
                if (EdgeA[e] == node) neighbors.Add(EdgeB[e]);
                if (EdgeB[e] == node) neighbors.Add(EdgeA[e]);
            }
            int best = -1;
            float bestD = maxDistance * maxDistance;
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (i == node || Kinds[i] != PedNodeKind.Walk || neighbors.Contains(i)) continue;
                float d = (Nodes[i] - Nodes[node]).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best >= 0) AddEdge(node, best);
        }

        public int Nearest(Vector3 p, PedNodeKind kind, int exclude = -1)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (i == exclude || Kinds[i] != kind) continue;
                float d = (Nodes[i] - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public int FindDoor(int propertyId)
        {
            for (int i = 0; i < Nodes.Count; i++)
                if (Kinds[i] == PedNodeKind.Door && NodeProperty[i] == propertyId) return i;
            return -1;
        }

        public List<int> Neighbors(int node)
        {
            if (_adjacency == null) BuildAdjacency();
            return _adjacency[node];
        }

        void BuildAdjacency()
        {
            _adjacency = new List<int>[Nodes.Count];
            for (int i = 0; i < Nodes.Count; i++) _adjacency[i] = new List<int>(4);
            for (int e = 0; e < EdgeA.Count; e++)
            {
                _adjacency[EdgeA[e]].Add(e);
                _adjacency[EdgeB[e]].Add(e);
            }
        }

        public int Other(int edge, int node) => EdgeA[edge] == node ? EdgeB[edge] : EdgeA[edge];

        /// <summary>A* по графу. Возвращает список узлов от start до goal (включительно) или null.</summary>
        public List<int> FindPath(int start, int goal)
        {
            if (start < 0 || goal < 0) return null;
            if (_adjacency == null) BuildAdjacency();
            var open = new List<int> { start };
            var came = new Dictionary<int, int>();
            var g = new Dictionary<int, float> { [start] = 0f };
            var closed = new HashSet<int>();
            while (open.Count > 0)
            {
                int bestIdx = 0;
                float bestF = float.MaxValue;
                for (int i = 0; i < open.Count; i++)
                {
                    float f = g[open[i]] + Vector3.Distance(Nodes[open[i]], Nodes[goal]);
                    if (f < bestF) { bestF = f; bestIdx = i; }
                }
                int cur = open[bestIdx];
                if (cur == goal)
                {
                    var path = new List<int> { cur };
                    while (came.TryGetValue(cur, out int p)) { cur = p; path.Add(cur); }
                    path.Reverse();
                    return path;
                }
                open.RemoveAt(bestIdx);
                closed.Add(cur);
                foreach (int e in _adjacency[cur])
                {
                    int n = Other(e, cur);
                    if (closed.Contains(n)) continue;
                    // Двери чужих зданий не используем как транзит.
                    if (Kinds[n] == PedNodeKind.Door && n != goal) continue;
                    float cost = g[cur] + Vector3.Distance(Nodes[cur], Nodes[n]);
                    if (g.TryGetValue(n, out float old) && old <= cost) continue;
                    g[n] = cost;
                    came[n] = cur;
                    if (!open.Contains(n)) open.Add(n);
                }
            }
            return null;
        }

        public int SignalBetween(int a, int b)
        {
            foreach (int e in Neighbors(a))
                if (Other(e, a) == b) return EdgeSignal[e];
            return NoSignal;
        }
    }
}

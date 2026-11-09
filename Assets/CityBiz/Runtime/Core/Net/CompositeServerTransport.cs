using System;
using System.Collections.Generic;

namespace CityBiz.Core.Net
{
    /// <summary>
    /// Объединяет несколько серверных транспортов: хост играет через Loopback,
    /// а игроки из локальной сети — через TCP, в одной и той же симуляции.
    /// Идентификаторы соединений у транспортов не пересекаются (Loopback — с 1, TCP — с 1001).
    /// </summary>
    public sealed class CompositeServerTransport : IServerTransport
    {
        readonly IServerTransport[] _parts;
        readonly Dictionary<int, IServerTransport> _owner = new Dictionary<int, IServerTransport>();

        public event Action<int> ClientConnected;
        public event Action<int> ClientDisconnected;
        public event Action<int, ArraySegment<byte>> DataReceived;

        public CompositeServerTransport(params IServerTransport[] parts)
        {
            _parts = parts;
            foreach (var part in parts)
            {
                var p = part;
                p.ClientConnected += id => { _owner[id] = p; ClientConnected?.Invoke(id); };
                p.ClientDisconnected += id => { _owner.Remove(id); ClientDisconnected?.Invoke(id); };
                p.DataReceived += (id, data) => { _owner[id] = p; DataReceived?.Invoke(id, data); };
            }
        }

        public void Send(int connectionId, ArraySegment<byte> data, bool reliable)
        {
            if (_owner.TryGetValue(connectionId, out var t)) t.Send(connectionId, data, reliable);
        }

        public void Poll()
        {
            foreach (var p in _parts) p.Poll();
        }
    }
}

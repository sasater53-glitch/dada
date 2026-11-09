using System;
using System.Collections.Generic;

namespace CityBiz.Core.Net
{
    public enum MsgType : byte
    {
        Hello = 1,        // C→S: имя, цвет
        Welcome,          // S→C: id игрока, id хоста
        Snapshot,         // S→C: полное состояние (при подключении)
        Delta,            // S→C: изменившиеся сущности
        Command,          // C→S: seq + команда
        CommandResult,    // S→C: seq + результат
        News,             // S→C: событие ленты
        PoseUpdate,       // C→S и S→C: позиция аватара (ненадёжно)
        PlayerLeft,       // S→C
    }

    /// <summary>Транспорт со стороны сервера. Реализации: Loopback (хост), NGO, любой UDP/WebSocket.</summary>
    public interface IServerTransport
    {
        event Action<int> ClientConnected;
        event Action<int> ClientDisconnected;
        event Action<int, ArraySegment<byte>> DataReceived;
        void Send(int connectionId, ArraySegment<byte> data, bool reliable);
        void Poll();
    }

    public interface IClientTransport
    {
        event Action Connected;
        event Action Disconnected;
        event Action<ArraySegment<byte>> DataReceived;
        bool IsConnected { get; }
        void Send(ArraySegment<byte> data, bool reliable);
        void Poll();
    }

    /// <summary>
    /// Внутрипроцессный транспорт для хоста и одиночной игры: тот же протокол и сериализация,
    /// что и по сети, поэтому одиночный режим проверяет сетевой путь целиком.
    /// </summary>
    public sealed class LoopbackHub : IServerTransport
    {
        readonly Dictionary<int, LoopbackClient> _clients = new Dictionary<int, LoopbackClient>();
        readonly Queue<(int conn, byte[] data)> _toServer = new Queue<(int, byte[])>();
        readonly Queue<int> _pendingConnects = new Queue<int>();
        int _nextId = 1;

        public long BytesToClients { get; private set; }
        public long BytesToServer { get; private set; }

        public event Action<int> ClientConnected;
        public event Action<int> ClientDisconnected;
        public event Action<int, ArraySegment<byte>> DataReceived;

        public LoopbackClient CreateClient()
        {
            var c = new LoopbackClient(this, _nextId++);
            _clients[c.ConnectionId] = c;
            _pendingConnects.Enqueue(c.ConnectionId);
            return c;
        }

        internal void EnqueueToServer(int conn, ArraySegment<byte> data)
        {
            var copy = new byte[data.Count];
            Buffer.BlockCopy(data.Array, data.Offset, copy, 0, data.Count);
            BytesToServer += copy.Length;
            _toServer.Enqueue((conn, copy));
        }

        internal void Disconnect(int conn)
        {
            if (_clients.Remove(conn)) ClientDisconnected?.Invoke(conn);
        }

        public void Send(int connectionId, ArraySegment<byte> data, bool reliable)
        {
            if (!_clients.TryGetValue(connectionId, out var c)) return;
            var copy = new byte[data.Count];
            Buffer.BlockCopy(data.Array, data.Offset, copy, 0, data.Count);
            BytesToClients += copy.Length;
            c.Inbox.Enqueue(copy);
        }

        public void Poll()
        {
            while (_pendingConnects.Count > 0)
            {
                int id = _pendingConnects.Dequeue();
                if (_clients.TryGetValue(id, out var c))
                {
                    c.MarkConnected();
                    ClientConnected?.Invoke(id);
                }
            }
            while (_toServer.Count > 0)
            {
                var (conn, data) = _toServer.Dequeue();
                DataReceived?.Invoke(conn, new ArraySegment<byte>(data));
            }
        }
    }

    public sealed class LoopbackClient : IClientTransport
    {
        readonly LoopbackHub _hub;
        internal readonly Queue<byte[]> Inbox = new Queue<byte[]>();
        bool _connected, _connectRaised;

        public int ConnectionId { get; }
        public bool IsConnected => _connected;

        public event Action Connected;
        public event Action Disconnected;
        public event Action<ArraySegment<byte>> DataReceived;

        internal LoopbackClient(LoopbackHub hub, int id)
        {
            _hub = hub;
            ConnectionId = id;
        }

        internal void MarkConnected() => _connected = true;

        public void Send(ArraySegment<byte> data, bool reliable)
        {
            if (_connected) _hub.EnqueueToServer(ConnectionId, data);
        }

        public void Disconnect()
        {
            if (!_connected) return;
            _connected = false;
            _hub.Disconnect(ConnectionId);
            Disconnected?.Invoke();
        }

        public void Poll()
        {
            if (_connected && !_connectRaised) { _connectRaised = true; Connected?.Invoke(); }
            while (Inbox.Count > 0) DataReceived?.Invoke(new ArraySegment<byte>(Inbox.Dequeue()));
        }
    }

    /// <summary>Сжатая поза аватара: сантиметры в varint и угол в одном байте.</summary>
    public struct PlayerPose
    {
        public int PlayerId;
        public float X, Y, Z, Yaw, Speed;

        public void Write(NetWriter w)
        {
            w.WriteInt(PlayerId);
            w.WriteInt((int)Math.Round(X * 100f)); w.WriteInt((int)Math.Round(Y * 100f)); w.WriteInt((int)Math.Round(Z * 100f));
            w.WriteByte((byte)((int)Math.Round(Yaw / 360f * 256f) & 0xFF));
            w.WriteByte((byte)Math.Min(255, (int)(Speed * 20f)));
        }

        public static PlayerPose Read(NetReader r) => new PlayerPose
        {
            PlayerId = r.ReadInt(), X = r.ReadInt() / 100f, Y = r.ReadInt() / 100f, Z = r.ReadInt() / 100f,
            Yaw = r.ReadByte() * 360f / 256f, Speed = r.ReadByte() / 20f,
        };
    }
}

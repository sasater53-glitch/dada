using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace CityBiz.Core.Net
{
    /// <summary>
    /// Простой TCP-транспорт для игры по локальной сети без сторонних пакетов.
    /// Кадры: 4 байта длины + данные. Сетевые потоки только кладут сообщения в очередь,
    /// вся обработка происходит в Poll() на главном потоке.
    /// </summary>
    public sealed class TcpServerTransport : IServerTransport, IDisposable
    {
        sealed class Conn
        {
            public int Id;
            public TcpClient Client;
            public NetworkStream Stream;
            public readonly object SendLock = new object();
        }

        readonly TcpListener _listener;
        readonly ConcurrentQueue<(int conn, byte[] data)> _inbox = new ConcurrentQueue<(int, byte[])>();
        readonly ConcurrentQueue<int> _connected = new ConcurrentQueue<int>();
        readonly ConcurrentQueue<int> _disconnected = new ConcurrentQueue<int>();
        readonly ConcurrentDictionary<int, Conn> _conns = new ConcurrentDictionary<int, Conn>();
        int _nextId;
        volatile bool _running;

        public const int MaxFrame = 4 * 1024 * 1024;

        public event Action<int> ClientConnected;
        public event Action<int> ClientDisconnected;
        public event Action<int, ArraySegment<byte>> DataReceived;

        public int Port { get; }

        public TcpServerTransport(int port)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _running = true;
            var t = new Thread(AcceptLoop) { IsBackground = true, Name = "CityBiz TCP accept" };
            t.Start();
        }

        void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch { break; }
                client.NoDelay = true;
                var conn = new Conn { Id = Interlocked.Increment(ref _nextId) + 1000, Client = client, Stream = client.GetStream() };
                _conns[conn.Id] = conn;
                _connected.Enqueue(conn.Id);
                var t = new Thread(() => ReadLoop(conn)) { IsBackground = true, Name = "CityBiz TCP read " + conn.Id };
                t.Start();
            }
        }

        void ReadLoop(Conn conn)
        {
            try
            {
                while (_running)
                {
                    var frame = TcpFraming.ReadFrame(conn.Stream);
                    if (frame == null) break;
                    _inbox.Enqueue((conn.Id, frame));
                }
            }
            catch { }
            Drop(conn.Id);
        }

        void Drop(int id)
        {
            if (_conns.TryRemove(id, out var c))
            {
                try { c.Client.Close(); } catch { }
                _disconnected.Enqueue(id);
            }
        }

        public void Send(int connectionId, ArraySegment<byte> data, bool reliable)
        {
            if (!_conns.TryGetValue(connectionId, out var c)) return;
            try
            {
                lock (c.SendLock) TcpFraming.WriteFrame(c.Stream, data);
            }
            catch { Drop(connectionId); }
        }

        public void Poll()
        {
            while (_connected.TryDequeue(out int id)) ClientConnected?.Invoke(id);
            while (_inbox.TryDequeue(out var msg)) DataReceived?.Invoke(msg.conn, new ArraySegment<byte>(msg.data));
            while (_disconnected.TryDequeue(out int id)) ClientDisconnected?.Invoke(id);
        }

        public void Dispose()
        {
            _running = false;
            try { _listener.Stop(); } catch { }
            foreach (var id in new List<int>(_conns.Keys)) Drop(id);
        }
    }

    public sealed class TcpClientTransport : IClientTransport, IDisposable
    {
        readonly TcpClient _client = new TcpClient();
        readonly ConcurrentQueue<byte[]> _inbox = new ConcurrentQueue<byte[]>();
        readonly object _sendLock = new object();
        NetworkStream _stream;
        volatile bool _connected, _connectPending, _disconnectPending;

        public bool IsConnected => _connected;
        public string Error { get; private set; }

        public event Action Connected;
        public event Action Disconnected;
        public event Action<ArraySegment<byte>> DataReceived;

        public TcpClientTransport(string host, int port)
        {
            var t = new Thread(() => Run(host, port)) { IsBackground = true, Name = "CityBiz TCP client" };
            t.Start();
        }

        void Run(string host, int port)
        {
            try
            {
                _client.NoDelay = true;
                _client.Connect(host, port);
                _stream = _client.GetStream();
                _connected = true;
                _connectPending = true;
                while (true)
                {
                    var frame = TcpFraming.ReadFrame(_stream);
                    if (frame == null) break;
                    _inbox.Enqueue(frame);
                }
            }
            catch (Exception e) { Error = e.Message; }
            _connected = false;
            _disconnectPending = true;
        }

        public void Send(ArraySegment<byte> data, bool reliable)
        {
            if (!_connected) return;
            try
            {
                lock (_sendLock) TcpFraming.WriteFrame(_stream, data);
            }
            catch (Exception e) { Error = e.Message; _connected = false; _disconnectPending = true; }
        }

        public void Poll()
        {
            if (_connectPending) { _connectPending = false; Connected?.Invoke(); }
            while (_inbox.TryDequeue(out var data)) DataReceived?.Invoke(new ArraySegment<byte>(data));
            if (_disconnectPending) { _disconnectPending = false; Disconnected?.Invoke(); }
        }

        public void Dispose()
        {
            try { _client.Close(); } catch { }
        }
    }

    static class TcpFraming
    {
        public static void WriteFrame(NetworkStream s, ArraySegment<byte> data)
        {
            int n = data.Count;
            var header = new[] { (byte)n, (byte)(n >> 8), (byte)(n >> 16), (byte)(n >> 24) };
            s.Write(header, 0, 4);
            s.Write(data.Array, data.Offset, n);
        }

        public static byte[] ReadFrame(NetworkStream s)
        {
            var header = new byte[4];
            if (!ReadExact(s, header, 4)) return null;
            int n = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);
            if (n < 0 || n > TcpServerTransport.MaxFrame) return null;
            var data = new byte[n];
            return ReadExact(s, data, n) ? data : null;
        }

        static bool ReadExact(NetworkStream s, byte[] buf, int count)
        {
            int read = 0;
            while (read < count)
            {
                int r = s.Read(buf, read, count - read);
                if (r <= 0) return false;
                read += r;
            }
            return true;
        }
    }
}

using System;
using System.Collections.Generic;

namespace CityBiz.Core.Net
{
    /// <summary>
    /// Авторитетный сервер: принимает команды, двигает симуляцию, рассылает дельты состояния (4 Гц)
    /// и позы аватаров (10 Гц, только в радиусе интереса получателя).
    /// </summary>
    public sealed class SimServer
    {
        public readonly CitySimulation Sim;
        readonly IServerTransport _transport;
        readonly DeltaReplicator _replicator = new DeltaReplicator();
        readonly Dictionary<int, int> _connToPlayer = new Dictionary<int, int>();
        readonly Dictionary<int, PlayerPose> _poses = new Dictionary<int, PlayerPose>();
        readonly HashSet<int> _dirtyPoses = new HashSet<int>();
        readonly NetWriter _w = new NetWriter(4096);
        float _deltaTimer, _poseTimer;

        public float DeltaInterval = 0.25f;
        public float PoseInterval = 0.1f;
        public float PoseRelevanceRadius = 250f;
        public int DeltasSent { get; private set; }

        public SimServer(CitySimulation sim, IServerTransport transport)
        {
            Sim = sim;
            _transport = transport;
            _transport.ClientConnected += _ => { };
            _transport.ClientDisconnected += OnDisconnected;
            _transport.DataReceived += OnData;
            Sim.NewsPublished += BroadcastNews;
        }

        public IEnumerable<int> Connections => _connToPlayer.Keys;

        public void Update(float realDeltaSeconds)
        {
            _transport.Poll();
            Sim.Advance(realDeltaSeconds * Sim.State.TimeScale);

            _deltaTimer += realDeltaSeconds;
            if (_deltaTimer >= DeltaInterval)
            {
                _deltaTimer = 0f;
                FlushDelta();
            }
            _poseTimer += realDeltaSeconds;
            if (_poseTimer >= PoseInterval)
            {
                _poseTimer = 0f;
                RelayPoses();
            }
        }

        public void FlushDelta()
        {
            _w.Clear();
            _replicator.WriteDelta(Sim.State, _w);
            foreach (int conn in _connToPlayer.Keys) _transport.Send(conn, _w.Segment, true);
            DeltasSent++;
        }

        void OnDisconnected(int conn)
        {
            if (!_connToPlayer.TryGetValue(conn, out int playerId)) return;
            _connToPlayer.Remove(conn);
            _poses.Remove(playerId);
            Sim.SetPlayerOnline(playerId, false);
            _w.Clear();
            _w.WriteByte((byte)MsgType.PlayerLeft);
            _w.WriteInt(playerId);
            foreach (int c in _connToPlayer.Keys) _transport.Send(c, _w.Segment, true);
            Sim.Publish(NewsCategory.Info, $"{Sim.State.PlayerName(playerId)} вышел из игры.");
        }

        void OnData(int conn, ArraySegment<byte> data)
        {
            NetReader r;
            MsgType type;
            try
            {
                r = new NetReader(data);
                type = (MsgType)r.ReadByte();
            }
            catch (FormatException) { return; }

            try
            {
                switch (type)
                {
                    case MsgType.Hello: HandleHello(conn, r); break;
                    case MsgType.Command: HandleCommand(conn, r); break;
                    case MsgType.PoseUpdate: HandlePose(conn, r); break;
                }
            }
            catch (FormatException)
            {
                // Битое сообщение от клиента не должно ронять сервер.
            }
        }

        void HandleHello(int conn, NetReader r)
        {
            if (_connToPlayer.ContainsKey(conn)) return;
            string name = r.ReadString();
            uint color = (uint)r.ReadVarULong();

            // Переподключение: возвращаем игроку его офлайн-профиль с тем же именем.
            int playerId = 0;
            foreach (var p in Sim.State.Players.Values)
                if (!p.IsAI && !p.Online && p.Name == name) { playerId = p.Id; break; }
            if (playerId == 0) playerId = Sim.AddHumanPlayer(name, color);
            Sim.SetPlayerOnline(playerId, true);
            if (_connToPlayer.Count == 0 && Sim.State.GetPlayer(Sim.HostPlayerId) == null) Sim.HostPlayerId = playerId;
            _connToPlayer[conn] = playerId;

            _w.Clear();
            _w.WriteByte((byte)MsgType.Welcome);
            _w.WriteInt(playerId);
            _w.WriteInt(Sim.HostPlayerId);
            _transport.Send(conn, _w.Segment, true);

            _w.Clear();
            _w.WriteByte((byte)MsgType.Snapshot);
            Sim.State.WriteSnapshot(_w);
            _transport.Send(conn, _w.Segment, true);
        }

        void HandleCommand(int conn, NetReader r)
        {
            if (!_connToPlayer.TryGetValue(conn, out int playerId)) return;
            int seq = r.ReadInt();
            var cmd = Command.Read(r);
            var result = Sim.Execute(playerId, cmd); // id берётся из соединения, а не из сообщения
            _w.Clear();
            _w.WriteByte((byte)MsgType.CommandResult);
            _w.WriteInt(seq);
            _w.WriteBool(result.Success);
            _w.WriteString(result.Message);
            _transport.Send(conn, _w.Segment, true);
        }

        void HandlePose(int conn, NetReader r)
        {
            if (!_connToPlayer.TryGetValue(conn, out int playerId)) return;
            var pose = PlayerPose.Read(r);
            pose.PlayerId = playerId;
            _poses[playerId] = pose;
            _dirtyPoses.Add(playerId);
        }

        void RelayPoses()
        {
            if (_dirtyPoses.Count == 0) return;
            foreach (var kv in _connToPlayer)
            {
                int receiver = kv.Value;
                bool hasOwn = _poses.TryGetValue(receiver, out var own);
                _w.Clear();
                _w.WriteByte((byte)MsgType.PoseUpdate);
                int countPos = _w.Length;
                var relevant = new List<PlayerPose>();
                foreach (int id in _dirtyPoses)
                {
                    if (id == receiver) continue;
                    var pose = _poses[id];
                    if (hasOwn)
                    {
                        float dx = pose.X - own.X, dz = pose.Z - own.Z;
                        if (dx * dx + dz * dz > PoseRelevanceRadius * PoseRelevanceRadius) continue;
                    }
                    relevant.Add(pose);
                }
                if (relevant.Count == 0) continue;
                _w.WriteInt(relevant.Count);
                foreach (var p in relevant) p.Write(_w);
                _transport.Send(kv.Key, _w.Segment, false);
            }
            _dirtyPoses.Clear();
        }

        void BroadcastNews(NewsItem n)
        {
            var w = new NetWriter(128);
            w.WriteByte((byte)MsgType.News);
            w.WriteLong(n.Minute);
            w.WriteByte((byte)n.Category);
            w.WriteString(n.Text);
            w.WriteInt(n.PropertyId);
            foreach (int conn in _connToPlayer.Keys) _transport.Send(conn, w.Segment, true);
        }
    }

    /// <summary>Клиентская реплика состояния. UI и мир Unity читают только её.</summary>
    public sealed class SimClient
    {
        public readonly CityState State;
        readonly IClientTransport _transport;
        readonly Dictionary<int, Action<CommandResult>> _pending = new Dictionary<int, Action<CommandResult>>();
        readonly NetWriter _w = new NetWriter(256);
        readonly string _name;
        readonly uint _color;
        int _seq;

        public int LocalPlayerId { get; private set; }
        public int HostPlayerId { get; private set; }
        public bool HasSnapshot { get; private set; }
        public bool IsHost => LocalPlayerId != 0 && LocalPlayerId == HostPlayerId;
        public readonly List<NewsItem> News = new List<NewsItem>();
        public const int MaxNews = 80;

        public event Action SnapshotReceived;
        public event Action StateChanged;
        public event Action<NewsItem> NewsReceived;
        public event Action<PlayerPose> PoseReceived;
        public event Action<int> PlayerLeft;
        public event Action<CommandResult> CommandCompleted;

        public SimClient(WorldDefinition world, IClientTransport transport, string name, uint color)
        {
            State = new CityState(world);
            _transport = transport;
            _name = name;
            _color = color;
            _transport.Connected += SendHello;
            _transport.DataReceived += OnData;
            if (_transport.IsConnected) SendHello();
        }

        public PlayerState LocalPlayer => State.GetPlayer(LocalPlayerId);

        void SendHello()
        {
            _w.Clear();
            _w.WriteByte((byte)MsgType.Hello);
            _w.WriteString(_name);
            _w.WriteVarULong(_color);
            _transport.Send(_w.Segment, true);
        }

        /// <summary>Время между дельтами экстраполируется локально — солнце движется плавно.</summary>
        public void Update(float realDeltaSeconds)
        {
            _transport.Poll();
            if (HasSnapshot) State.TimeMinutes += realDeltaSeconds * State.TimeScale;
        }

        public void Send(Command cmd, Action<CommandResult> onResult = null)
        {
            int seq = ++_seq;
            if (onResult != null) _pending[seq] = onResult;
            _w.Clear();
            _w.WriteByte((byte)MsgType.Command);
            _w.WriteInt(seq);
            cmd.Write(_w);
            _transport.Send(_w.Segment, true);
        }

        public void SendPose(PlayerPose pose)
        {
            _w.Clear();
            _w.WriteByte((byte)MsgType.PoseUpdate);
            pose.Write(_w);
            _transport.Send(_w.Segment, false);
        }

        void OnData(ArraySegment<byte> data)
        {
            var r = new NetReader(data);
            var type = (MsgType)r.ReadByte();
            switch (type)
            {
                case MsgType.Welcome:
                    LocalPlayerId = r.ReadInt();
                    HostPlayerId = r.ReadInt();
                    break;
                case MsgType.Snapshot:
                    State.ReadSnapshot(r);
                    HasSnapshot = true;
                    SnapshotReceived?.Invoke();
                    StateChanged?.Invoke();
                    break;
                case MsgType.Delta:
                    if (!HasSnapshot) return;
                    double localTime = State.TimeMinutes;
                    DeltaReplicator.ApplyDelta(State, r);
                    // Небольшое расхождение сглаживаем, большое (пауза, смена скорости) — применяем сразу.
                    double diff = State.TimeMinutes - localTime;
                    if (Math.Abs(diff) < 3.0) State.TimeMinutes = localTime + diff * 0.35;
                    StateChanged?.Invoke();
                    break;
                case MsgType.CommandResult:
                {
                    int seq = r.ReadInt();
                    var result = new CommandResult { Success = r.ReadBool(), Message = r.ReadString() };
                    if (_pending.TryGetValue(seq, out var cb)) { _pending.Remove(seq); cb(result); }
                    CommandCompleted?.Invoke(result);
                    break;
                }
                case MsgType.News:
                {
                    var n = new NewsItem { Minute = r.ReadLong(), Category = (NewsCategory)r.ReadByte(), Text = r.ReadString(), PropertyId = r.ReadInt() };
                    News.Add(n);
                    if (News.Count > MaxNews) News.RemoveAt(0);
                    NewsReceived?.Invoke(n);
                    break;
                }
                case MsgType.PoseUpdate:
                {
                    int count = r.ReadInt();
                    for (int i = 0; i < count; i++) PoseReceived?.Invoke(PlayerPose.Read(r));
                    break;
                }
                case MsgType.PlayerLeft:
                    PlayerLeft?.Invoke(r.ReadInt());
                    break;
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace CityBiz.Core.Net
{
    /// <summary>
    /// Дельта-репликация на уровне сущностей: каждую сетевую итерацию сущность сериализуется
    /// и сравнивается с последней отправленной версией; по сети уходят только изменившиеся записи.
    /// Одна дельта рассылается всем клиентам — сервер не хранит состояние на каждого клиента.
    /// </summary>
    public sealed class DeltaReplicator
    {
        const byte KindPlayer = 1, KindProperty = 2, KindBusiness = 3, KindOffer = 4, KindCandidates = 5;

        readonly Dictionary<long, byte[]> _lastSent = new Dictionary<long, byte[]>();
        readonly NetWriter _entity = new NetWriter(512);
        readonly NetWriter _section = new NetWriter(2048);
        readonly HashSet<long> _seen = new HashSet<long>();
        readonly List<long> _removed = new List<long>();

        static long Key(byte kind, int id) => ((long)kind << 32) | (uint)id;

        /// <summary>Пишет дельту в writer. Возвращает число изменившихся сущностей.</summary>
        public int WriteDelta(CityState s, NetWriter w)
        {
            _seen.Clear();
            int changes = 0;
            w.WriteByte((byte)MsgType.Delta);
            w.WriteDouble(s.TimeMinutes);
            w.WriteFloat(s.TimeScale);
            w.WriteByte((byte)s.Weather.Kind);
            w.WriteInt(s.Weather.HoursLeft);
            w.WriteInt(s.NextBusinessId); w.WriteInt(s.NextEmployeeId); w.WriteInt(s.NextOfferId);

            changes += WriteSection(w, KindPlayer, s.Players.Values, (p, ew) => p.Write(ew), p => p.Id);
            changes += WriteSection(w, KindProperty, s.Properties.Values, (p, ew) => p.Write(ew), p => p.Id);
            changes += WriteSection(w, KindBusiness, s.Businesses.Values, (b, ew) => b.Write(ew), b => b.Id);
            changes += WriteSection(w, KindOffer, s.Offers.Values, (o, ew) => o.Write(ew), o => o.Id);

            _entity.Clear();
            s.WriteCandidates(_entity);
            long candKey = Key(KindCandidates, 0);
            _seen.Add(candKey);
            bool candChanged = Changed(candKey);
            w.WriteBool(candChanged);
            if (candChanged) { s.WriteCandidates(w); changes++; }

            // Удалённые сущности (закрытые бизнесы, истёкшие предложения)
            _removed.Clear();
            foreach (var key in _lastSent.Keys) if (!_seen.Contains(key)) _removed.Add(key);
            w.WriteInt(_removed.Count);
            foreach (var key in _removed)
            {
                w.WriteByte((byte)(key >> 32));
                w.WriteInt((int)(key & 0xFFFFFFFF));
                _lastSent.Remove(key);
                changes++;
            }
            return changes;
        }

        int WriteSection<T>(NetWriter w, byte kind, IEnumerable<T> items, Action<T, NetWriter> write, Func<T, int> id)
        {
            _section.Clear();
            int count = 0;
            foreach (var item in items)
            {
                long key = Key(kind, id(item));
                _seen.Add(key);
                _entity.Clear();
                write(item, _entity);
                if (!Changed(key)) continue;
                var seg = _entity.Segment;
                _section.WriteBytes(seg.Array, 0, seg.Count);
                count++;
            }
            w.WriteInt(count);
            if (count > 0)
            {
                var seg = _section.Segment;
                w.WriteRaw(seg.Array, 0, seg.Count);
            }
            return count;
        }

        bool Changed(long key)
        {
            var seg = _entity.Segment;
            if (_lastSent.TryGetValue(key, out var prev) && prev.Length == seg.Count)
            {
                bool same = true;
                for (int i = 0; i < seg.Count; i++)
                    if (prev[i] != seg.Array[i]) { same = false; break; }
                if (same) return false;
            }
            _lastSent[key] = _entity.ToArray();
            return true;
        }

        /// <summary>Применение дельты на клиенте.</summary>
        public static void ApplyDelta(CityState s, NetReader r)
        {
            s.TimeMinutes = r.ReadDouble();
            s.TimeScale = r.ReadFloat();
            s.Weather.Kind = (WeatherKind)r.ReadByte();
            s.Weather.HoursLeft = r.ReadInt();
            s.NextBusinessId = r.ReadInt(); s.NextEmployeeId = r.ReadInt(); s.NextOfferId = r.ReadInt();

            int n = r.ReadInt();
            for (int i = 0; i < n; i++)
            {
                var er = new NetReader(r.ReadBytes());
                var p = new PlayerState(); p.Read(er); s.Players[p.Id] = p;
            }
            n = r.ReadInt();
            for (int i = 0; i < n; i++) s.ApplyProperty(new NetReader(r.ReadBytes()));
            n = r.ReadInt();
            for (int i = 0; i < n; i++)
            {
                var er = new NetReader(r.ReadBytes());
                var b = new BusinessState(); b.Read(er); s.Businesses[b.Id] = b;
            }
            n = r.ReadInt();
            for (int i = 0; i < n; i++)
            {
                var er = new NetReader(r.ReadBytes());
                var o = new TradeOffer(); o.Read(er); s.Offers[o.Id] = o;
            }
            if (r.ReadBool()) s.ReadCandidates(r);

            n = r.ReadInt();
            for (int i = 0; i < n; i++)
            {
                byte kind = r.ReadByte();
                int id = r.ReadInt();
                switch (kind)
                {
                    case KindPlayer: s.Players.Remove(id); break;
                    case KindBusiness: s.Businesses.Remove(id); break;
                    case KindOffer: s.Offers.Remove(id); break;
                }
            }
        }
    }
}

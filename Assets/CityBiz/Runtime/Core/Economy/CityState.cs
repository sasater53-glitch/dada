using System.Collections.Generic;
using CityBiz.Core.Net;

namespace CityBiz.Core
{
    /// <summary>
    /// Полное динамическое состояние города. На сервере — авторитетное, на клиенте — реплика,
    /// собранная из снапшота и дельт. Статические данные (PropertyDef) берутся из WorldDefinition.
    /// </summary>
    public sealed class CityState
    {
        public readonly WorldDefinition World;
        public double TimeMinutes;
        public float TimeScale = 1f; // игровых минут за реальную секунду
        public WeatherState Weather;

        public readonly SortedDictionary<int, PlayerState> Players = new SortedDictionary<int, PlayerState>();
        public readonly SortedDictionary<int, PropertyState> Properties = new SortedDictionary<int, PropertyState>();
        public readonly SortedDictionary<int, BusinessState> Businesses = new SortedDictionary<int, BusinessState>();
        public readonly List<EmployeeState> Candidates = new List<EmployeeState>();
        public readonly SortedDictionary<int, TradeOffer> Offers = new SortedDictionary<int, TradeOffer>();

        public int NextBusinessId = 1, NextEmployeeId = 1, NextOfferId = 1;

        public CityState(WorldDefinition world)
        {
            World = world;
            foreach (var def in world.AllProperties)
                Properties[def.Id] = new PropertyState { Id = def.Id, Def = def, RentPrice = def.BaseRent };
        }

        public GameTime Time => new GameTime(TimeMinutes);

        public PlayerState GetPlayer(int id) => Players.TryGetValue(id, out var p) ? p : null;
        public PropertyState GetProperty(int id) => Properties.TryGetValue(id, out var p) ? p : null;
        public BusinessState GetBusiness(int id) => id != 0 && Businesses.TryGetValue(id, out var b) ? b : null;

        public BusinessState BusinessAt(int propertyId)
        {
            var p = GetProperty(propertyId);
            return p == null ? null : GetBusiness(p.BusinessId);
        }

        public string PlayerName(int id)
        {
            if (id == 0) return "Городской фонд";
            var p = GetPlayer(id);
            return p != null ? p.Name : "Игрок #" + id;
        }

        public long PropertyValue(PropertyState p)
        {
            var mall = World.GetMall(p.Def.MallId);
            float mult = mall != null ? mall.RentMultiplier : 1f;
            return (long)(p.Def.BasePrice * mult * (0.75f + 0.25f * p.Condition));
        }

        // ---- Сериализация ----

        public void WriteSnapshot(NetWriter w)
        {
            w.WriteDouble(TimeMinutes); w.WriteFloat(TimeScale);
            w.WriteByte((byte)Weather.Kind); w.WriteInt(Weather.HoursLeft);
            w.WriteInt(NextBusinessId); w.WriteInt(NextEmployeeId); w.WriteInt(NextOfferId);
            w.WriteInt(Players.Count); foreach (var p in Players.Values) p.Write(w);
            w.WriteInt(Properties.Count); foreach (var p in Properties.Values) p.Write(w);
            w.WriteInt(Businesses.Count); foreach (var b in Businesses.Values) b.Write(w);
            WriteCandidates(w);
            w.WriteInt(Offers.Count); foreach (var o in Offers.Values) o.Write(w);
        }

        public void ReadSnapshot(NetReader r)
        {
            TimeMinutes = r.ReadDouble(); TimeScale = r.ReadFloat();
            Weather.Kind = (WeatherKind)r.ReadByte(); Weather.HoursLeft = r.ReadInt();
            NextBusinessId = r.ReadInt(); NextEmployeeId = r.ReadInt(); NextOfferId = r.ReadInt();
            Players.Clear();
            int n = r.ReadInt();
            for (int i = 0; i < n; i++) { var p = new PlayerState(); p.Read(r); Players[p.Id] = p; }
            n = r.ReadInt();
            for (int i = 0; i < n; i++) ApplyProperty(r);
            Businesses.Clear();
            n = r.ReadInt();
            for (int i = 0; i < n; i++) { var b = new BusinessState(); b.Read(r); Businesses[b.Id] = b; }
            ReadCandidates(r);
            Offers.Clear();
            n = r.ReadInt();
            for (int i = 0; i < n; i++) { var o = new TradeOffer(); o.Read(r); Offers[o.Id] = o; }
        }

        public void ApplyProperty(NetReader r)
        {
            var tmp = new PropertyState();
            tmp.Read(r);
            if (!Properties.TryGetValue(tmp.Id, out var existing))
            {
                tmp.Def = World.GetProperty(tmp.Id);
                Properties[tmp.Id] = tmp;
                return;
            }
            var def = existing.Def;
            Properties[tmp.Id] = tmp;
            tmp.Def = def;
        }

        public void WriteCandidates(NetWriter w)
        {
            w.WriteInt(Candidates.Count);
            foreach (var c in Candidates) c.Write(w);
        }

        public void ReadCandidates(NetReader r)
        {
            Candidates.Clear();
            int n = r.ReadInt();
            for (int i = 0; i < n; i++) { var c = new EmployeeState(); c.Read(r); Candidates.Add(c); }
        }
    }
}

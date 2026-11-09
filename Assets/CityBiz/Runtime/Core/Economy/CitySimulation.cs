using System;
using System.Collections.Generic;

namespace CityBiz.Core
{
    /// <summary>
    /// Авторитетная симуляция экономики города. Живёт только на сервере (или у хоста).
    /// Время продвигается вызовом Advance, вся логика выполняется на границах игровых часов,
    /// поэтому результат не зависит от FPS и частоты тиков.
    /// </summary>
    public sealed partial class CitySimulation
    {
        public const int MaxCandidates = 10;
        public const int FirstHumanPlayerId = 1;
        public const float OutsideOption = 0.4f; // доля спроса, уходящая в другие районы

        public readonly CityState State;
        readonly DeterministicRandom _rng;
        readonly List<RivalAI> _rivals = new List<RivalAI>();
        readonly List<BusinessState> _scratch = new List<BusinessState>();

        public event Action<NewsItem> NewsPublished;

        /// <summary>Id хоста: только он может менять скорость времени.</summary>
        public int HostPlayerId = FirstHumanPlayerId;

        public CitySimulation(WorldDefinition world, ulong seed, double startMinutes = -1)
        {
            State = new CityState(world);
            _rng = new DeterministicRandom(seed);
            State.TimeMinutes = startMinutes >= 0 ? startMinutes : GameTime.FromDayHour(1, 8, 30).TotalMinutes;
            State.Weather = new WeatherState { Kind = WeatherKind.Clear, HoursLeft = 5 };

            foreach (var rival in world.Rivals)
            {
                State.Players[rival.PlayerId] = new PlayerState
                {
                    Id = rival.PlayerId, Name = rival.Name, Cash = rival.StartingCash, IsAI = true, Online = true, Color = rival.Color,
                };
                _rivals.Add(new RivalAI(rival, DeterministicRandom.Hash(seed, (ulong)rival.PlayerId)));
            }

            foreach (var district in world.Districts)
                foreach (var init in district.Initial)
                    ApplyInitial(init);

            while (State.Candidates.Count < MaxCandidates) State.Candidates.Add(CreateCandidate(null));
            UpdateOpenStates();
            RecomputeNetWorth();
        }

        void ApplyInitial(InitialTenancy init)
        {
            var prop = State.GetProperty(init.PropertyId);
            if (prop == null) throw new ArgumentException("Unknown property in initial setup: " + init.PropertyId);
            prop.OwnerId = init.OwnerPlayerId;
            prop.TenantId = init.TenantPlayerId;
            prop.ForSale = init.ForSale;
            prop.SalePrice = init.ForSale ? (long)(State.PropertyValue(prop) * 1.1f) : 0;
            prop.ForRent = prop.TenantId == 0 && init.Business == BusinessType.None;
            prop.ResidentOccupied = init.ApartmentOccupied;
            if (init.Business == BusinessType.None) return;

            int operatorId = prop.OperatorId;
            if (operatorId == 0) throw new ArgumentException("Initial business needs an owner or tenant player: " + init.PropertyId);
            var def = EconomyCatalog.Get(init.Business);
            var b = new BusinessState
            {
                Id = State.NextBusinessId++, OwnerId = operatorId, PropertyId = prop.Id, Type = init.Business,
                Name = init.BusinessName ?? NameGenerator.DefaultBusinessName(init.Business, _rng),
                PriceLevel = init.PriceLevel, Reputation = 0.55f + _rng.Range(-0.05f, 0.1f), Level = 1, Security = 0,
            };
            for (int i = 0; i < Math.Max(def.MinStaff, init.Staff); i++)
            {
                var e = CreateCandidate(def.Roles[i % def.Roles.Length]);
                e.Skill = 0.45f + _rng.Range(0f, 0.3f);
                e.Wage = MarketWage(e.Role, e.Skill);
                b.Employees.Add(e);
            }
            if (def.UsesStock) b.Stock = TargetStock(b);
            State.Businesses[b.Id] = b;
            prop.BusinessId = b.Id;
        }

        public int AddHumanPlayer(string name, uint color)
        {
            int id = FirstHumanPlayerId;
            while (State.Players.ContainsKey(id)) id++;
            if (string.IsNullOrWhiteSpace(name)) name = "Игрок " + id;
            if (name.Length > 24) name = name.Substring(0, 24);
            State.Players[id] = new PlayerState { Id = id, Name = name, Cash = EconomyCatalog.StartingCash, Online = true, Color = color };
            RecomputeNetWorth();
            Publish(NewsCategory.Info, $"{name} начинает свой путь в бизнесе.");
            return id;
        }

        public void SetPlayerOnline(int id, bool online)
        {
            var p = State.GetPlayer(id);
            if (p != null) p.Online = online;
        }

        // ---------------- Время ----------------

        public void Advance(double minutes)
        {
            if (minutes <= 0) return;
            double target = State.TimeMinutes + minutes;
            while (true)
            {
                double nextHour = (Math.Floor(State.TimeMinutes / 60.0) + 1.0) * 60.0;
                if (nextHour > target) { State.TimeMinutes = target; break; }
                State.TimeMinutes = nextHour;
                OnHourStart();
            }
        }

        void OnHourStart()
        {
            var time = State.Time;
            if (time.Hour == 0) OnDayStart();

            if (--State.Weather.HoursLeft <= 0)
            {
                var prev = State.Weather.Kind;
                State.Weather = WeatherModel.Next(State.Weather, _rng);
                if (State.Weather.Kind != prev && (State.Weather.Kind == WeatherKind.Storm || State.Weather.Kind == WeatherKind.Rain || prev == WeatherKind.Storm))
                    Publish(NewsCategory.Weather, "Погода: " + WeatherState.DisplayName(State.Weather.Kind));
            }

            if (time.Hour == 6 || time.Hour == 14) AutoRestockAll();
            UpdateOpenStates();
            ProcessFootTraffic(time);
            ProcessServiceBusinesses(time);
            ExpireOffers();
            foreach (var p in State.Players.Values) p.Heat = Math.Max(0f, p.Heat - 0.01f);
            foreach (var ai in _rivals) ai.OnHour(this, time.Hour);
        }

        public bool IsBusinessOpen(BusinessState b, GameTime time)
        {
            var def = b.Def;
            if (def == null) return false;
            if (!def.IsOpenAt(time.Hour)) return false;
            if (def.WeekendMultiplier <= 0f && time.IsWeekend) return false;
            if (b.Employees.Count < def.MinStaff) return false;
            if (time.WholeMinutes < b.ClosedUntilMinute) return false;
            return true;
        }

        void UpdateOpenStates()
        {
            var time = State.Time;
            foreach (var b in State.Businesses.Values) b.IsOpenNow = IsBusinessOpen(b, time);
        }

        public float Quality(BusinessState b) => QualityOf(b, State.GetProperty(b.PropertyId));

        /// <summary>Качество обслуживания: одна формула для сервера и для отображения на клиенте.</summary>
        public static float QualityOf(BusinessState b, PropertyState prop)
        {
            float condition = prop != null ? prop.Condition : 1f;
            float q = 0.12f + 0.33f * b.AverageSkill + 0.2f * b.Equipment + 0.12f * condition
                      + 0.13f * (b.Level - 1) / 2f + 0.1f * b.AverageMorale;
            return Clamp01(q);
        }

        float ServiceCapacity(BusinessState b)
        {
            var def = b.Def;
            float cap = 0f;
            foreach (var e in b.Employees) cap += def.CustomersPerStaffHour * (0.6f + 0.6f * e.Skill);
            return cap * (0.5f + 0.5f * b.Equipment);
        }

        void ProcessFootTraffic(GameTime time)
        {
            foreach (var b in State.Businesses.Values) b.LastHourCustomers = 0;

            foreach (var district in State.World.Districts)
            {
                foreach (var def in EconomyCatalog.All)
                {
                    if (!def.FootTraffic) continue;
                    _scratch.Clear();
                    foreach (var b in State.Businesses.Values)
                    {
                        if (b.Type != def.Type || !b.IsOpenNow) continue;
                        var prop = State.GetProperty(b.PropertyId);
                        if (prop.Def.DistrictId == district.Id) _scratch.Add(b);
                    }
                    if (_scratch.Count == 0) continue;

                    float demand = def.BaseHourlyDemand * def.HourCurve[time.Hour] * district.TrafficMultiplier
                                   * State.Weather.FootTrafficMultiplier * (time.IsWeekend ? def.WeekendMultiplier : 1f);
                    if (State.Weather.IsWet) demand *= def.RainMultiplier;
                    if (demand <= 0f) continue;

                    float elasticity = def.PriceElasticity / Math.Max(0.5f, district.Wealth);
                    float total = 0f;
                    var weights = new float[_scratch.Count];
                    for (int i = 0; i < _scratch.Count; i++)
                    {
                        var b = _scratch[i];
                        var prop = State.GetProperty(b.PropertyId);
                        float quality = Quality(b);
                        weights[i] = prop.Def.FootTraffic * (float)Math.Pow(Math.Max(0.05f, quality), 1.2)
                                     * (0.35f + b.Reputation) * (float)Math.Pow(b.PriceLevel, -elasticity);
                        total += weights[i];
                    }

                    for (int i = 0; i < _scratch.Count; i++)
                        ServeCustomers(_scratch[i], def, district, demand * weights[i] / (total + OutsideOption));
                }
            }
        }

        void ServeCustomers(BusinessState b, BusinessTypeDef def, DistrictDef district, float expected)
        {
            float raw = expected + b.CustomerCarry;
            int wanted = (int)Math.Floor(raw);
            b.CustomerCarry = raw - wanted;

            float capacity = ServiceCapacity(b);
            int served = Math.Min(wanted, (int)capacity);
            if (def.UsesStock) served = Math.Min(served, (int)Math.Floor(b.Stock));
            int lost = wanted - served;
            if (def.UsesStock) b.Stock -= served;

            float baseRevenue = served * def.AvgTicket * district.Wealth;
            long revenue = (long)Math.Round(baseRevenue * b.PriceLevel);
            long cogs = (long)Math.Round(baseRevenue * def.CogsRatio);
            var owner = State.GetPlayer(b.OwnerId);
            if (owner != null) owner.Cash += revenue - cogs;
            b.TodayRevenue += revenue;
            b.TodayCosts += cogs;
            b.TodayCustomers += served;
            b.LastHourCustomers = served;

            if (wanted <= 0) return;
            float quality = Quality(b);
            float priceScore = 1f - Clamp01((b.PriceLevel - 0.8f) / 1.0f);
            float overflow = (float)lost / wanted;
            float s = Clamp01(0.2f + 0.5f * quality + 0.3f * priceScore - 0.35f * overflow);
            b.Satisfaction += (s - b.Satisfaction) * 0.2f;
            float weight = Math.Min(1f, wanted / 12f) * 0.035f;
            b.Reputation = Clamp01(b.Reputation + (s - b.Reputation) * weight);
        }

        void ProcessServiceBusinesses(GameTime time)
        {
            foreach (var b in State.Businesses.Values)
            {
                if (b.Type != BusinessType.Office || !b.IsOpenNow) continue;
                var def = b.Def;
                float utilisation = Clamp01(1.25f - 0.5f * b.PriceLevel * (1.3f - b.Reputation));
                float income = 0f;
                foreach (var e in b.Employees) income += def.ContractIncomePerStaffHour * (0.5f + e.Skill);
                income *= b.PriceLevel * utilisation * (0.6f + 0.4f * b.Equipment);
                long revenue = (long)Math.Round(income);
                var owner = State.GetPlayer(b.OwnerId);
                if (owner != null) owner.Cash += revenue;
                b.TodayRevenue += revenue;
                b.LastHourCustomers = (int)Math.Round(b.Employees.Count * utilisation);
                b.TodayCustomers += b.LastHourCustomers;
                float s = Clamp01(0.3f + 0.6f * Quality(b) - 0.2f * Math.Max(0f, b.PriceLevel - 1f));
                b.Satisfaction += (s - b.Satisfaction) * 0.2f;
                b.Reputation = Clamp01(b.Reputation + (s - b.Reputation) * 0.02f);
            }
        }

        // ---------------- Сутки ----------------

        void OnDayStart()
        {
            var incomeByPlayer = new Dictionary<int, long>();
            void AddIncome(int playerId, long delta)
            {
                incomeByPlayer.TryGetValue(playerId, out long v);
                incomeByPlayer[playerId] = v + delta;
            }
            void Book(int playerId, long delta)
            {
                if (playerId == 0) return;
                var p = State.GetPlayer(playerId);
                if (p == null) return;
                p.Cash += delta;
                AddIncome(playerId, delta);
            }

            // Аренда помещений, жильцы квартир, парковки, налог на имущество.
            int districtCustomers = 0;
            foreach (var b in State.Businesses.Values) districtCustomers += b.TodayCustomers;

            foreach (var prop in State.Properties.Values)
            {
                prop.Condition = Math.Max(0.2f, prop.Condition - 0.004f);
                prop.LastIncome = 0;

                if (prop.TenantId != 0)
                {
                    var tenant = State.GetPlayer(prop.TenantId);
                    if (tenant != null && tenant.Cash >= prop.RentPrice)
                    {
                        Book(prop.TenantId, -prop.RentPrice);
                        Book(prop.OwnerId, prop.RentPrice);
                        prop.DebtDays = 0;
                    }
                    else if (++prop.DebtDays >= EconomyCatalog.EvictionDebtDays)
                    {
                        Publish(NewsCategory.Property, $"{State.PlayerName(prop.TenantId)} выселен из «{prop.Def.Name}» за долги по аренде.", prop.Id);
                        Evict(prop);
                    }
                }

                if (prop.Def.Kind == PropertyKind.Apartment && prop.OwnerId != 0)
                {
                    float marketRent = prop.Def.BaseRent;
                    float ratio = prop.RentPrice / Math.Max(1f, marketRent);
                    if (prop.ResidentOccupied)
                    {
                        long net = (long)(prop.RentPrice * 0.92f); // 8% — коммунальные расходы
                        Book(prop.OwnerId, net);
                        prop.LastIncome = net;
                        if (_rng.Chance(0.02 + Math.Max(0f, ratio - 1.1f) * 0.25f)) prop.ResidentOccupied = false;
                    }
                    else if (prop.ForRent && _rng.Chance(Clamp(1.15f - 0.85f * ratio, 0.03f, 0.85f)))
                    {
                        prop.ResidentOccupied = true;
                        Publish(NewsCategory.Property, $"В «{prop.Def.Name}» заселились новые жильцы.", prop.Id);
                    }
                }

                if (prop.Def.Kind == PropertyKind.Parking && prop.OperatorId != 0)
                {
                    float occupancy = Clamp(0.3f + districtCustomers / 3000f, 0.2f, 0.95f);
                    long income = (long)(prop.Def.ParkingSpots * 14f * occupancy);
                    Book(prop.OperatorId, income);
                    prop.LastIncome = income;
                }

                if (prop.OwnerId != 0)
                    Book(prop.OwnerId, -(long)(State.PropertyValue(prop) * EconomyCatalog.PropertyTaxPerDay));
            }

            // Бизнесы: зарплаты, охрана, налоги, мораль, износ.
            var quitters = new List<EmployeeState>();
            foreach (var b in State.Businesses.Values)
            {
                var def = b.Def;
                long wages = b.DailyWages;
                long security = b.Security * EconomyCatalog.SecurityDailyCostPerLevel;
                long storage = 0;
                if (b.Type == BusinessType.Warehouse && b.Employees.Count >= def.MinStaff)
                    storage = (long)(def.StorageIncomePerDay * (0.6f + 0.6f * b.AverageSkill) * Math.Min(1.5f, b.PriceLevel));
                b.TodayRevenue += storage;
                long profit = b.TodayRevenue - b.TodayCosts - wages - security;
                long tax = profit > 0 ? (long)(profit * EconomyCatalog.TaxRate) : 0;
                var owner = State.GetPlayer(b.OwnerId);
                // Выручка и закупки уже прошли по кассе почасово; здесь — только суточные начисления.
                if (owner != null) owner.Cash += storage - wages - security - tax;
                b.TodayCosts += wages + security + tax;
                AddIncome(b.OwnerId, b.TodayRevenue - b.TodayCosts);

                b.YesterdayCustomers = b.TodayCustomers;
                b.YesterdayRevenue = b.TodayRevenue;
                b.YesterdayProfit = b.TodayRevenue - b.TodayCosts;
                b.TotalProfit += b.YesterdayProfit;
                b.TodayCustomers = 0;
                b.TodayRevenue = 0;
                b.TodayCosts = 0;
                b.Equipment = Math.Max(0.3f, b.Equipment - 0.008f);

                quitters.Clear();
                foreach (var e in b.Employees)
                {
                    float expected = MarketWage(e.Role, e.Skill);
                    e.Morale = Clamp01(e.Morale + (e.Wage / expected - 1f) * 0.25f + (b.YesterdayProfit > 0 ? 0.02f : -0.03f) + (0.75f - e.Morale) * 0.05f);
                    if (e.Morale < 0.2f && _rng.Chance(0.5)) quitters.Add(e);
                }
                foreach (var e in quitters)
                {
                    b.Employees.Remove(e);
                    Publish(NewsCategory.Business, $"{e.Name} уволился из «{b.Name}»: низкая мотивация.", b.PropertyId);
                    ReturnToMarket(e);
                }
            }

            foreach (var p in State.Players.Values)
                p.YesterdayIncome = incomeByPlayer.TryGetValue(p.Id, out long v) ? v : 0;

            RefreshCandidates();
            RecomputeNetWorth();
        }

        void Evict(PropertyState prop)
        {
            var b = State.GetBusiness(prop.BusinessId);
            if (b != null && b.OwnerId == prop.TenantId) RemoveBusiness(b);
            prop.TenantId = 0;
            prop.DebtDays = 0;
            prop.ForRent = true;
        }

        void RemoveBusiness(BusinessState b)
        {
            foreach (var e in b.Employees) ReturnToMarket(e);
            b.Employees.Clear();
            var prop = State.GetProperty(b.PropertyId);
            if (prop != null && prop.BusinessId == b.Id) prop.BusinessId = 0;
            State.Businesses.Remove(b.Id);
        }

        void RecomputeNetWorth()
        {
            foreach (var p in State.Players.Values) p.NetWorth = p.Cash;
            foreach (var prop in State.Properties.Values)
            {
                var owner = State.GetPlayer(prop.OwnerId);
                if (owner != null) owner.NetWorth += State.PropertyValue(prop);
            }
            foreach (var b in State.Businesses.Values)
            {
                var owner = State.GetPlayer(b.OwnerId);
                if (owner != null) owner.NetWorth += (long)(b.Def.SetupCost * (0.3f + b.Reputation * 0.6f));
            }
        }

        // ---------------- Рынок труда ----------------

        public static long MarketWage(StaffRole role, float skill)
        {
            float bonus = role == StaffRole.Cook || role == StaffRole.Analyst || role == StaffRole.Manager ? 45f : 0f;
            return (long)Math.Round(75f + bonus + 150f * skill);
        }

        EmployeeState CreateCandidate(StaffRole? role)
        {
            bool female = _rng.Chance(0.5);
            var r = role ?? (StaffRole)_rng.Range(0, 8);
            float skill = 0.2f + 0.75f * (float)Math.Pow(_rng.NextDouble(), 1.4);
            return new EmployeeState
            {
                Id = State.NextEmployeeId++, Name = NameGenerator.Person(_rng, female), Role = r, Skill = skill,
                Wage = (long)(MarketWage(r, skill) * _rng.Range(0.9f, 1.15f)), Morale = _rng.Range(0.6f, 0.9f),
                Female = female, Seed = _rng.Range(0, int.MaxValue),
            };
        }

        void ReturnToMarket(EmployeeState e)
        {
            e.Morale = 0.65f;
            e.Wage = MarketWage(e.Role, e.Skill);
            State.Candidates.Add(e);
            while (State.Candidates.Count > MaxCandidates + 4) State.Candidates.RemoveAt(0);
        }

        void RefreshCandidates()
        {
            for (int i = State.Candidates.Count - 1; i >= 0; i--)
                if (_rng.Chance(0.3)) State.Candidates.RemoveAt(i);
            while (State.Candidates.Count < MaxCandidates) State.Candidates.Add(CreateCandidate(null));
        }

        // ---------------- Товарные запасы ----------------

        public float TargetStock(BusinessState b)
        {
            var def = b.Def;
            float dailyDemand = 0f;
            foreach (float c in def.HourCurve) dailyDemand += c;
            dailyDemand *= def.BaseHourlyDemand * 0.6f;
            return Math.Max(dailyDemand, b.YesterdayCustomers * 1.3f) * 1.2f;
        }

        public float StockUnitCost(BusinessState b) => EconomyRules.StockUnitCost(State, b);

        void AutoRestockAll()
        {
            foreach (var b in State.Businesses.Values)
            {
                if (!b.Def.UsesStock || !b.AutoRestock) continue;
                float need = TargetStock(b) - b.Stock;
                if (need < 5f) continue;
                var owner = State.GetPlayer(b.OwnerId);
                if (owner == null) continue;
                float unit = StockUnitCost(b);
                int units = (int)Math.Min(need, Math.Max(0, owner.Cash) / unit);
                if (units <= 0) continue;
                long cost = (long)Math.Ceiling(units * unit);
                owner.Cash -= cost;
                b.Stock += units;
                b.TodayCosts += cost;
            }
        }

        // ---------------- Сделки ----------------

        void ExpireOffers()
        {
            long now = State.Time.WholeMinutes;
            List<int> expired = null;
            foreach (var o in State.Offers.Values)
                if (o.ExpiresMinute <= now) (expired ?? (expired = new List<int>())).Add(o.Id);
            if (expired != null) foreach (int id in expired) State.Offers.Remove(id);
        }

        // ---------------- Новости ----------------

        public void Publish(NewsCategory category, string text, int propertyId = 0)
        {
            NewsPublished?.Invoke(new NewsItem { Minute = State.Time.WholeMinutes, Category = category, Text = text, PropertyId = propertyId });
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}

using System;
using System.Collections.Generic;

namespace CityBiz.Core
{
    /// <summary>
    /// Авторитетная симуляция экономики торгового центра. Живёт только на сервере (или у хоста).
    /// Время продвигается вызовом Advance, вся логика выполняется на границах игровых часов,
    /// поэтому результат не зависит от FPS и частоты тиков.
    /// </summary>
    public sealed partial class CitySimulation
    {
        public const int MaxCandidates = 12;
        public const int FirstHumanPlayerId = 1;
        public const float OutsideOption = 0.45f; // доля спроса, уходящая в другие ТЦ и интернет

        // Посещаемость ТЦ по часам (доля от пиковой)
        static readonly float[] MallCurve = { 0.02f,0,0,0,0,0,0,0.05f,0.25f,0.45f,0.7f,0.8f,0.95f,1f,0.95f,0.9f,0.95f,1.05f,1.15f,1.1f,0.9f,0.6f,0.3f,0.05f };

        public readonly CityState State;
        readonly DeterministicRandom _rng;
        readonly List<RivalAI> _rivals = new List<RivalAI>();
        readonly List<BusinessState> _scratch = new List<BusinessState>();

        public event Action<NewsItem> NewsPublished;

        /// <summary>Id хоста: только он может менять скорость времени.</summary>
        public int HostPlayerId = FirstHumanPlayerId;

        /// <summary>Посетителей в ТЦ за последний час (для UI и визуализации толпы).</summary>
        public float LastHourVisitors { get; private set; }

        public CitySimulation(WorldDefinition world, ulong seed, double startMinutes = -1)
        {
            State = new CityState(world);
            _rng = new DeterministicRandom(seed);
            State.TimeMinutes = startMinutes >= 0 ? startMinutes : GameTime.FromDayHour(1, 11, 0).TotalMinutes;
            State.Weather = new WeatherState { Kind = WeatherKind.Cloudy, HoursLeft = 4 };

            foreach (var rival in world.Rivals)
            {
                State.Players[rival.PlayerId] = new PlayerState
                {
                    Id = rival.PlayerId, Name = rival.Name, Cash = rival.StartingCash, IsAI = true, Online = true, Color = rival.Color,
                };
                _rivals.Add(new RivalAI(rival, DeterministicRandom.Hash(seed, (ulong)rival.PlayerId)));
            }

            foreach (var mall in world.Malls)
                foreach (var init in mall.Initial)
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
            if (init.Business == BusinessType.None) return;

            int operatorId = prop.OperatorId;
            if (operatorId == 0) throw new ArgumentException("Initial business needs an owner or tenant player: " + init.PropertyId);
            var def = EconomyCatalog.Get(init.Business);
            if (!def.Allows(prop.Def.Kind)) throw new ArgumentException($"{init.Business} is not allowed in {prop.Def.Kind}");
            var b = new BusinessState
            {
                Id = State.NextBusinessId++, OwnerId = operatorId, PropertyId = prop.Id, Type = init.Business,
                Name = init.BusinessName ?? NameGenerator.Brand(init.Business, _rng),
                PriceLevel = init.PriceLevel, Reputation = 0.5f + _rng.Range(0f, 0.15f),
                Interior = init.Interior, Marketing = init.Marketing, EquipmentLevel = init.EquipmentLevel, Assortment = init.Assortment, Security = init.Security,
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
            Publish(NewsCategory.Info, $"{name} открывает своё дело в ТЦ.");
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
                if (State.Weather.Kind != prev && (State.Weather.IsWet || prev == WeatherKind.Storm))
                    Publish(NewsCategory.Weather, "Погода: " + WeatherState.DisplayName(State.Weather.Kind) + (State.Weather.IsWet ? " — в ТЦ больше посетителей" : ""));
            }

            if (time.Hour == 7 || time.Hour == 15) AutoRestockAll();
            UpdateOpenStates();
            ProcessVisitors(time);
            ExpireOffers();
            foreach (var p in State.Players.Values) p.Heat = Math.Max(0f, p.Heat - 0.01f);
            foreach (var ai in _rivals) ai.OnHour(this, time.Hour);
        }

        public bool IsBusinessOpen(BusinessState b, GameTime time)
        {
            var def = b.Def;
            if (def == null) return false;
            if (!def.IsOpenAt(time.Hour)) return false;
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
            float q = 0.1f + 0.28f * b.AverageSkill + 0.14f * b.EquipmentCondition + 0.08f * condition + 0.08f * b.AverageMorale
                      + 0.06f * (b.Interior - 1) + 0.03f * (b.EquipmentLevel - 1);
            return Clamp01(q);
        }

        /// <summary>Привлекательность точки для проходящих мимо: проходимость места, реклама и витрина.</summary>
        public static float Attraction(BusinessState b, PropertyState prop) =>
            prop.Def.FootTraffic * (1f + 0.18f * b.Marketing) * (1f + 0.04f * (b.Interior - 1));

        public static float ServiceCapacity(BusinessState b)
        {
            var def = b.Def;
            float cap = 0f;
            foreach (var e in b.Employees) cap += def.CustomersPerStaffHour * (0.6f + 0.6f * e.Skill);
            return cap * (0.5f + 0.5f * b.EquipmentCondition) * (1f + 0.2f * (b.EquipmentLevel - 1));
        }

        public static float TicketMultiplier(BusinessState b) => 1f + 0.1f * (b.Assortment - 1);

        /// <summary>Привлекательность ТЦ целиком: чем больше работающих магазинов, тем больше людей приходит.</summary>
        float MallAttractiveness(MallDef mall)
        {
            int shops = 0, open = 0;
            float marketing = 0f;
            foreach (var p in mall.Properties)
            {
                if (p.Kind == PropertyKind.Storage) continue;
                shops++;
                var b = State.BusinessAt(p.Id);
                if (b == null || !b.IsOpenNow) continue;
                open++;
                marketing += b.Marketing;
            }
            if (shops == 0) return 0f;
            return 0.55f + 0.45f * open / shops + Math.Min(0.15f, marketing * 0.004f);
        }

        void ProcessVisitors(GameTime time)
        {
            foreach (var b in State.Businesses.Values) b.LastHourCustomers = 0;
            float totalVisitors = 0f;

            foreach (var mall in State.World.Malls)
            {
                float visitors = mall.BaseVisitorsPerHour * MallCurve[time.Hour] * State.Weather.MallVisitMultiplier
                                 * (time.IsWeekend ? 1.35f : 1f) * MallAttractiveness(mall);
                totalVisitors += visitors;
                if (visitors <= 0f) continue;

                foreach (var def in EconomyCatalog.All)
                {
                    if (!def.FootTraffic) continue;
                    _scratch.Clear();
                    foreach (var b in State.Businesses.Values)
                    {
                        if (b.Type != def.Type || !b.IsOpenNow) continue;
                        if (State.GetProperty(b.PropertyId).Def.MallId == mall.Id) _scratch.Add(b);
                    }
                    if (_scratch.Count == 0) continue;

                    float demand = visitors * def.VisitShare * def.HourCurve[time.Hour] * (time.IsWeekend ? def.WeekendMultiplier : 1f);
                    if (State.Weather.IsWet) demand *= def.RainMultiplier;
                    if (demand <= 0f) continue;

                    float elasticity = def.PriceElasticity / Math.Max(0.5f, mall.Wealth);
                    float total = 0f;
                    var weights = new float[_scratch.Count];
                    for (int i = 0; i < _scratch.Count; i++)
                    {
                        var b = _scratch[i];
                        var prop = State.GetProperty(b.PropertyId);
                        float quality = QualityOf(b, prop);
                        weights[i] = Attraction(b, prop) * (float)Math.Pow(Math.Max(0.05f, quality), 1.2)
                                     * (0.35f + b.Reputation) * (1f + 0.05f * (b.Assortment - 1)) * (float)Math.Pow(b.PriceLevel, -elasticity);
                        total += weights[i];
                    }

                    for (int i = 0; i < _scratch.Count; i++)
                        ServeCustomers(_scratch[i], def, mall, demand * weights[i] / (total + OutsideOption));
                }
            }
            LastHourVisitors = totalVisitors;
        }

        void ServeCustomers(BusinessState b, BusinessTypeDef def, MallDef mall, float expected)
        {
            float raw = expected + b.CustomerCarry;
            int wanted = (int)Math.Floor(raw);
            b.CustomerCarry = raw - wanted;

            float capacity = ServiceCapacity(b);
            int served = Math.Min(wanted, (int)capacity);
            if (def.UsesStock) served = Math.Min(served, (int)Math.Floor(b.Stock));
            int lost = wanted - served;
            if (def.UsesStock) b.Stock -= served;

            float baseRevenue = served * def.AvgTicket * mall.Wealth * TicketMultiplier(b);
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
            float s = Clamp01(0.15f + 0.55f * quality + 0.3f * priceScore - 0.35f * overflow);
            b.Satisfaction += (s - b.Satisfaction) * 0.2f;
            float weight = Math.Min(1f, wanted / 12f) * 0.035f;
            b.Reputation = Clamp01(b.Reputation + (s - b.Reputation) * weight);
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

            // Аренда и налог на имущество
            foreach (var prop in State.Properties.Values)
            {
                prop.Condition = Math.Max(0.2f, prop.Condition - 0.003f);
                prop.LastIncome = 0;

                if (prop.TenantId != 0)
                {
                    var tenant = State.GetPlayer(prop.TenantId);
                    if (tenant != null && tenant.Cash >= prop.RentPrice)
                    {
                        Book(prop.TenantId, -prop.RentPrice);
                        Book(prop.OwnerId, prop.RentPrice);
                        if (prop.OwnerId != 0) prop.LastIncome = prop.RentPrice;
                        prop.DebtDays = 0;
                    }
                    else if (++prop.DebtDays >= EconomyCatalog.EvictionDebtDays)
                    {
                        Publish(NewsCategory.Property, $"{State.PlayerName(prop.TenantId)} выселен из «{prop.Def.Name}» за долги по аренде.", prop.Id);
                        Evict(prop);
                    }
                }

                if (prop.OwnerId != 0)
                    Book(prop.OwnerId, -(long)(State.PropertyValue(prop) * EconomyCatalog.PropertyTaxPerDay));
            }

            // Бизнесы: зарплаты, реклама, охрана, налоги, мораль, износ
            var quitters = new List<EmployeeState>();
            foreach (var b in State.Businesses.Values)
            {
                var def = b.Def;
                long wages = b.DailyWages;
                long upkeep = 0;
                foreach (var u in EconomyCatalog.AllUpgrades) upkeep += u.DailyCostPerLevel * b.GetLevel(u.Kind);
                long storage = 0;
                if (b.Type == BusinessType.Warehouse && b.Employees.Count >= def.MinStaff)
                    storage = (long)(def.StorageIncomePerDay * (0.6f + 0.6f * b.AverageSkill) * Math.Min(1.5f, b.PriceLevel) * (1f + 0.15f * (b.EquipmentLevel - 1)));
                b.TodayRevenue += storage;
                long profit = b.TodayRevenue - b.TodayCosts - wages - upkeep;
                long tax = profit > 0 ? (long)(profit * EconomyCatalog.TaxRate) : 0;
                var owner = State.GetPlayer(b.OwnerId);
                // Выручка и закупки уже прошли по кассе почасово; здесь — только суточные начисления.
                if (owner != null) owner.Cash += storage - wages - upkeep - tax;
                b.TodayCosts += wages + upkeep + tax;
                AddIncome(b.OwnerId, b.TodayRevenue - b.TodayCosts);

                b.YesterdayCustomers = b.TodayCustomers;
                b.YesterdayRevenue = b.TodayRevenue;
                b.YesterdayProfit = b.TodayRevenue - b.TodayCosts;
                b.TotalProfit += b.YesterdayProfit;
                b.TodayCustomers = 0;
                b.TodayRevenue = 0;
                b.TodayCosts = 0;
                b.EquipmentCondition = Math.Max(0.3f, b.EquipmentCondition - 0.007f);

                quitters.Clear();
                foreach (var e in b.Employees)
                {
                    float expected = MarketWage(e.Role, e.Skill);
                    float comfort = 0.01f * (b.Interior - 1); // в красивом зале работать приятнее
                    e.Morale = Clamp01(e.Morale + (e.Wage / expected - 1f) * 0.25f + (b.YesterdayProfit > 0 ? 0.02f : -0.03f) + comfort + (0.75f - e.Morale) * 0.05f);
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

        /// <summary>Сколько было вложено в развитие предприятия (для оценки капитала).</summary>
        public static long InvestedInUpgrades(BusinessState b, PropertyState prop)
        {
            long sum = 0;
            foreach (var u in EconomyCatalog.AllUpgrades)
                for (int lvl = u.MinLevel + 1; lvl <= b.GetLevel(u.Kind); lvl++) sum += EconomyRules.UpgradeCost(u, lvl, prop.Def);
            return sum;
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
                if (owner == null) continue;
                var prop = State.GetProperty(b.PropertyId);
                owner.NetWorth += (long)((b.Def.SetupCost + InvestedInUpgrades(b, prop) * 0.6f) * (0.3f + b.Reputation * 0.6f));
            }
        }

        // ---------------- Рынок труда ----------------

        public static long MarketWage(StaffRole role, float skill)
        {
            float bonus = role == StaffRole.Cook || role == StaffRole.Consultant || role == StaffRole.Manager ? 40f : 0f;
            return (long)Math.Round(70f + bonus + 140f * skill);
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
            float curve = 0f;
            foreach (float c in def.HourCurve) curve += c;
            float dailyDemand = State.World.Malls.Count > 0 ? State.World.Malls[0].BaseVisitorsPerHour * def.VisitShare * curve * 0.12f : 50f;
            return Math.Max(dailyDemand, b.YesterdayCustomers * 1.3f) * 1.2f * (1f + 0.1f * (b.Assortment - 1));
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
    }
}

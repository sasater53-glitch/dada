using System;
using System.Collections.Generic;

namespace CityBiz.Core
{
    /// <summary>
    /// ИИ-конкурент. Действует только через CitySimulation.Execute — по тем же правилам, что и живые игроки:
    /// нанимает персонал, двигает цены, развивает свои точки, открывает новые и вредит лидерам.
    /// </summary>
    public sealed class RivalAI
    {
        readonly RivalProfile _profile;
        readonly DeterministicRandom _rng;

        public RivalAI(RivalProfile profile, ulong seed)
        {
            _profile = profile;
            _rng = new DeterministicRandom(seed);
        }

        public void OnHour(CitySimulation sim, int hour)
        {
            var me = sim.State.GetPlayer(_profile.PlayerId);
            if (me == null) return;
            if (hour == 8) ManageBusinesses(sim, me);
            if (hour == 9 && _profile.UpgradeDrive > 0f) Develop(sim, me);
            if (hour == 10 && _profile.Expansion > 0f && me.Cash > 150_000 && _rng.Chance(_profile.Expansion * 0.25f)) Expand(sim, me);
            if (hour == 21 && _profile.Aggression > 0f && _rng.Chance(_profile.Aggression * 0.3f)) TrySabotage(sim, me);
        }

        List<BusinessState> Mine(CitySimulation sim, PlayerState me)
        {
            var mine = new List<BusinessState>();
            foreach (var b in sim.State.Businesses.Values) if (b.OwnerId == me.Id) mine.Add(b);
            return mine;
        }

        void ManageBusinesses(CitySimulation sim, PlayerState me)
        {
            foreach (var b in Mine(sim, me))
            {
                var def = b.Def;
                int desired = Math.Min(b.MaxStaff, def.MinStaff + (b.YesterdayCustomers > 150 ? 2 : 1));
                while (b.Employees.Count < desired)
                {
                    var c = BestCandidate(sim, def);
                    if (c == null || !sim.Execute(me.Id, Command.Make(CommandType.HireEmployee, b.Id, c.Id)).Success) break;
                }

                foreach (var e in b.Employees.ToArray())
                    if (e.Morale < 0.35f)
                        sim.Execute(me.Id, Command.Make(CommandType.RaiseWage, b.Id, e.Id, (long)(e.Wage * 1.15f)));

                // Цена: около средней цены чужих конкурентов с поправкой на стиль
                float avg = 1f, sum = 0f;
                int n = 0;
                foreach (var other in sim.State.Businesses.Values)
                    if (other.Type == b.Type && other.OwnerId != me.Id) { sum += other.PriceLevel; n++; }
                if (n > 0) avg = sum / n;
                float target = avg * _profile.PriceBias * (b.Satisfaction < 0.45f ? 0.95f : 1.02f);
                target = Math.Max(0.8f * _profile.PriceBias, Math.Min(1.25f * _profile.PriceBias, target));
                sim.Execute(me.Id, Command.Make(CommandType.SetPrice, b.Id, f: b.PriceLevel + (target - b.PriceLevel) * 0.5f));

                var prop = sim.State.GetProperty(b.PropertyId);
                if ((b.EquipmentCondition < 0.6f || prop.Condition < 0.5f) && me.Cash > sim.RepairCost(prop) * 3)
                    sim.Execute(me.Id, Command.Make(CommandType.Repair, prop.Id));
                if (b.YesterdayProfit < -5_000 && b.Reputation < 0.25f && _rng.Chance(0.3))
                    sim.Execute(me.Id, Command.Make(CommandType.CloseBusiness, b.Id));
            }
        }

        /// <summary>Вкладывает часть свободных денег в самое выгодное улучшение самой прибыльной точки.</summary>
        void Develop(CitySimulation sim, PlayerState me)
        {
            if (!_rng.Chance(_profile.UpgradeDrive * 0.5f)) return;
            BusinessState best = null;
            foreach (var b in Mine(sim, me))
                if (best == null || b.YesterdayProfit > best.YesterdayProfit) best = b;
            if (best == null || best.YesterdayProfit <= 0) return;
            var prop = sim.State.GetProperty(best.PropertyId);
            UpgradeKind? choice = null;
            long cheapest = long.MaxValue;
            foreach (var u in EconomyCatalog.AllUpgrades)
            {
                if (u.Kind == UpgradeKind.Security && best.Security >= 1 && _profile.Aggression < 0.5f) continue;
                long cost = EconomyRules.NextUpgradeCost(best, prop, u.Kind);
                if (cost > 0 && cost < cheapest) { cheapest = cost; choice = u.Kind; }
            }
            if (choice != null && me.Cash > cheapest * 4)
                sim.Execute(me.Id, Command.Make(CommandType.UpgradeBusiness, best.Id, (int)choice.Value));
        }

        static EmployeeState BestCandidate(CitySimulation sim, BusinessTypeDef def)
        {
            EmployeeState best = null;
            float bestScore = float.MinValue;
            foreach (var c in sim.State.Candidates)
            {
                float score = c.Skill * (Array.IndexOf(def.Roles, c.Role) >= 0 ? 1f : 0.7f) - c.Wage / 600f;
                if (score > bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        void Expand(CitySimulation sim, PlayerState me)
        {
            PropertyState bestProp = null;
            BusinessType bestType = BusinessType.None;
            float bestScore = float.MinValue;
            foreach (var prop in sim.State.Properties.Values)
            {
                if (!EconomyRules.CanRent(me.Id, prop)) continue;
                foreach (var def in EconomyCatalog.All)
                {
                    if (!def.Allows(prop.Def.Kind) || def.SetupCost + prop.RentPrice * 5 > me.Cash * 0.6f) continue;
                    int competitors = 0;
                    foreach (var b in sim.State.Businesses.Values) if (b.Type == def.Type) competitors++;
                    float preferred = Array.IndexOf(_profile.Preferred, def.Type) >= 0 ? 1.5f : 0f;
                    float score = prop.Def.FootTraffic + preferred - competitors * 0.35f + _rng.Range(0f, 0.5f) - prop.RentPrice / 4000f;
                    if (score > bestScore) { bestScore = score; bestProp = prop; bestType = def.Type; }
                }
            }
            if (bestProp == null) return;
            if (!sim.Execute(me.Id, Command.Make(CommandType.RentProperty, bestProp.Id)).Success) return;
            if (!sim.Execute(me.Id, Command.Make(CommandType.OpenBusiness, bestProp.Id, (int)bestType)).Success)
                sim.Execute(me.Id, Command.Make(CommandType.EndLease, bestProp.Id));
        }

        void TrySabotage(CitySimulation sim, PlayerState me)
        {
            if (sim.SabotageCooldownLeft(me) > 0 || me.Heat > 0.6f || me.Cash < 50_000) return;
            var myTypes = new HashSet<BusinessType>();
            foreach (var b in sim.State.Businesses.Values) if (b.OwnerId == me.Id) myTypes.Add(b.Type);

            BusinessState target = null;
            float bestScore = 0f;
            foreach (var b in sim.State.Businesses.Values)
            {
                if (b.OwnerId == me.Id || !myTypes.Contains(b.Type)) continue;
                var owner = sim.State.GetPlayer(b.OwnerId);
                float score = b.YesterdayRevenue * (owner != null && !owner.IsAI ? 1.5f : 0.6f) * (1f - 0.2f * b.Security);
                if (score > bestScore) { bestScore = score; target = b; }
            }
            if (target == null) return;

            SabotageKind kind;
            float roll = _rng.NextFloat();
            if (roll < 0.35f) kind = SabotageKind.FakeReviews;
            else if (roll < 0.55f) kind = SabotageKind.PoachEmployee;
            else if (roll < 0.75f) kind = SabotageKind.TipOffInspection;
            else if (roll < 0.9f) kind = SabotageKind.BreakEquipment;
            else kind = SabotageKind.PowerOutage;
            sim.Execute(me.Id, Command.Make(CommandType.Sabotage, target.Id, (int)kind));
        }

        /// <summary>ИИ принимает предложение о покупке, если цена не выше 115% рыночной стоимости.</summary>
        public static void ConsiderOffer(CitySimulation sim, PlayerState ai, TradeOffer offer)
        {
            var prop = sim.State.GetProperty(offer.PropertyId);
            bool good = prop != null && offer.Price <= sim.State.PropertyValue(prop) * 1.15f && ai.Cash > offer.Price * 1.5f;
            sim.Execute(ai.Id, Command.Make(CommandType.RespondOffer, offer.Id, good ? 1 : 0));
        }
    }
}

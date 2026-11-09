using System;
using System.Collections.Generic;

namespace CityBiz.Core
{
    /// <summary>
    /// ИИ-конкурент. Действует только через CitySimulation.Execute — по тем же правилам, что и живые игроки.
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
            if (hour == 10 && _profile.Expansion > 0f && me.Cash > 160_000 && _rng.Chance(_profile.Expansion * 0.3f)) Expand(sim, me);
            if (hour == 15 && _profile.Expansion > 0f && me.Cash > 400_000 && _rng.Chance(0.15f)) BuyRentedPremises(sim, me);
            if (hour == 21 && _profile.Aggression > 0f && _rng.Chance(_profile.Aggression * 0.3f)) TrySabotage(sim, me);
        }

        void ManageBusinesses(CitySimulation sim, PlayerState me)
        {
            var mine = new List<BusinessState>();
            foreach (var b in sim.State.Businesses.Values) if (b.OwnerId == me.Id) mine.Add(b);

            foreach (var b in mine)
            {
                var def = b.Def;
                int desired = Math.Min(def.MaxStaff, def.MinStaff + (b.YesterdayCustomers > 200 ? 2 : 1));
                while (b.Employees.Count < desired)
                {
                    var c = BestCandidate(sim, def);
                    if (c == null || !sim.Execute(me.Id, Command.Make(CommandType.HireEmployee, b.Id, c.Id)).Success) break;
                }

                foreach (var e in b.Employees.ToArray())
                    if (e.Morale < 0.35f)
                        sim.Execute(me.Id, Command.Make(CommandType.RaiseWage, b.Id, e.Id, (long)(e.Wage * 1.15f)));

                // Цена: держимся около средней цены чужих конкурентов с поправкой на стиль игрока.
                float avg = 1f; int n = 0; float sum = 0f;
                foreach (var other in sim.State.Businesses.Values)
                    if (other.Type == b.Type && other.OwnerId != me.Id) { sum += other.PriceLevel; n++; }
                if (n > 0) avg = sum / n;
                float target = avg * _profile.PriceBias * (b.Satisfaction < 0.45f ? 0.95f : 1.02f);
                target = Math.Max(0.8f * _profile.PriceBias, Math.Min(1.25f * _profile.PriceBias, target));
                sim.Execute(me.Id, Command.Make(CommandType.SetPrice, b.Id, f: b.PriceLevel + (target - b.PriceLevel) * 0.5f));

                var prop = sim.State.GetProperty(b.PropertyId);
                if ((b.Equipment < 0.6f || prop.Condition < 0.5f) && me.Cash > sim.RepairCost(prop) * 3)
                    sim.Execute(me.Id, Command.Make(CommandType.Repair, prop.Id));
                if (b.Security < 2 && b.YesterdayProfit > 2_500 && me.Cash > 60_000 && _rng.Chance(0.25))
                    sim.Execute(me.Id, Command.Make(CommandType.UpgradeSecurity, b.Id));
                if (b.Level < 3 && b.YesterdayProfit > 4_000 && me.Cash > 120_000 && _rng.Chance(0.2))
                    sim.Execute(me.Id, Command.Make(CommandType.UpgradeInterior, b.Id));
                if (b.YesterdayProfit < -6_000 && b.Reputation < 0.25f && _rng.Chance(0.3))
                    sim.Execute(me.Id, Command.Make(CommandType.CloseBusiness, b.Id));
            }
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
                if (prop.TenantId != 0 || prop.BusinessId != 0 || !prop.ForRent || prop.OwnerId == me.Id) continue;
                if (prop.Def.Kind == PropertyKind.Apartment || prop.Def.Kind == PropertyKind.Parking) continue;
                foreach (var def in EconomyCatalog.All)
                {
                    if (!def.Allows(prop.Def.Kind) || def.SetupCost + prop.RentPrice * 5 > me.Cash * 0.6f) continue;
                    int competitors = 0;
                    foreach (var b in sim.State.Businesses.Values) if (b.Type == def.Type) competitors++;
                    float score = prop.Def.FootTraffic - competitors * 0.6f + _rng.Range(0f, 0.5f) - prop.RentPrice / 4000f;
                    if (score > bestScore) { bestScore = score; bestProp = prop; bestType = def.Type; }
                }
            }
            if (bestProp == null) return;
            if (!sim.Execute(me.Id, Command.Make(CommandType.RentProperty, bestProp.Id)).Success) return;
            if (!sim.Execute(me.Id, Command.Make(CommandType.OpenBusiness, bestProp.Id, (int)bestType)).Success)
                sim.Execute(me.Id, Command.Make(CommandType.EndLease, bestProp.Id));
        }

        void BuyRentedPremises(CitySimulation sim, PlayerState me)
        {
            foreach (var prop in sim.State.Properties.Values)
            {
                if (prop.TenantId != me.Id || !sim.CanBuy(me.Id, prop)) continue;
                if (sim.PurchasePrice(prop) < me.Cash * 0.5f)
                {
                    sim.Execute(me.Id, Command.Make(CommandType.BuyProperty, prop.Id));
                    return;
                }
            }
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

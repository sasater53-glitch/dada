using System.Diagnostics;
using System.Linq;
using CityBiz.Core;
using CityBiz.Core.Net;
using NUnit.Framework;

namespace CityBiz.Tests
{
    public class CoreSimulationTests
    {
        static CitySimulation NewSim(ulong seed = 42) => new CitySimulation(MallContent.CreateWorld(), seed);

        static int Lot(CitySimulation sim, string lot) => sim.State.World.FindByLot(MallContent.MallId, lot).Id;

        static CommandResult Exec(CitySimulation sim, int player, CommandType type, int a = 0, int b = 0, long amount = 0, float f = 0, string text = null)
            => sim.Execute(player, Command.Make(type, a, b, amount, f, text));

        static BusinessState Open(CitySimulation sim, int player, string lot, BusinessType type, int staff)
        {
            int id = Lot(sim, lot);
            Assert.IsTrue(Exec(sim, player, CommandType.RentProperty, id).Success, "rent " + lot);
            var r = Exec(sim, player, CommandType.OpenBusiness, id, (int)type, text: "Тест");
            Assert.IsTrue(r.Success, r.Message);
            var b = sim.State.BusinessAt(id);
            for (int i = 0; i < staff; i++)
                Assert.IsTrue(Exec(sim, player, CommandType.HireEmployee, b.Id, sim.State.Candidates[0].Id).Success, "hire");
            return b;
        }

        static void AdvanceDays(CitySimulation sim, int days) => sim.Advance(days * GameTime.MinutesPerDay);

        static int Rich(CitySimulation sim, string name)
        {
            int id = sim.AddHumanPlayer(name, 0);
            sim.State.GetPlayer(id).Cash = 2_000_000;
            return id;
        }

        [Test]
        public void Mall_IsPopulated()
        {
            var sim = NewSim();
            var props = sim.State.Properties.Values.ToList();
            Assert.Greater(props.Count, 60);
            Assert.AreEqual(1, props.Count(p => p.Def.Kind == PropertyKind.Anchor));
            Assert.AreEqual(4, props.Count(p => p.Def.Kind == PropertyKind.Kiosk));
            Assert.AreEqual(6, props.Count(p => p.Def.Kind == PropertyKind.FoodStall));
            Assert.AreEqual(2, props.Count(p => p.Def.Kind == PropertyKind.Storage));
            Assert.AreEqual(3, props.Select(p => p.Def.Floor).Distinct().Count());
            Assert.Greater(sim.State.Businesses.Count, 35);
            Assert.Greater(props.Count(p => EconomyRules.CanRent(1, p)), 10, "игроку должны остаться свободные помещения");
            Assert.IsTrue(sim.State.Businesses.Values.All(b => b.Employees.Count >= b.Def.MinStaff));
            // Витрины одного ряда не перекрываются
            foreach (var row in props.Where(p => p.Def.Kind == PropertyKind.ShopUnit || p.Def.Kind == PropertyKind.Storage).GroupBy(p => (p.Def.Floor, p.Def.Side)))
            {
                var sorted = row.OrderBy(p => p.Def.PlanX).ToList();
                for (int i = 1; i < sorted.Count; i++)
                    Assert.GreaterOrEqual(sorted[i].Def.PlanX - sorted[i].Def.Width / 2, sorted[i - 1].Def.PlanX + sorted[i - 1].Def.Width / 2 - 0.01f);
            }
        }

        [Test]
        public void KioskStart_IsAffordable_AndEarns()
        {
            var sim = NewSim();
            int me = sim.AddHumanPlayer("Новичок", 0xFFFFFF);
            var kiosk = Open(sim, me, "K-2", BusinessType.IceCream, 1);
            AdvanceDays(sim, 2);
            Assert.Greater(kiosk.YesterdayCustomers, 40);
            Assert.Greater(kiosk.YesterdayRevenue, 0);
            Assert.Greater(sim.State.GetPlayer(me).Cash, 0);
        }

        [Test]
        public void Upgrades_IncreaseCustomersAndCostMoney()
        {
            int Customers(bool upgraded)
            {
                var sim = NewSim(9);
                int me = Rich(sim, "Инвестор");
                var b = Open(sim, me, "1N-09", BusinessType.ClothingStore, 3);
                foreach (var e in b.Employees) { e.Skill = 0.8f; e.Morale = 0.8f; }
                if (upgraded)
                {
                    long before = sim.State.GetPlayer(me).Cash;
                    foreach (var kind in new[] { UpgradeKind.Marketing, UpgradeKind.Marketing, UpgradeKind.Interior, UpgradeKind.Assortment, UpgradeKind.Equipment })
                    {
                        var r = Exec(sim, me, CommandType.UpgradeBusiness, b.Id, (int)kind);
                        Assert.IsTrue(r.Success, r.Message);
                    }
                    Assert.Less(sim.State.GetPlayer(me).Cash, before);
                    Assert.AreEqual(2, b.Marketing);
                    Assert.AreEqual(2, b.Interior);
                    Assert.Greater(b.ClosedUntilMinute, sim.State.Time.WholeMinutes, "ремонт закрывает точку");
                }
                AdvanceDays(sim, 3);
                return b.YesterdayCustomers;
            }
            int plain = Customers(false), upgraded = Customers(true);
            Assert.Greater(upgraded, plain * 1.2f, $"plain={plain} upgraded={upgraded}");
        }

        [Test]
        public void Upgrade_RespectsMaxLevel_AndExtendsStaff()
        {
            var sim = NewSim();
            int me = Rich(sim, "Инвестор");
            var b = Open(sim, me, "K-4", BusinessType.Cosmetics, 1);
            int maxStaffBefore = b.MaxStaff;
            for (int i = 0; i < 4; i++) Assert.IsTrue(Exec(sim, me, CommandType.UpgradeBusiness, b.Id, (int)UpgradeKind.Equipment).Success);
            Assert.AreEqual(5, b.EquipmentLevel);
            Assert.AreEqual(maxStaffBefore + 4, b.MaxStaff);
            Assert.IsFalse(Exec(sim, me, CommandType.UpgradeBusiness, b.Id, (int)UpgradeKind.Equipment).Success);
            var other = sim.State.Businesses.Values.First(x => x.OwnerId != me);
            Assert.IsFalse(Exec(sim, me, CommandType.UpgradeBusiness, other.Id, (int)UpgradeKind.Interior).Success, "чужой бизнес улучшать нельзя");
        }

        [Test]
        public void BusinessType_MustMatchPremises()
        {
            var sim = NewSim();
            int me = Rich(sim, "Тестер");
            int kiosk = Lot(sim, "K-2");
            Assert.IsTrue(Exec(sim, me, CommandType.RentProperty, kiosk).Success);
            Assert.IsFalse(Exec(sim, me, CommandType.OpenBusiness, kiosk, (int)BusinessType.Restaurant).Success);
            Assert.IsTrue(Exec(sim, me, CommandType.OpenBusiness, kiosk, (int)BusinessType.IceCream).Success);
        }

        [Test]
        public void BuyProperty_RequiresMoney_AndTransfersOwnership()
        {
            var sim = NewSim();
            int me = sim.AddHumanPlayer("Тестер", 0);
            var p = sim.State.GetPlayer(me);
            int lot = Lot(sim, "2S-07");
            p.Cash = 1_000;
            Assert.IsFalse(Exec(sim, me, CommandType.BuyProperty, lot).Success);
            p.Cash = 10_000_000;
            long price = sim.PurchasePrice(sim.State.GetProperty(lot));
            Assert.IsTrue(Exec(sim, me, CommandType.BuyProperty, lot).Success);
            Assert.AreEqual(me, sim.State.GetProperty(lot).OwnerId);
            Assert.AreEqual(10_000_000 - price, p.Cash);
            Assert.IsFalse(Exec(sim, me, CommandType.BuyProperty, Lot(sim, "A-1")).Success, "чужая собственность не продаётся");
        }

        [Test]
        public void OwnedUnit_RentedToAnotherPlayer_PaysOwner()
        {
            var sim = NewSim();
            int owner = Rich(sim, "Владелец"), tenant = Rich(sim, "Арендатор");
            int lot = Lot(sim, "3N-03");
            Assert.IsTrue(Exec(sim, owner, CommandType.BuyProperty, lot).Success);
            Assert.IsTrue(Exec(sim, owner, CommandType.ListForRent, lot, amount: 900).Success);
            Assert.IsTrue(Exec(sim, tenant, CommandType.RentProperty, lot).Success);
            AdvanceDays(sim, 1);
            Assert.AreEqual(900, sim.State.GetProperty(lot).LastIncome);
        }

        [Test]
        public void Competition_CheaperStallAttractsMoreCustomers()
        {
            var sim = NewSim();
            int a = Rich(sim, "A"), b = Rich(sim, "B");
            var cheap = Open(sim, a, "F-2", BusinessType.FastFood, 3);
            var pricey = Open(sim, b, "F-5", BusinessType.FastFood, 3);
            foreach (var e in cheap.Employees.Concat(pricey.Employees)) { e.Skill = 0.9f; e.Morale = 0.8f; }
            Assert.IsTrue(Exec(sim, a, CommandType.SetPrice, cheap.Id, f: 0.75f).Success);
            Assert.IsTrue(Exec(sim, b, CommandType.SetPrice, pricey.Id, f: 1.6f).Success);
            AdvanceDays(sim, 1);
            Assert.Greater(cheap.YesterdayCustomers, pricey.YesterdayCustomers * 1.5f);
        }

        [Test]
        public void Sabotage_SecurityReducesSuccessRate()
        {
            int Successes(int security)
            {
                var sim = NewSim(7);
                int me = sim.AddHumanPlayer("Вредитель", 0);
                var target = sim.State.Businesses.Values.First(b => b.OwnerId != me);
                target.Security = security;
                var p = sim.State.GetPlayer(me);
                int ok = 0;
                for (int i = 0; i < 300; i++)
                {
                    p.Cash = 1_000_000; p.Heat = 0; p.LastSabotageMinute = -100_000;
                    target.Reputation = 0.8f;
                    Exec(sim, me, CommandType.Sabotage, target.Id, (int)SabotageKind.FakeReviews);
                    if (target.Reputation < 0.8f - 0.01f) ok++;
                }
                return ok;
            }
            int unprotected = Successes(0), guarded = Successes(3);
            Assert.Greater(unprotected, 180);
            Assert.Less(guarded, unprotected - 60);
        }

        [Test]
        public void Sabotage_HasCooldown_AndCannotTargetOwnBusiness()
        {
            var sim = NewSim();
            int me = sim.AddHumanPlayer("Вредитель", 0);
            var mine = Open(sim, me, "K-2", BusinessType.IceCream, 1);
            Assert.IsFalse(Exec(sim, me, CommandType.Sabotage, mine.Id, (int)SabotageKind.FakeReviews).Success);
            var target = sim.State.Businesses.Values.First(b => b.OwnerId != me);
            Exec(sim, me, CommandType.Sabotage, target.Id, (int)SabotageKind.FakeReviews);
            var second = Exec(sim, me, CommandType.Sabotage, target.Id, (int)SabotageKind.FakeReviews);
            Assert.IsFalse(second.Success);
            StringAssert.Contains("Подождите", second.Message);
        }

        [Test]
        public void UnpaidRent_LeadsToEviction()
        {
            var sim = NewSim();
            int me = sim.AddHumanPlayer("Должник", 0);
            var shop = Open(sim, me, "K-2", BusinessType.IceCream, 1);
            int lot = shop.PropertyId;
            sim.State.GetPlayer(me).Cash = -50_000;
            AdvanceDays(sim, EconomyCatalog.EvictionDebtDays + 1);
            Assert.AreEqual(0, sim.State.GetProperty(lot).TenantId);
            Assert.IsNull(sim.State.GetBusiness(shop.Id));
        }

        [Test]
        public void PlayerTrade_OfferAccepted_TransfersPropertyAndMoney()
        {
            var sim = NewSim();
            int seller = Rich(sim, "Продавец"), buyer = sim.AddHumanPlayer("Покупатель", 0);
            int lot = Lot(sim, "K-4");
            Assert.IsTrue(Exec(sim, seller, CommandType.BuyProperty, lot).Success);
            Assert.IsTrue(Exec(sim, seller, CommandType.OfferPropertyToPlayer, lot, buyer, 90_000).Success);
            var offer = sim.State.Offers.Values.Single();
            long buyerCash = sim.State.GetPlayer(buyer).Cash;
            Assert.IsFalse(Exec(sim, seller, CommandType.RespondOffer, offer.Id, 1).Success, "только адресат отвечает");
            Assert.IsTrue(Exec(sim, buyer, CommandType.RespondOffer, offer.Id, 1).Success);
            Assert.AreEqual(buyer, sim.State.GetProperty(lot).OwnerId);
            Assert.AreEqual(buyerCash - 90_000, sim.State.GetPlayer(buyer).Cash);
        }

        [Test]
        public void RainBringsMoreVisitors()
        {
            float Visitors(WeatherKind kind)
            {
                var sim = NewSim(3);
                sim.State.Weather = new WeatherState { Kind = kind, HoursLeft = 100 };
                sim.Advance(60);
                return sim.LastHourVisitors;
            }
            Assert.Greater(Visitors(WeatherKind.Rain), Visitors(WeatherKind.Clear) * 1.2f);
        }

        [Test]
        public void Simulation_IsDeterministic()
        {
            byte[] Run()
            {
                var sim = NewSim(99);
                int me = sim.AddHumanPlayer("Игрок", 0);
                Open(sim, me, "K-2", BusinessType.IceCream, 2);
                AdvanceDays(sim, 5);
                var w = new NetWriter();
                sim.State.WriteSnapshot(w);
                return w.ToArray();
            }
            CollectionAssert.AreEqual(Run(), Run());
        }

        [Test]
        public void LongRun_EconomyStaysSane()
        {
            var sim = NewSim(2024);
            int me = sim.AddHumanPlayer("Наблюдатель", 0);
            var mine = Open(sim, me, "1S-05", BusinessType.Cafe, 2);
            AdvanceDays(sim, 30);
            foreach (var p in sim.State.Players.Values)
                TestContext.WriteLine($"{p.Name}: cash={p.Cash} worth={p.NetWorth} heat={p.Heat:F2} biz={sim.State.Businesses.Values.Count(b => b.OwnerId == p.Id)}");
            foreach (var g in sim.State.Businesses.Values.GroupBy(b => b.Type))
                TestContext.WriteLine($"  {g.Key}: n={g.Count()} avgCust={g.Average(b => b.YesterdayCustomers):F0} avgProfit={g.Average(b => b.YesterdayProfit):F0} avgDev={g.Average(b => b.DevelopmentScore):F2}");
            TestContext.WriteLine($"Кафе игрока: {(sim.State.GetBusiness(mine.Id) != null ? $"прибыль {mine.YesterdayProfit}, клиентов {mine.YesterdayCustomers}" : "закрыто")}");
            foreach (var p in sim.State.Players.Values)
                Assert.IsTrue(p.Cash > -1_000_000 && p.Cash < 100_000_000, p.Name + " cash " + p.Cash);
            foreach (var b in sim.State.Businesses.Values)
            {
                Assert.IsFalse(float.IsNaN(b.Reputation) || float.IsNaN(b.Stock), b.Name);
                Assert.That(b.Reputation, Is.InRange(0f, 1f));
            }
            Assert.GreaterOrEqual(sim.State.Businesses.Count, 35, "арендаторы не должны массово разоряться");
            Assert.Greater(sim.State.Businesses.Values.Where(b => b.OwnerId == MallContent.VetrovaId).Average(b => b.DevelopmentScore), 0.1f, "ИИ развивает точки");
        }

        [Test]
        public void NetBuffer_RoundTripsAllTypes()
        {
            var w = new NetWriter(4);
            w.WriteInt(-123456); w.WriteLong(long.MinValue); w.WriteFloat(3.25f); w.WriteDouble(-1e10);
            w.WriteString("Привет, ТЦ"); w.WriteString(null); w.WriteBool(true);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(-123456, r.ReadInt());
            Assert.AreEqual(long.MinValue, r.ReadLong());
            Assert.AreEqual(3.25f, r.ReadFloat());
            Assert.AreEqual(-1e10, r.ReadDouble());
            Assert.AreEqual("Привет, ТЦ", r.ReadString());
            Assert.IsNull(r.ReadString());
            Assert.IsTrue(r.ReadBool());
            Assert.IsTrue(r.EndOfData);
        }

        static byte[] EntityBytes(CityState s)
        {
            var w = new NetWriter();
            foreach (var p in s.Players.Values) p.Write(w);
            foreach (var p in s.Properties.Values) p.Write(w);
            foreach (var b in s.Businesses.Values) b.Write(w);
            foreach (var o in s.Offers.Values) o.Write(w);
            s.WriteCandidates(w);
            return w.ToArray();
        }

        [Test]
        public void Replication_ClientReplicaMatchesServer()
        {
            var world = MallContent.CreateWorld();
            var sim = new CitySimulation(world, 5);
            sim.State.TimeScale = 60f;
            var hub = new LoopbackHub();
            var server = new SimServer(sim, hub);
            var client = new SimClient(world, hub.CreateClient(), "Хост", 0x00FF00);
            CommandResult? rentResult = null;
            int kiosk = world.FindByLot(MallContent.MallId, "K-2").Id;
            for (int i = 0; i < 720; i++) // 3 игровых дня: 720 шагов × 0.1 с × 60 мин/с
            {
                server.Update(0.1f);
                client.Update(0.1f);
                if (client.HasSnapshot && rentResult == null && i == 50)
                    client.Send(Command.Make(CommandType.RentProperty, kiosk), r => rentResult = r);
            }
            server.FlushDelta();
            client.Update(0f);

            Assert.IsTrue(client.HasSnapshot);
            Assert.AreEqual(1, client.LocalPlayerId);
            Assert.IsTrue(client.IsHost);
            Assert.IsTrue(rentResult.HasValue && rentResult.Value.Success, rentResult?.Message);
            Assert.Greater(client.News.Count, 0);
            CollectionAssert.AreEqual(EntityBytes(sim.State), EntityBytes(client.State));
            Assert.AreEqual(sim.State.TimeMinutes, client.State.TimeMinutes, 1.0);
            var snap = new NetWriter();
            sim.State.WriteSnapshot(snap);
            Assert.Less(hub.BytesToClients / (double)server.DeltasSent, snap.Length * 0.5);
        }

        [Test]
        public void TcpTransport_HostAndRemoteClientPlayTogether()
        {
            var world = MallContent.CreateWorld();
            var sim = new CitySimulation(world, 11);
            using (var serverTransport = new TcpServerTransport(0))
            {
                var server = new SimServer(sim, serverTransport);
                using (var clientTransport = new TcpClientTransport("127.0.0.1", serverTransport.Port))
                {
                    var client = new SimClient(world, clientTransport, "Сетевой игрок", 0x123456);
                    CommandResult? result = null;
                    var sw = Stopwatch.StartNew();
                    bool sent = false;
                    while (sw.ElapsedMilliseconds < 5000 && result == null)
                    {
                        server.Update(0.02f);
                        client.Update(0.02f);
                        if (client.HasSnapshot && !sent)
                        {
                            sent = true;
                            client.Send(Command.Make(CommandType.Chat, text: "Привет всем!"), r => result = r);
                        }
                        System.Threading.Thread.Sleep(5);
                    }
                    Assert.IsTrue(result.HasValue && result.Value.Success, clientTransport.Error);
                    Assert.AreNotEqual(0, client.LocalPlayerId);
                    Assert.AreEqual(sim.State.Properties.Count, client.State.Properties.Count);
                }
            }
        }
    }
}

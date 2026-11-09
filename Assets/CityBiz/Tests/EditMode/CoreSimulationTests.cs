using System;
using System.Diagnostics;
using System.Linq;
using CityBiz.Core;
using CityBiz.Core.Net;
using NUnit.Framework;

namespace CityBiz.Tests
{
    public class CoreSimulationTests
    {
        static CitySimulation NewSim(ulong seed = 42) => new CitySimulation(StarterDistrictContent.CreateWorld(), seed);

        static CommandResult Exec(CitySimulation sim, int player, CommandType type, int a = 0, int b = 0, long amount = 0, float f = 0, string text = null)
            => sim.Execute(player, Command.Make(type, a, b, amount, f, text));

        static BusinessState OpenCafe(CitySimulation sim, int player, int propertyId, int staff)
        {
            Assert.IsTrue(Exec(sim, player, CommandType.RentProperty, propertyId).Success, "rent");
            Assert.IsTrue(Exec(sim, player, CommandType.OpenBusiness, propertyId, (int)BusinessType.Cafe, text: "Тест").Success, "open");
            var b = sim.State.BusinessAt(propertyId);
            for (int i = 0; i < staff; i++)
                Assert.IsTrue(Exec(sim, player, CommandType.HireEmployee, b.Id, sim.State.Candidates[0].Id).Success, "hire");
            return b;
        }

        static void AdvanceDays(CitySimulation sim, int days) => sim.Advance(days * GameTime.MinutesPerDay);

        [Test]
        public void StarterDistrict_IsPopulated()
        {
            var sim = NewSim();
            Assert.AreEqual(23, sim.State.Properties.Count);
            Assert.AreEqual(5, sim.State.Businesses.Count);
            Assert.AreEqual(3, sim.State.Players.Count);
            Assert.IsTrue(sim.State.Businesses.Values.All(b => b.Employees.Count >= b.Def.MinStaff));
            Assert.AreEqual(CitySimulation.MaxCandidates, sim.State.Candidates.Count);
        }

        [Test]
        public void RentOpenAndHire_BusinessServesCustomersAndEarns()
        {
            var sim = NewSim();
            int me = sim.AddHumanPlayer("Тестер", 0xFFFFFF);
            var cafe = OpenCafe(sim, me, 110, 2);
            AdvanceDays(sim, 1);
            Assert.Greater(cafe.YesterdayCustomers, 50);
            Assert.Greater(cafe.YesterdayRevenue, 0);
            Assert.AreEqual(me, sim.State.GetProperty(110).TenantId);
        }

        [Test]
        public void BuyProperty_RequiresMoney_AndTransfersOwnership()
        {
            var sim = NewSim();
            int me = sim.AddHumanPlayer("Тестер", 0);
            var p = sim.State.GetPlayer(me);
            p.Cash = 1_000;
            Assert.IsFalse(Exec(sim, me, CommandType.BuyProperty, 102).Success);
            p.Cash = 1_000_000;
            long price = sim.PurchasePrice(sim.State.GetProperty(102));
            Assert.IsTrue(Exec(sim, me, CommandType.BuyProperty, 102).Success);
            Assert.AreEqual(me, sim.State.GetProperty(102).OwnerId);
            Assert.AreEqual(1_000_000 - price, p.Cash);
            // Чужая собственность, не выставленная на продажу, не покупается.
            Assert.IsFalse(Exec(sim, me, CommandType.BuyProperty, 113).Success);
        }

        [Test]
        public void OwnedApartment_ProducesRentIncome()
        {
            var sim = NewSim();
            int me = sim.AddHumanPlayer("Тестер", 0);
            sim.State.GetPlayer(me).Cash = 1_000_000;
            Assert.IsTrue(Exec(sim, me, CommandType.BuyProperty, 102).Success); // квартира с жильцами
            long before = sim.State.GetPlayer(me).Cash;
            AdvanceDays(sim, 1);
            Assert.Greater(sim.State.GetProperty(102).LastIncome, 0);
            Assert.Greater(sim.State.GetPlayer(me).Cash, before - 200); // доход перекрывает налог
        }

        [Test]
        public void Competition_CheaperCafeAttractsMoreCustomers()
        {
            var sim = NewSim();
            int a = sim.AddHumanPlayer("A", 0), b = sim.AddHumanPlayer("B", 0);
            var cheap = OpenCafe(sim, a, 115, 3);
            var pricey = OpenCafe(sim, b, 116, 3);
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
                var target = sim.State.BusinessAt(101);
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
            var cafe = OpenCafe(sim, me, 110, 1);
            Assert.IsFalse(Exec(sim, me, CommandType.Sabotage, cafe.Id, (int)SabotageKind.FakeReviews).Success);
            var target = sim.State.BusinessAt(101);
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
            var cafe = OpenCafe(sim, me, 119, 1);
            cafe.AutoRestock = false;
            sim.State.GetPlayer(me).Cash = -50_000;
            AdvanceDays(sim, EconomyCatalog.EvictionDebtDays + 1);
            Assert.AreEqual(0, sim.State.GetProperty(119).TenantId);
            Assert.IsNull(sim.State.GetBusiness(cafe.Id));
        }

        [Test]
        public void PlayerTrade_OfferAccepted_TransfersPropertyAndMoney()
        {
            var sim = NewSim();
            int seller = sim.AddHumanPlayer("Продавец", 0), buyer = sim.AddHumanPlayer("Покупатель", 0);
            sim.State.GetPlayer(seller).Cash = 1_000_000;
            Assert.IsTrue(Exec(sim, seller, CommandType.BuyProperty, 111).Success);
            Assert.IsTrue(Exec(sim, seller, CommandType.OfferPropertyToPlayer, 111, buyer, 90_000).Success);
            var offer = sim.State.Offers.Values.Single();
            long buyerCash = sim.State.GetPlayer(buyer).Cash;
            Assert.IsFalse(Exec(sim, seller, CommandType.RespondOffer, offer.Id, 1).Success, "только адресат отвечает");
            Assert.IsTrue(Exec(sim, buyer, CommandType.RespondOffer, offer.Id, 1).Success);
            Assert.AreEqual(buyer, sim.State.GetProperty(111).OwnerId);
            Assert.AreEqual(buyerCash - 90_000, sim.State.GetPlayer(buyer).Cash);
        }

        [Test]
        public void Simulation_IsDeterministic()
        {
            byte[] Run()
            {
                var sim = NewSim(99);
                int me = sim.AddHumanPlayer("Игрок", 0);
                OpenCafe(sim, me, 110, 2);
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
            var mine = OpenCafe(sim, me, 110, 2);
            AdvanceDays(sim, 30);
            foreach (var p in sim.State.Players.Values)
                TestContext.WriteLine($"{p.Name}: cash={p.Cash} worth={p.NetWorth} heat={p.Heat:F2}");
            foreach (var b in sim.State.Businesses.Values)
                TestContext.WriteLine($"  {b.Name} [{b.Type}] owner={b.OwnerId} cust={b.YesterdayCustomers} rev={b.YesterdayRevenue} profit={b.YesterdayProfit} rep={b.Reputation:F2} staff={b.Employees.Count} price={b.PriceLevel:F2}");
            TestContext.WriteLine($"Кафе игрока существует: {sim.State.GetBusiness(mine.Id) != null}");
            foreach (var p in sim.State.Players.Values)
                Assert.IsTrue(p.Cash > -1_000_000 && p.Cash < 100_000_000, p.Name + " cash " + p.Cash);
            foreach (var b in sim.State.Businesses.Values)
            {
                Assert.IsFalse(float.IsNaN(b.Reputation) || float.IsNaN(b.Stock), b.Name);
                Assert.That(b.Reputation, Is.InRange(0f, 1f));
            }
            Assert.GreaterOrEqual(sim.State.Businesses.Count, 4, "ИИ-бизнесы не должны массово разоряться");
        }

        [Test]
        public void NetBuffer_RoundTripsAllTypes()
        {
            var w = new NetWriter(4);
            w.WriteInt(-123456); w.WriteLong(long.MinValue); w.WriteFloat(3.25f); w.WriteDouble(-1e10);
            w.WriteString("Привет, город"); w.WriteString(null); w.WriteBool(true);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(-123456, r.ReadInt());
            Assert.AreEqual(long.MinValue, r.ReadLong());
            Assert.AreEqual(3.25f, r.ReadFloat());
            Assert.AreEqual(-1e10, r.ReadDouble());
            Assert.AreEqual("Привет, город", r.ReadString());
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
            var world = StarterDistrictContent.CreateWorld();
            var sim = new CitySimulation(world, 5);
            sim.State.TimeScale = 60f;
            var hub = new LoopbackHub();
            var server = new SimServer(sim, hub);
            var client = new SimClient(world, hub.CreateClient(), "Хост", 0x00FF00);
            CommandResult? rentResult = null;
            for (int i = 0; i < 720; i++) // 3 игровых дня: 720 шагов × 0.1 с × 60 мин/с
            {
                server.Update(0.1f);
                client.Update(0.1f);
                if (client.HasSnapshot && rentResult == null && i == 50)
                    client.Send(Command.Make(CommandType.RentProperty, 110), r => rentResult = r);
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
            // Дельты должны быть заметно меньше полного снапшота.
            var snap = new NetWriter();
            sim.State.WriteSnapshot(snap);
            Assert.Less(hub.BytesToClients / (double)server.DeltasSent, snap.Length * 0.5);
        }

        [Test]
        public void TcpTransport_HostAndRemoteClientPlayTogether()
        {
            var world = StarterDistrictContent.CreateWorld();
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

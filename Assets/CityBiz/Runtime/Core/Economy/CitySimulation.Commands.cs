using System;

namespace CityBiz.Core
{
    public sealed partial class CitySimulation
    {
        /// <summary>Единая точка входа для всех действий игроков и ИИ. Сервер вызывает её с id отправителя.</summary>
        public CommandResult Execute(int playerId, Command cmd)
        {
            var player = State.GetPlayer(playerId);
            if (player == null) return CommandResult.Fail("Неизвестный игрок");
            try
            {
                switch (cmd.Type)
                {
                    case CommandType.BuyProperty: return BuyProperty(player, cmd.A);
                    case CommandType.SellPropertyToCity: return SellToCity(player, cmd.A);
                    case CommandType.ListForSale: return ListForSale(player, cmd.A, cmd.Amount);
                    case CommandType.ListForRent: return ListForRent(player, cmd.A, cmd.Amount);
                    case CommandType.RentProperty: return RentProperty(player, cmd.A);
                    case CommandType.EndLease: return EndLease(player, cmd.A);
                    case CommandType.OpenBusiness: return OpenBusiness(player, cmd.A, (BusinessType)cmd.B, cmd.Text);
                    case CommandType.CloseBusiness: return CloseBusiness(player, cmd.A);
                    case CommandType.SetPrice: return SetPrice(player, cmd.A, cmd.F);
                    case CommandType.HireEmployee: return Hire(player, cmd.A, cmd.B);
                    case CommandType.FireEmployee: return Fire(player, cmd.A, cmd.B);
                    case CommandType.RaiseWage: return RaiseWage(player, cmd.A, cmd.B, cmd.Amount);
                    case CommandType.OrderStock: return OrderStock(player, cmd.A, cmd.Amount);
                    case CommandType.SetAutoRestock: return SetAutoRestock(player, cmd.A, cmd.B != 0);
                    case CommandType.UpgradeSecurity: return UpgradeSecurity(player, cmd.A);
                    case CommandType.UpgradeInterior: return UpgradeInterior(player, cmd.A);
                    case CommandType.Repair: return Repair(player, cmd.A);
                    case CommandType.Sabotage: return Sabotage(player, cmd.A, (SabotageKind)cmd.B);
                    case CommandType.OfferPropertyToPlayer: return OfferProperty(player, cmd.A, cmd.B, cmd.Amount);
                    case CommandType.RespondOffer: return RespondOffer(player, cmd.A, cmd.B != 0);
                    case CommandType.TransferMoney: return Transfer(player, cmd.A, cmd.Amount);
                    case CommandType.Chat: return Chat(player, cmd.Text);
                    case CommandType.SetTimeScale: return SetTimeScale(player, cmd.F);
                    default: return CommandResult.Fail("Неизвестная команда");
                }
            }
            finally
            {
                UpdateOpenStates();
            }
        }

        bool TryPay(PlayerState p, long amount, out CommandResult fail)
        {
            if (amount < 0 || p.Cash < amount)
            {
                fail = CommandResult.Fail($"Недостаточно средств: нужно {EconomyCatalog.Money(amount)}");
                return false;
            }
            p.Cash -= amount;
            fail = default;
            return true;
        }

        BusinessState OwnBusiness(PlayerState p, int businessId, out CommandResult fail)
        {
            var b = State.GetBusiness(businessId);
            if (b == null) { fail = CommandResult.Fail("Бизнес не найден"); return null; }
            if (b.OwnerId != p.Id) { fail = CommandResult.Fail("Это не ваш бизнес"); return null; }
            fail = default;
            return b;
        }

        public long PurchasePrice(PropertyState prop) => EconomyRules.PurchasePrice(State, prop);

        public bool CanBuy(int playerId, PropertyState prop) => EconomyRules.CanBuy(playerId, prop);

        CommandResult BuyProperty(PlayerState p, int propertyId)
        {
            var prop = State.GetProperty(propertyId);
            if (prop == null) return CommandResult.Fail("Объект не найден");
            if (prop.OwnerId == p.Id) return CommandResult.Fail("Объект уже ваш");
            if (!CanBuy(p.Id, prop)) return CommandResult.Fail("Владелец не продаёт этот объект");
            long price = PurchasePrice(prop);
            if (!TryPay(p, price, out var fail)) return fail;

            var seller = State.GetPlayer(prop.OwnerId);
            if (seller != null) seller.Cash += price;
            foreach (var o in new System.Collections.Generic.List<TradeOffer>(State.Offers.Values))
                if (o.PropertyId == prop.Id) State.Offers.Remove(o.Id);

            prop.OwnerId = p.Id;
            prop.ForSale = false;
            prop.SalePrice = 0;
            if (prop.TenantId == p.Id) prop.TenantId = 0;
            prop.ForRent = prop.TenantId == 0 && prop.BusinessId == 0;
            Publish(NewsCategory.Property, $"{p.Name} купил «{prop.Def.Name}» за {EconomyCatalog.Money(price)}.", prop.Id);
            RecomputeNetWorth();
            return CommandResult.Ok($"Вы купили «{prop.Def.Name}»");
        }

        CommandResult SellToCity(PlayerState p, int propertyId)
        {
            var prop = State.GetProperty(propertyId);
            if (prop == null || prop.OwnerId != p.Id) return CommandResult.Fail("Это не ваша собственность");
            long price = (long)(State.PropertyValue(prop) * EconomyCatalog.SellBackRatio);
            p.Cash += price;
            prop.OwnerId = 0;
            prop.ForSale = false;
            prop.RentPrice = prop.Def.BaseRent;
            // Свой бизнес остаётся работать — игрок становится арендатором у города.
            var b = State.GetBusiness(prop.BusinessId);
            if (prop.TenantId == 0 && b != null && b.OwnerId == p.Id) prop.TenantId = p.Id;
            prop.ForRent = prop.TenantId == 0;
            RecomputeNetWorth();
            return CommandResult.Ok($"Продано городу за {EconomyCatalog.Money(price)}");
        }

        CommandResult ListForSale(PlayerState p, int propertyId, long price)
        {
            var prop = State.GetProperty(propertyId);
            if (prop == null || prop.OwnerId != p.Id) return CommandResult.Fail("Это не ваша собственность");
            if (price < 0 || price > 100_000_000) return CommandResult.Fail("Некорректная цена");
            prop.ForSale = price > 0;
            prop.SalePrice = price;
            if (price > 0) Publish(NewsCategory.Property, $"«{prop.Def.Name}» выставлен на продажу за {EconomyCatalog.Money(price)}.", prop.Id);
            return CommandResult.Ok(price > 0 ? "Объект выставлен на продажу" : "Объект снят с продажи");
        }

        CommandResult ListForRent(PlayerState p, int propertyId, long rent)
        {
            var prop = State.GetProperty(propertyId);
            if (prop == null || prop.OwnerId != p.Id) return CommandResult.Fail("Это не ваша собственность");
            if (rent < 0 || rent > 1_000_000) return CommandResult.Fail("Некорректная ставка");
            if (prop.Def.Kind != PropertyKind.Apartment && prop.TenantId != 0)
                return CommandResult.Fail("Нельзя менять условия, пока действует договор аренды");
            if (prop.Def.Kind != PropertyKind.Apartment && prop.BusinessId != 0)
                return CommandResult.Fail("В помещении работает ваш бизнес");
            prop.ForRent = rent > 0;
            if (rent > 0) prop.RentPrice = rent;
            return CommandResult.Ok(rent > 0 ? $"Аренда: {EconomyCatalog.Money(rent)}/день" : "Объект снят с аренды");
        }

        CommandResult RentProperty(PlayerState p, int propertyId)
        {
            var prop = State.GetProperty(propertyId);
            if (prop == null) return CommandResult.Fail("Объект не найден");
            if (prop.Def.Kind == PropertyKind.Apartment) return CommandResult.Fail("Квартиры сдаются только жильцам — их можно купить");
            if (prop.OwnerId == p.Id) return CommandResult.Fail("Это ваше помещение — открывайте бизнес без аренды");
            if (prop.TenantId != 0 || prop.BusinessId != 0 || !prop.ForRent) return CommandResult.Fail("Помещение не сдаётся");
            if (!TryPay(p, prop.RentPrice, out var fail)) return fail; // первый день оплачивается сразу
            var owner = State.GetPlayer(prop.OwnerId);
            if (owner != null) owner.Cash += prop.RentPrice;
            prop.TenantId = p.Id;
            prop.ForRent = false;
            prop.DebtDays = 0;
            Publish(NewsCategory.Property, $"{p.Name} арендовал «{prop.Def.Name}».", prop.Id);
            return CommandResult.Ok($"Договор аренды подписан: {EconomyCatalog.Money(prop.RentPrice)}/день");
        }

        CommandResult EndLease(PlayerState p, int propertyId)
        {
            var prop = State.GetProperty(propertyId);
            if (prop == null || prop.TenantId != p.Id) return CommandResult.Fail("Вы не арендуете это помещение");
            var b = State.GetBusiness(prop.BusinessId);
            if (b != null && b.OwnerId == p.Id) RemoveBusiness(b);
            prop.TenantId = 0;
            prop.ForRent = true;
            prop.DebtDays = 0;
            return CommandResult.Ok("Договор аренды расторгнут");
        }

        public bool CanOperate(int playerId, PropertyState prop) => EconomyRules.CanOperate(playerId, prop);

        CommandResult OpenBusiness(PlayerState p, int propertyId, BusinessType type, string name)
        {
            var prop = State.GetProperty(propertyId);
            if (prop == null) return CommandResult.Fail("Объект не найден");
            if (!CanOperate(p.Id, prop)) return CommandResult.Fail("Сначала купите или арендуйте помещение");
            if (prop.BusinessId != 0) return CommandResult.Fail("Здесь уже работает бизнес");
            var def = EconomyCatalog.Get(type);
            if (def == null) return CommandResult.Fail("Неизвестный тип бизнеса");
            if (!def.Allows(prop.Def.Kind)) return CommandResult.Fail($"«{def.Name}» нельзя открыть в помещении типа «{EconomyCatalog.KindName(prop.Def.Kind)}»");
            if (!TryPay(p, def.SetupCost, out var fail)) return fail;

            name = string.IsNullOrWhiteSpace(name) ? NameGenerator.DefaultBusinessName(type, _rng) : name.Trim();
            if (name.Length > 28) name = name.Substring(0, 28);
            var b = new BusinessState
            {
                Id = State.NextBusinessId++, OwnerId = p.Id, PropertyId = prop.Id, Type = type, Name = name,
                Reputation = 0.45f, PriceLevel = 1f, TodayCosts = def.SetupCost,
            };
            State.Businesses[b.Id] = b;
            prop.BusinessId = b.Id;
            prop.ForRent = false;
            Publish(NewsCategory.Business, $"{p.Name} открывает «{name}» ({def.Name}) в «{prop.Def.Name}».", prop.Id);
            return CommandResult.Ok($"«{name}» открыт! Наймите минимум {def.MinStaff} сотрудн.");
        }

        CommandResult CloseBusiness(PlayerState p, int businessId)
        {
            var b = OwnBusiness(p, businessId, out var fail);
            if (b == null) return fail;
            Publish(NewsCategory.Business, $"«{b.Name}» закрывается.", b.PropertyId);
            RemoveBusiness(b);
            var prop = State.GetProperty(b.PropertyId);
            if (prop != null && prop.TenantId == 0) prop.ForRent = prop.OwnerId == 0;
            return CommandResult.Ok("Бизнес закрыт, сотрудники уволены");
        }

        CommandResult SetPrice(PlayerState p, int businessId, float level)
        {
            var b = OwnBusiness(p, businessId, out var fail);
            if (b == null) return fail;
            if (float.IsNaN(level)) return CommandResult.Fail("Некорректная цена");
            b.PriceLevel = Math.Max(0.6f, Math.Min(1.8f, level));
            return CommandResult.Ok($"Уровень цен: {b.PriceLevel:P0}");
        }

        CommandResult Hire(PlayerState p, int businessId, int candidateId)
        {
            var b = OwnBusiness(p, businessId, out var fail);
            if (b == null) return fail;
            if (b.Employees.Count >= b.Def.MaxStaff) return CommandResult.Fail("Штат укомплектован");
            int idx = State.Candidates.FindIndex(c => c.Id == candidateId);
            if (idx < 0) return CommandResult.Fail("Кандидат уже нашёл работу");
            var e = State.Candidates[idx];
            if (!TryPay(p, e.Wage, out fail)) return fail; // подъёмные — одна дневная ставка
            b.TodayCosts += e.Wage;
            State.Candidates.RemoveAt(idx);
            // Работа не по профилю — навык используется частично.
            if (Array.IndexOf(b.Def.Roles, e.Role) < 0) e.Skill *= 0.8f;
            e.Morale = Math.Max(e.Morale, 0.7f);
            b.Employees.Add(e);
            return CommandResult.Ok($"{e.Name} принят на работу");
        }

        CommandResult Fire(PlayerState p, int businessId, int employeeId)
        {
            var b = OwnBusiness(p, businessId, out var fail);
            if (b == null) return fail;
            var e = b.Employees.Find(x => x.Id == employeeId);
            if (e == null) return CommandResult.Fail("Сотрудник не найден");
            if (!TryPay(p, e.Wage, out fail)) return fail; // выходное пособие
            b.TodayCosts += e.Wage;
            b.Employees.Remove(e);
            foreach (var other in b.Employees) other.Morale = Math.Max(0f, other.Morale - 0.04f);
            ReturnToMarket(e);
            return CommandResult.Ok($"{e.Name} уволен");
        }

        CommandResult RaiseWage(PlayerState p, int businessId, int employeeId, long wage)
        {
            var b = OwnBusiness(p, businessId, out var fail);
            if (b == null) return fail;
            var e = b.Employees.Find(x => x.Id == employeeId);
            if (e == null) return CommandResult.Fail("Сотрудник не найден");
            if (wage < 30 || wage > 5_000) return CommandResult.Fail("Некорректная ставка");
            float delta = (wage - e.Wage) / (float)Math.Max(1, e.Wage);
            e.Morale = Math.Max(0f, Math.Min(1f, e.Morale + delta * 0.8f));
            e.Wage = wage;
            return CommandResult.Ok($"Ставка {e.Name}: {EconomyCatalog.Money(wage)}/день");
        }

        CommandResult OrderStock(PlayerState p, int businessId, long units)
        {
            var b = OwnBusiness(p, businessId, out var fail);
            if (b == null) return fail;
            if (!b.Def.UsesStock) return CommandResult.Fail("Этому бизнесу не нужен товар");
            if (units <= 0 || units > 5_000) return CommandResult.Fail("Некорректный объём");
            long cost = (long)Math.Ceiling(units * StockUnitCost(b));
            if (!TryPay(p, cost, out fail)) return fail;
            b.Stock += units;
            b.TodayCosts += cost;
            return CommandResult.Ok($"Закуплено {units} ед. за {EconomyCatalog.Money(cost)}");
        }

        CommandResult SetAutoRestock(PlayerState p, int businessId, bool on)
        {
            var b = OwnBusiness(p, businessId, out var fail);
            if (b == null) return fail;
            b.AutoRestock = on;
            return CommandResult.Ok(on ? "Автозакупка включена" : "Автозакупка выключена");
        }

        CommandResult UpgradeSecurity(PlayerState p, int businessId)
        {
            var b = OwnBusiness(p, businessId, out var fail);
            if (b == null) return fail;
            if (b.Security >= 3) return CommandResult.Fail("Максимальный уровень охраны");
            long cost = EconomyCatalog.SecurityUpgradeCost * (b.Security + 1);
            if (!TryPay(p, cost, out fail)) return fail;
            b.Security++;
            return CommandResult.Ok($"Охрана: уровень {b.Security}");
        }

        CommandResult UpgradeInterior(PlayerState p, int businessId)
        {
            var b = OwnBusiness(p, businessId, out var fail);
            if (b == null) return fail;
            if (b.Level >= 3) return CommandResult.Fail("Интерьер уже максимального уровня");
            long cost = EconomyCatalog.InteriorUpgradeCost * b.Level;
            if (!TryPay(p, cost, out fail)) return fail;
            b.Level++;
            b.TodayCosts += cost;
            return CommandResult.Ok($"Интерьер улучшен до уровня {b.Level}");
        }

        public long RepairCost(PropertyState prop) => EconomyRules.RepairCost(State, prop);

        CommandResult Repair(PlayerState p, int propertyId)
        {
            var prop = State.GetProperty(propertyId);
            if (prop == null) return CommandResult.Fail("Объект не найден");
            if (prop.OwnerId != p.Id && !CanOperate(p.Id, prop)) return CommandResult.Fail("Нет прав на ремонт");
            long cost = RepairCost(prop);
            if (cost <= 0) return CommandResult.Fail("Ремонт не требуется");
            if (!TryPay(p, cost, out var fail)) return fail;
            prop.Condition = 1f;
            var b = State.GetBusiness(prop.BusinessId);
            if (b != null)
            {
                b.Equipment = 1f;
                if (b.OwnerId == p.Id) b.TodayCosts += cost;
            }
            return CommandResult.Ok($"Ремонт выполнен за {EconomyCatalog.Money(cost)}");
        }

        // ---------------- Вредительство ----------------

        public int SabotageCooldownLeft(PlayerState p) => EconomyRules.SabotageCooldownLeft(State, p);

        CommandResult Sabotage(PlayerState p, int targetBusinessId, SabotageKind kind)
        {
            var target = State.GetBusiness(targetBusinessId);
            if (target == null) return CommandResult.Fail("Цель не найдена");
            if (target.OwnerId == p.Id) return CommandResult.Fail("Нельзя вредить собственному бизнесу");
            if ((int)kind >= SabotageCatalog.All.Length) return CommandResult.Fail("Неизвестное действие");
            if (p.Heat >= 1f) return CommandResult.Fail("Полиция следит за вами — затаитесь на время");
            int cd = SabotageCooldownLeft(p);
            if (cd > 0) return CommandResult.Fail($"Подождите ещё {cd / 60}ч {cd % 60}мин");
            var def = SabotageCatalog.Get(kind);
            if (!TryPay(p, def.Cost, out var fail)) return fail;
            p.LastSabotageMinute = State.Time.WholeMinutes;
            p.Heat += def.HeatGain;

            float success = EconomyRules.SabotageSuccess(def, target);
            float detection = EconomyRules.SabotageDetection(def, target, p);
            bool succeeded = _rng.Chance(success);
            bool detected = _rng.Chance(detection);
            string effect = succeeded ? ApplySabotage(target, kind) : null;

            if (detected)
            {
                long fine = (long)(def.Cost * SabotageCatalog.FineMultiplier);
                p.Cash -= fine;
                p.Heat += 0.3f;
                p.Reputation = Math.Max(0f, p.Reputation - 0.15f);
                foreach (var b in State.Businesses.Values)
                    if (b.OwnerId == p.Id) b.Reputation = Math.Max(0f, b.Reputation - 0.08f);
                Publish(NewsCategory.Police, $"Полиция задержала исполнителей: заказчик — {p.Name}. Штраф {EconomyCatalog.Money(fine)}.", target.PropertyId);
            }

            if (succeeded)
            {
                Publish(NewsCategory.Sabotage, $"«{target.Name}»: {effect}", target.PropertyId);
                return CommandResult.Ok(detected ? "Сработало, но вас раскрыли!" : "Операция прошла успешно");
            }
            return detected ? CommandResult.Fail("Провал: исполнителей поймали") : CommandResult.Fail("Операция сорвалась, но вас не заметили");
        }

        string ApplySabotage(BusinessState target, SabotageKind kind)
        {
            var prop = State.GetProperty(target.PropertyId);
            switch (kind)
            {
                case SabotageKind.FakeReviews:
                    target.Reputation = Math.Max(0f, target.Reputation - 0.15f);
                    return "в сети появилась волна негативных отзывов.";
                case SabotageKind.BreakEquipment:
                    target.Equipment = Math.Max(0.1f, target.Equipment - 0.45f);
                    return "сломалось оборудование, обслуживание замедлилось.";
                case SabotageKind.PowerOutage:
                    target.ClosedUntilMinute = State.Time.WholeMinutes + 5 * 60;
                    return "авария электросети, заведение временно закрыто.";
                case SabotageKind.PoachEmployee:
                    if (target.Employees.Count == 0) return "сотрудников переманить не удалось — штат пуст.";
                    var best = target.Employees[0];
                    foreach (var e in target.Employees) if (e.Skill > best.Skill) best = e;
                    target.Employees.Remove(best);
                    ReturnToMarket(best);
                    return $"{best.Name} неожиданно уволился.";
                case SabotageKind.TipOffInspection:
                    float state = Math.Min(target.Equipment, prop != null ? prop.Condition : 1f);
                    if (state >= 0.6f && Quality(target) >= 0.5f) return "прошла внеплановая проверка — нарушений нет.";
                    var owner = State.GetPlayer(target.OwnerId);
                    if (owner != null) owner.Cash -= 15_000;
                    target.ClosedUntilMinute = State.Time.WholeMinutes + 4 * 60;
                    target.Reputation = Math.Max(0f, target.Reputation - 0.05f);
                    return "санинспекция выписала штраф и закрыла точку на 4 часа.";
                default:
                    return "ничего не произошло.";
            }
        }

        // ---------------- Взаимодействие игроков ----------------

        CommandResult OfferProperty(PlayerState p, int propertyId, int buyerId, long price)
        {
            var prop = State.GetProperty(propertyId);
            if (prop == null || prop.OwnerId != p.Id) return CommandResult.Fail("Это не ваша собственность");
            var buyer = State.GetPlayer(buyerId);
            if (buyer == null || buyer.Id == p.Id) return CommandResult.Fail("Покупатель не найден");
            if (price <= 0) return CommandResult.Fail("Некорректная цена");
            var offer = new TradeOffer
            {
                Id = State.NextOfferId++, FromPlayerId = p.Id, ToPlayerId = buyerId, PropertyId = propertyId, Price = price,
                ExpiresMinute = State.Time.WholeMinutes + GameTime.MinutesPerDay,
            };
            State.Offers[offer.Id] = offer;
            Publish(NewsCategory.Property, $"{p.Name} предлагает {buyer.Name} купить «{prop.Def.Name}» за {EconomyCatalog.Money(price)}.", prop.Id);
            if (buyer.IsAI) RivalAI.ConsiderOffer(this, buyer, offer);
            return CommandResult.Ok("Предложение отправлено");
        }

        CommandResult RespondOffer(PlayerState p, int offerId, bool accept)
        {
            if (!State.Offers.TryGetValue(offerId, out var offer) || offer.ToPlayerId != p.Id) return CommandResult.Fail("Предложение не найдено");
            State.Offers.Remove(offerId);
            if (!accept) return CommandResult.Ok("Предложение отклонено");
            var prop = State.GetProperty(offer.PropertyId);
            var seller = State.GetPlayer(offer.FromPlayerId);
            if (prop == null || seller == null || prop.OwnerId != seller.Id) return CommandResult.Fail("Продавец больше не владеет объектом");
            if (!TryPay(p, offer.Price, out var fail)) return fail;
            seller.Cash += offer.Price;
            prop.OwnerId = p.Id;
            prop.ForSale = false;
            if (prop.TenantId == p.Id) prop.TenantId = 0;
            var b = State.GetBusiness(prop.BusinessId);
            if (b != null && b.OwnerId == seller.Id && prop.TenantId == 0) prop.TenantId = seller.Id; // бизнес продавца остаётся арендатором
            Publish(NewsCategory.Property, $"Сделка: {p.Name} купил у {seller.Name} «{prop.Def.Name}» за {EconomyCatalog.Money(offer.Price)}.", prop.Id);
            RecomputeNetWorth();
            return CommandResult.Ok("Сделка завершена");
        }

        CommandResult Transfer(PlayerState p, int toId, long amount)
        {
            var to = State.GetPlayer(toId);
            if (to == null || to.Id == p.Id) return CommandResult.Fail("Получатель не найден");
            if (amount <= 0) return CommandResult.Fail("Некорректная сумма");
            if (!TryPay(p, amount, out var fail)) return fail;
            to.Cash += amount;
            Publish(NewsCategory.Info, $"{p.Name} перевёл {to.Name} {EconomyCatalog.Money(amount)}.");
            return CommandResult.Ok("Перевод выполнен");
        }

        CommandResult Chat(PlayerState p, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return CommandResult.Fail("Пустое сообщение");
            text = text.Trim();
            if (text.Length > 140) text = text.Substring(0, 140);
            Publish(NewsCategory.Chat, $"{p.Name}: {text}");
            return CommandResult.Ok();
        }

        CommandResult SetTimeScale(PlayerState p, float scale)
        {
            if (p.Id != HostPlayerId) return CommandResult.Fail("Скоростью времени управляет хост");
            if (float.IsNaN(scale)) return CommandResult.Fail("Некорректное значение");
            State.TimeScale = Math.Max(0f, Math.Min(60f, scale));
            return CommandResult.Ok();
        }
    }
}

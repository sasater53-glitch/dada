using System.Collections.Generic;
using CityBiz.Core.Net;

namespace CityBiz.Core
{
    public sealed class PlayerState
    {
        public int Id;
        public string Name;
        public long Cash;
        public bool IsAI;
        public bool Online;
        public uint Color;
        public float Reputation = 0.5f; // деловая репутация: падает при пойманном вредительстве
        public float Heat;              // подозрения полиции, затухает со временем
        public long LastSabotageMinute = -100_000;
        public long NetWorth;
        public long YesterdayIncome;

        public void Write(NetWriter w)
        {
            w.WriteInt(Id); w.WriteString(Name); w.WriteLong(Cash); w.WriteBool(IsAI); w.WriteBool(Online);
            w.WriteVarULong(Color); w.WriteFloat(Reputation); w.WriteFloat(Heat); w.WriteLong(LastSabotageMinute);
            w.WriteLong(NetWorth); w.WriteLong(YesterdayIncome);
        }

        public void Read(NetReader r)
        {
            Id = r.ReadInt(); Name = r.ReadString(); Cash = r.ReadLong(); IsAI = r.ReadBool(); Online = r.ReadBool();
            Color = (uint)r.ReadVarULong(); Reputation = r.ReadFloat(); Heat = r.ReadFloat(); LastSabotageMinute = r.ReadLong();
            NetWorth = r.ReadLong(); YesterdayIncome = r.ReadLong();
        }
    }

    public sealed class PropertyState
    {
        public int Id;
        public int OwnerId;     // 0 — городской фонд
        public int TenantId;    // игрок-арендатор (0 — нет)
        public bool ForSale;
        public long SalePrice;
        public bool ForRent = true;
        public long RentPrice;
        public float Condition = 0.9f;
        public int BusinessId;
        public int DebtDays;
        public long LastIncome;       // доход владельца за прошлый день (аренда)

        public PropertyDef Def; // локальная ссылка, не сериализуется

        /// <summary>Кто управляет помещением: арендатор, а если его нет — владелец.</summary>
        public int OperatorId => TenantId != 0 ? TenantId : OwnerId;

        public void Write(NetWriter w)
        {
            w.WriteInt(Id); w.WriteInt(OwnerId); w.WriteInt(TenantId); w.WriteBool(ForSale); w.WriteLong(SalePrice);
            w.WriteBool(ForRent); w.WriteLong(RentPrice); w.WriteFloat(Condition); w.WriteInt(BusinessId);
            w.WriteInt(DebtDays); w.WriteLong(LastIncome);
        }

        public void Read(NetReader r)
        {
            Id = r.ReadInt(); OwnerId = r.ReadInt(); TenantId = r.ReadInt(); ForSale = r.ReadBool(); SalePrice = r.ReadLong();
            ForRent = r.ReadBool(); RentPrice = r.ReadLong(); Condition = r.ReadFloat(); BusinessId = r.ReadInt();
            DebtDays = r.ReadInt(); LastIncome = r.ReadLong();
        }
    }

    public sealed class EmployeeState
    {
        public int Id;
        public string Name;
        public StaffRole Role;
        public float Skill;
        public long Wage;      // в день
        public float Morale = 0.8f;
        public bool Female;
        public int Seed;       // внешний вид NPC

        public void Write(NetWriter w)
        {
            w.WriteInt(Id); w.WriteString(Name); w.WriteByte((byte)Role); w.WriteFloat(Skill); w.WriteLong(Wage);
            w.WriteFloat(Morale); w.WriteBool(Female); w.WriteInt(Seed);
        }

        public void Read(NetReader r)
        {
            Id = r.ReadInt(); Name = r.ReadString(); Role = (StaffRole)r.ReadByte(); Skill = r.ReadFloat(); Wage = r.ReadLong();
            Morale = r.ReadFloat(); Female = r.ReadBool(); Seed = r.ReadInt();
        }
    }

    public sealed class BusinessState
    {
        public int Id;
        public int OwnerId;
        public int PropertyId;
        public BusinessType Type;
        public string Name;
        public float PriceLevel = 1f;     // 0.6 .. 1.8
        public float Reputation = 0.5f;   // 0..1, отзывы клиентов
        public float EquipmentCondition = 1f; // 0..1, изнашивается и ломается при вредительстве
        public int Interior = 1, Marketing, EquipmentLevel = 1, Assortment = 1; // уровни развития
        public int Security;              // 0..3
        public float Stock;
        public bool AutoRestock = true;
        public long ClosedUntilMinute;    // принудительное закрытие (авария, проверка)
        public bool IsOpenNow;
        public float Satisfaction = 0.6f;
        public readonly List<EmployeeState> Employees = new List<EmployeeState>();

        public int TodayCustomers, YesterdayCustomers, LastHourCustomers;
        public long TodayRevenue, TodayCosts, YesterdayRevenue, YesterdayProfit, TotalProfit;
        public float CustomerCarry; // дробные покупатели между часами (на сервере)

        public BusinessTypeDef Def => EconomyCatalog.Get(Type);

        public int GetLevel(UpgradeKind kind)
        {
            switch (kind)
            {
                case UpgradeKind.Interior: return Interior;
                case UpgradeKind.Marketing: return Marketing;
                case UpgradeKind.Equipment: return EquipmentLevel;
                case UpgradeKind.Assortment: return Assortment;
                default: return Security;
            }
        }

        public void SetLevel(UpgradeKind kind, int level)
        {
            switch (kind)
            {
                case UpgradeKind.Interior: Interior = level; break;
                case UpgradeKind.Marketing: Marketing = level; break;
                case UpgradeKind.Equipment: EquipmentLevel = level; break;
                case UpgradeKind.Assortment: Assortment = level; break;
                default: Security = level; break;
            }
        }

        /// <summary>Общий уровень развития предприятия 0..1 (для UI и рейтинга).</summary>
        public float DevelopmentScore
        {
            get
            {
                float s = 0f, max = 0f;
                foreach (var u in EconomyCatalog.AllUpgrades)
                {
                    s += GetLevel(u.Kind) - u.MinLevel;
                    max += u.MaxLevel - u.MinLevel;
                }
                return max > 0 ? s / max : 0f;
            }
        }

        public int MaxStaff => Def.MaxStaff + (EquipmentLevel - 1);

        public float AverageSkill
        {
            get
            {
                if (Employees.Count == 0) return 0f;
                float s = 0f;
                foreach (var e in Employees) s += e.Skill;
                return s / Employees.Count;
            }
        }

        public float AverageMorale
        {
            get
            {
                if (Employees.Count == 0) return 0f;
                float s = 0f;
                foreach (var e in Employees) s += e.Morale;
                return s / Employees.Count;
            }
        }

        public long DailyWages
        {
            get
            {
                long s = 0;
                foreach (var e in Employees) s += e.Wage;
                return s;
            }
        }

        public void Write(NetWriter w)
        {
            w.WriteInt(Id); w.WriteInt(OwnerId); w.WriteInt(PropertyId); w.WriteByte((byte)Type); w.WriteString(Name);
            w.WriteFloat(PriceLevel); w.WriteFloat(Reputation); w.WriteFloat(EquipmentCondition); w.WriteInt(Interior); w.WriteInt(Marketing); w.WriteInt(EquipmentLevel); w.WriteInt(Assortment); w.WriteInt(Security);
            w.WriteFloat(Stock); w.WriteBool(AutoRestock); w.WriteLong(ClosedUntilMinute); w.WriteBool(IsOpenNow); w.WriteFloat(Satisfaction);
            w.WriteInt(Employees.Count);
            foreach (var e in Employees) e.Write(w);
            w.WriteInt(TodayCustomers); w.WriteInt(YesterdayCustomers); w.WriteInt(LastHourCustomers);
            w.WriteLong(TodayRevenue); w.WriteLong(TodayCosts); w.WriteLong(YesterdayRevenue); w.WriteLong(YesterdayProfit); w.WriteLong(TotalProfit);
            w.WriteFloat(CustomerCarry);
        }

        public void Read(NetReader r)
        {
            Id = r.ReadInt(); OwnerId = r.ReadInt(); PropertyId = r.ReadInt(); Type = (BusinessType)r.ReadByte(); Name = r.ReadString();
            PriceLevel = r.ReadFloat(); Reputation = r.ReadFloat(); EquipmentCondition = r.ReadFloat(); Interior = r.ReadInt(); Marketing = r.ReadInt(); EquipmentLevel = r.ReadInt(); Assortment = r.ReadInt(); Security = r.ReadInt();
            Stock = r.ReadFloat(); AutoRestock = r.ReadBool(); ClosedUntilMinute = r.ReadLong(); IsOpenNow = r.ReadBool(); Satisfaction = r.ReadFloat();
            int n = r.ReadInt();
            Employees.Clear();
            for (int i = 0; i < n; i++) { var e = new EmployeeState(); e.Read(r); Employees.Add(e); }
            TodayCustomers = r.ReadInt(); YesterdayCustomers = r.ReadInt(); LastHourCustomers = r.ReadInt();
            TodayRevenue = r.ReadLong(); TodayCosts = r.ReadLong(); YesterdayRevenue = r.ReadLong(); YesterdayProfit = r.ReadLong(); TotalProfit = r.ReadLong();
            CustomerCarry = r.ReadFloat();
        }
    }

    /// <summary>Предложение о продаже недвижимости между игроками.</summary>
    public sealed class TradeOffer
    {
        public int Id;
        public int FromPlayerId;
        public int ToPlayerId;
        public int PropertyId;
        public long Price;
        public long ExpiresMinute;

        public void Write(NetWriter w)
        {
            w.WriteInt(Id); w.WriteInt(FromPlayerId); w.WriteInt(ToPlayerId); w.WriteInt(PropertyId); w.WriteLong(Price); w.WriteLong(ExpiresMinute);
        }

        public void Read(NetReader r)
        {
            Id = r.ReadInt(); FromPlayerId = r.ReadInt(); ToPlayerId = r.ReadInt(); PropertyId = r.ReadInt(); Price = r.ReadLong(); ExpiresMinute = r.ReadLong();
        }
    }

    public enum NewsCategory : byte { Info, Business, Property, Sabotage, Police, Chat, Weather }

    public struct NewsItem
    {
        public long Minute;
        public NewsCategory Category;
        public string Text;
        public int PropertyId; // для привязки к месту в мире (0 — нет)
    }
}

using CityBiz.Core.Net;

namespace CityBiz.Core
{
    public enum CommandType : byte
    {
        None,
        BuyProperty,          // A = propertyId
        SellPropertyToCity,   // A = propertyId
        ListForSale,          // A = propertyId, Amount = цена (0 — снять с продажи)
        ListForRent,          // A = propertyId, Amount = аренда в день (0 — снять)
        RentProperty,         // A = propertyId
        EndLease,             // A = propertyId
        OpenBusiness,         // A = propertyId, B = BusinessType, Text = название
        CloseBusiness,        // A = businessId
        SetPrice,             // A = businessId, F = уровень цены
        HireEmployee,         // A = businessId, B = candidateId
        FireEmployee,         // A = businessId, B = employeeId
        RaiseWage,            // A = businessId, B = employeeId, Amount = новая ставка
        OrderStock,           // A = businessId, Amount = единиц
        SetAutoRestock,       // A = businessId, B = 0/1
        UpgradeSecurity,      // A = businessId
        UpgradeInterior,      // A = businessId
        Repair,               // A = propertyId
        Sabotage,             // A = businessId цели, B = SabotageKind
        OfferPropertyToPlayer,// A = propertyId, B = покупатель, Amount = цена
        RespondOffer,         // A = offerId, B = 1 принять / 0 отклонить
        TransferMoney,        // A = получатель, Amount = сумма
        Chat,                 // Text
        SetTimeScale,         // F = минут в секунду (только хост)
    }

    /// <summary>
    /// Команда игрока. Все изменения состояния идут только через команды, которые сервер
    /// валидирует — клиент не может «нарисовать» себе деньги.
    /// </summary>
    public struct Command
    {
        public CommandType Type;
        public int A, B;
        public long Amount;
        public float F;
        public string Text;

        public static Command Make(CommandType type, int a = 0, int b = 0, long amount = 0, float f = 0f, string text = null) =>
            new Command { Type = type, A = a, B = b, Amount = amount, F = f, Text = text };

        public void Write(NetWriter w)
        {
            w.WriteByte((byte)Type); w.WriteInt(A); w.WriteInt(B); w.WriteLong(Amount); w.WriteFloat(F); w.WriteString(Text);
        }

        public static Command Read(NetReader r) => new Command
        {
            Type = (CommandType)r.ReadByte(), A = r.ReadInt(), B = r.ReadInt(), Amount = r.ReadLong(), F = r.ReadFloat(), Text = r.ReadString(),
        };

        public override string ToString() => $"{Type}(A={A}, B={B}, Amount={Amount}, F={F}, Text={Text})";
    }

    public struct CommandResult
    {
        public bool Success;
        public string Message;

        public static CommandResult Ok(string msg = null) => new CommandResult { Success = true, Message = msg };
        public static CommandResult Fail(string msg) => new CommandResult { Success = false, Message = msg };
    }
}

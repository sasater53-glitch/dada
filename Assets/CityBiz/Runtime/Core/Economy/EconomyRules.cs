using System;

namespace CityBiz.Core
{
    /// <summary>
    /// Чистые правила экономики, общие для сервера (валидация команд) и клиента (подсказки в UI).
    /// Клиент только показывает их результат — решение всё равно принимает сервер.
    /// </summary>
    public static class EconomyRules
    {
        public static bool CanBuy(int playerId, PropertyState prop) =>
            prop.OwnerId != playerId && (prop.OwnerId == 0 || prop.ForSale);

        public static long PurchasePrice(CityState s, PropertyState prop) => prop.OwnerId == 0 ? s.PropertyValue(prop) : prop.SalePrice;

        public static bool CanOperate(int playerId, PropertyState prop) =>
            prop.TenantId == playerId || (prop.OwnerId == playerId && prop.TenantId == 0);

        public static bool CanRent(int playerId, PropertyState prop) =>
            prop.OwnerId != playerId && prop.TenantId == 0 && prop.BusinessId == 0 && prop.ForRent;

        /// <summary>Стоимость перехода на уровень level: растёт с уровнем и площадью помещения.</summary>
        public static long UpgradeCost(UpgradeDef u, int level, PropertyDef prop)
        {
            float areaFactor = Math.Max(0.4f, Math.Min(4f, (float)Math.Sqrt(prop.Area / 100f)));
            int step = level - u.MinLevel; // 1 для первого улучшения
            return (long)(Math.Round(u.BaseCost * areaFactor * step * (1f + 0.35f * (step - 1)) / 100f) * 100f);
        }

        public static long NextUpgradeCost(BusinessState b, PropertyState prop, UpgradeKind kind)
        {
            var u = EconomyCatalog.Upgrade(kind);
            int next = b.GetLevel(kind) + 1;
            return next > u.MaxLevel ? -1 : UpgradeCost(u, next, prop.Def);
        }

        public static long RepairCost(CityState s, PropertyState prop)
        {
            float cost = (1f - prop.Condition) * EconomyCatalog.RepairCostPerPoint * Math.Max(0.4f, prop.Def.Area / 100f);
            var b = s.GetBusiness(prop.BusinessId);
            if (b != null) cost += (1f - b.EquipmentCondition) * 5_000f * (0.6f + 0.2f * b.EquipmentLevel);
            return (long)Math.Ceiling(cost);
        }

        public static int SabotageCooldownLeft(CityState s, PlayerState p) =>
            (int)Math.Max(0, p.LastSabotageMinute + SabotageCatalog.CooldownMinutes - s.Time.WholeMinutes);

        public static float SabotageSuccess(SabotageDef def, BusinessState target) =>
            def.SuccessChance * (1f - SabotageCatalog.SecuritySuccessPenalty * target.Security);

        public static float SabotageDetection(SabotageDef def, BusinessState target, PlayerState attacker) =>
            Math.Min(0.95f, def.DetectionChance + SabotageCatalog.SecurityDetectionBonus * target.Security + attacker.Heat * 0.1f);

        public static float StockUnitCost(CityState s, BusinessState b)
        {
            float cost = b.Def.StockUnitCost;
            foreach (var w in s.Businesses.Values)
                if (w.Type == BusinessType.Warehouse && w.OwnerId == b.OwnerId && w.Employees.Count >= w.Def.MinStaff)
                    return cost * 0.7f;
            return cost;
        }
    }
}

namespace CityBiz.Core
{
    public enum SabotageKind : byte { FakeReviews, BreakEquipment, PowerOutage, PoachEmployee, TipOffInspection }

    public sealed class SabotageDef
    {
        public SabotageKind Kind;
        public string Name;
        public string Description;
        public long Cost;
        public float SuccessChance;
        public float DetectionChance;
        public float HeatGain;
    }

    public static class SabotageCatalog
    {
        public const int CooldownMinutes = 6 * 60;
        public const float SecuritySuccessPenalty = 0.17f;  // за уровень охраны цели
        public const float SecurityDetectionBonus = 0.15f;
        public const float FineMultiplier = 2.5f;

        static readonly SabotageDef[] Defs =
        {
            new SabotageDef { Kind = SabotageKind.FakeReviews, Name = "Фейковые отзывы", Cost = 3_000, SuccessChance = 0.8f, DetectionChance = 0.2f, HeatGain = 0.1f,
                Description = "Репутация цели снижается на 15%." },
            new SabotageDef { Kind = SabotageKind.BreakEquipment, Name = "Испортить оборудование", Cost = 7_000, SuccessChance = 0.65f, DetectionChance = 0.35f, HeatGain = 0.2f,
                Description = "Оборудование ломается: качество обслуживания падает до ремонта." },
            new SabotageDef { Kind = SabotageKind.PowerOutage, Name = "Отключить электричество", Cost = 9_000, SuccessChance = 0.6f, DetectionChance = 0.3f, HeatGain = 0.25f,
                Description = "Точка закрыта на 5 игровых часов." },
            new SabotageDef { Kind = SabotageKind.PoachEmployee, Name = "Переманить сотрудника", Cost = 4_000, SuccessChance = 0.55f, DetectionChance = 0.1f, HeatGain = 0.05f,
                Description = "Лучший сотрудник цели увольняется и появляется на рынке труда." },
            new SabotageDef { Kind = SabotageKind.TipOffInspection, Name = "Наводка санинспекции", Cost = 2_500, SuccessChance = 0.9f, DetectionChance = 0.05f, HeatGain = 0.05f,
                Description = "Если оборудование или помещение в плохом состоянии — штраф и закрытие на 4 часа." },
        };

        public static SabotageDef Get(SabotageKind kind) => Defs[(int)kind];
        public static SabotageDef[] All => Defs;
    }
}

namespace CityBiz.Core
{
    public static class NameGenerator
    {
        static readonly string[] MaleFirst = { "Алексей", "Иван", "Дмитрий", "Максим", "Артём", "Никита", "Сергей", "Павел", "Егор", "Кирилл", "Роман", "Олег", "Тимур", "Денис" };
        static readonly string[] FemaleFirst = { "Анна", "Мария", "Екатерина", "Ольга", "Дарья", "Алина", "Полина", "Виктория", "Софья", "Ксения", "Юлия", "Ирина", "Вера", "Лилия" };
        static readonly string[] Surnames = { "Смирнов", "Кузнецов", "Попов", "Соколов", "Лебедев", "Козлов", "Новиков", "Морозов", "Волков", "Зайцев", "Павлов", "Семенов", "Голубев", "Орлов", "Белов", "Комаров" };

        public static string Person(DeterministicRandom rng, bool female)
        {
            string first = female ? rng.Pick(FemaleFirst) : rng.Pick(MaleFirst);
            string last = rng.Pick(Surnames) + (female ? "а" : "");
            return first + " " + last;
        }

        public static string Surname(DeterministicRandom rng) => rng.Pick(Surnames);

        public static string DefaultBusinessName(BusinessType type, DeterministicRandom rng)
        {
            string[] options;
            switch (type)
            {
                case BusinessType.Cafe: options = new[] { "Кофейня «Зерно»", "Кафе «Утро»", "Кофе и Точка", "Café Bloom" }; break;
                case BusinessType.Restaurant: options = new[] { "Ресторан «Терраса»", "Бистро «Платан»", "Trattoria Verde", "Гриль-бар «Уголь»" }; break;
                case BusinessType.ClothingStore: options = new[] { "Бутик «Линия»", "Urban Wear", "Магазин «Стиль»", "Mode&Co" }; break;
                case BusinessType.Supermarket: options = new[] { "Маркет «Соседи»", "Супермаркет «Корзина»", "Fresh Market", "Продукты 24" }; break;
                case BusinessType.Office: options = new[] { "Консалтинг «Вектор»", "Studio Grid", "Агентство «Пиксель»", "Альфа Аналитика" }; break;
                case BusinessType.Warehouse: options = new[] { "Логистика «Склад-Про»", "Терминал «Восток»", "Склад №7" }; break;
                default: options = new[] { "Бизнес" }; break;
            }
            return rng.Pick(options);
        }
    }
}

using System.Collections.Generic;

namespace CityBiz.Core
{
    public static class NameGenerator
    {
        static readonly string[] MaleFirst = { "Алексей", "Иван", "Дмитрий", "Максим", "Артём", "Никита", "Сергей", "Павел", "Егор", "Кирилл", "Роман", "Олег", "Тимур", "Денис" };
        static readonly string[] FemaleFirst = { "Анна", "Мария", "Екатерина", "Ольга", "Дарья", "Алина", "Полина", "Виктория", "Софья", "Ксения", "Юлия", "Ирина", "Вера", "Лилия" };
        static readonly string[] Surnames = { "Смирнов", "Кузнецов", "Попов", "Соколов", "Лебедев", "Козлов", "Новиков", "Морозов", "Волков", "Зайцев", "Павлов", "Семенов", "Голубев", "Орлов", "Белов", "Комаров" };

        static readonly Dictionary<BusinessType, string[]> Brands = new Dictionary<BusinessType, string[]>
        {
            { BusinessType.Cafe, new[] { "Кофейня «Зерно»", "Café Bloom", "Кофе и Точка", "Brew Lab", "Шоколадница-Лайт", "Coffee Mood" } },
            { BusinessType.Restaurant, new[] { "Trattoria Verde", "Гриль-бар «Уголь»", "Ресторан «Терраса»", "Asia House", "Бистро «Платан»" } },
            { BusinessType.FastFood, new[] { "Burger Point", "Пицца Мания", "Суши Wok", "Шаурма №1", "Taco Loco", "Блинная «Тесто»" } },
            { BusinessType.IceCream, new[] { "Gelato", "Пломбир+", "Frozen Joy", "Сладкий остров" } },
            { BusinessType.ClothingStore, new[] { "Urban Wear", "Mode&Co", "Линия", "Basic Lab", "Denim Story", "Nord Style", "Sport Zone", "Kids Fashion" } },
            { BusinessType.Electronics, new[] { "Техно Мир", "Gadget Store", "Pixel", "Мобильный центр", "Smart Home" } },
            { BusinessType.Toys, new[] { "Детский мир игрушек", "Toy Planet", "Кубик", "Hobby Games" } },
            { BusinessType.Cosmetics, new[] { "Beauty Box", "Аромат", "Парфюм Лайн", "Natura" } },
            { BusinessType.Bookstore, new[] { "Читай-ка", "Книжный квартал", "Page Turner" } },
            { BusinessType.Jewelry, new[] { "Золотая линия", "Diamond Hall", "Ювелирный дом «Аметист»" } },
            { BusinessType.Supermarket, new[] { "Гипермаркет «Корзина»", "Fresh Market", "Продукты 24" } },
            { BusinessType.Warehouse, new[] { "Склад-Про", "Логистика ТЦ", "Кладовая" } },
        };

        public static string Person(DeterministicRandom rng, bool female)
        {
            string first = female ? rng.Pick(FemaleFirst) : rng.Pick(MaleFirst);
            string last = rng.Pick(Surnames) + (female ? "а" : "");
            return first + " " + last;
        }

        public static string Brand(BusinessType type, DeterministicRandom rng) =>
            Brands.TryGetValue(type, out var list) ? rng.Pick(list) : "Магазин";

        public static string DefaultBusinessName(BusinessType type, DeterministicRandom rng) => Brand(type, rng);
    }
}

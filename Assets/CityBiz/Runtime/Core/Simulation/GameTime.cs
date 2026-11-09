namespace CityBiz.Core
{
    /// <summary>Игровое время в минутах от начала мира. День 1 начинается в понедельник 00:00.</summary>
    public readonly struct GameTime
    {
        public const int MinutesPerHour = 60;
        public const int MinutesPerDay = 1440;

        static readonly string[] DayNames = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };

        public readonly double TotalMinutes;

        public GameTime(double totalMinutes) { TotalMinutes = totalMinutes; }

        public long WholeMinutes => (long)TotalMinutes;
        public int Day => (int)(WholeMinutes / MinutesPerDay) + 1;
        public int Hour => (int)(WholeMinutes % MinutesPerDay / MinutesPerHour);
        public int Minute => (int)(WholeMinutes % MinutesPerHour);
        public int DayOfWeek => (Day - 1) % 7;
        public bool IsWeekend => DayOfWeek >= 5;

        /// <summary>Время суток 0..24 с дробной частью — для плавного движения солнца.</summary>
        public float HourOfDay => (float)(TotalMinutes % MinutesPerDay / MinutesPerHour);

        public static GameTime FromDayHour(int day, int hour, int minute = 0) =>
            new GameTime((day - 1) * (double)MinutesPerDay + hour * MinutesPerHour + minute);

        public string Clock => $"{Hour:00}:{Minute:00}";
        public override string ToString() => $"День {Day} ({DayNames[DayOfWeek]}) {Clock}";
    }
}

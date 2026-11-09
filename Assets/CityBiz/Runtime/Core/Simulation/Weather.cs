namespace CityBiz.Core
{
    public enum WeatherKind : byte { Clear, Cloudy, Rain, Storm, Fog }

    public struct WeatherState
    {
        public WeatherKind Kind;
        public int HoursLeft;

        public static string DisplayName(WeatherKind k)
        {
            switch (k)
            {
                case WeatherKind.Clear: return "Ясно";
                case WeatherKind.Cloudy: return "Облачно";
                case WeatherKind.Rain: return "Дождь";
                case WeatherKind.Storm: return "Гроза";
                case WeatherKind.Fog: return "Туман";
                default: return k.ToString();
            }
        }

        public bool IsWet => Kind == WeatherKind.Rain || Kind == WeatherKind.Storm;

        /// <summary>Множитель уличного трафика: в дождь людей на улице меньше.</summary>
        public float FootTrafficMultiplier
        {
            get
            {
                switch (Kind)
                {
                    case WeatherKind.Rain: return 0.65f;
                    case WeatherKind.Storm: return 0.4f;
                    case WeatherKind.Fog: return 0.85f;
                    case WeatherKind.Cloudy: return 0.95f;
                    default: return 1f;
                }
            }
        }
    }

    /// <summary>Марковская модель погоды — решается только на сервере и реплицируется.</summary>
    public static class WeatherModel
    {
        // Строки: текущая погода, столбцы: вероятность следующей (Clear, Cloudy, Rain, Storm, Fog).
        static readonly float[,] Transitions =
        {
            { 0.55f, 0.35f, 0.04f, 0.01f, 0.05f },
            { 0.35f, 0.30f, 0.25f, 0.05f, 0.05f },
            { 0.10f, 0.45f, 0.30f, 0.10f, 0.05f },
            { 0.05f, 0.30f, 0.60f, 0.05f, 0.00f },
            { 0.45f, 0.40f, 0.10f, 0.00f, 0.05f },
        };

        public static WeatherState Next(WeatherState current, DeterministicRandom rng)
        {
            float roll = rng.NextFloat();
            int row = (int)current.Kind;
            var next = WeatherKind.Clear;
            for (int i = 0; i < 5; i++)
            {
                roll -= Transitions[row, i];
                if (roll <= 0f) { next = (WeatherKind)i; break; }
            }
            int duration = next == WeatherKind.Storm ? rng.Range(1, 3) : rng.Range(3, 9);
            return new WeatherState { Kind = next, HoursLeft = duration };
        }
    }
}

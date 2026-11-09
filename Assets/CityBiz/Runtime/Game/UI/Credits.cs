namespace CityBiz.Game
{
    /// <summary>
    /// Титры и лицензии сторонних ассетов (дублирует THIRD_PARTY_ASSETS.md). Строки CC BY обязательны к показу в игре.
    /// </summary>
    public static class Credits
    {
        public static readonly string[] RequiredAttributions =
        {
            "\"Local Forecast - Elevator\" Kevin MacLeod (incompetech.com) Licensed under Creative Commons: By Attribution 3.0 http://creativecommons.org/licenses/by/3.0/",
            "\"Casa Bossa Nova\" Kevin MacLeod (incompetech.com) Licensed under Creative Commons: By Attribution 3.0 http://creativecommons.org/licenses/by/3.0/",
            "\"WWS Escalator\" Work With Sounds / La Fonderie (via Wikimedia Commons) Licensed under Creative Commons: By Attribution 4.0 https://creativecommons.org/licenses/by/4.0/",
        };

        public static readonly (string title, string body)[] Sections =
        {
            ("Kenney (CC0 1.0) — www.kenney.nl",
             "Furniture Kit, Food Kit, Mini Characters 1 (модели, анимации), Impact Sounds (шаги), Interface Sounds (звуки интерфейса), RPG Audio (двери, монеты, ткань).\nЛицензия: https://creativecommons.org/publicdomain/zero/1.0/"),
            ("Poly Haven (CC0 1.0) — polyhaven.com",
             "Модели: potted_plant_01/02/04, pachira_aquatica_01, modular_street_seating, steel_frame_shelves_01, wooden_display_shelves_01, CoffeeCart_01, Chandelier_01, WetFloorSign_01, standing_chalkboard_01, planter_box_01, modern_arm_chair_01, Sofa_01, rubber_duck_toy, industrial_storage_cart.\nТекстуры: marble_tiles, terrazzo_tiles, white_plaster_02, ceiling_interior, herringbone_parquet, laminate_floor, large_grey_tiles, concrete_floor_worn_001, metal_plate, fabric_leather_01, long_white_tiles, wood_floor, marble_01, square_tiles.\nЛицензия: https://polyhaven.com/license"),
            ("Wikimedia Commons — звуки",
             "«1 minute at the alexa mall in berlin» (thore, public domain) · «Shopping mall less crowded» (natalie, public domain) · «Restaurant ambience» (stephan, public domain) · «Rain (1)» (ezwa, public domain) · «Cash register» (public domain) · «Airplane Chime Sound Effect» (Sharelk, CC0 1.0) · «WWS Escalator» (Work With Sounds / La Fonderie, CC BY 4.0)."),
            ("Музыка — Kevin MacLeod (incompetech.com)",
             "«Local Forecast - Elevator», «Casa Bossa Nova» — CC BY 3.0. Перекодировано из MP3 в OGG Vorbis без изменения содержания."),
        };
    }
}

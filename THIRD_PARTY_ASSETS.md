# Сторонние ассеты

Все ассеты лежат в `Assets/CityBiz/Resources/` и распространяются под разрешительными лицензиями (CC0 / общественное достояние / CC BY). Для CC BY обязательна атрибуция — она показывается в игре: Главное меню / Пауза → «Авторы и лицензии» (тексты — `Runtime/Game/UI/Credits.cs`).

## Kenney (CC0 1.0)

Автор: Kenney (www.kenney.nl). Лицензия: Creative Commons Zero (CC0) — https://creativecommons.org/publicdomain/zero/1.0/ . Атрибуция не требуется (но приветствуется). Копии `License.txt` лежат рядом с ассетами.

| Пакет | Источник | Папка в проекте |
|---|---|---|
| Furniture Kit | https://kenney.nl/assets/furniture-kit | `Resources/Kenney/Furniture/` (FBX, материалы без текстур — цвета заданы материалами) |
| Food Kit | https://kenney.nl/assets/food-kit | `Resources/Kenney/Food/` (FBX + `Textures/colormap.png`) |
| Mini Characters 1 | https://kenney.nl/assets/mini-characters-1 | `Resources/Kenney/Characters/` (FBX с анимациями + `Textures/colormap.png`) |
| Impact Sounds | https://kenney.nl/assets/impact-sounds | `Resources/Audio/Footsteps/` (footstep_concrete_000–004, footstep_carpet_000–004) |
| Interface Sounds | https://kenney.nl/assets/interface-sounds | `Resources/Audio/UI/` (click_001, confirmation_001/002, error_001, open_001, close_001, select_001) |
| RPG Audio | https://kenney.nl/assets/rpg-audio | `Resources/Audio/Sfx/` (handleCoins, handleCoins2, doorOpen_1, doorClose_1, cloth1); лицензия — `Sfx/License_Kenney_rpg-audio.txt` |

## Poly Haven (CC0 1.0)

Автор: Poly Haven (https://polyhaven.com) и авторы отдельных ассетов. Лицензия: CC0 — https://polyhaven.com/license . Атрибуция не требуется.

### Модели → `Resources/PolyHaven/Models/<id>/`
Источник каждой модели: `https://polyhaven.com/a/<id>`. Версия 1k FBX. Альфа-каналы листьев (`*_alpha_1k`) слиты в `*_diff_1k.png` (RGBA). Карты rough/metal/alpha удалены; часть PNG-текстур (нормали и крупные диффузы) уменьшена до 512×512 для размера репозитория.

`potted_plant_01`, `potted_plant_02`, `potted_plant_04`, `pachira_aquatica_01`, `modular_street_seating`, `steel_frame_shelves_01`, `wooden_display_shelves_01`, `CoffeeCart_01`, `Chandelier_01`, `WetFloorSign_01`, `standing_chalkboard_01`, `planter_box_01`, `modern_arm_chair_01`, `Sofa_01`, `rubber_duck_toy`, `industrial_storage_cart`.

### Текстуры → `Resources/PolyHaven/Textures/<id>_diff.jpg`, `<id>_nor_gl.jpg`
Источник: `https://polyhaven.com/a/<id>`. 1k, кроме `marble_tiles` и `terrazzo_tiles` (2k).

`marble_tiles`, `terrazzo_tiles`, `white_plaster_02`, `ceiling_interior`, `herringbone_parquet`, `laminate_floor`, `large_grey_tiles`, `concrete_floor_worn_001`, `metal_plate`, `fabric_leather_01`, `long_white_tiles`, `wood_floor`, `marble_01`, `square_tiles`.

## Wikimedia Commons — звуки

Файлы перекодированы в OGG Vorbis (амбиент — моно). Источник — страница файла `https://commons.wikimedia.org/wiki/File:<имя файла>`.

| Файл в проекте | Оригинал на Commons | Автор | Лицензия |
|---|---|---|---|
| `Audio/Ambience/mall_crowd.ogg` | File:1 minute at the alexa mall in berlin.ogg | thore | Public domain |
| `Audio/Ambience/mall_quiet.ogg` | File:Shopping mall less crowded.ogg | natalie | Public domain |
| `Audio/Ambience/foodcourt.ogg` | File:Restaurant ambience.ogg | stephan | Public domain |
| `Audio/Ambience/rain.ogg` | File:Rain (1).ogg | ezwa | Public domain |
| `Audio/Sfx/cash_register.ogg` | File:Cash register.ogg | загрузчик «Me» | Public domain |
| `Audio/Sfx/announcement_chime.ogg` | File:Airplane Chime Sound Effect.ogg | Sharelk | CC0 1.0 — http://creativecommons.org/publicdomain/zero/1.0/ |
| `Audio/Ambience/escalator.ogg` | File:WWS Escalator.ogg | Work With Sounds / La Fonderie | CC BY 4.0 — https://creativecommons.org/licenses/by/4.0/ |
| `Audio/Music/local_forecast_elevator.ogg` | File:Local Forecast - Elevator (ISRC USUAN1300012).mp3 | Kevin MacLeod | CC BY 3.0 — http://creativecommons.org/licenses/by/3.0/ |
| `Audio/Music/casa_bossa_nova.ogg` | File:Casa Bossa Nova (ISRC USUAN1600012).mp3 | Kevin MacLeod | CC BY 3.0 — http://creativecommons.org/licenses/by/3.0/ |

### Обязательные строки атрибуции (CC BY)

- "Local Forecast - Elevator" Kevin MacLeod (incompetech.com) Licensed under Creative Commons: By Attribution 3.0 http://creativecommons.org/licenses/by/3.0/
- "Casa Bossa Nova" Kevin MacLeod (incompetech.com) Licensed under Creative Commons: By Attribution 3.0 http://creativecommons.org/licenses/by/3.0/
- "WWS Escalator" Work With Sounds / La Fonderie (via Wikimedia Commons) Licensed under Creative Commons: By Attribution 4.0 https://creativecommons.org/licenses/by/4.0/

Музыка перекодирована из MP3 в OGG Vorbis (изменение формата без правки содержания).

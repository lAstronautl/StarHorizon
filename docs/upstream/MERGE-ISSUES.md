# Полный список проблем после апстрима Frontier

Апстрим: `new-frontiers-14/frontier-station-14` master @ `45c3ca45a5` (#4971). Влит merge-коммитом `ed25ad2b` в ветку `claude/bold-ritchie-hq4nob`. Движок `RobustToolbox` не менялся (`feb9e1db`).

## 1. Что проверено и что нет

| Проверка | Результат |
|---|---|
| Сборка `Content.Server`, `Content.Client`, `Content.YAMLLinter` | 0 ошибок |
| Сборка всего решения `SpaceStation14.sln` (Debug), включая тесты, бенчмарки, упаковку | 0 ошибок |
| Запуск сервера (`Content.Server`) | Дошёл до `Ready`, раунд на `dev_map`, 0 ERRO, 0 FATL |
| Запуск клиента на виртуальном экране | Подключился, вошёл в `GameplayState`; единственная ошибка — Discord IPC (нет Discord в окружении) |
| YAML-линтер | 901 ошибок; из них исправлено 163, осталось 738 (раздел 5) |
| Сравнение ошибок линтера с состоянием до слияния | **Не сделано**: прогон на базовом коммите не стартовал, неизвестно, сколько из оставшихся ошибок новые |
| Unit/integration-тесты (`Content.Tests`, `Content.IntegrationTests`) | **Не запускались** |
| Сборка Release, упаковка клиента/сервера | **Не проверялась** |
| Загрузка основных карт и шаттлов (`Outpost`, `Shipyard`, POI, ваши шаттлы) | **Не проверялась**, грузился только `dev_map` |
| Игровой процесс (верфь, навигация, пинпоинтер, экспедиции, карго, медицина, оружие) | **Не проверялся** |
| Миграции БД | Апстрим новых не добавлял; предупреждение EF про `PRAGMA` от миграций Horizon (в т. ч. `HairGradient`) присутствует и ломает `PoolTestLogHandler` в тестах/линтере |

## 2. Потерянные или заменённые фичи Horizon (нужен перенос)

Апстримная версия файла взята целиком, правки Horizon в нём потеряны:

1. **Heat-distortion** (тепловое искажение) в атмос-оверлее: `SharedGasTileOverlaySystem` (Shared), `GasTileOverlaySystem` (Server и Client). Удалён `Content.Client/Atmos/Overlays/GasTileHeatOverlay.cs`. Ресурсы (шейдер и т. п.) остались без использования. Frontier переписал оверлей (file-scoped namespace, `partial`, `ThermalByte`), правка Horizon на старой структуре не совмещается.
2. **Jukebox «все треки доступны без пластинок»**: `Content.Server/Audio/Jukebox/JukeboxSystem.cs` и клиентский `JukeboxBoundUserInterface.cs` взяты у апстрима (новый UI по wizden#42210, очередь треков вместо шаффла). Поле `artist` добавлено в 9 ваших треков вручную: из `Артист - Название` разделено у 3 треков `mediaplayer.yml`, у 6 треков `Standard.yml` поставлено `Unknown`.
3. **Пинпоинтер**: оставлена версия Horizon (`SharedPinpointerSystem`, Goobstation-ретаргет по whitelist). Ретаргет Frontier по do-after (`PinpointerDoAfterEvent`, `OnPinpointerDoAfter`) не перенесён. В `PinpointerComponent` добавлено поле `MaxRange` из Frontier, в `PinpointerSystem` — `oldDist`.
4. **Верфь, UI**: `ShipyardConsoleMenu.xaml(.cs)`, `ShipyardConsoleBoundUserInterface.cs`, `VesselRow.xaml` оставлены версией Horizon (cash slot, rename, deed). Выбор атмосферы шаттла Frontier (#4209, шаттлы без кислорода) в UI **не перенесён**; серверная часть есть, у `ShipyardConsolePurchaseMessage` аргумент `atmosphere` сделан необязательным.
5. **`NavScreen.xaml`** оставлен версией Horizon. Блоки Frontier (Service Flags, часть Inertia dampener) в разметке отсутствуют; компиляция проходит, т. к. код на них не ссылается, но в игре этих органов управления нет.
6. **`DockingScreen.xaml`**: взят апстримный layout со scroll-контейнером, кнопка `UndockAllButton` Horizon добавлена вручную.
7. **Радиальные меню**: типы переименованы под новый API Wizden (`RadialMenuTextureButton`→`RadialMenuButton`, `RadialMenuOption`→`RadialMenuOptionBase`, `Sprite`→`IconSpecifier`) в `_White/RadialSelector`, `_Horizon/Mech/UI/MechEquipmentMenu`, `AdvancedRCD`. Внешний вид и поведение этих меню в игре не проверялись.

## 3. Ручные решения при слиянии (проверить логику)

- **`DamageableSystem` / `DamageModifyEvent`**: у `TryChangeDamage` теперь оба параметра, `armorPenetration` (Goobstation, Frontier) и `tool` (Horizon Attachments, RMC14); у `DamageModifyEvent` два конструктора. Вызовы с позиционным `tool` на 7-м месте нужно проверить.
- **`ShipyardSystem.TryPurchaseShuttle`**: сигнатура `(…, ShuttleAtmospherePrototype? atmos = null, string? priorityTag = null)`; вызов в `DerelictYardSystem` переведён на именованный `priorityTag:`.
- **`NFCargoSystem.Pallet`**: после слияния восстановлены `TryComp<WhitelistedMarketModifierComponent>` и закрывающая скобка; логика насыщения рынка Horizon и модификаторов Frontier совмещена вручную.
- **`SharedPortalSystem`**: взят апстрим, `OneUse`-порталы Horizon возвращены (`DeleteOneUsePortal`).
- **`MeleeHitEvent`**: теперь принимает `HashSet`; исправлены `SharedMeleeWeaponSystem` и `_White/Xenomorphs/TailLash`.
- **`IngestionBlockerComponent`**: `Enabled` оставлено свойством с `[AutoNetworkedField]`.
- **Prototypes `Food`→`Edible`** (13 файлов) и **`Drink`→`Edible { edible: Drink }`** (3 файла): поля перенесены без сверки, часть значений (например, задержки) могла поменять тип или смысл.
- **`ProtoId<EntityPrototype>`→`EntProtoId`** в `DecayGridCommand`, `AttachableData` (RMC14), `CanBeTakenUnderControlComponent`.
- **`HumanoidAppearanceComponent`**: значения по умолчанию `UndergarmentTop/Bottom` теперь `null` (маркинги белья отключены у Horizon).
- **`PuddleSystem.Evaporation.cs`**, **`NarcolepsySystem` (Server)**, старые Drink-системы и **`PowerCellSlotWeaponBatterySystem`** удалены: апстрим перенёс/заменил эту функциональность.
- **Возвращены из апстрима** 10 `.rsi`, удалённых Horizon раньше, но используемых прототипами Frontier; в 41 `.rsi` добавлены недостающие состояния (119), ничего не удалялось; возвращён аудиофайл `_DV/Jukebox/Caravan_Palace_Lone_Digger-MONO.ogg`.
- **Возвращены определения в `_Horizon`**: `DrinkBottleVisualsAll`, `DrinkGlassBase` (родители, удалённые апстримом, но нужные контенту Horizon); теги `DrinkCup`/`DrinkBottle` добавлены в `drinks_bottles_plastic.yml`.
- **Удалены дубли ID** (35 блоков в `drinks_metamorphic.yml`, `shipyard_civilian.yml`, `power_cell_pistols.yml`, `walls.yml` и т. д.): оставлено определение из файла, где его держит апстрим; ChameleonPDA в `pda.yml` продублирован слиянием, удалена копия без пометки.

## 4. Массовые решения «побеждает Horizon» (риск тихой потери правок Frontier)

- **251 конфликтный блок в 148 файлах**: оставлен вариант Horizon с пометкой `Horizon` (`# Horizon` в YAML, `// Horizon` в C#). Список: `horizon-wins-hunks.md`. Часть таких блоков уже ломала сборку (потерянные `using`, скобки, строки); найденное исправлено, но блоки, где всё собралось, но смысл изменился, никто не проверял.
- **318 файлов в картах, текстурах, `ServerInfo`** решены в пользу Horizon: список в `horizon-wins-assets.md`. Исправления карт и новые версии спрайтов Frontier в этих файлах не взяты.
- **559 файлов** взяты из апстрима целиком по эвристике «наш файл совпадает со старой версией Frontier». Эвристика не отслеживает переименования, возможны ошибки в обе стороны.
- **227 файлов** слиты трёхсторонне с «ближайшей версией Frontier» в роли базы, а не с настоящим общим предком.
- **14 файлов** (метаданные текстур и 4 шаттла) git склеил с чужими файлами из-за сходства содержимого; откатил к версии Horizon.

## 5. Оставшиеся ошибки YAML-линтера

Всего 901; исправлено 163 (`vendPrise`→`vendPrice` — 43, маркинги `Undergarment*` — ~120). Осталось **738**. Полный текст: `yamllinter-errors-after-merge.txt`. Большинство в областях Horizon и сторонних форков, поэтому вероятно старые, но без сравнения с базой это не доказано.

| Класс | Кол-во |
|---|---|
| Нет строки локализации | 357 |
| Неизвестное поле компонента или прототипа | 198 |
| Ссылка на несуществующий прототип | 132 |
| Файл не найден (звук/текстура) | 19 |
| Проблема со спрайтом RSI | 14 |
| Прочее | 13 |
| Неверный путь звука | 4 |
| Недопустимый символ в ID прототипа | 1 |

### Нет строки локализации (357)

Области: _Horizon/_Fractions (157), _Horizon/Reagents (70), _Horizon/Recipes (46), _Horizon/Stacks (27), _Horizon/StationDeployment (20), _RMC14/Recipes (12), _NF/Loadouts (8), _Backmen/Research (7)

- 14 × `No localization message found with id AnroxComputer`s`
- 5 × `No localization message found with id power cell`
- 4 × `No localization message found with id `
- 3 × `No localization message found with id capacitor stock part`
- 2 × `No localization message found with id ipc-overheat-popup`
- 2 × `No localization message found with id микросхема вентилятора`
- 2 × `No localization message found with id гарнитура`
- 2 × `No localization message found with id нижнее белье (верх)`
- 2 × `No localization message found with id нижнее белье (низ)`
- 2 × `No localization message found with id носки`

### Неизвестное поле компонента или прототипа (198)

Области: _Horizon/_Fractions (57), _Horizon/Entities (44), _White/Entities (15), Entities (10), Void (10), _Horizon/Recipes (6), _Horizon/Reagents (5), _Horizon/Mobs (4)

- 9 × `Field "rotate" not found in "Content.Client.DamageState.DamageStateVisualsComponent".`
- 8 × `Field "noSpawn" not found in "Robust.Shared.Prototypes.EntityPrototype".`
- 7 × `Field "enabled" not found in "Content.Shared.Weapons.Reflect.ReflectComponent".`
- 6 × `Field "modifiers" not found in "Content.Shared.Damage.DamageModifierSet".`
- 6 × `Field "state" not found in "Content.Shared.Item.ItemComponent".`
- 6 × `Field "icon" not found in "Content.Shared.Construction.Prototypes.ConstructionPrototype".`
- 5 × `Field "sprite" not found in "Robust.Client.GameObjects.IconComponent".`
- 5 × `Field "state" not found in "Robust.Client.GameObjects.IconComponent".`
- 4 × `Field "category" not found in "Content.Shared.Research.Prototypes.LatheRecipePrototype".`
- 4 × `Field "state" not found in "Content.Shared.PDA.PdaComponent".`

### Ссылка на несуществующий прототип (132)

Области: _Horizon/_Fractions (32), _Backmen/Entities (19), _Horizon/Entities (17), _DV/Recipes (10), _NF/Shipyard (7), Entities (6), Catalog (5), _Mono/Entities (4)

- 3 × `No Content.Shared.Damage.Prototypes.DamageTypePrototype found with id Bleed`
- 2 × `No Content.Shared.Chat.Prototypes.EmotePrototype found with id Blink`
- 2 × `No Content.Shared.Tag.TagPrototype found with id CartridgeRevolver357Magnum`
- 2 × `No Content.Shared.Construction.Prototypes.ConstructionGraphPrototype found with id VectorTactical`
- 2 × `No Content.Shared.Research.Prototypes.TechnologyPrototype found with id NFBulkRifleAmmo`
- 2 × `No Robust.Shared.Audio.SoundCollectionPrototype found with id buzzes`
- 2 × `No Content.Shared.Chat.Prototypes.EmoteSoundsPrototype found with id IPCBodyEmotes`
- 2 × `No Content.Shared.Chat.Prototypes.EmotePrototype found with id SiliconDeathgasp`
- 1 × `No Content.Shared.Body.Prototypes.MetabolizerTypePrototype found with id Diona`
- 1 × `No Robust.Shared.Prototypes.EntityPrototype found with id ClothingHeadHatCentcommCavalier`

### Файл не найден (звук/текстура) (19)

Области: _Horizon/Entities (5), _Horizon/Shipyard (4), _Backmen/Entities (3), _Horizon/Stacks (3), _Goobstation/Entities (1), _NF/Shipyard (1), _White/Actions (1), _White/Entities (1)

- 1 × `File not found. (/Audio/Backmen/Misc/empty_alarm.ogg)`
- 1 × `File not found. (/Audio/Backmen/Misc/error.ogg)`
- 1 × `File not found. (/Audio/Corvax/Weapons/Guns/Cock/shotgun_cock.ogg)`
- 1 × `File not found. (/Audio/Goobstation/Supermatter/delamming.ogg)`
- 1 × `File not found. (/Audio/_Horizon/Magic/Eldritch/voidblink.ogg)`
- 1 × `File not found. (/Audio/Magic/Demon/demon_consume.ogg)`
- 1 × `File not found. (/Audio/Voice/Felinid/cat_scream2.ogg)`
- 1 × `File not found. (/Audio/StarHorizon/Items/Toys/chainsawsound.ogg)`
- 1 × `File not found. (/Audio/_StarHorizon/Weapons/Guns/Barrettm82/barrettm82_cock.ogg)`
- 1 × `File not found. (/Uploaded/Ophiophagus/Nfsd/asper-l.yml)`

### Проблема со спрайтом RSI (14)

Области: _Horizon/Entities (4), _Horizon/_Fractions (4), _Horizon/Stacks (3), Void (2), _White/Actions (1)

- 9 × `Invalid RSI state`
- 5 × `Failed to load RSI`

### Прочее (13)

Области: _DV/Recipes (10), _Horizon/Bodyipc.yml (1), _Horizon/outpost.yml (1), _White/themes.yml (1)

- 1 × `Entity Prototype AroFlag was not found!`
- 1 × `Entity Prototype IntersexFlag was not found!`
- 1 × `Entity Prototype PanFlag was not found!`
- 1 × `Entity Prototype AceFlag was not found!`
- 1 × `Entity Prototype TransRoyalShrimpFlag was not found!`
- 1 × `Entity Prototype TransFlag was not found!`
- 1 × `Entity Prototype LesbianFlag was not found!`
- 1 × `Entity Prototype GayFlag was not found!`
- 1 × `Entity Prototype BiFlag was not found!`
- 1 × `Entity Prototype EnbyFlag was not found!`

### Неверный путь звука (4)

Области: _Backmen/Entities (2), _Horizon/Entities (2)

- 4 × `SoundSpecifier value is not a valid resource path!`

### Недопустимый символ в ID прототипа (1)

Области: _NF/Catalog (1)

- 1 × `Prototype 'UplinkNFPistol.30Magazine' (Content.Shared.Store.ListingPrototype) contains disallowed character '.'.`

## 6. Предупреждения при запуске сервера

- 25 × `system.chat: Duplicate of emote word …` (дубли слов эмоций в русской и английской локали, например «шипит», «рычит», «honk»).
- `eng: MainLoop: Cannot keep up!` при старте (слабое окружение, не обязательно проблема).
- `net.ent: Got late MsgEntity` (запросы гайдбука и спонсоров пришли поздно, вероятно из-за нагрузки).

## 7. Окружение, в котором проверялось

- Песочница без IPv6 и звука: сервер запускался с `net.bindto=0.0.0.0`, `status.enabled=false`, клиент с `ALSOFT_DRIVERS=null` на Xvfb (программный OpenGL).
- Для линтера локально (не в коммитах) ослаблялся `PoolTestLogHandler`: учитывались только ошибки уровня ERROR, иначе линтер падал на предупреждениях EF и спонсорского модуля.
- В репозитории `Content.IntegrationTests/PoolTestLogHandler.cs` не изменён.

## 8. Рекомендуемый порядок работ

1. Прогнать линтер на базовом коммите `db776aa5` и сравнить с `yamllinter-errors-after-merge.txt`, чтобы отделить новые ошибки от старых.
2. Пройти `horizon-wins-hunks.md` (148 файлов) и вернуть нужное из версий Frontier.
3. Загрузить на сервере основные карты и шаттлы, посмотреть ошибки загрузки.
4. Вручную проверить в игре: верфь, навигацию, пинпоинтер, экспедиции (в т. ч. Syndicate HQ), карго с насыщением рынка, радиальные меню, Jukebox.
5. Перенести потерянные фичи из раздела 2.
6. Запустить `Content.Tests` и `Content.IntegrationTests`.

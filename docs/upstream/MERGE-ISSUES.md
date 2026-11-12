# Проблемы после апстрима Frontier (45c3ca45a5)

Статус: сборка Server/Client проходит, YAMLLinter находит ошибки (полный список: `yamllinter-errors-after-merge.txt`, 901 строк).

Сравнение с базовым коммитом (до слияния) не выполнено - часть ошибок может быть старой.

## 1. Потерянные правки Horizon (заменены версией апстрима, нужен перенос)

- Heat-distortion (GasTileOverlay: Shared/Server/Client, `GasTileHeatOverlay`)
- Jukebox: все треки доступны без пластинок
- Пинпоинтер: оставлена версия Horizon, ретаргет Frontier по do-after не перенесён
- Верфь (UI): нет выбора атмосферы шаттла Frontier
- NavScreen.xaml: нет блоков Frontier (Service Flags и др.)

## 2. Ручные решения при слиянии

- Content.Shared/Atmos/EntitySystems/SharedGasTileOverlaySystem.cs: Horizon heat-distortion (+76 строк) заменён версией апстрима, нужен ручной перенос
- Content.Shared/Pinpointer/SharedPinpointerSystem.cs: оставлена версия Horizon целиком (Goob-ретаргет по whitelist); do-after ретаргет Frontier не перенесён
- Content.Server/Traits/Assorted/NarcolepsySystem.cs: удалён (апстрим перенёс в Shared)
- Content.Server/Audio/Jukebox/JukeboxSystem.cs: взят апстрим (Frontier заменил шаффл очередью); Horizon-правка 'все треки доступны без пластинок' нужно перенести заново
- Content.Server/Atmos/EntitySystems/GasTileOverlaySystem.cs: взят апстрим; Horizon heat-distortion нужно перенести заново
- Content.Server/Fluids/EntitySystems/PuddleSystem.Evaporation.cs: удалён (апстрим перенёс в Shared)
- Radial: переименованы типы под новый API Wizden (RadialMenuTextureButton->RadialMenuButton, RadialMenuOption->RadialMenuOptionBase) в _White/RadialSelector, _Horizon/Mech, AdvancedRCD
- Content.Client/Shuttles/UI/DockingScreen.xaml: взят апстрим (scroll-раскладка Frontier) + кнопка UndockAllButton Horizon
- Content.Client/_NF/Shipyard/{BUI/ShipyardConsoleBoundUserInterface.cs,UI/ShipyardConsoleMenu.xaml(.cs),UI/VesselRow.xaml}: оставлена версия Horizon (cash slot/rename/deed); выбор атмосферы шаттла Frontier в UI не перенесён
- Heat distortion (Horizon): серверная/клиентская/общая часть атмос-оверлея заменена версиями апстрима; GasTileHeatOverlay.cs удалён — фичу нужно перепортировать на новую структуру GasTileOverlay
- Удалены (апстрим заменил): drinks_bottles.yml (-> drinks_bottles_plastic.yml), DrinkSystem/SharedDrinkSystem/DrinkComponent (-> Ingestion)
- DrinkBottleVisualsAll: возвращён в _Horizon/Entities/Objects/Consumable/Drinks/bottle_visuals.yml (нужен _Fractions/AnCo)
- Возвращены из апстрима 10 .rsi, удалённых ранее Horizon, но используемых прототипами Frontier
- DrinkGlassBase: возвращён в _Horizon/.../drink_glass_base.yml (нужен vases.yml)
- RSI: в 41 .rsi добавлены недостающие состояния апстрима (119 состояний), ничего не удалялось
- Food->Edible (Ingestion refactor Wizden): 13 файлов прототипов
- Drink->Edible{edible: Drink}: 3 файла прототипов
- _Horizon/Weapons/PowerCellSlotWeaponBatterySystem.cs удалён: функциональность теперь в PowerCellSystem апстрима (GetCharge/ChangeCharge forwarding)
- ProtoId<EntityPrototype> -> EntProtoId (требование Robust DebugAssert) в 3 файлах Horizon/RMC14

## 3. Ошибки YAMLLinter по классам

| Кол-во | Сообщение |
|---|---|
| 241 | `Field "X" not found in "X".` |
| 57 | `No Content.Shared.Humanoid.Markings.MarkingPrototype found with id UndergarmentTopTanktop` |
| 55 | `No Content.Shared.Humanoid.Markings.MarkingPrototype found with id UndergarmentBottomBoxers` |
| 19 | `File not found.` |
| 14 | `No localization message found with id AnroxComputer`s` |
| 9 | `Invalid RSI state` |
| 5 | `Failed to load RSI` |
| 5 | `No localization message found with id power cell` |
| 4 | `No Content.Shared.Humanoid.Markings.MarkingPrototype found with id UndergarmentBottomBoxersReptilian` |
| 4 | `SoundSpecifier value is not a valid resource path!` |
| 4 | `No localization message found with id` |
| 4 | `No localization message found with id нижнее белье` |
| 3 | `No localization message found with id фруктовое вино` |
| 3 | `No localization message found with id молотый кофе` |
| 3 | `No localization message found with id capacitor stock part` |
| 3 | `No Content.Shared.Damage.Prototypes.DamageTypePrototype found with id Bleed` |
| 2 | `No Content.Shared.Humanoid.Markings.MarkingPrototype found with id UndergarmentTopTanktopVox` |
| 2 | `No Content.Shared.Humanoid.Markings.MarkingPrototype found with id UndergarmentBottomBoxersVox` |
| 2 | `No Content.Shared.Chat.Prototypes.EmotePrototype found with id Blink` |
| 2 | `No Content.Shared.Tag.TagPrototype found with id CartridgeRevolver357Magnum` |
| 2 | `No Content.Shared.Construction.Prototypes.ConstructionGraphPrototype found with id VectorTactical` |
| 2 | `No Content.Shared.Research.Prototypes.TechnologyPrototype found with id NFBulkRifleAmmo` |
| 2 | `No Robust.Shared.Audio.SoundCollectionPrototype found with id buzzes` |
| 2 | `No localization message found with id ipc-overheat-popup` |
| 2 | `No Content.Shared.Chat.Prototypes.EmoteSoundsPrototype found with id IPCBodyEmotes` |

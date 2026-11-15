# PR Frontier, влитые в Horizon — с переводом на русский

Источник: https://github.com/new-frontiers-14/frontier-station-14, ветка `master`.
Диапазон: `ea516ee5` (#4483, прежняя точка синхронизации) → `45c3ca45` (#4971). Влито merge-коммитом `ed25ad2b` в ветку `claude/bold-ritchie-hq4nob`.

Формат записи: `оригинальное название ([#номер](ссылка)) — перевод`. У двух PR в оригинале нет номера, вместо него ссылка на коммит.
Всего PR: **355** (служебные автоматические коммиты changelog не перечислены).
Перевод сделан по названиям PR, содержимое каждого PR не читалось. Названия кораблей, фракций и внутриигровых предметов оставлены как есть. Группа определена автоматически по файлам, которые менял PR.

## Оглавление

- [Исправления](#исправления) (57)
- [Экспедиции, подземелья, POI](#экспедиции-подземелья-poi) (16)
- [Шаттлы, верфи, карты шаттлов](#шаттлы-верфи-карты-шаттлов) (31)
- [Оружие, броня, боеприпасы](#оружие-броня-боеприпасы) (12)
- [Мобы, NPC, персонажи, роли](#мобы-npc-персонажи-роли) (19)
- [Медицина, химия, ботаника, кухня](#медицина-химия-ботаника-кухня) (13)
- [Одежда, косметика, предметы](#одежда-косметика-предметы) (51)
- [Атмос, энергия, инженерия, крафт](#атмос-энергия-инженерия-крафт) (57)
- [Экономика, карго, баунти, торговля](#экономика-карго-баунти-торговля) (17)
- [Интерфейс и клиент](#интерфейс-и-клиент) (34)
- [Код и системы (Server/Shared)](#код-и-системы-servershared) (24)
- [Прочее](#прочее) (24)

## Исправления

- fix errant transparency of strap, ([#4980](https://github.com/new-frontiers-14/frontier-station-14/pull/4980)) — Исправлена ошибочная прозрачность ремня
- HOTFIX Fix pickup effects occurring with verb creation (#38705) ([#4883](https://github.com/new-frontiers-14/frontier-station-14/pull/4883)) — ХОТФИКС: исправлены эффекты подбора, срабатывавшие при создании вербов (#38705)
- Cherry pick more popup/interaction verb fixes (Locked borgs etc., paper bins, shuttle console and more!) ([#4898](https://github.com/new-frontiers-14/frontier-station-14/pull/4898)) — Cherry-pick ещё исправлений всплывающих окон и вербов взаимодействия (заблокированные борги, бумажные ящики, консоль шаттла и др.)
- Fix reality breaking when solar panels get destroyed ([#4950](https://github.com/new-frontiers-14/frontier-station-14/pull/4950)) — Исправлено «ломание реальности» при уничтожении солнечных панелей
- Reparented the Plasteel Arming Sword to fix broken inventory icon, made sheathable to retain functionality ([#4968](https://github.com/new-frontiers-14/frontier-station-14/pull/4968)) — Plasteel Arming Sword получил нового родителя, чтобы исправить сломанную иконку инвентаря, и может вкладываться в ножны
- Food to edible final part (cleanup and fixes) ([#4780](https://github.com/new-frontiers-14/frontier-station-14/pull/4780)) — Food → edible, завершающая часть (очистка и исправления)
- Fix Bocadilla value which has been raising by making SmartFridge constructible. ([#4956](https://github.com/new-frontiers-14/frontier-station-14/pull/4956)) — Исправлена цена Bocadilla, которая росла из-за возможности строить SmartFridge
- [HOTFIX] Fix for FTL bug (#46577) ([#4940](https://github.com/new-frontiers-14/frontier-station-14/pull/4940)) — [ХОТФИКС] Исправление бага FTL (#46577)
- HOTFIX - Attack wideswing structure from list to set ([#4951](https://github.com/new-frontiers-14/frontier-station-14/pull/4951)) — ХОТФИКС: широкий удар по строениям теперь использует set вместо list
- Tiny PDA Fixes ([#4857](https://github.com/new-frontiers-14/frontier-station-14/pull/4857)) — Мелкие исправления КПК
- Fix blueprint lithograph selection bug ([#4822](https://github.com/new-frontiers-14/frontier-station-14/pull/4822)) — Исправлена ошибка выбора литографии чертежей
- Port Upstream Meat Hook Fixes ([#4932](https://github.com/new-frontiers-14/frontier-station-14/pull/4932)) — Порт исправлений Meat Hook из апстрима
- Fixed missing vox emotes removed by upstream merge ([#4933](https://github.com/new-frontiers-14/frontier-station-14/pull/4933)) — Возвращены пропавшие эмоции воксов, удалённые слиянием с апстримом
- Fix reversed hand swapping ([#4901](https://github.com/new-frontiers-14/frontier-station-14/pull/4901)) — Исправлена обратная смена рук
- Fix reptilian displacement maps ([#4931](https://github.com/new-frontiers-14/frontier-station-14/pull/4931)) — Исправлены карты смещения рептилоидов
- Fix Ammo Count Updating ([#4930](https://github.com/new-frontiers-14/frontier-station-14/pull/4930)) — Исправлено обновление счётчика патронов
- Grounding Rod Price Fix ([#4789](https://github.com/new-frontiers-14/frontier-station-14/pull/4789)) — Исправлена цена заземляющего стержня
- Fix psychologist bed construction graph ([#4837](https://github.com/new-frontiers-14/frontier-station-14/pull/4837)) — Исправлен граф строительства кровати психолога
- Clugg Component fix ([#4752](https://github.com/new-frontiers-14/frontier-station-14/pull/4752)) — Исправление компонента Clugg
- Fix supercompacted rock prototypes ([#4836](https://github.com/new-frontiers-14/frontier-station-14/pull/4836)) — Исправлены прототипы суперсжатой породы
- Fix visual bug with masks appearing to be pulled down after re-equipping (#40332) ([#4843](https://github.com/new-frontiers-14/frontier-station-14/pull/4843)) — Исправлен визуальный баг: маски выглядели спущенными после повторной экипировки (#40332)
- Suit storage fixes, melee animation tweaks ([#4834](https://github.com/new-frontiers-14/frontier-station-14/pull/4834)) — Исправления хранилища костюма, правки анимации ближнего боя
- Fix generator descriptions ([#4863](https://github.com/new-frontiers-14/frontier-station-14/pull/4863)) — Исправлены описания генераторов
- Puddles predicted and bugfixes (Wizden#38871 et al.) ([#4617](https://github.com/new-frontiers-14/frontier-station-14/pull/4617)) — Предсказание луж и исправления ошибок (Wizden#38871 и др.)
- Make the janicart safe to drink ([#4823](https://github.com/new-frontiers-14/frontier-station-14/pull/4823)) — Janicart стал безопасным для питья
- Make Organs Grindable ([#4556](https://github.com/new-frontiers-14/frontier-station-14/pull/4556)) — Органы можно перемалывать
- Make syndicate secway contraband ([#4819](https://github.com/new-frontiers-14/frontier-station-14/pull/4819)) — Синдикатский секвей теперь контрабанда
- General Arbitrage Fixes, Dingleberry Edition #1 ([#4808](https://github.com/new-frontiers-14/frontier-station-14/pull/4808)) — Общие исправления арбитража, выпуск «Dingleberry» №1
- Fix RGB staff not working (#40258) ([#4796](https://github.com/new-frontiers-14/frontier-station-14/pull/4796)) — Исправлен не работающий RGB-посох (#40258)
- fixed vulnerability in changelog tool ([#4767](https://github.com/new-frontiers-14/frontier-station-14/pull/4767)) — Исправлена уязвимость в инструменте changelog
- Fix sharkminnow teeth drops ([#4769](https://github.com/new-frontiers-14/frontier-station-14/pull/4769)) — Исправлено выпадение зубов акулы-малька
- Fix the Svalinn to run off the power cell ([#4727](https://github.com/new-frontiers-14/frontier-station-14/pull/4727)) — Исправлено: Svalinn работает от энергоячейки
- Fix inconsistent animal breeding behavior ([#4738](https://github.com/new-frontiers-14/frontier-station-14/pull/4738)) — Исправлено непоследовательное поведение разведения животных
- Implement fix. ([#4750](https://github.com/new-frontiers-14/frontier-station-14/pull/4750)) — Исправление внедрено.
- POI (Update) Edison's Power Plant: Anticipatory Edison Fixes ([#4695](https://github.com/new-frontiers-14/frontier-station-14/pull/4695)) — POI (обновление) электростанция Edison: упреждающие исправления Edison
- Fix intercom and signal timer construction ([#4722](https://github.com/new-frontiers-14/frontier-station-14/pull/4722)) — Исправлено строительство интеркома и таймера сигнала
- Overpressure fix ([#4737](https://github.com/new-frontiers-14/frontier-station-14/pull/4737)) — Исправление избыточного давления
- fix pilots of pet mechs can use their mech's equipment ([#4663](https://github.com/new-frontiers-14/frontier-station-14/pull/4663)) — Исправлено: пилоты питомцев-мехов могут пользоваться оборудованием меха
- Fix Medical Dispatch Fax ([#4678](https://github.com/new-frontiers-14/frontier-station-14/pull/4678)) — Исправлен факс медицинской диспетчерской
- Make the expedition console send the grid it's on ([#4446](https://github.com/new-frontiers-14/frontier-station-14/pull/4446)) — Консоль экспедиций теперь отправляет грид, на котором стоит
- Fix assertion when spawning with multi-handed items in loadout ([#4647](https://github.com/new-frontiers-14/frontier-station-14/pull/4647)) — Исправлена ошибка при спавне с многорукими предметами в снаряжении
- SSF Rusty banner copyright fix ([#4646](https://github.com/new-frontiers-14/frontier-station-14/pull/4646)) — Исправление авторских прав баннера SSF Rusty
- Hotfix: Balena ATM withdraw-only ([#4618](https://github.com/new-frontiers-14/frontier-station-14/pull/4618)) — Хотфикс: банкомат Balena — только снятие
- Fix blueprints not fitting in document cases ([#4507](https://github.com/new-frontiers-14/frontier-station-14/pull/4507)) — Исправлено: чертежи не помещались в кейсы для документов
- Hangable posters and paintings follow up (fixed) ([#4509](https://github.com/new-frontiers-14/frontier-station-14/pull/4509)) — Подвесные плакаты и картины: доработка (исправлено)
- make tritium a better fuel for a radiation collector (#1112) ([#4544](https://github.com/new-frontiers-14/frontier-station-14/pull/4544)) — Тритий стал лучшим топливом для радиационного коллектора (#1112)
- Fix Infoshades inventory footprint, hide health icon ([#4449](https://github.com/new-frontiers-14/frontier-station-14/pull/4449)) — Исправлены размеры инвентаря Infoshades, скрыт значок здоровья
- Make inflatable dock rotatable while anchored ([#4468](https://github.com/new-frontiers-14/frontier-station-14/pull/4468)) — Надувной док можно вращать, пока он закреплён
- Cooking Bag Description Fix ([#4540](https://github.com/new-frontiers-14/frontier-station-14/pull/4540)) — Исправлено описание сумки для готовки
- Station (Fix) Edison Power Plant internal power issue ([#4411](https://github.com/new-frontiers-14/frontier-station-14/pull/4411)) — Станция (исправление): проблема с питанием внутри электростанции Edison
- Fix references to cult & wizard staff guidebook page names ([#4522](https://github.com/new-frontiers-14/frontier-station-14/pull/4522)) — Исправлены ссылки на названия страниц руководства о культе и посохах волшебников
- Fix objects construction using machine part stacks. ([#4525](https://github.com/new-frontiers-14/frontier-station-14/pull/4525)) — Исправлено строительство объектов из стеков деталей машин
- Make vgroids show up in the direction of barrier gate ([#4456](https://github.com/new-frontiers-14/frontier-station-14/pull/4456)) — Vgroid теперь появляются в направлении барьерных ворот
- Shuttle (Update) Feb '26 Mapping Feedback Bug hitlist ([#4336](https://github.com/new-frontiers-14/frontier-station-14/pull/4336)) — Шаттл (обновление): список багов по отзывам мапперов за февраль 2026
- Visual bugfix for Goblin Tool Sparks effects ([#4354](https://github.com/new-frontiers-14/frontier-station-14/pull/4354)) — Визуальное исправление эффектов искр инструментов гоблина
- fix(discord): link ([#4499](https://github.com/new-frontiers-14/frontier-station-14/pull/4499)) — Исправление: ссылка discord
- Fix light pop effect attribution ([#4513](https://github.com/new-frontiers-14/frontier-station-14/pull/4513)) — Исправлено указание автора эффекта «light pop»

## Экспедиции, подземелья, POI

- Syndicate HQ Replacing the AdvancedRadar with a AtmosphericConsole ([#4885](https://github.com/new-frontiers-14/frontier-station-14/pull/4885)) — В штабе Синдиката AdvancedRadar заменён на AtmosphericConsole
- Asteroid worldgen changes - Making the sector more compact ([#4923](https://github.com/new-frontiers-14/frontier-station-14/pull/4923)) — Изменения мировой генерации астероидов: сектор стал компактнее
- Cargo Depot Sell Console Replaced ([#4824](https://github.com/new-frontiers-14/frontier-station-14/pull/4824)) — Заменена консоль продаж грузового депо
- NFSDO POI (Update) NFSDO: smaller lights and extra bucket ([#4854](https://github.com/new-frontiers-14/frontier-station-14/pull/4854)) — POI NFSDO (обновление): меньше света и дополнительное ведро
- Syndicate HQ Expedition Dungeon, and Faction-Specific Dungeon Selection ([#4526](https://github.com/new-frontiers-14/frontier-station-14/pull/4526)) — Подземелье экспедиции штаба Синдиката и выбор подземелий по фракциям
- Add a second staff shipyard to FO ([#4753](https://github.com/new-frontiers-14/frontier-station-14/pull/4753)) — Добавлена вторая верфь персонала для FO
- Replaced ShelfChemistryChemistrySecure with ShelfChemistry at NFSDO ([#4757](https://github.com/new-frontiers-14/frontier-station-14/pull/4757)) — В NFSDO ShelfChemistryChemistrySecure заменён на ShelfChemistry
- Remove references to now removed Ripley Chassis from existing maps ([#4802](https://github.com/new-frontiers-14/frontier-station-14/pull/4802)) — Удалены ссылки на удалённое шасси Ripley с существующих карт
- Added ID Computer to MD, moved around the air alarm, wall recharger, and made the records computer wallmounted ([#4698](https://github.com/new-frontiers-14/frontier-station-14/pull/4698)) — В MD добавлен ID-компьютер, перемещены воздушная сигнализация и настенная зарядка, компьютер записей сделан настенным
- Station (Update) Medical Dispatch: Caddy Addition ([#4597](https://github.com/new-frontiers-14/frontier-station-14/pull/4597)) — Станция (обновление) Medical Dispatch: добавлен Caddy
- Concealed Landmines Tweaks ([#4447](https://github.com/new-frontiers-14/frontier-station-14/pull/4447)) — Правки скрытых мин
- Shuttle (Update) FSA Dragoon & Aegis ([#4602](https://github.com/new-frontiers-14/frontier-station-14/pull/4602)) — Шаттл (обновление) FSA Dragoon и Aegis
- Moves Curie into PM Office ([#4596](https://github.com/new-frontiers-14/frontier-station-14/pull/4596)) — Curie перенесена в офис PM
- Adding cyborg recharger stations to various POIs. ([#4506](https://github.com/new-frontiers-14/frontier-station-14/pull/4506)) — Добавлены зарядные станции киборгов на разные POI
- Replaced the Mercenary EVA spawner with the Asteroid mob spawner on the basalt VGRoid, made adjustments to the spawner for Frontier (Basilisks cause crashes) ([#4479](https://github.com/new-frontiers-14/frontier-station-14/pull/4479)) — Спавнер Mercenary EVA заменён на спавнер мобов астероида на базальтовом VGRoid, спавнер подправлен для Frontier (базилиски вызывают краши)
- POI (Update) NFSD Outpost: jani button is no longer a jani trap! ([#4310](https://github.com/new-frontiers-14/frontier-station-14/pull/4310)) — POI (обновление) аванпост NFSD: кнопка уборщика больше не ловушка!

## Шаттлы, верфи, карты шаттлов

- Fixing ShuttleFTL component to allow proper FTL to attached shuttles or grids ([#4707](https://github.com/new-frontiers-14/frontier-station-14/pull/4707)) — Исправлен компонент ShuttleFTL: корректный FTL для присоединённых шаттлов и гридов
- how does this damn thing work again ([#4908](https://github.com/new-frontiers-14/frontier-station-14/pull/4908)) — Как же эта чёртова штука вообще работает
- Renames inter ship to Intra-ship and adds some mapping variants ([#4876](https://github.com/new-frontiers-14/frontier-station-14/pull/4876)) — Inter ship переименован в Intra-ship, добавлены варианты для маппинга
- Give players the option to buy shuttles with an oxygen-free atmosphere ([#4209](https://github.com/new-frontiers-14/frontier-station-14/pull/4209)) — Игроки могут покупать шаттлы с бескислородной атмосферой
- Shuttle (Update) Vagabond ([#4457](https://github.com/new-frontiers-14/frontier-station-14/pull/4457)) — Шаттл (обновление) Vagabond
- Shuttle (Update) Empress ([#4721](https://github.com/new-frontiers-14/frontier-station-14/pull/4721)) — Шаттл (обновление) Empress
- Enable shuttle collisions by default ([#4815](https://github.com/new-frontiers-14/frontier-station-14/pull/4815)) — Столкновения шаттлов включены по умолчанию
- Shuttle (Hotfix) NT-Backpacker Top-right Thruster state ([#4829](https://github.com/new-frontiers-14/frontier-station-14/pull/4829)) — Шаттл (хотфикс) NT-Backpacker: состояние правого верхнего двигателя
- Disable smuggling faxes on staff ships ([#4783](https://github.com/new-frontiers-14/frontier-station-14/pull/4783)) — Отключены факсы контрабанды на кораблях персонала
- Pirate Role Interview Required ([#4728](https://github.com/new-frontiers-14/frontier-station-14/pull/4728)) — Для пиратской роли теперь нужно собеседование
- Shuttle (Update) Backpacker ([#4679](https://github.com/new-frontiers-14/frontier-station-14/pull/4679)) — Шаттл (обновление) Backpacker
- Shuttle (Update): Tyne 2.0 ([#4632](https://github.com/new-frontiers-14/frontier-station-14/pull/4632)) — Шаттл (обновление): Tyne 2.0
- updated eagle.yml ([#4687](https://github.com/new-frontiers-14/frontier-station-14/pull/4687)) — Обновлён eagle.yml
- HorizontalExpand inertia dapener buttons ([#4649](https://github.com/new-frontiers-14/frontier-station-14/pull/4649)) — HorizontalExpand у кнопок инерционного демпфера
- Shuttle (Update) Eagle: (Update-4) ([#4623](https://github.com/new-frontiers-14/frontier-station-14/pull/4623)) — Шаттл (обновление) Eagle: (обновление-4)
- Shipyard guidebook divisions ([#4644](https://github.com/new-frontiers-14/frontier-station-14/pull/4644)) — Деление гайдбука по верфям
- Balena + Jackrabbit updates ([#4620](https://github.com/new-frontiers-14/frontier-station-14/pull/4620)) — Обновления Balena и Jackrabbit
- Ports undock all from Wayfarer/Monolith ([#4543](https://github.com/new-frontiers-14/frontier-station-14/pull/4543)) — Порт «отстыковаться от всего» из Wayfarer/Monolith
- Shuttle (Update) Prowler ([#4598](https://github.com/new-frontiers-14/frontier-station-14/pull/4598)) — Шаттл (обновление) Prowler
- coffeehouse apc warmfix ([#4599](https://github.com/new-frontiers-14/frontier-station-14/pull/4599)) — Исправление тёплого освещения APC в coffeehouse
- Shuttle (Update) Baroness arbitrage ([коммит bcf03553](https://github.com/new-frontiers-14/frontier-station-14/commit/bcf03553b4ae7a48b2844d73c055f8a526d4c414)) — Шаттл (обновление) Baroness: арбитраж
- Shuttle (Update) Ambition: gains a mothership console ([#4595](https://github.com/new-frontiers-14/frontier-station-14/pull/4595)) — Шаттл (обновление) Ambition: получает консоль материнского корабля
- Update Broadhead with pipe layering and no syndie bedsheet ([#4523](https://github.com/new-frontiers-14/frontier-station-14/pull/4523)) — Обновлён Broadhead: слои труб и без синдикатского постельного белья
- Shuttle (Hotfix) PT Balena ([#4572](https://github.com/new-frontiers-14/frontier-station-14/pull/4572)) — Шаттл (хотфикс) PT Balena
- Shuttle (Update): SSF Camper [Decal and minor Additions] ([#4408](https://github.com/new-frontiers-14/frontier-station-14/pull/4408)) — Шаттл (обновление): SSF Camper [декаль и небольшие дополнения]
- The Ninth Circle - Event POI ([#4512](https://github.com/new-frontiers-14/frontier-station-14/pull/4512)) — The Ninth Circle — событийная точка интереса (POI)
- And so, the comet's tail shines bright once more! ([#4425](https://github.com/new-frontiers-14/frontier-station-14/pull/4425)) — И вот хвост кометы снова ярко сияет!
- Bookworm QoL changes + modernisation pass ([#4455](https://github.com/new-frontiers-14/frontier-station-14/pull/4455)) — Улучшения Bookworm и проход модернизации
- Shuttle (Update) Investigator ([#4359](https://github.com/new-frontiers-14/frontier-station-14/pull/4359)) — Шаттл (обновление) Investigator
- Geo dude ([#4360](https://github.com/new-frontiers-14/frontier-station-14/pull/4360)) — Geo dude
- Hold on, the Kestrel isn't an exped vessel! ([#4494](https://github.com/new-frontiers-14/frontier-station-14/pull/4494)) — Погодите, Kestrel — не экспедиционное судно!

## Оружие, броня, боеприпасы

- Re-adding the upstream Energy Shotgun ([#4891](https://github.com/new-frontiers-14/frontier-station-14/pull/4891)) — Возвращён энергетический дробовик из апстрима
- Added and Tweaked the eNFSD Sword! ([#4759](https://github.com/new-frontiers-14/frontier-station-14/pull/4759)) — Добавлен и доработан меч eNFSD!
- Melee adjustments: Giving some love to underused weapons (and nerfing the crusher dagger) ([#4826](https://github.com/new-frontiers-14/frontier-station-14/pull/4826)) — Правки ближнего боя: внимание малоиспользуемому оружию (и ослабление кинжала-крашера)
- Partial AP ([#4816](https://github.com/new-frontiers-14/frontier-station-14/pull/4816)) — Частичное бронебойное (AP)
- Add pirate carbine to uplink ([#4691](https://github.com/new-frontiers-14/frontier-station-14/pull/4691)) — Пиратский карабин добавлен в аплинк
- Upstream melee animations cherrypick + tweaks to bayoneted rifles ([#4742](https://github.com/new-frontiers-14/frontier-station-14/pull/4742)) — Cherrypick анимаций ближнего боя из апстрима и правки штыковых винтовок
- New NFSD Grenade Launcher Rounds ([#4705](https://github.com/new-frontiers-14/frontier-station-14/pull/4705)) — Новые патроны для гранатомёта NFSD
- Sniper Update ([#4200](https://github.com/new-frontiers-14/frontier-station-14/pull/4200)) — Обновление снайперской винтовки
- Give xenoborg modules their weapons back ([#4732](https://github.com/new-frontiers-14/frontier-station-14/pull/4732)) — Ксеноборгам вернули оружие их модулей
- Adding .35, .45, and shotgun shell big box ammunition options ([#4518](https://github.com/new-frontiers-14/frontier-station-14/pull/4518)) — Добавлены большие коробки боеприпасов .35, .45 и дробовых патронов
- Add new laser repeater weapon ([#4547](https://github.com/new-frontiers-14/frontier-station-14/pull/4547)) — Добавлено новое оружие — лазерный повторитель
- Buff to underpowered melee weapons ([#4493](https://github.com/new-frontiers-14/frontier-station-14/pull/4493)) — Усиление слабых видов оружия ближнего боя

## Мобы, NPC, персонажи, роли

- Emotional Support Pets Part 3 ([#4778](https://github.com/new-frontiers-14/frontier-station-14/pull/4778)) — Питомцы эмоциональной поддержки, часть 3
- Changed medibot heal threshold to match tricordrazine ([#4916](https://github.com/new-frontiers-14/frontier-station-14/pull/4916)) — Порог лечения медботов приведён в соответствие с трикордразином
- Carp Suit works now ([#4848](https://github.com/new-frontiers-14/frontier-station-14/pull/4848)) — Костюм карпа теперь работает
- Update public_affairs.yml ([#4498](https://github.com/new-frontiers-14/frontier-station-14/pull/4498)) — Обновление public_affairs.yml
- Add medical tracker for Pun and Yip ([#4776](https://github.com/new-frontiers-14/frontier-station-14/pull/4776)) — Добавлен медицинский трекер для Pun и Yip
- Hair, Felinid Tails, Lizard Wings, and Lizard Tails From RMC ([#4713](https://github.com/new-frontiers-14/frontier-station-14/pull/4713)) — Причёски, хвосты фелинидов, крылья и хвосты ящеров из RMC
- Emotional Support Pets Part 2 ([#4699](https://github.com/new-frontiers-14/frontier-station-14/pull/4699)) — Питомцы эмоциональной поддержки, часть 2
- Pitbull Modifications ([#4710](https://github.com/new-frontiers-14/frontier-station-14/pull/4710)) — Изменения питбуля
- edit yml ([#4748](https://github.com/new-frontiers-14/frontier-station-14/pull/4748)) — Правка yml
- Makes Hamsters Wearable as Hats ([#4700](https://github.com/new-frontiers-14/frontier-station-14/pull/4700)) — Хомяков можно носить как шляпы
- Ports cyborg chips by @deltanedas from DeltaV to Frontier. ([#4400](https://github.com/new-frontiers-14/frontier-station-14/pull/4400)) — Порт киборг-чипов от @deltanedas из DeltaV во Frontier
- Added the appropriate tags to curie that let her use the VIM ([#4703](https://github.com/new-frontiers-14/frontier-station-14/pull/4703)) — Curie добавлены нужные теги, чтобы она могла пользоваться VIM
- _DV Cherrypick: Kitsune Markings to Felinid & Vulpkanin Markings ([#4631](https://github.com/new-frontiers-14/frontier-station-14/pull/4631)) — Cherrypick из _DV: маркинги китсунэ для фелинидов и вулпканинов
- Space Adders are no longer able to slip through shutters, airlocks, etc. ([#4659](https://github.com/new-frontiers-14/frontier-station-14/pull/4659)) — Космические гадюки больше не проскальзывают через ставни, шлюзы и т. п.
- Adding Common Radio Key for Command Cats ([#4604](https://github.com/new-frontiers-14/frontier-station-14/pull/4604)) — Добавлен общий радиоключ для командных кошек
- Edited Moth.xml ([#4584](https://github.com/new-frontiers-14/frontier-station-14/pull/4584)) — Правка Moth.xml
- Caddy ([#4514](https://github.com/new-frontiers-14/frontier-station-14/pull/4514)) — Caddy
- Ticks no longer go under doors ([#4564](https://github.com/new-frontiers-14/frontier-station-14/pull/4564)) — Клещи больше не проходят под дверями
- Cherry-pick NPCs behaving better in nograv/space by @Ilya246 ([#4107](https://github.com/new-frontiers-14/frontier-station-14/pull/4107)) — Cherry-pick: NPC лучше ведут себя в невесомости/космосе от @Ilya246

## Медицина, химия, ботаника, кухня

- Increase paper cup volume from 5u to 15u ([#4852](https://github.com/new-frontiers-14/frontier-station-14/pull/4852)) — Объём бумажного стаканчика увеличен с 5u до 15u
- None pizza left beef ([#4797](https://github.com/new-frontiers-14/frontier-station-14/pull/4797)) — Пицца «None» осталась с говядиной (исправление рецепта)
- Plant analyzer UI now remember last scanned plant and properly updates scanning mode ([#4662](https://github.com/new-frontiers-14/frontier-station-14/pull/4662)) — Интерфейс анализатора растений запоминает последнее отсканированное растение и корректно обновляет режим сканирования
- Minor Pizza Recipe Changes ([#4601](https://github.com/new-frontiers-14/frontier-station-14/pull/4601)) — Небольшие изменения рецептов пиццы
- Rejoice, bartenders! Bluespace shakers! ([#4733](https://github.com/new-frontiers-14/frontier-station-14/pull/4733)) — Радуйтесь, бармены! Bluespace-шейкеры!
- The Initial Rise of the Egg-based Dish (Egg-based Food Cooking Update) ([#4583](https://github.com/new-frontiers-14/frontier-station-14/pull/4583)) — Начало подъёма блюд из яиц (обновление приготовления яичной еды)
- Added animal stomach variant for making raw sausage ([#4591](https://github.com/new-frontiers-14/frontier-station-14/pull/4591)) — Добавлен вариант животного желудка для приготовления сырой колбасы
- Plant Analyzer no longer stops working ([#4655](https://github.com/new-frontiers-14/frontier-station-14/pull/4655)) — Анализатор растений больше не перестаёт работать
- cooking oils, and oil are now slippery ([#4638](https://github.com/new-frontiers-14/frontier-station-14/pull/4638)) — Кулинарные масла и масло теперь скользкие
- Tea Service ([#4569](https://github.com/new-frontiers-14/frontier-station-14/pull/4569)) — Чайный сервиз
- Port: Cryo pod UI from wizden ([#4562](https://github.com/new-frontiers-14/frontier-station-14/pull/4562)) — Порт: интерфейс криокапсулы из wizden
- Fools Silver Cigarettes ([#4533](https://github.com/new-frontiers-14/frontier-station-14/pull/4533)) — Серебряные сигареты «Fools»
- Added Vox Chitter and Clicking (#40878) ([#4485](https://github.com/new-frontiers-14/frontier-station-14/pull/4485)) — Добавлены щебет и щёлканье воксов (#40878)

## Одежда, косметика, предметы

- Departmental EVA suit functionality for scurrets! ([#4966](https://github.com/new-frontiers-14/frontier-station-14/pull/4966)) — Функциональность отделочных EVA-костюмов для scurret-ов!
- Adds VisionCorrection component to NFSD Glasses ([#4889](https://github.com/new-frontiers-14/frontier-station-14/pull/4889)) — Компонент VisionCorrection добавлен в очки NFSD
- Vox and Harpy sprites update ([#4773](https://github.com/new-frontiers-14/frontier-station-14/pull/4773)) — Обновление спрайтов воксов и гарпий
- Fire Helmet light ([#4869](https://github.com/new-frontiers-14/frontier-station-14/pull/4869)) — Свет на пожарном шлеме
- Add in-hand and helmet clothing configurations to Black Cat Plushie ([#4927](https://github.com/new-frontiers-14/frontier-station-14/pull/4927)) — Добавлены настройки одежды в руках и шлема для плюшевого Чёрного кота
- Update MNK loadouts ([#4903](https://github.com/new-frontiers-14/frontier-station-14/pull/4903)) — Обновление снаряжения MNK
- cat ears hat now properly fits cats ([#4775](https://github.com/new-frontiers-14/frontier-station-14/pull/4775)) — Шапка с кошачьими ушами теперь правильно садится на кошек
- Offset Canes Loadout and Lathe Update ([#4870](https://github.com/new-frontiers-14/frontier-station-14/pull/4870)) — Смещение тростей в снаряжении и обновление станка
- Update hardsuit-helmets.yml ([#4910](https://github.com/new-frontiers-14/frontier-station-14/pull/4910)) — Обновление hardsuit-helmets.yml
- Ninja sword, legal no more! ([#4882](https://github.com/new-frontiers-14/frontier-station-14/pull/4882)) — Меч ниндзя — больше не легален!
- Pirate Lord's Hardsuit ([#4873](https://github.com/new-frontiers-14/frontier-station-14/pull/4873)) — Скафандр Пиратского лорда
- Brigmedic headset ids to First Responder ([#4755](https://github.com/new-frontiers-14/frontier-station-14/pull/4755)) — ID гарнитуры Brigmedic выданы First Responder
- pAI borg brain ([#4859](https://github.com/new-frontiers-14/frontier-station-14/pull/4859)) — Мозг pAI для борга
- Slowdown Reduction readded to Combat Boots ([#4761](https://github.com/new-frontiers-14/frontier-station-14/pull/4761)) — Снова добавлено уменьшение замедления боевым ботинкам
- Aurecorsa Pirate hardsuit (and pirate duffels) ([#4575](https://github.com/new-frontiers-14/frontier-station-14/pull/4575)) — Скафандр пирата Aurecorsa (и пиратские вещмешки)
- Consistent Lights For EVA Suits ([#4677](https://github.com/new-frontiers-14/frontier-station-14/pull/4677)) — Единое освещение для скафандров EVA
- Misc wheelchair changes ([#4747](https://github.com/new-frontiers-14/frontier-station-14/pull/4747)) — Разные изменения инвалидных колясок
- Change encryption key descriptions, and add greeting keys to loadouts ([#4573](https://github.com/new-frontiers-14/frontier-station-14/pull/4573)) — Изменены описания ключей шифрования, добавлены ключи приветствия в снаряжение
- Super Harmonica ([#4777](https://github.com/new-frontiers-14/frontier-station-14/pull/4777)) — Супер-гармоника
- Use non-throwing ways of reading loadout groups ([#4739](https://github.com/new-frontiers-14/frontier-station-14/pull/4739)) — Чтение групп снаряжения без выбрасывающих исключения способов
- Adds inhands & helmet sprites for the Gnome plushie ([#4766](https://github.com/new-frontiers-14/frontier-station-14/pull/4766)) — Добавлены инхенды и спрайты шлема для плюшевого гнома
- Removed marking poster rolls as contraband ([#4684](https://github.com/new-frontiers-14/frontier-station-14/pull/4684)) — Рулоны плакатов больше не помечаются контрабандой
- Delta v port clothes and misc ([#4744](https://github.com/new-frontiers-14/frontier-station-14/pull/4744)) — Порт одежды и прочего из DeltaV
- Damage modifiers for equippable items. ([#4570](https://github.com/new-frontiers-14/frontier-station-14/pull/4570)) — Модификаторы урона для надеваемых предметов
- Hamster mask modifications ([#4616](https://github.com/new-frontiers-14/frontier-station-14/pull/4616)) — Изменения маски-хомяка
- Added meson scanner goggles ([#4648](https://github.com/new-frontiers-14/frontier-station-14/pull/4648)) — Добавлены очки-сканеры мезонов
- Prevent loadout wristsets from being marked as contraband ([#4688](https://github.com/new-frontiers-14/frontier-station-14/pull/4688)) — Наручные наборы из снаряжения больше не помечаются контрабандой
- Adds brigmed glasses ([#4693](https://github.com/new-frontiers-14/frontier-station-14/pull/4693)) — Добавлены очки brigmed
- Yarrmoth Helmet Update ([#4716](https://github.com/new-frontiers-14/frontier-station-14/pull/4716)) — Обновление шлема Yarrmoth
- Health analyzer in loadout uses powered version ([#4689](https://github.com/new-frontiers-14/frontier-station-14/pull/4689)) — Анализатор здоровья в снаряжении теперь использует версию с питанием
- Add a clue to the Greenhorn pAI about being insertable in PDAs ([#4711](https://github.com/new-frontiers-14/frontier-station-14/pull/4711)) — Добавлена подсказка для pAI Greenhorn о том, что его можно вставлять в КПК
- Adds inhands & helmet sprites for the Petrocks ([#4621](https://github.com/new-frontiers-14/frontier-station-14/pull/4621)) — Добавлены инхенды и спрайты шлема для Petrock-ов
- Hamster hat modifications ([#4615](https://github.com/new-frontiers-14/frontier-station-14/pull/4615)) — Изменения шляпы-хомяка
- Added command variants of NFSD wristcomms for sergeant/bailiff, replaced existing wristcomms on loadouts ([#4612](https://github.com/new-frontiers-14/frontier-station-14/pull/4612)) — Добавлены командные варианты наручных коммуникаторов NFSD для сержанта/бейлифа, заменены прежние в снаряжении
- Allow Card items in wallet ([#4661](https://github.com/new-frontiers-14/frontier-station-14/pull/4661)) — Карточные предметы можно класть в бумажник
- Reversed parent order for FaceHuggerPlushie ([#4585](https://github.com/new-frontiers-14/frontier-station-14/pull/4585)) — Изменён порядок родителей FaceHuggerPlushie на обратный
- Adds inhands & helmet sprites for the Goblin plushie ([#4501](https://github.com/new-frontiers-14/frontier-station-14/pull/4501)) — Добавлены инхенды и спрайты шлема для плюшевого гоблина
- squares aren't real, idiot ([#4592](https://github.com/new-frontiers-14/frontier-station-14/pull/4592)) — Квадратов не существует, дурак
- A fancy new Navy Officer Sabre! ([#4530](https://github.com/new-frontiers-14/frontier-station-14/pull/4530)) — Новая шикарная сабля флотского офицера!
- Expands pAI access to radio ([#4497](https://github.com/new-frontiers-14/frontier-station-14/pull/4497)) — pAI получили расширенный доступ к радио
- Experimental Salvager Suit Helmet Sprites Addition ([#4528](https://github.com/new-frontiers-14/frontier-station-14/pull/4528)) — Экспериментальные спрайты шлема утилизатора
- Pride month FREE! ([#4566](https://github.com/new-frontiers-14/frontier-station-14/pull/4566)) — Месяц гордости БЕСПЛАТНО!
- Rogue Suit ([#4555](https://github.com/new-frontiers-14/frontier-station-14/pull/4555)) — Rogue-костюм
- Clown Skirts made obtainable ([#4561](https://github.com/new-frontiers-14/frontier-station-14/pull/4561)) — Юбки клоуна стали доступны для получения
- Wrist comms ([#4450](https://github.com/new-frontiers-14/frontier-station-14/pull/4450)) — Наручные коммуникаторы
- Lizard Plushies addition ([#4482](https://github.com/new-frontiers-14/frontier-station-14/pull/4482)) — Добавлены плюшевые ящерицы
- Harpy and vox states for pirate hardsuits ([#4511](https://github.com/new-frontiers-14/frontier-station-14/pull/4511)) — Состояния для гарпий и воксов у пиратских скафандров
- Adds Arcadia Beret ([#4517](https://github.com/new-frontiers-14/frontier-station-14/pull/4517)) — Добавлен берет Arcadia
- Loadout item grouping ([#4353](https://github.com/new-frontiers-14/frontier-station-14/pull/4353)) — Группировка предметов снаряжения
- Departmental Cat Suits & SuitStorage Capability: Part 1 ([#4444](https://github.com/new-frontiers-14/frontier-station-14/pull/4444)) — Костюмы-кошки по отделам и возможность хранилища костюма: часть 1
- Adds papers to loadout ([#4492](https://github.com/new-frontiers-14/frontier-station-14/pull/4492)) — Бумаги добавлены в снаряжение

## Атмос, энергия, инженерия, крафт

- Added Plant Analyzer to Medical TechFab ([#4971](https://github.com/new-frontiers-14/frontier-station-14/pull/4971)) — Анализатор растений добавлен в медицинский техфаб
- Add StationBeaconPart (?????) to Flatpack Vendor, add StaticPrice ([#4967](https://github.com/new-frontiers-14/frontier-station-14/pull/4967)) — В Flatpack Vendor добавлен StationBeaconPart (?????) и StaticPrice
- Changed NFSD Flag and NFSD Banner ([#4875](https://github.com/new-frontiers-14/frontier-station-14/pull/4875)) — Изменены флаг и баннер NFSD
- allows blast doors to connect with walls and windows ([#4809](https://github.com/new-frontiers-14/frontier-station-14/pull/4809)) — Взрывные двери теперь могут соединяться со стенами и окнами
- Makes all remaining circuit boards autopopulate the circuit imprinter when researched + some liquid dispensers boards adjustments ([#4942](https://github.com/new-frontiers-14/frontier-station-14/pull/4942)) — Все оставшиеся платы автоматически заполняют принтер схем после изучения, плюс правки плат жидкостных раздатчиков
- Add pirate techfab layers ([#4905](https://github.com/new-frontiers-14/frontier-station-14/pull/4905)) — Добавлены слои пиратского техфаба
- a pair of heels make their way into an AutoDrobe ([#4925](https://github.com/new-frontiers-14/frontier-station-14/pull/4925)) — Пара каблуков появилась в AutoDrobe
- Add a handheld version of televisions ([#4945](https://github.com/new-frontiers-14/frontier-station-14/pull/4945)) — Добавлена портативная версия телевизоров
- Added missing .45 ammo to mercenary static packs ([#4949](https://github.com/new-frontiers-14/frontier-station-14/pull/4949)) — В статические наборы наёмников добавлены недостающие патроны .45
- add intercoms to autolathe ([#4924](https://github.com/new-frontiers-14/frontier-station-14/pull/4924)) — Интеркомы добавлены в автолат
- Change distro mixer suffix to include "Ship" ([#4895](https://github.com/new-frontiers-14/frontier-station-14/pull/4895)) — В суффикс смесителя распределения добавлено «Ship»
- Added missing recipes from BioGenMaterialsStatic that weren't part of the NF namespace version for consistency ([#4919](https://github.com/new-frontiers-14/frontier-station-14/pull/4919)) — Добавлены недостающие рецепты из BioGenMaterialsStatic, которых не было в версии пространства имён NF, для единообразия
- Reverted changes made by #4729 to nanomed inventory ([#4938](https://github.com/new-frontiers-14/frontier-station-14/pull/4938)) — Откат изменений #4729 в ассортименте nanomed
- Porting Pipe layer selector for ghosted atmos devices from Starlight ([#4886](https://github.com/new-frontiers-14/frontier-station-14/pull/4886)) — Порт выбора слоя труб для призрачных атмосферных устройств из Starlight
- Invincible shuttle wall and window ([#4741](https://github.com/new-frontiers-14/frontier-station-14/pull/4741)) — Неразрушимая стена и окно шаттла
- Add Screen Timer Electronics to Engi and NFSD techfabs ([#4841](https://github.com/new-frontiers-14/frontier-station-14/pull/4841)) — Электроника «Screen Timer» добавлена в техфабы инженеров и NFSD
- Add Smoking Supplies to Biogen Emag Inventory ([#4754](https://github.com/new-frontiers-14/frontier-station-14/pull/4754)) — В аварийный ассортимент Biogen добавлены курительные принадлежности
- Corrected inverted .60 round recipes ([#4884](https://github.com/new-frontiers-14/frontier-station-14/pull/4884)) — Исправлены перевёрнутые рецепты патронов .60
- Pirate Bounty Rework ([#4807](https://github.com/new-frontiers-14/frontier-station-14/pull/4807)) — Переработка пиратских баунти
- Added RCDandRemote Ressistent Airlock ([#4791](https://github.com/new-frontiers-14/frontier-station-14/pull/4791)) — Добавлен устойчивый шлюз для RCD и пульта
- Update purity text and power supply section in offshore gas mining guide ([#4851](https://github.com/new-frontiers-14/frontier-station-14/pull/4851)) — Обновлён текст о чистоте и разделе энергоснабжения в руководстве по морской добыче газа
- Port inline filters/multifilter from coyote ([#4723](https://github.com/new-frontiers-14/frontier-station-14/pull/4723)) — Порт встроенных фильтров / мультифильтра из Coyote
- Port Optical Thermal temperature shading ([#4641](https://github.com/new-frontiers-14/frontier-station-14/pull/4641)) — Порт оптического теплового затенения температуры
- Improve Cyborgs with New Modules and QoL Changes ([#4794](https://github.com/new-frontiers-14/frontier-station-14/pull/4794)) — Улучшены киборги: новые модули и удобства
- Made cassette tapes record longer and rewind a bit faster ([#4740](https://github.com/new-frontiers-14/frontier-station-14/pull/4740)) — Кассеты теперь записывают дольше и перематываются немного быстрее
- Increase the number of construction ghosts per tile ([#4725](https://github.com/new-frontiers-14/frontier-station-14/pull/4725)) — Увеличено число призраков строительства на тайл
- Smartfridges are no longer gridbound ([#4795](https://github.com/new-frontiers-14/frontier-station-14/pull/4795)) — Умные холодильники больше не привязаны к гриду
- (De)Constructable Diagonal walls ([#4610](https://github.com/new-frontiers-14/frontier-station-14/pull/4610)) — Разбираемые и собираемые диагональные стены
- Buildable asteroid facade walls and brick walls ([#4377](https://github.com/new-frontiers-14/frontier-station-14/pull/4377)) — Строящиеся стены-фасады астероида и кирпичные стены
- Update silo.yml ([#4793](https://github.com/new-frontiers-14/frontier-station-14/pull/4793)) — Обновление silo.yml
- Handheld radio rework ([#4342](https://github.com/new-frontiers-14/frontier-station-14/pull/4342)) — Переработка портативных раций
- Add YarRPED downgrader for pirates ([#4690](https://github.com/new-frontiers-14/frontier-station-14/pull/4690)) — Добавлен даунгрейдер YarRPED для пиратов
- Adding the Hazard Catwalk. A catwalk with hazard stripes, and no other properties. ([#4606](https://github.com/new-frontiers-14/frontier-station-14/pull/4606)) — Добавлен «опасный» мостик: решётчатый настил с полосами опасности и без прочих свойств
- More fuel in your generators ([#4704](https://github.com/new-frontiers-14/frontier-station-14/pull/4704)) — Больше топлива в генераторах
- Hide Autolink Doors/Buttons ([#4714](https://github.com/new-frontiers-14/frontier-station-14/pull/4714)) — Скрытие автосвязанных дверей и кнопок
- Service Lathe Additions ([#4607](https://github.com/new-frontiers-14/frontier-station-14/pull/4607)) — Дополнения для сервисного станка
- Recipe (New) (De)Constructable Reinforced Wooden Tables ([#4605](https://github.com/new-frontiers-14/frontier-station-14/pull/4605)) — Рецепт (новый): разбираемые и собираемые укреплённые деревянные столы
- Recipe (New) Craftable Anomalite ([#4576](https://github.com/new-frontiers-14/frontier-station-14/pull/4576)) — Рецепт (новый): крафт аномалита
- Happy Birthday Daniel!!! ([#4628](https://github.com/new-frontiers-14/frontier-station-14/pull/4628)) — С днём рождения, Даниил!!!
- Caddy needs a Doctor ID ([#4656](https://github.com/new-frontiers-14/frontier-station-14/pull/4656)) — Caddy'ю нужен ID доктора
- Added the pride cloaks to the Pride-o-Mat's inventory. ([#4567](https://github.com/new-frontiers-14/frontier-station-14/pull/4567)) — Плащи прайда добавлены в ассортимент Pride-o-Mat
- CentComm Shipyards ([#4611](https://github.com/new-frontiers-14/frontier-station-14/pull/4611)) — Верфи CentComm
- Add Pick N Pack Guns to the NFSDfab and Mercfab ([#4546](https://github.com/new-frontiers-14/frontier-station-14/pull/4546)) — Добавлены Pick N Pack Guns в NFSDfab и Mercfab
- Added missing construction entries for the uranium directional windows ([#4614](https://github.com/new-frontiers-14/frontier-station-14/pull/4614)) — Добавлены недостающие записи крафта для урановых направленных окон
- allow fat as frying oil, allow fat in service selective dropper ([#4637](https://github.com/new-frontiers-14/frontier-station-14/pull/4637)) — Жир можно использовать как масло для фритюра и в избирательной капельнице сервиса
- Minor spelling and grammar corrections ([#4650](https://github.com/new-frontiers-14/frontier-station-14/pull/4650)) — Небольшие исправления орфографии и грамматики
- Lamp Altar ([#4568](https://github.com/new-frontiers-14/frontier-station-14/pull/4568)) — Алтарь-лампа
- Remove anchorable flag from signs ([#4593](https://github.com/new-frontiers-14/frontier-station-14/pull/4593)) — У табличек убран флаг «можно закреплять»
- One single line of YAML (I am glad it was this easy) ([#4531](https://github.com/new-frontiers-14/frontier-station-14/pull/4531)) — Одна-единственная строка YAML (хорошо, что всё оказалось так просто)
- Moveable Iceboxes ([#4551](https://github.com/new-frontiers-14/frontier-station-14/pull/4551)) — Перемещаемые ледники (холодильные ящики)
- Increase MaxItem Size Treasure Chest Can Store ([#4516](https://github.com/new-frontiers-14/frontier-station-14/pull/4516)) — Увеличен максимальный размер предметов, которые вмещает сундук с сокровищами
- More posters ([#4397](https://github.com/new-frontiers-14/frontier-station-14/pull/4397)) — Больше плакатов
- Spawned wallmounted substations and memory cells now properly give back machine parts on deconstruct ([#4542](https://github.com/new-frontiers-14/frontier-station-14/pull/4542)) — Заспавненные настенные подстанции и ячейки памяти теперь возвращают детали машины при разборке
- Portable Recharger can now be worn on your belt and suit storage slots ([#4527](https://github.com/new-frontiers-14/frontier-station-14/pull/4527)) — Портативное зарядное устройство теперь можно носить на поясе и в слоте хранения костюма
- SmartFridge Ingredients ([#4504](https://github.com/new-frontiers-14/frontier-station-14/pull/4504)) — Ингредиенты для умного холодильника
- Pirate Longcoat Update ([#4510](https://github.com/new-frontiers-14/frontier-station-14/pull/4510)) — Обновление длинного пиратского плаща
- Add hangable posters and paintings, NFWallmountableSystem ([#4481](https://github.com/new-frontiers-14/frontier-station-14/pull/4481)) — Добавлены подвесные плакаты и картины, система NFWallmountableSystem

## Экономика, карго, баунти, торговля

- STC Traffic Warning & Citation Tweaks ([#4915](https://github.com/new-frontiers-14/frontier-station-14/pull/4915)) — Правки предупреждений и штрафов STC Traffic
- Disambiguate crusher into crusher axe ([#4850](https://github.com/new-frontiers-14/frontier-station-14/pull/4850)) — Разделение crusher на crusher axe
- Made the station AI core crate unavailable from cargo ([#4929](https://github.com/new-frontiers-14/frontier-station-14/pull/4929)) — Ящик с ядром станционного ИИ недоступен в карго
- Added Laser Repeater to loot pools ([#4673](https://github.com/new-frontiers-14/frontier-station-14/pull/4673)) — Лазерный повторитель добавлен в пулы лута
- Whitelist-Based Market Modifiers for Sale Consoles ([#4489](https://github.com/new-frontiers-14/frontier-station-14/pull/4489)) — Модификаторы рынка на основе белого списка для консолей продажи
- Bureaucratic Consistency Pass ([#4770](https://github.com/new-frontiers-14/frontier-station-14/pull/4770)) — Проход по бюрократической согласованности
- Syndicate Estoc Rifle ([#4548](https://github.com/new-frontiers-14/frontier-station-14/pull/4548)) — Синдикатская винтовка Estoc
- Restores T4 blueprints to silicon expeds ([#4730](https://github.com/new-frontiers-14/frontier-station-14/pull/4730)) — Чертежи T4 возвращены экспедициям silicon
- Add more Light Crates to the TM Cargo Request ([#4758](https://github.com/new-frontiers-14/frontier-station-14/pull/4758)) — Больше световых ящиков в запросе TM Cargo
- Ports the mail trolley from Starlight by @Happyrobot33 ([#4552](https://github.com/new-frontiers-14/frontier-station-14/pull/4552)) — Порт почтовой тележки из Starlight от @Happyrobot33
- Update Forms to use the New Markdown Tags From RMC ([#4667](https://github.com/new-frontiers-14/frontier-station-14/pull/4667)) — Формы обновлены под новые теги Markdown из RMC
- Add Metal Foam grenades to Mall ([#4708](https://github.com/new-frontiers-14/frontier-station-14/pull/4708)) — Гранаты с металлической пеной добавлены в Mall
- Changed mentions of old CargoOrderConsoleComponent to new NFCargoOrderConsoleComponent ([#4627](https://github.com/new-frontiers-14/frontier-station-14/pull/4627)) — Упоминания старого CargoOrderConsoleComponent заменены на новый NFCargoOrderConsoleComponent
- added nitrogen grenades ([#4643](https://github.com/new-frontiers-14/frontier-station-14/pull/4643)) — Добавлены азотные гранаты
- removes Lone Digger from jukebox due to copyright ([#4652](https://github.com/new-frontiers-14/frontier-station-14/pull/4652)) — Lone Digger убран из музыкального автомата из-за авторских прав
- Removes practice ammo from loot pools (V2) ([#4608](https://github.com/new-frontiers-14/frontier-station-14/pull/4608)) — Учебные патроны убраны из пулов лута (V2)
- Cybersun Pen made cheaper ([#4535](https://github.com/new-frontiers-14/frontier-station-14/pull/4535)) — Ручка Cybersun стала дешевле

## Интерфейс и клиент

- Add vague hints to artifact effects instead of 'unknown' ([#4978](https://github.com/new-frontiers-14/frontier-station-14/pull/4978)) — Туманные подсказки об эффектах артефактов вместо «неизвестно»
- Remove the NSF Wasp from the Shipyard ([#4878](https://github.com/new-frontiers-14/frontier-station-14/pull/4878)) — Корабль NSF Wasp убран из верфи
- Add the Blink Emote ([#4939](https://github.com/new-frontiers-14/frontier-station-14/pull/4939)) — Добавлена эмоция «Моргнуть»
- Safezone rule rewording ([#4856](https://github.com/new-frontiers-14/frontier-station-14/pull/4856)) — Переформулировка правила безопасной зоны
- Remove Outdated Cryo Rules ([#4844](https://github.com/new-frontiers-14/frontier-station-14/pull/4844)) — Удалены устаревшие правила криокамер
- Chem Rebalance ([#4657](https://github.com/new-frontiers-14/frontier-station-14/pull/4657)) — Ребаланс химии
- Caddy Medical Cat Accent ([#4672](https://github.com/new-frontiers-14/frontier-station-14/pull/4672)) — Медицинский акцент кошки Caddy
- Flappy Moff ([#4536](https://github.com/new-frontiers-14/frontier-station-14/pull/4536)) — Flappy Moff
- Brass (Filled & Reebe) plating changed to floors ([#4832](https://github.com/new-frontiers-14/frontier-station-14/pull/4832)) — Латунные (Filled и Reebe) покрытия заменены на полы
- Missing locales and typos cleanup ([#4803](https://github.com/new-frontiers-14/frontier-station-14/pull/4803)) — Очистка недостающих локалей и опечаток
- Shield ExamineDamage ([#4818](https://github.com/new-frontiers-14/frontier-station-14/pull/4818)) — Показ урона щита при осмотре (ExamineDamage)
- Syndicate Spray Painter, Spray Painter Filtering & Docking Airlock Painting ([#4458](https://github.com/new-frontiers-14/frontier-station-14/pull/4458)) — Синдикатский краскопульт, фильтрация краскопульта и покраска стыковочных шлюзов
- wording change ([#4788](https://github.com/new-frontiers-14/frontier-station-14/pull/4788)) — Изменена формулировка
- Pirate Yarrmag ([#4720](https://github.com/new-frontiers-14/frontier-station-14/pull/4720)) — Пиратский Yarrmag
- Remove streetpunk leave -> delta'd accent replacement ([#4671](https://github.com/new-frontiers-14/frontier-station-14/pull/4671)) — Убрана замена акцента «streetpunk leave → delta'd»
- Shuttle (New): NT Dendron ([#4438](https://github.com/new-frontiers-14/frontier-station-14/pull/4438)) — Шаттл (новый): NT Dendron
- Shuttle (Update) Spectre ([#4692](https://github.com/new-frontiers-14/frontier-station-14/pull/4692)) — Шаттл (обновление) Spectre
- Emotional Support Pets and Loadout ([#4609](https://github.com/new-frontiers-14/frontier-station-14/pull/4609)) — Питомцы эмоциональной поддержки и снаряжение
- Update shuttle_maps.yml ([#4680](https://github.com/new-frontiers-14/frontier-station-14/pull/4680)) — Обновление shuttle_maps.yml
- Shuttle (New) (Syndicate) CS Voltage ([#4529](https://github.com/new-frontiers-14/frontier-station-14/pull/4529)) — Шаттл (новый) (Синдикат) CS Voltage
- Better VIM Instructions ([#4582](https://github.com/new-frontiers-14/frontier-station-14/pull/4582)) — Улучшенные инструкции для VIM
- Shuttle (Hotfix) NT Backpacker: Added Extra copyright ([#4587](https://github.com/new-frontiers-14/frontier-station-14/pull/4587)) — Шаттл (хотфикс) NT Backpacker: добавлено дополнительное указание авторских прав
- Shuttle (New): SSF Rusty ([#4600](https://github.com/new-frontiers-14/frontier-station-14/pull/4600)) — Шаттл (новый): SSF Rusty
- Fillable Forms Markdown Tags: Port ([#4565](https://github.com/new-frontiers-14/frontier-station-14/pull/4565)) — Теги Markdown для заполняемых форм: порт
- Shuttle (New) PT Jackrabbit ([#4579](https://github.com/new-frontiers-14/frontier-station-14/pull/4579)) — Шаттл (новый) PT Jackrabbit
- Remove autodespawn from vgroid bosses ([#4549](https://github.com/new-frontiers-14/frontier-station-14/pull/4549)) — Убрано автоматическое исчезновение боссов vgroid
- Shuttle (New) NT Backpacker ([#4334](https://github.com/new-frontiers-14/frontier-station-14/pull/4334)) — Шаттл (новый) NT Backpacker
- Shuttle (New): SSF Coffeehouse ([коммит 43af85ad](https://github.com/new-frontiers-14/frontier-station-14/commit/43af85ad9f818e793c2bb60f5ad6d9a1e93a5967)) — Шаттл (новый): SSF Coffeehouse
- Ship (New) PT Balena ([#4560](https://github.com/new-frontiers-14/frontier-station-14/pull/4560)) — Корабль (новый) PT Balena
- Minor Drozd Code Oopsie ([#4308](https://github.com/new-frontiers-14/frontier-station-14/pull/4308)) — Небольшая ошибка в коде Drozd
- Ammmunition ([#4538](https://github.com/new-frontiers-14/frontier-station-14/pull/4538)) — Боеприпасы
- Ports more chat indicators from DeltaV (DeltaV#4679) ([#4347](https://github.com/new-frontiers-14/frontier-station-14/pull/4347)) — Порт ещё индикаторов чата из DeltaV (DeltaV#4679)
- Overhaul jukebox UI (wizden#42210) ([#4259](https://github.com/new-frontiers-14/frontier-station-14/pull/4259)) — Переработка интерфейса музыкального автомата (wizden#42210)
- Wizard staves damage adjustments, contra status updates for wizard items ([#4422](https://github.com/new-frontiers-14/frontier-station-14/pull/4422)) — Правки урона посохов волшебников и обновления статусов для предметов волшебников

## Код и системы (Server/Shared)

- Makes IoC compatible with the RT 277.0.0 [Dependency] source generator ([#4811](https://github.com/new-frontiers-14/frontier-station-14/pull/4811)) — IoC совместим с генератором [Dependency] из RT 277.0.0
- No Admin PDA will be sentient again. ([#4866](https://github.com/new-frontiers-14/frontier-station-14/pull/4866)) — Админ-КПК снова не станет разумным
- Drop carried person when they are cryo'd ([#4724](https://github.com/new-frontiers-14/frontier-station-14/pull/4724)) — Носимый персонаж выпадает, когда его криозаморозили
- Your hunger will now start at 150 (Okay) on spawn ([#4806](https://github.com/new-frontiers-14/frontier-station-14/pull/4806)) — Голод теперь начинается на 150 (Окей) при спавне
- Dirty EmitterComponent when changing BoltType by signal ([#4810](https://github.com/new-frontiers-14/frontier-station-14/pull/4810)) — Компонент Emitter помечается изменённым при смене BoltType по сигналу
- Change vgroids to spawn in an arc around Barrier Gate ([#4590](https://github.com/new-frontiers-14/frontier-station-14/pull/4590)) — vgroid теперь появляются дугой вокруг Barrier Gate
- Micro-QoL: Skip appraisal of bluespace event grids when unnecessary ([#4800](https://github.com/new-frontiers-14/frontier-station-14/pull/4800)) — Микро-удобство: пропуск оценки гридов событий bluespace, когда не нужно
- Allow wheelchair users to crawl ([#4718](https://github.com/new-frontiers-14/frontier-station-14/pull/4718)) — Пользователи инвалидных колясок могут ползать
- Huggable wheelchair users! ([#4669](https://github.com/new-frontiers-14/frontier-station-14/pull/4669)) — Инвалидов-колясочников можно обнимать!
- Disabled the more destructive admin smites & adjusted verb icons ([#4588](https://github.com/new-frontiers-14/frontier-station-14/pull/4588)) — Отключены наиболее разрушительные админские кары, изменены иконки вербов
- Ports ExpireIDCard radio alerts from Harmony by @BlueMoon059 ([#4545](https://github.com/new-frontiers-14/frontier-station-14/pull/4545)) — Порт оповещений ExpireIDCard по радио из Harmony от @BlueMoon059
- Predict ReplaceOnTrigger spawning and despawning ([#4709](https://github.com/new-frontiers-14/frontier-station-14/pull/4709)) — Предсказание появления и исчезновения ReplaceOnTrigger
- Check for virtual item before using popup ([#4726](https://github.com/new-frontiers-14/frontier-station-14/pull/4726)) — Проверка виртуального предмета перед использованием всплывающего окна
- Goobstation Cherrypick: Custom Admin Drop Pods ([#4626](https://github.com/new-frontiers-14/frontier-station-14/pull/4626)) — Goobstation Cherrypick: кастомные админские дроп-поды
- Science (buff) node scanner range increase ([#4674](https://github.com/new-frontiers-14/frontier-station-14/pull/4674)) — Наука (усиление): увеличена дальность сканера узлов
- Bags can insert their content into a microwave when used on it ([#4346](https://github.com/new-frontiers-14/frontier-station-14/pull/4346)) — Сумки при использовании на микроволновке высыпают в неё содержимое
- Halved the time for butchering on a meat spike ([#4557](https://github.com/new-frontiers-14/frontier-station-14/pull/4557)) — Время разделки на мясном шипе уменьшено вдвое
- tweak the variation value of sound_mecha_hydraulic ([#4563](https://github.com/new-frontiers-14/frontier-station-14/pull/4563)) — Правка значения вариации звука sound_mecha_hydraulic
- Adds a nullcheck on a racecondition that updates admin status. ([#4553](https://github.com/new-frontiers-14/frontier-station-14/pull/4553)) — Добавлена проверка на null при гонке условий, обновляющей статус администратора
- Xenoartifacts trigger from rads ([#4539](https://github.com/new-frontiers-14/frontier-station-14/pull/4539)) — Ксеноартефакты срабатывают от радиации
- Added Mech hydraulic sound variation ([#4521](https://github.com/new-frontiers-14/frontier-station-14/pull/4521)) — Добавлено разнообразие звука гидравлики меха
- Add commands for managing machine DRM ([#4532](https://github.com/new-frontiers-14/frontier-station-14/pull/4532)) — Добавлены команды управления DRM машин
- Don't reset respawn timer when dying as a ghost role ([#4487](https://github.com/new-frontiers-14/frontier-station-14/pull/4487)) — Таймер респауна не сбрасывается, если умереть будучи призрачной ролью
- Prioritize signing papers over eating them ([#4491](https://github.com/new-frontiers-14/frontier-station-14/pull/4491)) — Подпись бумаг приоритетнее, чем их поедание

## Прочее

- No more Christmas in November ([#4964](https://github.com/new-frontiers-14/frontier-station-14/pull/4964)) — Больше никакого Рождества в ноябре
- ZERO REMOVED!!! ([#4899](https://github.com/new-frontiers-14/frontier-station-14/pull/4899)) — ZERO УДАЛЁН!!!
- Mechs no longer slip ([#4926](https://github.com/new-frontiers-14/frontier-station-14/pull/4926)) — Мехи больше не скользят
- Updated Xeno prototype summoned by artifacts ([#4914](https://github.com/new-frontiers-14/frontier-station-14/pull/4914)) — Обновлён прототип ксено, призываемого артефактами
- Readded knuckle crack emote to moths, unintentionally removed by #4536 ([#4934](https://github.com/new-frontiers-14/frontier-station-14/pull/4934)) — Эмоция хруста костяшек возвращена мотылькам (случайно убрана #4536)
- Restores the DeathGasp Emote ([#4861](https://github.com/new-frontiers-14/frontier-station-14/pull/4861)) — Возвращена эмоция DeathGasp
- Private-er Medical Lobby art ([#4804](https://github.com/new-frontiers-14/frontier-station-14/pull/4804)) — Более приватный арт медицинского лобби
- what's a crusher dagger glaive? ([#4860](https://github.com/new-frontiers-14/frontier-station-14/pull/4860)) — Что такое «crusher dagger glaive»?
- Adds an eye slot to monkey entities ([#4812](https://github.com/new-frontiers-14/frontier-station-14/pull/4812)) — Добавлен слот для глаз у сущностей-обезьян
- Appraisal tool improvements ([#4820](https://github.com/new-frontiers-14/frontier-station-14/pull/4820)) — Улучшения инструмента оценки
- More mail lobby art ([#4779](https://github.com/new-frontiers-14/frontier-station-14/pull/4779)) — Больше арта для почтового лобби
- Corrected spelling of disassembled ([#4729](https://github.com/new-frontiers-14/frontier-station-14/pull/4729)) — Исправлена орфография слова «disassembled»
- Remove GITHUB_TOKEN from changelog validation env ([#4790](https://github.com/new-frontiers-14/frontier-station-14/pull/4790)) — Убран GITHUB_TOKEN из окружения проверки changelog
- Open detective slot at roundstart ([#4746](https://github.com/new-frontiers-14/frontier-station-14/pull/4746)) — Слот детектива открыт на старте раунда
- Missing Sounds for Moths and Arachnids ([#4696](https://github.com/new-frontiers-14/frontier-station-14/pull/4696)) — Недостающие звуки для мотыльков и арахнидов
- Change listplayers command permissions to require the PII flag (#40324) ([#4706](https://github.com/new-frontiers-14/frontier-station-14/pull/4706)) — Права команды listplayers теперь требуют флаг PII (#40324)
- Admeme Drop Pods - Volume Edit ([#4701](https://github.com/new-frontiers-14/frontier-station-14/pull/4701)) — Админские дроп-поды: правка громкости
- Changes mead to use honey instead of sugar ([#4624](https://github.com/new-frontiers-14/frontier-station-14/pull/4624)) — Медовуха теперь делается из мёда вместо сахара
- I'm a Maptainer Now! ([#4537](https://github.com/new-frontiers-14/frontier-station-14/pull/4537)) — Теперь я «Maptainer»!
- Tag Defenition Hotfix ([#4666](https://github.com/new-frontiers-14/frontier-station-14/pull/4666)) — Хотфикс определения тега
- Add two-way ATMs to mapchecker blacklist for ships ([#4619](https://github.com/new-frontiers-14/frontier-station-14/pull/4619)) — Двусторонние банкоматы добавлены в чёрный список mapchecker для кораблей
- Removes practice ammo from exped pools ([#4519](https://github.com/new-frontiers-14/frontier-station-14/pull/4519)) — Учебные патроны убраны из пулов экспедиций
- Cherry-pick the PR form update from wizden so it makes it easier to file issue. ([#4496](https://github.com/new-frontiers-14/frontier-station-14/pull/4496)) — Cherry-pick обновления формы PR из wizden, чтобы было проще создавать issue
- Added ingredient tag to the Cooking Bag's whitelist ([#4503](https://github.com/new-frontiers-14/frontier-station-14/pull/4503)) — Тег ингредиента добавлен в белый список сумки повара

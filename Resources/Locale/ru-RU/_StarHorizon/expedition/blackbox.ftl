expedition-blackbox-announcement = Задача: найдите чёрный ящик в глубине комплекса, активируйте его и скачайте данные. Пока данные качаются, оставайтесь рядом с ящиком: его сигнал привлечёт врагов.
expedition-blackbox-started = Скачивание данных началось ({$seconds} с). Оставайтесь в зоне {$radius} м вокруг ящика: без вас скачивание встаёт. Сигнал засекли, враги идут!
expedition-blackbox-busy = Ящик уже передаёт данные.
expedition-blackbox-done = Данные чёрного ящика скачаны. Задача выполнена!
expedition-hud-objective-activate = Активировать: {$name}
expedition-hud-objective-download = Скачать данные
expedition-hud-paused = (зона пуста)
ent-ExpeditionBlackBox = чёрный ящик
    .desc = Бортовой самописец пропавшего корабля, всё ещё подключённый к обломкам. Скачать данные получится не сразу, и его сигнал не останется незамеченным.
salvage-expedition-type-Retrieval = Чёрный ящик

salvage-expedition-type-Mining = Квота добычи
salvage-expedition-type-Repair = Ремонт шаттла
salvage-expedition-type-Sampling = Сбор образцов

expedition-depot-dropped = Приёмник сброшен рядом с вами. Сдавайте в него добычу.
expedition-depot-rejected = Приёмник это не принимает.
expedition-depot-accepted = Принято: {$count}
expedition-quota-announcement = Задача: добудьте и сдайте в приёмник {$count} руды. Приёмник сбросят в капсуле.
expedition-quota-done = Квота добычи выполнена!
expedition-samples-announcement = Задача: найдите в комплексе светящиеся образцы и сдайте в приёмник {$count} шт. Приёмник сбросят в капсуле.
expedition-samples-done = Приёмник с образцами поднят фултоном. Задача выполнена!
expedition-repair-announcement = Задача: эвакуационный шаттл рухнул посреди комплекса. Найдите и почините разбросанные по комплексу узлы шаттла, затем удержите его, пока запускаются системы.
expedition-repair-incoming = Эвакуационный шаттл теряет управление и садится в районе ({$x}, {$y})!
expedition-repair-crashed = Шаттл рухнул посреди комплекса. Повреждено узлов: {$count}, их разбросало по комплексу. Найдите и почините их.
expedition-repair-start = Вы начинаете чинить узел...
expedition-repair-point-done = Узел починен.
expedition-repair-defend = Узлы починены, системы запускаются. Удержите шаттл {$seconds} секунд: враги уже идут!
expedition-repair-done = Системы шаттла запущены. Шаттл готов к эвакуации!
expedition-repair-failed = Шаттл не починили. Высылаем резервный шаттл. Задача провалена.
expedition-extraction-ready = Эвакуация через {$minutes} мин. с вашего шаттла. Не опаздывайте на борт!

expedition-hud-ore = руда
expedition-hud-objective-quota = Сдать руду
expedition-hud-objective-samples = Сдать: {$name}
expedition-hud-objective-repair = Починить узлы шаттла
expedition-hud-objective-defend = Удержать шаттл

ent-ExpeditionDepot = приёмник экспедиции
    .desc = Автоматический приёмник. Руда и образцы в нём сразу учитываются и отправляются на орбиту.
ent-ExpeditionSample = инопланетный образец
    .desc = Светящийся кристаллический нарост. Учёные хорошо платят за такие.
ent-ExpeditionRepairPoint = повреждённый узел шаттла
    .desc = Одна из систем шаттла, выбитая при крушении. Её можно залатать руками.
ent-ExpeditionDropSiteMarker = место падения капсул экспедиции
    .desc = { ent-MarkerBase.desc }
    .suffix = Капсулы волн

expedition-samples-fulton = Образцы собраны! Привяжите приёмник к фултону, чтобы отправить их на орбиту.
expedition-samples-fulton-attached = Приёмник привязан к фултону: подъём через {$seconds} с.
expedition-depot-verb-fulton = Привязать к фултону
expedition-hud-objective-fulton = Отправить приёмник фултоном

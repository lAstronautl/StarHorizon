freight-console-window-title = Консоль перевозок
freight-console-here = Этот адрес:
freight-console-refresh-in = Новые заказы через { $time }
freight-console-tab-orders = Заказы
freight-console-tab-manifest = Накладная и продажа
freight-console-no-offers = Сейчас нет доступных заказов. Список обновится позже.
freight-console-from = Откуда:
freight-console-to = Куда:
freight-console-route-line = • [bold]{ $destination }[/bold] — { $crates } — [color=#ffd966]{ $credits } сп.[/color], { $tokens } ток.
freight-console-total = { $credits } сп. · { $tokens } ток.
freight-console-total-line = Итого: [color=#ffd966]{ $total }[/color]
freight-console-take-button = Взять
freight-console-manifest-header = Накладная
freight-console-insert-hint = Вставьте накладную в слот консоли. Накладная принимается только на её адресе «Откуда».
freight-console-here-ok = (вы на месте)
freight-console-here-wrong = (внесите накладную там)
freight-console-spawn-button = Выдать грузы
freight-console-no-manifest = Накладная не вставлена.
freight-console-sell-header = Продажа
freight-console-pallets-status = На поддонах: подходит [bold]{ $sellable }[/bold] на [color=#ffd966]{ $value } сп.[/color], [color={ $wrongColor }]не по адресу: { $wrong }[/color]
freight-console-sell-button = Продать грузы на поддонах
freight-console-spawned = Накладная принята, грузы выданы.
freight-console-nothing-to-sell = На поддонах нет грузов для этого адреса.
freight-console-offer-gone = Этот заказ уже недоступен.
freight-console-printing = Принтер занят, подождите.
freight-console-wrong-source = Эта накладная принимается только на адресе загрузки: { $source }.
freight-console-sold = Продано ящиков: { $count }. Получено: { $credits } спесо, { $tokens } торг. токенов.
freight-no-destination = не определён
freight-crate-description = Груз по накладной «{ $addressee }». Доставить: { $destination }.
freight-manifest-name = накладная: { $order }
freight-manifest-title = НАКЛАДНАЯ НА ПЕРЕВОЗКУ — { $order }
freight-manifest-source = Откуда: { $source }
freight-manifest-to = Куда:
freight-manifest-line = • { $destination }: { $name } ×{ $count } ({ $credits } сп., { $tokens } ток. за ящик)
freight-manifest-footer = Внесите накладную в консоль перевозок на адресе «{ $source }», затем доставьте каждый ящик по указанному адресу и продайте его на поддоне.
freight-destination-cargo-a = Точка Продажи A
freight-destination-cargo-b = Точка Продажи B
freight-destination-cargo-c = Точка Продажи C
freight-destination-cargo-d = Точка Продажи D
freight-destination-cargo-e = Точка Продажи E
freight-destination-cargo-f = Точка Продажи F
freight-destination-cargo-g = Точка Продажи G
freight-destination-cargo-h = Точка Продажи H
freight-destination-edison = Электростанция Эдисона
freight-destination-nfsd = Аванпост ДШНФ
freight-destination-frontier = Аванпост Фронтира
freight-destination-nt-deport = Склад NT
freight-destination-pirate-deport = Точка Продажи Пиратов
freight-destination-syndicate-deport = Точка Продажи Синдиката
freight-destination-gsec-deport = Торговый аутпост Горлекс
freight-order-engineering = Инженерные поставки
freight-order-engineering-desc = Инструменты и топливо для мастерских.
freight-order-medical = Медицинские поставки
freight-order-medical-desc = Препараты и медицинское оборудование.
freight-order-science = Научный груз
freight-order-science-desc = Оборудование и артефакты для лабораторий.
freight-order-supplies = Снабжение
freight-order-supplies-desc = Расходные материалы и продовольствие.
freight-order-arms = Оружейная партия
freight-order-arms-desc = Боеприпасы и детали вооружения.
freight-order-black-market = Тихая поставка
freight-order-black-market-desc = Груз без лишних вопросов. Только с чёрных складов.
stack-trade-token =
    { $amount ->
        [one] торговый токен
        [few] торговых токена
       *[other] торговых токенов
    }

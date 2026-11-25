marketplace-item-slot-name = Лоток торговой площадки

## Окно

marketplace-window-title = Торговая площадка сектора
marketplace-header-title = [font size=16][bold][color=#7CC4FF]ТОРГОВАЯ ПЛОЩАДКА[/color] [color=#E8EEF7]СЕКТОРА[/color][/bold][/font]
marketplace-header-terminal = [color={$mutedColor}]Терминал:[/color] [color={$accentColor}]{$grid}[/color]  [color={$mutedColor}]· ПКМ или Shift+ЛКМ по предмету — осмотреть[/color]

marketplace-stat-balance = [color={$mutedColor}]Баланс[/color]  [bold][color={$priceColor}]{$balance}[/color][/bold]
marketplace-stat-no-account = [color=#FF6B6B]Нет банковского счёта[/color]
marketplace-stat-tax = [color={$mutedColor}]Налог[/color]  [bold]{$tax}%[/bold]
marketplace-stat-listings = [color={$mutedColor}]Лотов[/color]  [bold]{$count}[/bold]
marketplace-stat-pending = [color={$mutedColor}]Ожидает выплаты[/color]  [bold][color=#FFD866]{$amount}[/color][/bold]

marketplace-tab-market = Рынок
marketplace-tab-sell = Продать
marketplace-tab-mine = Мои лоты

marketplace-search-placeholder = Поиск: предмет, продавец, станция или #номер лота
marketplace-sort-label = Сортировка:
marketplace-sort-newest = Сначала новые
marketplace-sort-price-asc = Цена: по возрастанию
marketplace-sort-price-desc = Цена: по убыванию
marketplace-sort-name = По названию
marketplace-sort-count = По количеству
marketplace-empty = На рынке пока нет ни одного лота.
marketplace-empty-search = Ничего не найдено.
marketplace-mine-empty = У вас нет выставленных лотов.

marketplace-examine-hint = ПКМ или Shift+ЛКМ — осмотреть предмет

## Карточка лота

marketplace-card-price-unit = [bold][color={$priceColor}]{$price}[/color][/bold] [color={$mutedColor}]за шт.[/color]
marketplace-card-price-single = [bold][color={$priceColor}]{$price}[/color][/bold]
marketplace-card-stock = [color={$mutedColor}]В наличии:[/color] {$count} шт.
marketplace-card-seller = [color={$mutedColor}]Продавец:[/color] {$seller}
marketplace-card-id = [color={$mutedColor}]ID-карта:[/color] {$idName}
marketplace-card-station = [color={$mutedColor}]Станция:[/color] [color={$accentColor}]{$grid}[/color]  [color={$mutedColor}]· Лот #{$id}[/color]
marketplace-card-total = [color={$mutedColor}]Итого:[/color] [bold][color={$color}]{$total}[/color][/bold]
marketplace-card-own = [color={$accentColor}]Ваш лот[/color]
marketplace-card-amount = Кол-во:
marketplace-card-buy = Купить
marketplace-card-withdraw = Снять

## Продажа

marketplace-sell-slot-title = Предмет в терминале
marketplace-sell-slot-empty = [color={$mutedColor}]Лоток пуст[/color]
marketplace-sell-slot-hint = [color={$mutedColor}]Положите предмет в консоль, чтобы выставить его на продажу. Стакаемые ресурсы можно продавать частями.[/color]
marketplace-sell-available = [color={$mutedColor}]В наличии:[/color] {$count} шт.
marketplace-sell-eject = Извлечь предмет
marketplace-sell-title = Новый лот
marketplace-sell-amount = Количество
marketplace-sell-all = Всё
marketplace-sell-price-unit = Цена за штуку
marketplace-sell-price-single = Цена
marketplace-sell-sum = Сумма продажи
marketplace-sell-tax = Налог сектора ({$tax}%)
marketplace-sell-receive = Вы получите
marketplace-sell-merge = [color={$accentColor}]Будет объединено с вашим лотом #{$id} (сейчас {$count} шт.) — та же цена и тот же ресурс.[/color]
marketplace-sell-button = Выставить на рынок
marketplace-sell-note = [color={$mutedColor}]Налог {$tax}% удерживается только при продаже. Лот можно снять в любой момент; все лоты сбрасываются в конце смены.[/color]

## Сообщения

marketplace-no-id = Без ID-карты
marketplace-unknown-grid = Неизвестная станция

marketplace-error-no-item = В лотке нет предмета.
marketplace-error-no-account = У вас нет банковского счёта.
marketplace-error-bad-price = Цена должна быть от 1 до {$max}.
marketplace-error-forbidden = Этот предмет нельзя выставить на продажу.
marketplace-error-too-many-listings = Нельзя иметь больше {$max} лотов одновременно.
marketplace-error-failed = Не удалось выставить предмет.
marketplace-error-no-listing = Этот лот больше недоступен.
marketplace-error-not-owner = Это не ваш лот.
marketplace-error-price-changed = Цена лота изменилась, проверьте ещё раз.
marketplace-error-own-listing = Нельзя купить собственный лот.
marketplace-error-bad-amount = Можно купить от 1 до {$max} шт.
marketplace-error-too-expensive = Слишком крупная сделка, уменьшите количество.
marketplace-error-insufficient-funds = Недостаточно средств: нужно {$amount}.

marketplace-popup-listed = {$item} выставлен на рынок (лот #{$id}).
marketplace-popup-merged = {$item} добавлен к вашему лоту #{$id}.
marketplace-popup-withdrawn = Лот #{$id} снят с продажи.
marketplace-popup-bought = Куплено: {$item} ×{$amount} за {$total}.
marketplace-popup-sold = Продано: {$item} ×{$amount}. Зачислено {$payout} (налог {$tax}).
marketplace-popup-sold-pending = Продано: {$item} ×{$amount}. {$payout} ожидает выплаты на консоли торговой площадки.
marketplace-popup-pending-paid = Зачислена выручка с торговой площадки: {$amount}.

## Прототипы

ent-ComputerMarketplace = консоль торговой площадки
    .desc = Подключена к торговой сети сектора. Положите предмет внутрь, чтобы продать его другим космонавтам, или купите товары, выставленные на любой станции.
ent-MarketplaceComputerCircuitboard = плата консоли торговой площадки
    .desc = Печатная плата для консоли торговой площадки.

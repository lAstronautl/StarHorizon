ui-options-hear-self = Слышать себя в голосовом чате
ui-options-voice-chat-volume = Громкость голосового чата
ui-options-function-voice-push-to-talk = Голосовой чат: говорить (рядом)
ui-options-function-voice-push-to-talk-radio = Голосовой чат: говорить (рация)

ui-escape-voice-chat = Голосовой чат

cmd-voicechat-desc = Показывает инструкцию по голосовому чату с вашей ссылкой и кодом
cmd-voicechat-help = Использование: voicechat
cmd-voicechat-unavailable = Голосовой чат на этом сервере не включён.

voice-link-connected = Ваш микрофон подключён.
voice-link-disconnected = Ваш микрофон ещё не подключён.
voice-link-computer-title = На этом компьютере
voice-link-computer-text = Открывает страницу голосового чата в браузере.
voice-link-phone-title = На телефоне
voice-link-phone-text = Откройте этот адрес и введите код:
voice-link-expiry = Не закрывайте страницу, пока играете. Код действует, пока вы на сервере.
voice-link-loading = Получаем вашу ссылку...
voice-link-open = Открыть в браузере
voice-link-copy = Копировать ссылку

cmd-voicemute-desc = Включает или выключает голосовой чат игрока.
cmd-voicemute-help = Использование: voicemute <имя пользователя>
cmd-voicemute-hint = <имя пользователя>
cmd-voicemute-muted = Голосовой чат игрока {$player} отключён.
cmd-voicemute-unmuted = Голосовой чат игрока {$player} включён.

comms-console-menu-voice-broadcast-button = Голосовое объявление
comms-console-menu-voice-broadcast-button-tooltip = Ненадолго обратиться ко всей станции через голосовой чат.
comms-console-menu-voice-broadcast-stop = Завершить голосовое объявление ({$seconds} с)
comms-console-menu-voice-broadcast-busy = Идёт голосовое объявление
comms-console-menu-voice-broadcast-cooldown = Голосовое объявление ({$seconds} с)

voice-broadcast-denied = Доступ запрещён.
voice-broadcast-not-connected = Сначала откройте голосовой чат: Esc, Голосовой чат.
voice-broadcast-started = Вы вещаете на всю станцию в течение {$seconds} с.
voice-broadcast-ended = Ваше голосовое объявление закончилось.

voice-radio-button-off = Рядом
voice-radio-button-tooltip = Рация в голосовом чате: выберите канал, на котором говорите, каналы, которые слышите, и громкость рации.

voice-verb-category = Голосовой чат
voice-verb-mute = Заглушить
voice-verb-unmute = Включить звук
voice-verb-volume = Громкость {$percent}%

voice-speaker-unknown = Неизвестный
voice-route-radio = Рация
voice-route-telephone = Телефон
voice-route-holopad = Голопад
voice-route-camera = Камера
voice-route-broadcast = Объявление
voice-speaker-self = Вы
voice-self-blocked = Не можете говорить

voice-radio-popup-talk = Говорить
voice-radio-popup-hear = Слышать
voice-radio-popup-volume = Громкость
voice-radio-popup-volume-value = {$percent}%

voice-loudness-shout = Крик
voice-loudness-whisper = Шёпот
voice-loudness-megaphone = Мегафон

admin-player-actions-window-voice-logs = Записи голоса
cmd-voicelogs-desc = Открывает просмотр записей голосового чата.
cmd-voicelogs-help = Использование: voicelogs [имя пользователя]
cmd-voicelogs-hint = [имя пользователя]
voice-logs-verb = Записи голоса
voice-logs-title = Записи голоса
voice-logs-round = Раунд
voice-logs-round-entry = Раунд {$id} · {$length}
voice-logs-round-current = Раунд {$id} (текущий) · {$length}
voice-logs-player = Игрок
voice-logs-add = Добавить
voice-logs-refresh = Обновить
voice-logs-play = Воспроизвести
voice-logs-pause = Пауза
voice-logs-fit = Уместить
voice-logs-loading = Загрузка…
voice-logs-empty = Выберите игрока и нажмите «Добавить», чтобы добавить его дорожку.
voice-logs-mute = Заглушить дорожку
voice-logs-solo = Только эта дорожка
voice-logs-remove = Убрать дорожку
voice-logs-lobby = Лобби
voice-logs-blocked = {$name} (заблокирован)

voice-god-verb = Глас Божий
voice-god-verb-stop = Прекратить Глас Божий
voice-god-started = Теперь вы говорите с {$target} как Глас Божий. Больше никто вас не слышит.
voice-god-started-offline = Теперь вы говорите с {$target} как Глас Божий, но ваша страница голосового чата не открыта. Откройте её через Esc > Голосовой чат.
voice-god-hear-self = Вы тоже будете слышать себя как голос, с тем же свечением на экране.
voice-god-stopped = Вы больше не говорите как Глас Божий.
voice-god-name = Голос
voice-god-log-name = Глас Божий для {$target}
voice-route-god = Божественный
voice-self-god = Глас Божий
cmd-voiceofgod-desc = Говорить как Глас Божий с одним игроком, всеми рядом, отделом или всей станцией. Без аргументов — остановить.
cmd-voiceofgod-help = Использование: voiceofgod <имя пользователя> [hearSelf] | voiceofgod radius <клетки> [hearSelf] | voiceofgod department <id> [hearSelf] | voiceofgod station [hearSelf]
cmd-voiceofgod-hint = <имя пользователя> | radius | department | station
cmd-voiceofgod-radius-hint = <клетки>
cmd-voiceofgod-department-hint = <отдел>
cmd-voiceofgod-hear-self-hint = [hearSelf]
cmd-voiceofgod-bad-hear-self = hearSelf должен быть true или false.
cmd-voiceofgod-bad-radius = Укажите радиус в клетках от 1 до {$max}.
cmd-voiceofgod-bad-department = Такого отдела не существует.
voice-god-target-radius = все в радиусе {$radius} клеток от вас
voice-god-target-department = отдел {$department}
voice-god-target-station = вся станция
voice-god-cue = В вашей голове звучит голос...

voice-mic-offline = Голосовой чат не открыт. Нажмите, чтобы открыть.
voice-mic-ready = Микрофон включён. Нажмите, чтобы выключить.
voice-mic-live = Вы в эфире. Нажмите, чтобы выключить микрофон.
voice-mic-blocked = Ваш персонаж сейчас не может говорить.
voice-mic-muted = Микрофон выключен. Нажмите, чтобы включить.
voice-mic-deafened = Звук отключён. Нажмите, чтобы вернуть звук и включить микрофон.
voice-deafen-off = Отключить весь голосовой чат и микрофон.
voice-deafen-on = Звук отключён. Нажмите, чтобы вернуть.
voice-radio-popup-mute-all = Заглушить все
voice-radio-popup-unmute-all = Включить все

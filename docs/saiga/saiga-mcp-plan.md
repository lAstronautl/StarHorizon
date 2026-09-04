# MCP-сервер игровых тулзов агента в SS14-сервере (Monolith)

## Цель

Выставить **игровые действия embodied-агента** (`observe / move_to / pick_up / attack`)
как **MCP-сервер** (Model Context Protocol, JSON-RPC 2.0), чтобы **любая внешняя LLM**
(Claude, GPT, чья угодно — с MCP-поддержкой) могла подключиться и рулить агентом по
стандартному протоколу.

**Ключевое:** проект НЕ привязан к собственной модели «Сайга». Сайга — лишь один из
возможных клиентов, опциональный. Сервер выставляет наружу **действия в мире**, а не
саму LLM. Поэтому `saiga_chat`/`saiga_status` из прошлой редакции плана **исключены** —
они выставляли свою модель, что противоположно задаче.

**Выбранный дизайн (обоснование развилок):**
- **Транспорт — свой минимальный MCP поверх существующего `IStatusHost`**, а не официальный
  C# SDK `ModelContextProtocol`. SDK завязан на ASP.NET Core/Kestrel/`Microsoft.Extensions.Hosting`;
  движок крутит собственный `SpaceWizards.HttpListener` за `IStatusHost`. Тащить Kestrel вторым
  сетевым стеком в игровой процесс ради нескольких инструментов — дорого. По спецификации
  Streamable HTTP **POST с одиночным JSON-RPC-запросом можно отвечать одиночным
  `application/json`** — SSE не нужен. Реализация = один обработчик на `/mcp` +
  `System.Text.Json`. Content.Server **не в песочнице**, поэтому `System.Net`/`System.Text.Json`
  легальны.
- **Версия протокола:** анонсируем `2025-06-18`, эхо-им версию клиента из `initialize`, если задана.
- **Безопасность не выкидывается, а становится слоем ВНУТРИ сервера.** Существующий
  `allowlist (enum AllowedTool) + law-set` валидатор переезжает внутрь `Execute` каждого тула:
  `LLM → MCP client → /mcp → McpTool.Execute → allowlist + law-set check → выполнение в игре`.
- **ECS-действия исполняются на главном потоке.** Хендлеры `IStatusHost` идут вне игрового
  потока, а игровые тулзы трогают мир ⇒ тело `Execute` оборачиваем в `ITaskManager.RunOnMainThread`.
  Это главное отличие от чат-обёртки, которой это было не нужно.
- **Сам embodied-агент остаётся in-process.** Его per-tick петля продолжает звать игровую
  логику напрямую (минимальный риск, latency-бюджет не трогаем). MCP — только для **внешних**
  клиентов.

## Проверенные факты кодовой базы (транспорт — подтверждено в прошлой сессии)

- `IStatusHost.AddHandler(StatusHostHandlerAsync)`, где `delegate Task<bool>
  StatusHostHandlerAsync(IStatusHandlerContext)` (вернуть `false` → передать следующему
  обработчику, `true` → обработано). Образец — `Content.Server/.../WatchdogApi.cs`
  (хендлеры вешаются в `IPostInjectInit.PostInject()`).
- `IStatusHandlerContext` реально имеет: `RequestMethod` (`HttpMethod`), `Url`, `RequestHeaders`
  (`IReadOnlyDictionary<string,StringValues>`), `RequestBodyJsonAsync<T>()`,
  `RespondJsonAsync(object, HttpStatusCode)`, `RespondErrorAsync(HttpStatusCode)`,
  `RespondNoContentAsync()`, `RespondAsync(...)`, `AcceptWebSocketAsync()`.
- Аутентификация — копируем `Content.Server/Administration/ServerApi.cs::CheckAccess`: парсит
  `Authorization: <scheme> <value>`, сверяет `CryptographicOperations.FixedTimeEquals(UTF8(value),
  UTF8(_token))`. ServerApi использует схему `SS14Token`; **для MCP берём `Bearer`** (его шлёт
  `claude mcp add --header`).
- Привязка: `status.bind` (CVars.cs ~680-702), по умолчанию тот же порт, что у игры (1212), TCP.

## ✅ РЕАЛЬНАЯ архитектура (сверено в коде 2026-06-28)

Прошлая редакция плана стояла на двух выдуманных посылках — обе опровергнуты:

- **`enum AllowedTool` / `IsToolAllowed(tool, lawSet)` НЕ существует.** Все «lawset» в репе —
  это система законов боргов (`Content.Server/Silicons/Laws/SiliconLawSystem.cs`), к Сайге
  отношения не имеет. Никакого allowlist-валидатора у агента нет.
- **Сервер НЕ исполняет действия.** `SaigaAgentBrainSystem` (`Content.Server/_Mono/Saiga/`):
  реагирует на `ListenEvent` → считает восприятие `GetNearby` → просит у Сайги `{say}` →
  **детерминированно** выбирает действие в `ResolveMovement` → `(string Act, NetEntity? Target)`
  → `RaiseNetworkEvent(new SaigaAgentDecisionResponseEvent(say, act, target), session)`.
  **Действие выполняется на КЛИЕНТЕ.** Сервер только решает и шлёт событие в сессию игрока.

Подтверждённые факты:
- Действие = пара `(string Act, NetEntity? Target)`. Набор `Act`: `stop / follow / pickup /
  pull / throw / drop / swap / store / build / none` (`ResolveMovement`, строки 215-279).
- Событие: `SaigaAgentDecisionResponseEvent(string? say, string? action, NetEntity? target)`
  (`Content.Shared/_Mono/Saiga/SaigaAgentEvents.cs`), `[NetSerializable]`.
- Агент = сущность с `ActorComponent` (→ `actor.PlayerSession` : `ICommonSession`) и
  `SaigaAgentStateComponent`. Перцепция: `GetNearby` (range 10, фильтры container/subfloor/
  audio/spawner + line-of-sight через `ExamineSystemShared.InRangeUnOccluded`).
- `SaigaManager.ChatAsync(...)`, `LogTranscript(...)`, `bool Enabled` — публичны.
- Транспорт-паттерн `ServerApi`: `IPostInjectInit.PostInject()` → `_statusHost.AddHandler`;
  `RunOnMainThread<T>(Func<T>)` через `TaskCompletionSource` + `_taskManager.RunOnMainThread`;
  auth `CheckAccess` (Authorization → scheme+value → `FixedTimeEquals`).

## Как это меняет дизайн MCP (v1)

- **Без allowlist/law-set** — их нет; гейт = Bearer-токен + ограниченный набор тулзов +
  никаких произвольных параметров (без override endpoint и т.п.).
- **Тулзы-действия не «исполняют» в ECS, а поднимают тот же `SaigaAgentDecisionResponseEvent`
  в сессию агента** — внешняя LLM просто заменяет собой `ResolveMovement` (выбирает act+target
  напрямую), переиспользуя существующий клиентский путь исполнения.
- **Один `EntitySystem` вместо менеджера+системы.** EntitySystem авто-инстанцируется движком
  (правки `ServerContentIoC.cs`/`EntryPoint.cs` НЕ нужны), даёт и `[Dependency] IStatusHost`,
  и прямой `RaiseNetworkEvent(evt, session)`, и доступ к перцепции. Это минимизирует движущиеся
  части vs паттерн-менеджер `ServerApi`.
- **Адресация агента:** обязательный параметр `agent` в каждом туле — сетевой id (`NetEntity`)
  или имя сущности; резолвится в `EntityUid` + `ICommonSession`. `observe` отдаёт сетевые id
  окружения, чтобы LLM ссылалась на них в `target`.

## Новые файлы

1. **`Content.Shared/_Mono/Saiga/AgentMcpCVars.cs`** — `[CVarDefs]`:
   - `agent.mcp.enabled` → `bool`, default `false`, `CVar.SERVERONLY`.
   - `agent.mcp.token` → `string`, default `""`, `CVar.SERVERONLY | CVar.CONFIDENTIAL`
     (пустой токен ⇒ MCP выключен, fail-closed).
   (имя CVar-секции `agent.mcp.*`, т.к. это больше не про Сайгу.)

2. **`Content.Server/_Mono/Saiga/Mcp/McpToolRegistry.cs`** — транспорт-независимый слой
   (БЕЗ изменений относительно прошлой редакции — он переиспользуем):
   - `McpTool { string Name; string Description; JsonNode InputSchema;
     Func<JsonElement, CancellationToken, Task<McpToolResult>> Execute; }`
   - `readonly record struct McpToolResult(string Text, bool IsError);`
   - `McpToolRegistry`: `Register`, `TryGet`, `IReadOnlyCollection<McpTool> Tools`.

3. **`Content.Server/_Mono/Saiga/Mcp/AgentMcpTools.cs`** — `RegisterAll(registry, deps...)`:
   регистрирует игровые тулзы. Каждый тул:
   - **`observe`** — args `{agent}` → возвращает картину мира агента (то, что сейчас отдаёт
     observe-часть пайплайна) текстом/JSON.
   - **`move_to`** — args `{agent, target}` (координата/сущность).
   - **`pick_up`** — args `{agent, item}`.
   - **`attack`** — args `{agent, target}`.
   - Внутри КАЖДОГО `Execute`:
     ```
     1. распарсить args из JsonElement;
     2. tool := AllowedTool.X;  if (!IsToolAllowed(tool, lawSetДляAgent))
            return McpToolResult(isError:true, "запрещено law-set'ом");
     3. провалидировать параметры (тот же контроль, что в текущем пайплайне; без SSRF/произвольных таргетов);
     4. await _taskManager.RunOnMainThread(() => <реальный C#-вызов действия>);
     5. вернуть McpToolResult(текстовый результат, isError:false).
     ```
   - Ошибки исполнения тула кладём **внутрь result** (`isError:true`), не в JSON-RPC error
     (конвенция MCP).
   - JSON Schema каждого тула — объектом в `InputSchema` (для `tools/list`).

4. **`Content.Server/_Mono/Saiga/Mcp/AgentMcpManager.cs`** — транспорт (зеркало
   `WatchdogApi`/`ServerApi`):
   - `[Dependency] IStatusHost _statusHost;` `[Dependency] ITaskManager _taskManager;`
     `[Dependency] IConfigurationManager _cfg;` + зависимости, нужные тулзам (системы/менеджеры,
     двигающие агента — взять реальные из пункта «факты к подтверждению»).
   - `IPostInjectInit.PostInject()`: создать sawmill, `AgentMcpTools.RegisterAll(...)`,
     `_statusHost.AddHandler(HandleAsync)`.
   - `Initialize()`: подписаться на `agent.mcp.enabled`/`agent.mcp.token` через
     `_cfg.OnValueChanged(..., true)`.
   - `HandleAsync(ctx)`:
     - `ctx.Url.AbsolutePath != "/mcp"` → `return false`.
     - не enabled или пустой токен → `RespondErrorAsync(NotFound)`, `return true`.
     - метод != POST → `RespondAsync(..., MethodNotAllowed)`.
     - `CheckAuth(ctx)` (Bearer + `FixedTimeEquals`) провал → `RespondErrorAsync(Unauthorized)` (401).
     - `RequestBodyJsonAsync<JsonElement>()`; ошибка → JSON-RPC `-32700 Parse error`.
     - `DispatchAsync(ctx, root)`; `return true`.
   - `DispatchAsync` — JSON-RPC по полю `method`:
     - `initialize` → `{protocolVersion: negotiate, capabilities:{tools:{listChanged:false}},
       serverInfo:{name:"ss14-agent-mcp", version:"0.1.0"}}`.
     - `notifications/initialized` → `RespondNoContentAsync()`.
     - `ping` → `{}`.
     - `tools/list` → `{tools: registry.Tools → {name, description, inputSchema}}`.
     - `tools/call` → найти тул, `Execute(args)`; результат → `{content:[{type:"text",text}], isError}`.
       Неизвестный метод → `-32601`; неизвестный тул → `-32602`.
   - Хелперы: `RespondRpcResult/Error(ctx, id, ...)` → `ctx.RespondJsonAsync({jsonrpc:"2.0",
     id, result|error})` (HTTP 200); `CheckAuth`; `NegotiateVersion`.

## Правки существующих файлов

- **`Content.Server/IoC/ServerContentIoC.cs`** (рядом с `SaigaManager`, ~стр. 93):
  `IoCManager.Register<AgentMcpManager>(); // Mono — MCP game tools`
- **`Content.Server/Entry/EntryPoint.cs`** (после init `SaigaManager`, ~стр. 134):
  `IoCManager.Resolve<AgentMcpManager>().Initialize(); // Mono — MCP game tools`
  (`PostInject()` навесит обработчик при сборке графа IoC, как у `ServerApi`.)

## Порядок сборки

0. **Сначала** — подтвердить факты из раздела «⚠️ Факты к подтверждению» (валидатор + реальные
   методы действий + как адресуется агент).
1. `AgentMcpCVars.cs` → 2. `McpToolRegistry.cs` → 3. `AgentMcpTools.cs` (с реальными вызовами и
   allowlist/law-set внутри) → 4. `AgentMcpManager.cs` → 5. правки `ServerContentIoC.cs` +
   `EntryPoint.cs` → 6. `dotnet build Content.Server`.
   Usings: `System.Text.Json[.Nodes]`, `System.Security.Cryptography`, `System.Text`, `System.Net`,
   `Robust.Server.ServerStatus`, `Robust.Shared.Configuration`, `Robust.Shared.Asynchronous`
   (`ITaskManager`), `Content.Shared._Mono.Saiga`.

## Проверка (end-to-end)

Конфиг сервера: `agent.mcp.enabled true`, `agent.mcp.token "devsecret"`.
`dotnet run --project Content.Server` (статус-HTTP на порту игры, 1212).

**(a) curl:**
```bash
# initialize → result.serverInfo.name == "ss14-agent-mcp", protocolVersion == "2025-06-18"
curl -s http://127.0.0.1:1212/mcp -H 'Authorization: Bearer devsecret' \
  -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"curl","version":"0"}}}'

# tools/list → result.tools[] содержит observe / move_to / pick_up / attack с inputSchema
curl -s http://127.0.0.1:1212/mcp -H 'Authorization: Bearer devsecret' \
  -H 'Content-Type: application/json' -d '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'

# tools/call observe
curl -s http://127.0.0.1:1212/mcp -H 'Authorization: Bearer devsecret' \
  -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"observe","arguments":{"agent":"<uid>"}}}'

# law-set negative: вызвать тул, запрещённый текущим law-set → result.isError == true
```
Негатив: без `Authorization` → 401; `agent.mcp.enabled false` → `/mcp` отдаёт 404; прочие
статус-эндпоинты (`/info`) работают.

**(b) любая внешняя LLM через Claude Code как клиент:**
```bash
claude mcp add --transport http agent http://127.0.0.1:1212/mcp --header "Authorization: Bearer devsecret"
claude mcp list      # agent → connected
# в сессии: «вызови observe для агента <uid>, потом move_to к ближайшему предмету»
```

## Дальше (вне v1)

- **RAG-сервер по crafting-recipe YAML** — отдельный MCP-сервер (не встроенный в C#), агент
  цепляется к нему как клиент.
- **Сайга как опциональный клиент** — если хочется, чтобы локальная модель тоже ходила через
  этот же реестр тулзов (делить будущие игровые инструменты), её можно подключить как ещё
  один MCP-клиент; embodied-петля при этом остаётся in-process для latency.

## Замечания по безопасности

- `status.bind` по умолчанию слушает все интерфейсы → для dev-машины рекомендуется
  `status.bind 127.0.0.1:1212` (внешний доступ — через SSH-туннель). Bearer-токен — основной гейт.
- Весь существующий security-слой (allowlist + law sets + валидация параметров) **обязателен
  внутри каждого `Execute`** — MCP это транспорт, а не замена проверкам. Для статьи это и есть
  вклад: «security-слой реализован как MCP-сервер ⇒ переносим на произвольные LLM-бэкенды».

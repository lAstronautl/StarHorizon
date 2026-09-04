# Saiga — ИИ-агент с локальной LLM + MCP-сервер для Space Station 14

Исследовательский мод для [Space Station 14](https://spacestation14.io/) (форк Monolith /
Forge-Station, RobustToolbox, .NET): даёт персонажу **ИИ-мозг** на локальной LLM и выставляет
**игровые действия агента как MCP-сервер** (Model Context Protocol, JSON-RPC 2.0), чтобы агентом
могла управлять **любая внешняя LLM** (Claude, GPT, Qwen, Сайга…) по стандартному протоколу.

> ⚠️ Это **исходник мода, не самостоятельное приложение.** C#-код зависит от RobustToolbox и
> проектов `Content.*` SS14 — собирается только внутри форка SS14. См. [установку](#установка).

> 📦 **Портировано в StarHorizon.** Код уже скопирован в дерево этого репозитория
> (`Content.Server/_Mono/Saiga`, `Content.Client/_Mono/SaigaAgent`, `Content.Shared/_Mono/Saiga`,
> `Resources/Prototypes/_Mono/...`), `SaigaManager` зарегистрирован в
> `Content.Server/IoC/ServerContentIoC.cs` и `Content.Server/Entry/EntryPoint.cs`. `Content.Server`
> и `Content.Client` собираются без ошибок. При портировании на два места пришлось внести правки
> под API этого форка: `ActiveListenerComponent`/`ListenEvent` здесь лежат в `Content.Shared.Speech`
> (а не в `Content.Server.Speech`, как в Monolith), а конструктор `HumanoidCharacterAppearance`
> принимает цвет волос как один `Color`, а не `List<Color>`. Python-скрипт из `agent-runner/`
> перенесён в `Tools/saiga-agent-runner/` без изменений. Инструкция по установке ниже написана для
> отдельного репозитория-мода — шаги 1 и 2 (копирование файлов, правка IoC) уже выполнены.

## Что внутри

| Слой | Файлы | Роль |
|---|---|---|
| **Локальная LLM** | `Content.Server/_Mono/Saiga/SaigaManager.cs` | Ходит в локальный Ollama **или** OpenAI-совместимый сервер (LM Studio/vLLM/llama.cpp/OpenAI). CSV-метрики + JSONL-транскрипты. |
| **Мозг агента** | `SaigaAgentBrainSystem.cs` | Серверный: услышал речь → перцепция → спросил модель `{say}` → детерминированное действие. |
| **Клиент-рулёжка** | `Content.Client/_Mono/SaigaAgent/SaigaAgentSystem.cs` | Исполняет движение/взаимодействия (жмёт клавиши/шлёт ввод), headless-автостарт. |
| **MCP-сервер** | `Content.Server/_Mono/Saiga/Mcp/SaigaMcpSystem.cs` | Эндпоинт `/mcp` (JSON-RPC) на движковом `IStatusHost`, Bearer-auth, 21 инструмент. |
| **Память-граф** | `Mcp/SaigaMemoryComponent.cs` | Граф мира агента (узлы = виденные сущности, рёбра = «рядом»), пишется из `observe`. |
| **Слух** | `Mcp/SaigaHearingComponent.cs` | Буфер услышанной речи → тул `listen` (реакция на игроков чисто через MCP). |

Внешняя LLM **заменяет собой** keyword-резолвер: тулзы-действия поднимают **то же** событие
`SaigaAgentDecisionResponseEvent`, что и мозг, в сессию агента — исполнение остаётся на клиенте,
per-tick петля агента — in-process.

## MCP-инструменты (21)

**Перцепция / память** — `observe {filter?}` (filter — имена через запятую, вернуть только их),
`listen` (кто что сказал + id говорящего), `recall {query?}`, `where_is {name}`
**Движение** — `move_to {target}` (вплотную), `follow {target}`, `stop`
**Манипуляция** — `pickup`, `pull`, `drop`, `swap`, `store`, `throw`, `place`, `use_on`, `activate`
(включить предмет в руке: зажечь сварочник/фонарик)
**Речь** — `say {text}`
**Крафт / постройка** — `recipes {query?}`, `craft {recipe}`, `construct {recipe}`, `build`

У каждого тула обязателен `agent` (сетевой id или имя персонажа). Инструменты помечаются в
`observe` своим качеством (`[инстр:Anchoring]` и т.п.). Сервер проверяет рецепты и держит всё за
Bearer-токеном. `observe` пишет виденное в память-граф — агент может действовать по `recall`/
`where_is` вместо повторной перцепции.

## Управление агентом через локальную модель — `agent-runner/`

MCP-first: вместо встроенного мозга агентом управляет **любая модель** как обычный MCP-клиент.
[`agent-runner/`](agent-runner/) — Python-цикл (без зависимостей): воспринимает через
`observe`/`recall`/`listen`, действует через вызовы MCP-тулзов. Один интерфейс для всех моделей.

```
[ твоя LLM ] --вызовы тулзов--> agent-runner --MCP /mcp--> [ игровые тулзы агента ]
```

- **Бэкенды:** `ollama` (нативный) и `openai` (LM Studio, vLLM, llama.cpp, OpenAI).
- **Режимы:** цель (`--goal`), слушающий (`--listen` — реагирует на речь рядом), prompt-режим
  (`--tool-mode prompt` для моделей без function-calling).
- **`--plan`** — сперва составить план шагов, потом выполнять (надёжнее на многошаговых задачах).
- **Анти-залипание:** рвёт повтор того же вызова, подталкивает действовать после серии «осмотров».

Подробнее — [`agent-runner/README.md`](agent-runner/README.md).

## Конфигурация (CVars)

```
saiga.enabled            true            # мастер-переключатель интеграции
saiga.api_format         ollama          # ollama | openai (для openai api_url оканчивается на /v1)
saiga.api_url            http://localhost:11434
saiga.model              hf.co/QuantFactory/saiga_gemma2_9b-GGUF:Q4_K_S
saiga.mcp.enabled        true            # выставить эндпоинт /mcp
saiga.mcp.token          <секрет>        # Bearer-токен (пусто = эндпоинт закрыт, fail-closed)
saiga.agent.autostart    true            # headless-клиент агента сам заходит и включается
saiga.transcript_path    <путь>.jsonl    # (опц.) логировать все диалоги/действия
```

## Подключить внешнюю LLM (Claude Code)

```bash
claude mcp add --transport http saiga http://127.0.0.1:1212/mcp \
  --header "Authorization: Bearer <секрет>"
claude mcp list      # saiga -> connected
```

Быстрый смоук без клиента:
```bash
curl -s http://127.0.0.1:1212/mcp -H 'Authorization: Bearer <секрет>' \
  -H 'Content-Type: application/json' -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

## Установка

См. [INSTALL.md](INSTALL.md) — скопировать деревья `Content.*` / `Resources` в свой форк SS14 и
применить две однострочные правки IoC.

## Документация

- [`docs/architecture-notes.md`](docs/architecture-notes.md) — разбор «чистый MCP-first vs гибрид vs детерминированный».
- [`docs/saiga-mcp-plan.md`](docs/saiga-mcp-plan.md) — дизайн MCP-сервера.
- [`docs/SAIGA_RESEARCH.md`](docs/SAIGA_RESEARCH.md) — исследовательские заметки/метрики.

## Известные ограничения (бэклог)

- **Подбор еды может её «съесть»** (pickup через клавишу Use по еде с занятой рукой) — нужен чистый pickup.
- **Нет обхода преград** — только грубый axis-slide, без настоящего пафайндинга.
- **Нет работы с инвентарём** — агент не видит руки/сумку/карманы/одежду, не раскладывает вещи, не одевается.

## Лицензия

Код автора мода, собран против и производный от Space Station 14 (MIT) и форка Monolith/
Forge-Station. Выпущено под [MIT](LICENSE). Движок и контент SS14 — собственность их авторов.

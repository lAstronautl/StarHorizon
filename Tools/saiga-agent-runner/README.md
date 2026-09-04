# agent-runner

Drive the in-game SS14 agent with **any** LLM — local or remote — entirely through the MCP
server. This is the MCP-first "brain": the model is just another MCP client that perceives with
`observe`/`recall` and acts by calling MCP tools (function-calling). It replaces the built-in
C# brain's direct Ollama coupling, so there's **one** control interface for every model.

```
[ your LLM ] --(tool calls)--> runner --(MCP /mcp)--> [ SS14 agent tools ]
```

Zero dependencies (Python 3, stdlib only).

## Backends

| `--backend` | Talks to | Use for |
|---|---|---|
| `ollama` | native `POST {url}/api/chat` | Ollama (Saiga/Gemma/...) |
| `openai` | `POST {url}/chat/completions` | **LM Studio**, vLLM, llama.cpp server, OpenAI, any OpenAI-compatible server |

The chosen model **must support tool/function calling**.

## Run

Prereqs: the SS14 server up with `saiga.mcp.enabled=true` + a token, and an agent in-game
(its network id or character name — see the root `INSTALL.md`).

LM Studio (OpenAI-compatible, default port 1234):

```bash
python runner.py --agent 6311 --token devsecret \
    --backend openai --backend-url http://localhost:1234/v1 --model your-loaded-model \
    --goal "Осмотрись, подойди к ближайшему предмету и возьми его."
```

Ollama:

```bash
python runner.py --agent 6311 --token devsecret \
    --backend ollama --backend-url http://localhost:11434 --model llama3.1 \
    --goal "Иди к Мире и поздоровайся."
```

Remote OpenAI:

```bash
python runner.py --agent SaigaAI --token devsecret \
    --backend openai --backend-url https://api.openai.com/v1 --api-key sk-... --model gpt-4o \
    --goal "..."
```

## Listen mode (MCP-first chat)

Stay alive and react to nearby speech via the `listen` tool — no built-in C# brain needed:

```bash
python runner.py --agent 6311 --token devsecret \
    --backend ollama --model your-model --tool-mode prompt --listen
```

The loop polls `listen`; when someone talks to the agent it runs a short episode
(observe + say + optionally move) and goes back to listening. Ctrl-C to stop.

## Options

| Flag | Default | Meaning |
|---|---|---|
| `--mcp` | `http://127.0.0.1:1212/mcp` | MCP endpoint |
| `--token` | (required) | Bearer token = `saiga.mcp.token` |
| `--agent` | (required) | agent network id or character name |
| `--backend` | `openai` | `ollama` or `openai` |
| `--backend-url` | per backend | model server base url |
| `--model` | (required) | model name/tag |
| `--api-key` | none | OpenAI key (LM Studio: any) |
| `--goal` | (required) | what the agent should do |
| `--max-steps` | `12` | tool-call iterations cap |
| `--delay` | `1.0` | seconds between steps |

## How it works

1. `initialize` + `tools/list` against `/mcp`; converts MCP tool schemas to function specs,
   hiding the `agent` param (the runner injects it automatically — it's bound to one agent).
2. Loop: ask the model with the tool specs → for each returned tool call, invoke `tools/call`
   and feed the text result back → repeat until the model stops calling tools or `--max-steps`.

## Notes

- Tool-calling quality is the model's job. Small local models can be unreliable at it; prefer an
  instruct model with solid function-calling.
- The runner binds to a single agent. Run one process per agent to drive several.

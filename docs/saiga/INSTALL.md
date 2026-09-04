# Install

This is a mod for an SS14 fork (developed against **Monolith / Forge-Station**, RobustToolbox,
.NET). It is **not** a standalone project — it builds only inside an SS14 content tree.

## 1. Copy the files

Copy these folders from this repo into the root of your SS14 fork, preserving paths:

```
Content.Server/_Mono/Saiga/...
Content.Client/_Mono/SaigaAgent/...
Content.Shared/_Mono/Saiga/...
Resources/Prototypes/_Mono/...
```

## 2. Register the SaigaManager (two one-line edits)

The MCP server, brain and NPC systems are `EntitySystem`s and auto-register — but the
`SaigaManager` IoC service needs two lines.

**`Content.Server/IoC/ServerContentIoC.cs`** — inside `Register()`:

```diff
+using Content.Server._Mono.Saiga; // Mono — local LLM (Ollama/Saiga)
 ...
             IoCManager.Register<TTSManager>(); // Corvax-TTS
+            IoCManager.Register<SaigaManager>(); // Mono — local LLM (Ollama/Saiga)
```

**`Content.Server/Entry/EntryPoint.cs`** — inside `Init()`, after the other manager inits:

```diff
+using Content.Server._Mono.Saiga; // Mono — local LLM (Ollama/Saiga)
 ...
                 IoCManager.Resolve<TTSManager>().Initialize(); // Corvax-TTS
+                IoCManager.Resolve<SaigaManager>().Initialize(); // Mono — local LLM (Ollama/Saiga)
```

> Namespaces/sibling lines may differ between forks — just place the `Register`/`Resolve` calls
> alongside the other managers.

## 3. Build & run

```bash
dotnet build Content.Server/Content.Server.csproj
dotnet run --project Content.Server -- \
  --cvar saiga.enabled=true \
  --cvar saiga.mcp.enabled=true \
  --cvar saiga.mcp.token=devsecret
```

## 4. Get an agent in-game

- **Manual:** join as a character, open the client console and run `saiga_agent on`
  (optionally `saiga_agent goal <text>`).
- **Headless:** run a second client and let it auto-join + enable itself:

```bash
dotnet run --project Content.Client -- --headless --connect --username SaigaAI \
  --cvar saiga.agent.autostart=true --cvar res.texturepreloadingenabled=false
```

## 5. Local model

Run [Ollama](https://ollama.com/) with a Saiga/Gemma model and point `saiga.api_url` at it.
The MCP transport (`tools/list`, `initialize`, auth) works without a model; only `say` / the
agent brain need Ollama.

## Security notes

- `saiga.mcp.token` empty ⇒ `/mcp` returns 404 (fail-closed). Set a secret to enable.
- `status.bind` listens on all interfaces by default — for a dev machine prefer
  `status.bind 127.0.0.1:1212` and tunnel over SSH. The Bearer token is the gate.

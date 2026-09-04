#!/usr/bin/env python3
"""
Saiga MCP agent-runner — drive the in-game SS14 agent with ANY local/remote LLM,
all through the MCP server. This is the de-duplicated "brain": instead of the
built-in C# brain calling Ollama directly, the model here is just another MCP
client and picks MCP tools via function-calling.

Backends:
  - ollama : native Ollama  POST {url}/api/chat
  - openai : OpenAI-compatible POST {url}/chat/completions
             (LM Studio, vLLM, llama.cpp server, OpenAI, ...)

Zero third-party deps (urllib only).

Example:
  python runner.py --agent 6311 --token devsecret \\
      --backend openai --backend-url http://localhost:1234/v1 --model your-model \\
      --goal "Осмотрись, подойди к ближайшему предмету и возьми его."
"""
import argparse, json, sys, time, urllib.request, urllib.error


def _post(url, payload, headers, timeout=120):
    data = json.dumps(payload).encode()
    req = urllib.request.Request(url, data=data, headers={"Content-Type": "application/json", **headers})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.load(r)


# ---------------- MCP client ----------------

class Mcp:
    def __init__(self, url, token, agent):
        self.url, self.agent = url, agent
        self.headers = {"Authorization": f"Bearer {token}"}
        self._id = 0

    def _rpc(self, method, params=None):
        self._id += 1
        body = {"jsonrpc": "2.0", "id": self._id, "method": method}
        if params is not None:
            body["params"] = params
        resp = _post(self.url, body, self.headers, timeout=30)
        if "error" in resp:
            raise RuntimeError(f"MCP error on {method}: {resp['error']}")
        return resp.get("result", {})

    def initialize(self):
        self._rpc("initialize", {"protocolVersion": "2025-06-18", "capabilities": {},
                                 "clientInfo": {"name": "saiga-agent-runner", "version": "0.1.0"}})

    def list_tools(self):
        return self._rpc("tools/list").get("tools", [])

    def call_tool(self, name, arguments):
        args = dict(arguments)
        args.setdefault("agent", self.agent)  # bind every call to our agent
        res = self._rpc("tools/call", {"name": name, "arguments": args})
        text = ""
        for block in res.get("content", []):
            if block.get("type") == "text":
                text += block.get("text", "")
        return text, bool(res.get("isError"))


def to_function_specs(mcp_tools):
    """MCP tools/list -> OpenAI/Ollama function specs, hiding the auto-injected `agent` param."""
    specs = []
    for t in mcp_tools:
        schema = json.loads(json.dumps(t.get("inputSchema", {"type": "object", "properties": {}})))
        props = schema.get("properties", {})
        props.pop("agent", None)
        schema["required"] = [r for r in schema.get("required", []) if r != "agent"]
        specs.append({"type": "function", "function": {
            "name": t["name"], "description": t.get("description", ""), "parameters": schema}})
    return specs


# ---------------- LLM backends ----------------

class OllamaBackend:
    def __init__(self, url, model):
        self.url = url.rstrip("/") + "/api/chat"
        self.model = model

    def chat(self, messages, tools):
        resp = _post(self.url, {"model": self.model, "messages": messages,
                                "tools": tools, "stream": False}, {})
        msg = resp.get("message", {}) or {}
        calls = []
        for tc in msg.get("tool_calls", []) or []:
            fn = tc.get("function", {})
            args = fn.get("arguments", {})
            if isinstance(args, str):
                args = json.loads(args or "{}")
            calls.append({"id": tc.get("id", ""), "name": fn.get("name"), "args": args})
        return msg.get("content", "") or "", calls

    def chat_json(self, messages):
        resp = _post(self.url, {"model": self.model, "messages": messages,
                                "stream": False, "format": "json"}, {})
        return (resp.get("message", {}) or {}).get("content", "") or ""

    def chat_text(self, messages):
        resp = _post(self.url, {"model": self.model, "messages": messages, "stream": False}, {})
        return (resp.get("message", {}) or {}).get("content", "") or ""


class OpenAIBackend:
    def __init__(self, url, model, api_key):
        self.url = url.rstrip("/") + "/chat/completions"
        self.model = model
        self.headers = {"Authorization": f"Bearer {api_key or 'not-needed'}"}

    def chat(self, messages, tools):
        resp = _post(self.url, {"model": self.model, "messages": messages,
                                "tools": tools, "stream": False}, self.headers)
        choices = resp.get("choices")
        if not choices:  # server returned an error (e.g. context overflow) instead of a completion
            print(f"[runner] backend error: {resp.get('error', resp)}", file=sys.stderr)
            return "", []
        msg = choices[0].get("message", {}) or {}
        calls = []
        for tc in msg.get("tool_calls", []) or []:
            fn = tc.get("function", {})
            try:
                a = json.loads(fn.get("arguments") or "{}")
            except Exception:
                a = {}
            calls.append({"id": tc.get("id", ""), "name": fn.get("name"), "args": a})
        return msg.get("content") or "", calls

    def chat_json(self, messages):
        # No response_format: some OpenAI-compatible servers (LM Studio) 400 on json_object.
        resp = _post(self.url, {"model": self.model, "messages": messages, "stream": False}, self.headers)
        ch = resp.get("choices")
        return (ch[0].get("message", {}).get("content") or "") if ch else ""

    def chat_text(self, messages):
        resp = _post(self.url, {"model": self.model, "messages": messages, "stream": False}, self.headers)
        ch = resp.get("choices")
        return (ch[0].get("message", {}).get("content") or "") if ch else ""


# ---------------- agent loop ----------------

SYSTEM = (
    "/no_think\n"  # Qwen3: suppress chain-of-thought; act via tool calls
    "Ты — персонаж в игре Space Station 14. У тебя есть инструменты (tools) — их список и описания "
    "уже даны тебе отдельно, не нужно их перечислять. "
    "Делай РОВНО то, о чём просит игрок: если это вопрос или приветствие — ответь через say; "
    "если просят действие — выполни нужными инструментами; лишнего не делай. "
    "Чтобы найти кого-то/что-то, используй observe — лучше с filter, напр. observe(filter=\"Сахарова\"). "
    "ВАЖНО про id: бери их ТОЛЬКО из результатов observe или listen (в listen есть id говорящего) — "
    "НИКОГДА не выдумывай числа. "
    "Если просят идти за тобой/к тебе — цель это говорящий, его id уже есть в сообщении (listen): "
    "сразу вызови follow или move_to с этим id, без лишних observe. "
    "Если сделал observe, чтобы найти цель — СЛЕДУЮЩИМ же ходом соверши действие с её id, "
    "не застревай на повторных observe. Закончил просьбу — остановись."
)

# Reusable knowledge about SS14 hand-tools, injected into the agent prompt.
TOOL_HINT = (
    "\n\nИнструменты в мире помечены в observe как [инстр:Качество]. "
    "Чтобы применить инструмент к чему-то: сначала pickup этот инструмент, затем use_on по цели. Качества:\n"
    "- Prying (лом) — взломать/открыть: двери-шлюзы, панели, ящики;\n"
    "- Anchoring (гаечный ключ) — прикрутить/открутить болтами: рама машины, мебель, трубы, шкафы;\n"
    "- Screwing (отвёртка) — винты/панели: открыть электронику, собрать устройства;\n"
    "- Cutting (кусачки) — перерезать: провода, решётки, наручники;\n"
    "- Welding (сварка) — сварить/починить/разрезать стены и решётки;\n"
    "- Pulsing (мультитул) — диагностика проводов.\n"
    "ВАЖНО: переключаемые инструменты (сварочник, фонарик) надо СНАЧАЛА включить тулом activate "
    "(взял сварочник -> activate -> потом use_on по цели -> в конце снова activate чтобы погасить).\n"
    "Действия инструментов идут с задержкой (DoAfter): после use_on не уходи, подожди и проверь observe."
)


# "Look" tools gather info but change nothing — too many in a row = stuck.
LOOK_TOOLS = {"observe", "recall", "where_is", "listen"}


def native_episode(mcp, backend, tools, goal, max_steps, delay):
    """One bounded native-tool-calling episode toward `goal`; returns on stop/limit.
    Anti-stuck: breaks on a repeated identical call, nudges after 2 look-only turns."""
    messages = [{"role": "system", "content": SYSTEM},
                {"role": "user", "content": goal}]
    last_sig = None
    look_streak = 0
    for step in range(max_steps):
        content, calls = backend.chat(messages, tools)
        if content:
            print(f"[model] {content}")
        if not calls:
            return

        sig = tuple((c["name"], json.dumps(c["args"], sort_keys=True, ensure_ascii=False)) for c in calls)
        if sig == last_sig:
            print("[runner] тот же вызов повторно — прерываю эпизод (залип).", file=sys.stderr)
            return
        last_sig = sig

        messages.append({"role": "assistant", "content": content or "",
                         "tool_calls": [{"id": c["id"] or f"c{step}_{i}", "type": "function",
                                         "function": {"name": c["name"],
                                                      "arguments": json.dumps(c["args"], ensure_ascii=False)}}
                                        for i, c in enumerate(calls)]})
        for i, c in enumerate(calls):
            text, is_err = mcp.call_tool(c["name"], c["args"])
            print(f"[tool] {c['name']}({c['args']}) -> {'ERR ' if is_err else ''}{text}")
            messages.append({"role": "tool", "tool_call_id": c["id"] or f"c{step}_{i}",
                             "name": c["name"], "content": f"{'ERR ' if is_err else ''}{text}"})

        look_streak = look_streak + 1 if all(c["name"] in LOOK_TOOLS for c in calls) else 0
        if look_streak >= 2:
            messages.append({"role": "user", "content":
                "Ты уже осмотрелся — хватит observe/recall. Соверши ДЕЙСТВИЕ "
                "(move_to/pickup/place/follow/use_on/...) с нужным id, либо stop."})
            look_streak = 0
        time.sleep(delay)


def plan_steps(backend, request):
    """Ask the model for a short numbered plan (plain text, no tools) for a request.
    Helps multi-step tasks: the agent then executes the plan instead of freelancing."""
    msgs = [{"role": "system", "content":
             "/no_think\nТы планируешь действия персонажа в Space Station 14. Разбей просьбу игрока "
             "на короткий нумерованный список конкретных шагов (1., 2., 3., ...). Только список, кратко. "
             "Простая просьба (приветствие/вопрос) — ответь одной строкой «1. Ответить словами»."},
            {"role": "user", "content": request}]
    try:
        return backend.chat_text(msgs).strip()
    except Exception as e:
        print(f"[runner] план не вышел: {e}", file=sys.stderr)
        return ""


def run_native(args, mcp, backend, tools):
    goal, steps = f"Цель: {args.goal}", args.max_steps
    if args.plan:
        plan = plan_steps(backend, args.goal)
        if plan:
            print(f"[plan]\n{plan}")
            goal = f"Цель: {args.goal}\nТвой план:\n{plan}\nВыполняй план по шагам инструментами."
            steps = max(args.max_steps, 14)
    native_episode(mcp, backend, tools, goal, steps, args.delay)
    print("[runner] эпизод завершён.", file=sys.stderr)


def tool_catalog(mcp_tools):
    lines = []
    for t in mcp_tools:
        schema = t.get("inputSchema", {})
        props = [k for k in schema.get("properties", {}) if k != "agent"]
        req = [r for r in schema.get("required", []) if r != "agent"]
        sig = ", ".join(f"{k}{'*' if k in req else ''}" for k in props) or "—"
        lines.append(f"- {t['name']}({sig}): {t.get('description', '')}")
    return "\n".join(lines)


def _prompt_sysmsg(mcp_tools):
    return (
        "/no_think\n"  # Qwen3: suppress chain-of-thought so output stays a clean JSON action
        "Ты управляешь персонажем в Space Station 14. Инструменты (* — обязательный аргумент):\n"
        + tool_catalog(mcp_tools)
        + TOOL_HINT
        + "\n\nОтвечай СТРОГО одним JSON-объектом: {\"tool\": \"<имя>\", \"args\": {...}}. "
          "Сущности адресуй по сетевому id из observe. Когда цель достигнута — {\"tool\": \"stop\"}. "
          "Никакого текста вне JSON."
    )


def prompt_episode(mcp, backend, mcp_tools, goal, max_steps, delay):
    """One bounded prompt-mode episode toward `goal`; returns on stop/limit. For
    models WITHOUT native tool-calling: tool catalog in the prompt, JSON action out."""
    valid = {t["name"] for t in mcp_tools}
    messages = [{"role": "system", "content": _prompt_sysmsg(mcp_tools)},
                {"role": "user", "content": f"{goal}\nПервый ход (только JSON):"}]
    for _ in range(max_steps):
        txt = backend.chat_json(messages).strip()
        print(f"[model] {txt}")
        try:
            action = json.loads(txt)
        except Exception:
            messages.append({"role": "user", "content": "Невалидный JSON. Ответь строго {\"tool\":...,\"args\":...}."})
            continue
        tool = str(action.get("tool", "")).strip()
        a = action.get("args") or {}
        if tool in ("stop", "done", "none", ""):
            return
        if tool not in valid:
            messages.append({"role": "user", "content": f"Нет инструмента «{tool}». Доступны: {', '.join(sorted(valid))}."})
            continue
        text, is_err = mcp.call_tool(tool, a)
        print(f"[tool] {tool}({a}) -> {'ERR ' if is_err else ''}{text}")
        messages.append({"role": "assistant", "content": txt})
        messages.append({"role": "user", "content": f"Результат {tool}: {'ERR ' if is_err else ''}{text}\nСледующий ход (только JSON):"})
        time.sleep(delay)


def run_prompt(args, mcp, backend, mcp_tools):
    prompt_episode(mcp, backend, mcp_tools, f"Цель: {args.goal}", args.max_steps, args.delay)
    print("[runner] эпизод завершён.", file=sys.stderr)


def run_listen(args, mcp, backend, raw_tools):
    """Stay alive and react to speech (the `listen` tool) — MCP-first conversation.
    Uses the same tool path as the chosen mode: native tool-calls for native models."""
    native = args.tool_mode == "native"
    fn_tools = to_function_specs(raw_tools) if native else None
    print("[runner] слушаю речь рядом с агентом... (Ctrl-C — выход)", file=sys.stderr)
    while True:
        try:
            heard, _ = mcp.call_tool("listen", {})
            if heard.startswith("Пока ничего") or heard.startswith("Новых реплик нет"):
                time.sleep(args.delay)
                continue
            print(f"[heard] {heard}")
            goal = (f"{heard}\nВыполни эту просьбу игрока. Если это вопрос или приветствие — просто "
                    f"ответь через say; если просят действие — сделай его. Не делай лишнего.")
            steps = 8
            if args.plan:
                plan = plan_steps(backend, heard)
                if plan:
                    print(f"[plan]\n{plan}")
                    goal = f"{heard}\nТвой план:\n{plan}\nВыполняй план по шагам инструментами. Не делай лишнего."
                    steps = 14
            if native:
                native_episode(mcp, backend, fn_tools, goal, steps, args.delay)
            else:
                prompt_episode(mcp, backend, raw_tools, goal, steps, args.delay)
        except Exception as e:
            print(f"[runner] эпизод упал, продолжаю слушать: {e}", file=sys.stderr)
        time.sleep(args.delay)


def run(args):
    mcp = Mcp(args.mcp, args.token, args.agent)
    mcp.initialize()
    raw_tools = mcp.list_tools()
    print(f"[runner] MCP connected, {len(raw_tools)} tools, agent={args.agent}, mode={args.tool_mode}",
          file=sys.stderr)

    if args.backend == "ollama":
        backend = OllamaBackend(args.backend_url or "http://localhost:11434", args.model)
    else:
        backend = OpenAIBackend(args.backend_url or "http://localhost:1234/v1", args.model, args.api_key)

    if args.listen:
        run_listen(args, mcp, backend, raw_tools)
    elif args.tool_mode == "prompt":
        run_prompt(args, mcp, backend, raw_tools)
    else:
        run_native(args, mcp, backend, to_function_specs(raw_tools))


def main():
    p = argparse.ArgumentParser(description="Drive the SS14 Saiga agent with any LLM via MCP.")
    p.add_argument("--mcp", default="http://127.0.0.1:1212/mcp", help="MCP endpoint URL")
    p.add_argument("--token", required=True, help="Bearer token (saiga.mcp.token)")
    p.add_argument("--agent", required=True, help="agent network id or character name")
    p.add_argument("--backend", choices=["ollama", "openai"], default="openai")
    p.add_argument("--tool-mode", choices=["native", "prompt"], default="native",
                   help="native function-calling, or prompt+JSON for models without tool support (Saiga/gemma2)")
    p.add_argument("--backend-url", default=None, help="model server base url")
    p.add_argument("--model", required=True, help="model name/tag served by the backend")
    p.add_argument("--api-key", default=None, help="api key for openai backend (LM Studio: any)")
    p.add_argument("--goal", default="Осмотрись и веди себя как обычный член экипажа.",
                   help="what the agent should do (goal mode)")
    p.add_argument("--listen", action="store_true",
                   help="stay alive and react to nearby speech via the listen tool (MCP-first chat)")
    p.add_argument("--plan", action="store_true",
                   help="plan the request into steps first, then execute (better at multi-step tasks)")
    p.add_argument("--max-steps", type=int, default=12)
    p.add_argument("--delay", type=float, default=1.0, help="seconds between steps")
    args = p.parse_args()
    try:
        run(args)
    except urllib.error.URLError as e:
        print(f"[runner] сетевая ошибка: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()

// Модуль «Сервер» (в окне): связь окна с серверной частью предмета.
// Доступен, когда окно привязано к предмету (вкладка «Логика» → «Сервер (предмет)»).
"use strict";
(() => {
    const serverVars = api => (api.root.item ? api.root.item.server.vars.map(v => [v.name, v.name]) : []);
    UIBlocks.registerModule({
        id: "link", name: "Сервер", color: "#1f9bd1", scope: "client",
        blocks: [
            { id: "srv_send", shape: "cmd", text: "отправить на сервер {A} значение {V}",
              help: "Сервер получит действие в блоке «когда пришло действие …»",
              paletteIf: api => !!api.root.item,
              args: { A: { type: "text", def: "действие" }, V: { type: "text", def: "" } },
              gen: (ctx, a) => `ServerSend?.Invoke(${a.A.code}, ${a.V.code});` },
            { id: "srv_update", shape: "hat", text: "когда пришло обновление с сервера",
              help: "Сервер присылает переменные предмета после каждого действия и при открытии окна",
              paletteIf: api => !!api.root.item,
              gen: (ctx, a, run) => `UiServerStateChanged += () => ${run};` },
            { id: "srv_var", shape: "reporter", text: "с сервера {N}",
              help: "Значение переменной предмета (создаются в «Сервер (предмет)» → «Переменные»)",
              paletteIf: api => !!api.root.item && api.root.item.server.vars.length > 0,
              args: { N: { type: "choice", options: serverVars, required: true } },
              out: (b, api) => { const v = api.root.item && api.root.item.server.vars.find(x => x.name === b.args.N); return v ? v.type : "text"; },
              gen: (ctx, a) => {
                  const t = ctx.serverVarType(a.N.raw);
                  const g = `UiServerVar(${ctx.str(a.N.raw)})`;
                  return t === "num" ? `UiNum(${g})` : t === "bool" ? `UiBool(${g})` : g;
              } },
        ],
    });
})();

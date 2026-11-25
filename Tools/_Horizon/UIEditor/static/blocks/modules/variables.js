// Модуль «Переменные». Переменная — поле класса окна (число, текст или да/нет).
"use strict";
(() => {
    const TYPES = { num: "число", text: "текст", bool: "да/нет" };
    UIBlocks.registerModule({
        id: "variables", name: "Переменные", color: "#ff7a1a",
        paletteTop(pal, api) {
            const btn = document.createElement("button");
            btn.className = "blk-pal-btn";
            btn.textContent = "Создать переменную";
            btn.onclick = () => {
                const name = prompt("Имя переменной:");
                if (!name) return;
                const t = prompt("Тип: 1 — число, 2 — текст, 3 — да/нет", "1");
                api.addVariable(name, { 1: "num", 2: "text", 3: "bool" }[String(t).trim()] || "num");
            };
            pal.appendChild(btn);
            for (const v of api.program.vars) {
                const row = document.createElement("div");
                row.className = "blk-var-row";
                row.textContent = `${v.name} (${TYPES[v.type]})`;
                const del = document.createElement("b");
                del.textContent = "×";
                del.title = "Удалить переменную";
                del.onclick = () => { if (confirm(`Удалить переменную «${v.name}»?`)) api.removeVariable(v.name); };
                row.appendChild(del);
                pal.appendChild(row);
            }
        },
        blocks: [
            { id: "var_get", shape: "reporter", text: "{V}",
              args: { V: { type: "var" } },
              out: (b, api) => (api.program.vars.find(v => v.name === b.args.V) || {}).type || "text",
              paletteIf: api => api.program.vars.length > 0,
              gen: (ctx, a) => ctx.varField(a.V.raw) },
            { id: "var_set", shape: "cmd", text: "задать {V} = {X}",
              args: { V: { type: "var" }, X: { type: "any", def: "0" } },
              paletteIf: api => api.program.vars.length > 0,
              gen: (ctx, a) => {
                  const t = ctx.varType(a.V.raw);
                  let x = a.X;
                  let code = x.code;
                  if (t === "num" && x.type === "text") code = `UiNum(${code})`;
                  else if (t === "text" && x.type !== "text") code = `UiText(${code})`;
                  else if (t === "bool" && x.type === "num") code = `(${code} != 0)`;
                  else if (t === "bool" && x.type === "text") code = `UiBool(${code})`;
                  return `${ctx.varField(a.V.raw)} = ${code};`;
              } },
            { id: "var_change", shape: "cmd", text: "изменить {V} на {N}",
              args: { V: { type: "var", vtypes: ["num"] }, N: { type: "num", def: 1 } },
              paletteIf: api => api.program.vars.some(v => v.type === "num"),
              gen: (ctx, a) => `${ctx.varField(a.V.raw)} += ${a.N.code};` },
        ],
    });
})();

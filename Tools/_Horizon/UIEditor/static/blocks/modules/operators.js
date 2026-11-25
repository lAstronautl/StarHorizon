// Модуль «Операторы»: математика, сравнение, логика, строки.
"use strict";
(() => {
    const bin = (id, sym, out = "num", t = "num") => ({
        id, shape: out === "bool" ? "bool" : "reporter", text: `{A} ${sym} {B}`, out,
        args: { A: { type: t, def: "" }, B: { type: t, def: out === "bool" ? "50" : "" } },
        gen: (ctx, a) => `${a.A.code} ${sym === "×" ? "*" : sym === "÷" ? "/" : sym === "≠" ? "!=" : sym} ${a.B.code}`,
    });
    UIBlocks.registerModule({
        id: "operators", name: "Операторы", color: "#4caf50",
        blocks: [
            bin("op_add", "+"), bin("op_sub", "-"), bin("op_mul", "×"), bin("op_div", "÷"),
            { id: "op_mod", shape: "reporter", text: "остаток {A} / {B}", out: "num",
              args: { A: { type: "num", def: "" }, B: { type: "num", def: "" } },
              gen: (ctx, a) => `${a.A.code} % ${a.B.code}` },
            { id: "op_random", shape: "reporter", text: "случайное от {A} до {B}", out: "num",
              args: { A: { type: "num", def: 1 }, B: { type: "num", def: 10 } },
              gen: (ctx, a) => { ctx.use("random"); return `UiRandom(${a.A.code}, ${a.B.code})`; } },
            bin("op_gt", ">", "bool"), bin("op_lt", "<", "bool"),
            { id: "op_eq", shape: "bool", text: "{A} = {B}",
              args: { A: { type: "text", def: "" }, B: { type: "text", def: "50" } },
              gen: (ctx, a) => `UiEq(${a.A.code}, ${a.B.code})` },
            { id: "op_and", shape: "bool", text: "{A} и {B}", args: { A: { type: "bool" }, B: { type: "bool" } },
              gen: (ctx, a) => `${a.A.code} && ${a.B.code}` },
            { id: "op_or", shape: "bool", text: "{A} или {B}", args: { A: { type: "bool" }, B: { type: "bool" } },
              gen: (ctx, a) => `${a.A.code} || ${a.B.code}` },
            { id: "op_not", shape: "bool", text: "не {A}", args: { A: { type: "bool" } },
              gen: (ctx, a) => `!${a.A.code}` },
            { id: "op_true", shape: "bool", text: "истина", gen: () => "true" },
            { id: "op_join", shape: "reporter", text: "слить {A} {B}", out: "text",
              args: { A: { type: "text", def: "яблоко " }, B: { type: "text", def: "банан" } },
              gen: (ctx, a) => `string.Concat(${a.A.code}, ${a.B.code})` },
            { id: "op_length", shape: "reporter", text: "длина {A}", out: "num",
              args: { A: { type: "text", def: "яблоко" } },
              gen: (ctx, a) => `(double) ${a.A.code}.Length` },
            { id: "op_contains", shape: "bool", text: "{A} содержит {B}?",
              args: { A: { type: "text", def: "яблоко" }, B: { type: "text", def: "я" } },
              gen: (ctx, a) => `${a.A.code}.Contains(${a.B.code}, StringComparison.OrdinalIgnoreCase)` },
            { id: "op_round", shape: "reporter", text: "округлить {A}", out: "num",
              args: { A: { type: "num", def: "" } },
              gen: (ctx, a) => `Math.Round(${a.A.code})` },
            { id: "op_math", shape: "reporter", text: "{F} от {A}", out: "num",
              args: { F: { type: "choice", options: [["модуль", "Abs"], ["вниз", "Floor"], ["вверх", "Ceiling"], ["корень", "Sqrt"]] }, A: { type: "num", def: "" } },
              gen: (ctx, a) => `Math.${a.F.raw}(${a.A.code})` },
        ],
    });
})();

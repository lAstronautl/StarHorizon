#!/usr/bin/env python3
"""
StarHorizon UI Editor — локальный сервер визуального редактора XAML-интерфейсов SS14.

Запуск (из корня репозитория или из любой папки):
    python Tools/_Horizon/UIEditor/ui_editor.py
После запуска откройте в браузере http://127.0.0.1:8714

Сервер слушает только localhost, читает .xaml из репозитория, отдаёт текстуры
из Resources/Textures, строки локализации из Resources/Locale и сохраняет
только файлы .xaml / .xaml.cs внутри Content.* папок репозитория.

Только стандартная библиотека Python 3.8+.
"""

import argparse
import json
import mimetypes
import os
import re
import sys
import threading
import webbrowser
from http.server import HTTPServer, BaseHTTPRequestHandler
from socketserver import ThreadingMixIn
from urllib.parse import urlparse, parse_qs, unquote

HERE = os.path.dirname(os.path.abspath(__file__))
STATIC = os.path.join(HERE, "static")
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))

SKIP_DIRS = {".git", "bin", "obj", "node_modules", "RobustToolbox", ".vs", ".idea"}
WRITE_ROOTS = ("Content.Client", "Content.Replay", "Content.Shared", "Content.Server")
ALLOWED_EXT = (".xaml", ".xaml.cs", ".logic.cs")
LOAD_ANCHOR = "RobustXamlLoader.Load(this);"


def cs_info(full):
    """Namespace, имя класса и наличие вызова InitializeLogic() в .xaml.cs."""
    with open(full, encoding="utf-8-sig") as fh:
        text = fh.read()
    ns = re.search(r"^\s*namespace\s+([\w.]+)", text, re.M)
    cls = re.search(r"partial\s+class\s+(\w+)", text)
    return {
        "exists": True,
        "namespace": ns.group(1) if ns else None,
        "className": cls.group(1) if cls else None,
        "hasCall": "InitializeLogic();" in text,
        "hasAnchor": LOAD_ANCHOR in text,
    }


def ensure_logic_call(full):
    """Вставляет InitializeLogic(); сразу после RobustXamlLoader.Load(this); если его ещё нет."""
    with open(full, encoding="utf-8-sig", newline="") as fh:
        text = fh.read()
    if "InitializeLogic();" in text:
        return "present"
    idx = text.find(LOAD_ANCHOR)
    if idx < 0:
        return "no-anchor"
    line_start = text.rfind("\n", 0, idx) + 1
    indent = re.match(r"[ \t]*", text[line_start:]).group(0)
    nl = "\r\n" if "\r\n" in text else "\n"
    end = idx + len(LOAD_ANCHOR)
    logic_name = os.path.basename(full)[: -len(".xaml.cs")] + ".logic.cs"
    text = text[:end] + nl + indent + "InitializeLogic(); // логика из блоков: " + logic_name + text[end:]
    with open(full, "w", encoding="utf-8", newline="") as fh:
        fh.write(text)
    return "added"

_loc_cache = {}


def is_inside(base, path):
    base = os.path.realpath(base)
    path = os.path.realpath(path)
    return os.path.commonpath([base, path]) == base


def list_xaml():
    result = []
    for root, dirs, files in os.walk(REPO):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for f in files:
            if f.endswith(".xaml"):
                full = os.path.join(root, f)
                result.append(os.path.relpath(full, REPO).replace(os.sep, "/"))
    result.sort()
    return result


def _locale_stamp(base):
    """Количество и максимальное время изменения .ftl — чтобы подхватывать новые ключи без перезапуска."""
    count, latest = 0, 0.0
    for root, _dirs, files in os.walk(base):
        for f in files:
            if f.endswith(".ftl"):
                count += 1
                try:
                    latest = max(latest, os.path.getmtime(os.path.join(root, f)))
                except OSError:
                    pass
    return count, latest


def load_locale(lang):
    base = os.path.join(REPO, "Resources", "Locale", lang)
    stamp = _locale_stamp(base) if os.path.isdir(base) else None
    cached = _loc_cache.get(lang)
    if cached and cached[0] == stamp:
        return cached[1]
    data = {}
    if not os.path.isdir(base):
        _loc_cache[lang] = (stamp, data)
        return data
    key_re = re.compile(r"^([A-Za-z0-9_\-]+)\s*=\s*(.*)$")
    for root, _dirs, files in os.walk(base):
        for f in files:
            if not f.endswith(".ftl"):
                continue
            try:
                with open(os.path.join(root, f), encoding="utf-8-sig") as fh:
                    cur_key = None
                    for line in fh:
                        line = line.rstrip("\n\r")
                        m = key_re.match(line)
                        if m:
                            cur_key = m.group(1)
                            data[cur_key] = m.group(2).strip()
                            continue
                        if cur_key and line.startswith((" ", "\t")):
                            s = line.strip()
                            if s.startswith(".") or s.startswith("[") or s.startswith("*["):
                                # атрибуты / варианты — берём первый вариант, если значения нет
                                if not data[cur_key] and "]" in s:
                                    data[cur_key] = s.split("]", 1)[1].strip()
                                continue
                            if s and not data[cur_key]:
                                data[cur_key] = s
                            elif s and not data[cur_key].endswith("->"):
                                data[cur_key] += " " + s
                        else:
                            cur_key = None
            except OSError:
                pass
    _loc_cache[lang] = (stamp, data)
    return data


class Handler(BaseHTTPRequestHandler):
    server_version = "SHUIEditor/1.0"

    def log_message(self, fmt, *args):
        if os.environ.get("UI_EDITOR_VERBOSE"):
            sys.stderr.write("[ui-editor] " + fmt % args + "\n")

    # --- helpers -------------------------------------------------------
    def _send(self, code, body, ctype="application/json; charset=utf-8"):
        if isinstance(body, str):
            body = body.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _json(self, obj, code=200):
        self._send(code, json.dumps(obj, ensure_ascii=False))

    def _error(self, code, msg):
        self._json({"error": msg}, code)

    def _check_origin(self):
        # Запрещаем запись со сторонних сайтов (CSRF на localhost)
        origin = self.headers.get("Origin")
        if origin is None:
            return True
        host = self.headers.get("Host", "")
        return urlparse(origin).netloc == host

    # --- GET -----------------------------------------------------------
    def do_GET(self):
        url = urlparse(self.path)
        q = parse_qs(url.query)
        path = unquote(url.path)

        if path == "/api/info":
            return self._json({"repo": os.path.basename(REPO), "server": True})

        if path == "/api/files":
            return self._json({"files": list_xaml()})

        if path == "/api/file":
            rel = q.get("path", [""])[0]
            full = os.path.join(REPO, rel)
            if not rel.endswith(ALLOWED_EXT) or not is_inside(REPO, full):
                return self._error(400, "bad path")
            if not os.path.isfile(full):
                return self._error(404, "not found")
            with open(full, encoding="utf-8-sig") as fh:
                return self._json({"path": rel, "content": fh.read()})

        if path == "/api/locale":
            lang = q.get("lang", ["ru-RU"])[0]
            if not re.fullmatch(r"[A-Za-z\-]+", lang):
                return self._error(400, "bad lang")
            return self._json(load_locale(lang))

        if path == "/api/csinfo":
            rel = q.get("path", [""])[0]
            full = os.path.join(REPO, rel)
            if not rel.endswith(".xaml.cs") or not is_inside(REPO, full):
                return self._error(400, "bad path")
            if not os.path.isfile(full):
                return self._json({"exists": False})
            return self._json(cs_info(full))

        if path == "/api/blockmodules":
            mdir = os.path.join(STATIC, "blocks", "modules")
            names = sorted(f for f in os.listdir(mdir) if f.endswith(".js")) if os.path.isdir(mdir) else []
            return self._json({"modules": names})

        if path.startswith("/tex/"):
            rel = path[len("/tex/"):].lstrip("/")
            base = os.path.join(REPO, "Resources")
            full = os.path.join(base, rel)
            if not is_inside(base, full) or not os.path.isfile(full):
                # /Textures/... — пути в XAML начинаются от Resources
                return self._error(404, "not found")
            ctype = mimetypes.guess_type(full)[0] or "application/octet-stream"
            if not ctype.startswith("image/"):
                return self._error(400, "not an image")
            with open(full, "rb") as fh:
                return self._send(200, fh.read(), ctype)

        # static
        if path in ("", "/"):
            path = "/index.html"
        full = os.path.join(STATIC, path.lstrip("/"))
        if not is_inside(STATIC, full) or not os.path.isfile(full):
            return self._error(404, "not found")
        ctype = mimetypes.guess_type(full)[0] or "application/octet-stream"
        if ctype.startswith("text/") or ctype.endswith("javascript"):
            ctype += "; charset=utf-8"
        with open(full, "rb") as fh:
            self._send(200, fh.read(), ctype)

    # --- POST ----------------------------------------------------------
    def do_POST(self):
        if not self._check_origin():
            return self._error(403, "bad origin")
        url = urlparse(self.path)
        if url.path not in ("/api/save", "/api/ensure-logic-call"):
            return self._error(404, "not found")
        length = int(self.headers.get("Content-Length", "0"))
        if length > 8 * 1024 * 1024:
            return self._error(413, "too large")
        try:
            data = json.loads(self.rfile.read(length).decode("utf-8"))
        except ValueError:
            return self._error(400, "bad json")

        rel = str(data.get("path", "")).replace("\\", "/").lstrip("/")
        if url.path == "/api/ensure-logic-call":
            full = os.path.join(REPO, rel)
            if not rel.endswith(".xaml.cs") or rel.split("/")[0] not in WRITE_ROOTS or not is_inside(REPO, full):
                return self._error(400, "bad path")
            if not os.path.isfile(full):
                return self._json({"result": "missing"})
            return self._json({"result": ensure_logic_call(full)})
        content = data.get("content")
        overwrite = bool(data.get("overwrite", True))
        if not isinstance(content, str):
            return self._error(400, "no content")
        if not rel.endswith(ALLOWED_EXT):
            return self._error(400, "можно сохранять только .xaml, .xaml.cs и .logic.cs")
        if not rel.split("/")[0] in WRITE_ROOTS:
            return self._error(400, "сохранять можно только внутри " + ", ".join(WRITE_ROOTS))
        full = os.path.join(REPO, rel)
        if not is_inside(REPO, full):
            return self._error(400, "bad path")
        if os.path.exists(full) and not overwrite:
            return self._json({"path": rel, "skipped": True})
        os.makedirs(os.path.dirname(full), exist_ok=True)
        with open(full, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(content)
        return self._json({"path": rel, "saved": True})


class Server(ThreadingMixIn, HTTPServer):
    daemon_threads = True


def main():
    ap = argparse.ArgumentParser(description="StarHorizon SS14 UI Editor")
    ap.add_argument("--port", type=int, default=8714)
    ap.add_argument("--no-browser", action="store_true", help="не открывать браузер")
    args = ap.parse_args()

    srv = Server(("127.0.0.1", args.port), Handler)
    url = "http://127.0.0.1:%d/" % args.port
    print("StarHorizon UI Editor: " + url)
    print("Репозиторий: " + REPO)
    print("Ctrl+C — остановить")
    if not args.no_browser:
        threading.Timer(0.5, lambda: webbrowser.open(url)).start()
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()

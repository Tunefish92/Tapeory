#!/usr/bin/env python3
"""Real-life test of the desktop engine, and a sample database to try the app with.

Starts the engine the way the desktop app does (--engine, a secret, its own data folder), sets up
the local SQLite database and exercises every endpoint: settings, fonts, barcodes, uploads,
templates (versions, preview, export/import, .lbx import, groups), printers (a fake network
printer on 127.0.0.1:9100 receives the raster data), print jobs, bulk printing from a data file,
statistics, backups and restores,
and accounts. The data folder it leaves behind is a sample database:

    sign in as  demo / Tapeory-Demo-2026  (administrator)
            or  alex / Alex-Password-2026 (user)

Usage: engine_realtest.py ENGINE DATA_FOLDER LBX_FILE PNG_FILE
       (ENGINE: Tapeory.Api from a desktop build, e.g. engine/Tapeory.Api in the tar.gz)
"""
import base64, http.cookiejar, http.server, json, os, secrets, socket, subprocess, sys, threading, time, urllib.error, urllib.request, uuid

ENGINE, DATA, LBX, PNG = sys.argv[1:5]
DATA = os.path.abspath(DATA)
TOKEN = secrets.token_hex(32)
results = []
received = []  # what the fake printer got


def check(name, condition, detail=""):
    results.append((name, bool(condition)))
    print(("PASS " if condition else "FAIL ") + name + (f"  [{detail}]" if detail and not condition else ""))
    return condition


def fake_printer():
    server = socket.socket()
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind(("127.0.0.1", 9100))
    server.listen()
    while True:
        connection, _ = server.accept()
        connection.settimeout(3)
        chunks = []
        try:
            while data := connection.recv(65536):
                chunks.append(data)
        except socket.timeout:
            pass
        connection.close()
        if chunks:
            received.append(b"".join(chunks))


threading.Thread(target=fake_printer, daemon=True).start()

# ---- engine ---------------------------------------------------------------------------------
env = dict(os.environ, TAPEORY_ENGINE_TOKEN=TOKEN, TAPEORY_STORAGE_PATH=DATA)
env.pop("ConnectionStrings__Default", None)
os.makedirs(DATA, exist_ok=True)
log = open(os.path.join(DATA, "engine-test.log"), "w")
engine = subprocess.Popen([ENGINE, "--engine"], env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=log, text=True,
                          cwd=os.path.dirname(os.path.abspath(ENGINE)))
base = None
deadline = time.time() + 60
while time.time() < deadline:
    line = engine.stdout.readline()
    if not line:
        break
    if line.startswith("TAPEORY_ENGINE_READY"):
        base = line.split()[1]
        break
if not check("engine starts and reports its address", base):
    sys.exit(1)
threading.Thread(target=lambda: [None for _ in engine.stdout], daemon=True).start()

opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))


def call(method, path, body=None, token=TOKEN, raw=None, content_type=None, host=None):
    headers = {"X-Requested-With": "Tapeory"}
    data = None
    if token:
        headers["X-Tapeory-Token"] = token
    if host:
        headers["Host"] = host
    if body is not None:
        data, headers["Content-Type"] = json.dumps(body).encode(), "application/json"
    if raw is not None:
        data, headers["Content-Type"] = raw, content_type
    request = urllib.request.Request(base + path, data=data, method=method, headers=headers)
    try:
        with opener.open(request, timeout=60) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()


def js(method, path, body=None, **kw):
    status, payload = call(method, path, body, **kw)
    try:
        return status, json.loads(payload) if payload else None
    except ValueError:
        return status, payload


def upload(path, name, data, content_type):
    boundary = uuid.uuid4().hex
    body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{name}\"\r\n"
            f"Content-Type: {content_type}\r\n\r\n").encode() + data + f"\r\n--{boundary}--\r\n".encode()
    status, payload = call("POST", path, raw=body, content_type=f"multipart/form-data; boundary={boundary}")
    return status, json.loads(payload) if payload else None


def wait_job(job_id):
    for _ in range(60):
        _, job = js("GET", f"/api/print-jobs/{job_id}")
        if job["status"] not in ("Pending", "Queued", "Rendering", "Sending", "Printing", "Processing"):
            return job
        time.sleep(0.5)
    return job


# ---- desktop guard ----------------------------------------------------------------------------
check("request without the secret is refused", js("GET", "/api/setup/status", token=None)[0] in (401, 403))
check("request with a wrong secret is refused", js("GET", "/api/setup/status", token="x" * 64)[0] in (401, 403))
check("request with a foreign Host is refused", js("GET", "/api/setup/status", host="evil.example")[0] in (400, 401, 403))

# ---- setup --------------------------------------------------------------------------------------
status, setup = js("GET", "/api/setup/status")
check("setup status: not configured, desktop", status == 200 and not setup["configured"] and setup["desktop"], setup)
check("the API waits for the setup (503)", js("GET", "/api/templates")[0] == 503)
status, body = js("POST", "/api/setup/database/local")
check("set up SQLite on this computer", status in (200, 204), body)
check("setup status: configured", js("GET", "/api/setup/status")[1]["configured"])
check("the database file exists", os.path.exists(os.path.join(DATA, "tapeory.db")))

# ---- health, auth, settings -------------------------------------------------------------------
status, health = js("GET", "/api/health")
check("health: database connected", status == 200 and health.get("databaseConnected"), health)
status, state = js("GET", "/api/auth/state")
check("auth state: desktop, no accounts", status == 200 and state["desktop"] and not state["hasUsers"], state)
status, settings = js("PUT", "/api/settings", {"language": "de", "theme": "dark", "unit": "inch"})
check("save settings", status == 200 and settings["unit"] == "inch", settings)
check("read settings back", js("GET", "/api/settings")[1] == {"language": "de", "theme": "dark", "unit": "inch", "preReleases": False})
check("switch pre-releases on for the update check", js("PUT", "/api/settings", {"preReleases": True})[1]["preReleases"] is True
      and js("GET", "/api/settings")[1]["language"] == "de")
js("PUT", "/api/settings", {"preReleases": False})
js("PUT", "/api/settings", {"language": "en", "theme": "system", "unit": "mm"})

# ---- fonts, barcodes, uploads -----------------------------------------------------------------
status, fonts = js("GET", "/api/fonts")
check("font list", status == 200 and len(fonts) > 5)
status, payload = call("GET", "/api/fonts/file?family=Inter&bold=false")
check("font file (Inter)", status == 200 and payload[:4] in (b"OTTO", b"\x00\x01\x00\x00"))
for symbology, data in [("qr", "https://tapeory.example"), ("code128", "TAPE-0001"), ("ean13", "400638133393"), ("dataMatrix", "DM1")]:
    status, code = js("POST", "/api/barcodes/encode", {"symbology": symbology, "data": data})
    check(f"barcode {symbology}", status == 200 and code["columns"] > 0, code)
check("an invalid EAN-13 is a readable error", js("POST", "/api/barcodes/encode", {"symbology": "ean13", "data": "abc"})[0] == 400)
status, image = upload("/api/uploads/images", "logo.png", open(PNG, "rb").read(), "image/png")
check("upload an image", status in (200, 201), image)
status, payload = call("GET", f"/api/uploads/images/{image['id']}")
check("download the image", status == 200 and payload[:4] == b"\x89PNG")

# ---- templates --------------------------------------------------------------------------------
doc = {"formatVersion": 1, "widthMm": 60, "heightMm": 12, "objects": [
    {"type": "text", "id": "t1", "x": 2, "y": 1, "rotation": 0, "text": "Tapeory", "width": 30, "height": 5, "fontSize": 10,
     "fontFamily": "Inter", "fontWeight": "bold", "align": "left", "fill": "#000000"},
    {"type": "dynamicField", "id": "f1", "x": 2, "y": 6, "rotation": 0, "fieldName": "name", "label": "Name", "defaultValue": "Box",
     "required": True, "width": 30, "height": 5, "fontSize": 9, "fontFamily": "Arial", "fontWeight": "normal", "align": "left",
     "fill": "#000000"},
    {"type": "rect", "id": "r1", "x": 34, "y": 1, "rotation": 0, "width": 8, "height": 10, "stroke": "#000000", "strokeWidth": 0.5},
    {"type": "line", "id": "l1", "x": 0, "y": 0, "rotation": 0, "points": [33, 1, 33, 11], "stroke": "#000000", "strokeWidth": 0.3},
    {"type": "image", "id": "i1", "x": 43, "y": 1, "rotation": 0, "width": 5, "height": 5, "uploadedFileId": image["id"], "url": image["url"]},
    {"type": "barcode", "id": "b1", "x": 49, "y": 1, "rotation": 0, "width": 10, "height": 10, "symbology": "qr",
     "data": "{name}", "fieldName": "name", "showText": False, "fill": "#000000"},
]}
fields = [{"name": "name", "label": "Name", "defaultValue": "Box", "required": True}]
request = {"name": "Storage box", "description": "Sample label", "category": "Storage", "tags": ["box"], "widthMm": 60,
           "heightMm": 12, "editorJson": json.dumps(doc), "fields": fields}
status, template = js("POST", "/api/templates", request)
check("create a template", status == 201, template)
tid = template["id"]
check("search templates", any(t["id"] == tid for t in js("GET", "/api/templates?search=storage")[1]))
doc["objects"][0]["text"] = "Tapeory v2"
status, version = js("POST", f"/api/templates/{tid}/versions", {"widthMm": 60, "heightMm": 12, "editorJson": json.dumps(doc), "fields": fields})
check("save a new version", status in (200, 201) and version["versionNumber"] == 2, version)
check("list versions", len(js("GET", f"/api/templates/{tid}/versions")[1]) == 2)
status, updated = js("PUT", f"/api/templates/{tid}", {"name": "Storage box", "description": "Sample label", "category": "Storage",
                                                      "tags": ["box"], "status": "Published"})
check("publish the template", status == 200 and updated["status"] == "Published", updated)
status, payload = call("POST", f"/api/templates/{tid}/preview", {"fieldValues": {"name": "Cables"}})
check("render a preview", status == 200 and payload[:4] == b"\x89PNG")
check("thumbnail", call("GET", f"/api/templates/{tid}/thumbnail?v=2")[1][:4] == b"\x89PNG")
status, exported = call("GET", f"/api/templates/{tid}/export")
file = json.loads(exported) if status == 200 else {}
check("export as a .tapeory file", file.get("format") == "tapeory-template" and isinstance(file.get("document"), dict), exported[:200])
check("the file holds the template's image, not a link to it",
      len(file.get("images", [])) == 1 and base64.b64decode(file["images"][0]["data"]) == open(PNG, "rb").read()
      and b"uploadedFileId" not in exported)
status, copy = js("POST", f"/api/templates/{tid}/duplicate", {"name": "Storage box (copy)"})
check("duplicate", status in (200, 201), copy)
status, imported = js("POST", "/api/templates/import", raw=exported, content_type="application/json")
check("import the file", status in (200, 201), imported)
if status in (200, 201):
    values = {"fieldValues": {"name": "Cables"}}
    check("the imported template renders exactly the same label",
          call("POST", f"/api/templates/{imported['id']}/preview", values)[1] == call("POST", f"/api/templates/{tid}/preview", values)[1])
    images = [o for o in json.loads(imported["currentVersion"]["editorJson"])["objects"] if o["type"] == "image"]
    check("with its own copy of the image", len(images) == 1 and images[0]["uploadedFileId"] != image["id"], images)
status, lbx = upload("/api/templates/import-lbx", "sample.lbx", open(LBX, "rb").read(), "application/octet-stream")
check("import a P-touch Editor .lbx", status in (200, 201), lbx)
if status in (200, 201):
    check("download the original .lbx", call("GET", f"/api/templates/{lbx['id']}/original-lbx")[1][:2] == b"PK")
status, renamed = js("POST", "/api/templates/groups/rename", {"from": "Storage", "to": "Workshop"})
check("rename a group", status == 200 and renamed["updatedCount"] >= 2, renamed)
check("set a preview image", js("PUT", f"/api/templates/{tid}/preview-image", {"uploadedFileId": image["id"]})[0] in (200, 204))
check("delete a template", js("DELETE", f"/api/templates/{copy['id']}")[0] == 204)

# ---- printers ---------------------------------------------------------------------------------
status, models = js("GET", "/api/printers/models")
check("printer models", any(m["name"] == "PT-P750W" for m in models))
status, usb = js("GET", "/api/printers/usb")
check("the USB printer list answers", status == 200 and isinstance(usb, list), usb)
status, printer = js("POST", "/api/printers", {"name": "Workshop P750W", "model": "PT-P750W", "connectionType": "IpAddress",
                                             "address": "127.0.0.1", "port": 9100, "labelMediaWidthMm": 12, "enabled": True})
check("add a network printer", status == 201, printer)
pid = printer["id"]
check("make it the default", js("PUT", f"/api/printers/{pid}/default")[0] in (200, 204))
status, test = js("POST", f"/api/printers/{pid}/test-connection")
check("test the connection (fake printer)", status == 200 and test["isSuccess"], test)
check("printer status answers", js("GET", f"/api/printers/{pid}/status")[0] == 200)
before = len(received)
# The fake printer can't say which tape it has, so without a test print size nothing is sent.
status, body = js("POST", f"/api/printers/{pid}/test-print")
check("a test print for an unknown tape is refused", status == 200 and not body["isSuccess"] and "test print size" in body["errorMessage"], body)
time.sleep(1)
check("and nothing is sent to the printer", len(received) == before)
js("PUT", f"/api/printers/{pid}", {"name": "Workshop P750W", "model": "PT-P750W", "connectionType": "IpAddress", "address": "127.0.0.1",
                                    "port": 9100, "labelMediaWidthMm": 50, "labelMediaHeightMm": 12, "enabled": True})
status, body = js("POST", f"/api/printers/{pid}/test-print")
check("test print with a size set", status == 200 and body["isSuccess"], body)
time.sleep(1)
check("the fake printer got the test print", len(received) > before)
status, offline = js("POST", "/api/printers", {"name": "Offline", "model": "PT-P750W", "connectionType": "IpAddress",
                                             "address": "127.0.0.1", "port": 9, "enabled": True})
check("an offline printer fails the test", not js("POST", f"/api/printers/{offline['id']}/test-connection")[1]["isSuccess"])
status, changed = js("PUT", f"/api/printers/{offline['id']}", {"name": "Offline (renamed)", "model": "PT-P750W",
                                                             "connectionType": "IpAddress", "address": "127.0.0.1", "port": 9, "enabled": False})
check("edit a printer", status == 200 and changed["name"] == "Offline (renamed)", changed)
status, usb_printer = js("POST", "/api/printers", {"name": "USB", "model": "PT-P750W", "connectionType": "Usb", "usbIdentifier": "lp9",
                                                 "enabled": True})
check("add a USB printer (bound to this computer)", status == 201 and usb_printer.get("onThisComputer"), usb_printer)
check("a USB printer that isn't plugged in fails the test",
      not js("POST", f"/api/printers/{usb_printer['id']}/test-connection")[1]["isSuccess"])
js("DELETE", f"/api/printers/{usb_printer['id']}")
check("delete a printer", js("DELETE", f"/api/printers/{offline['id']}")[0] == 204)

status, area = js("GET", "/api/printers/print-area?widthMm=60&heightMm=9&media=tze-9")
check("the unprintable margins of a 9 mm tape (Brother: 7 dots, about 1 mm, top and bottom)",
      status == 200 and area["topMm"] == 0.99 and area["bottomMm"] == 0.99 and area["leftMm"] == 0 and area["mediaId"] == "tze-9", area)
status, area = js("GET", "/api/printers/print-area?widthMm=100&heightMm=36&media=tze-36")
check("a 36 mm tape has its own margins (Brother: 29 dots at 360 dpi, about 2 mm)",
      status == 200 and area["mediaId"] == "tze-36" and 1.9 < area["topMm"] < 2.1 and 1.9 < area["bottomMm"] < 2.1, area)

# ---- printing ---------------------------------------------------------------------------------
before = len(received)
status, job = js("POST", "/api/print-jobs", {"templateId": tid, "printerId": pid, "printerName": None, "quality": "Standard",
                                            "cutMode": "AutoCut", "items": [{"fieldValues": {"name": "Cables"}, "quantity": 2},
                                                                            {"fieldValues": {"name": "Screws"}, "quantity": 1}]})
check("create a print job", status in (200, 201, 202), job)
job = wait_job(job["id"])
check("the job finishes", job["status"] == "Completed", job)
time.sleep(1)
check("the fake printer got the labels", len(received) > before)
if len(received) > before:
    data = received[-1]
    check("raster data: initialise … print", b"\x1b@" in data[:400] and data.rstrip(b"\x00").endswith(b"\x1a"))
check("job item preview", call("GET", f"/api/print-jobs/items/{job['items'][0]['id']}/preview")[1][:4] == b"\x89PNG")
status, job2 = js("POST", "/api/print-jobs", {"templateId": tid, "printerId": pid, "printerName": None,
                                             "items": [{"fieldValues": {}, "quantity": 1}]})
check("a job with the default value", status in (200, 201, 202), job2)
wait_job(job2["id"])
check("list print jobs", len(js("GET", "/api/print-jobs")[1]) >= 2)
check("delete a print job", js("DELETE", f"/api/print-jobs/{job2['id']}")[0] == 204)

# ---- bulk printing from a data file -------------------------------------------------------------
def parse_data(name, data, **fields):
    boundary = uuid.uuid4().hex
    parts = [f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{name}\"\r\n"
             f"Content-Type: application/octet-stream\r\n\r\n".encode() + data + b"\r\n"]
    for key, value in {"templateId": tid, **fields}.items():
        parts.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"{key}\"\r\n\r\n{value}\r\n".encode())
    status, payload = call("POST", "/api/print-data/parse", raw=b"".join(parts) + f"--{boundary}--\r\n".encode(),
                           content_type=f"multipart/form-data; boundary={boundary}")
    return status, json.loads(payload) if payload else None


names = [f"Kiste {i + 1}" for i in range(30)]
# As German Excel saves a CSV: semicolons, Windows-1252.
csv = ("Name;Anzahl\r\n" + "".join(f"{name};{'2' if i == 0 else ''}\r\n" for i, name in enumerate(names))).encode("cp1252")
status, data = parse_data("kisten.csv", csv)
check("read a CSV file: separator and header found", status == 200 and data["separator"] == ";" and data["separatorDetected"]
      and data["hasHeader"] and data["fields"] == {"name": 0} and data["quantityColumn"] == 1 and len(data["rows"]) == 31, data)
status, data = parse_data("größen.txt", "Größe S\tA\nGröße M\tB\n".encode())
check("read a text file without a header", status == 200 and data["separator"] == "tab" and not data["hasHeader"]
      and data["rows"][0] == ["Größe S", "A"], data)
status, data = parse_data("unklar.txt", b"Schmidt, Anna;1\nMeier, Ben;2\n")
check("an unclear separator is reported, not guessed", status == 200 and not data["separatorDetected"], data)
status, data = parse_data("unklar.txt", b"Schmidt, Anna;1\nMeier, Ben;2\n", separator=";")
check("and the chosen separator is used", status == 200 and data["rows"] == [["Schmidt, Anna", "1"], ["Meier, Ben", "2"]], data)
check("a file that isn't text is refused with a reason", parse_data("logo.png", open(PNG, "rb").read())[0] == 400)
records = json.dumps({"items": [{"name": name, "place": {"shelf": "A"}} for name in names[:3]]}).encode()
status, data = parse_data("kisten.json", records)
check("read a JSON file: the records' properties are the columns", status == 200 and data["kind"] == "json" and data["hasHeader"]
      and data["rows"][0] == ["name", "place.shelf"] and data["rows"][1] == ["Kiste 1", "A"] and data["fields"] == {"name": 0}, data)


class Records(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        seen.append(self.headers.get("X-Api-Key"))
        self.send_response(200 if self.path == "/items" else 404)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(records if self.path == "/items" else b"{}")

    def log_message(self, *args):
        pass


seen = []
rest = http.server.HTTPServer(("127.0.0.1", 0), Records)
threading.Thread(target=rest.serve_forever, daemon=True).start()
address = f"http://127.0.0.1:{rest.server_port}"
status, data = js("POST", "/api/print-data/fetch", {"templateId": tid, "url": address + "/items", "headerName": "X-Api-Key", "headerValue": "s3cret"})
check("get the data from a web address", status == 200 and data["kind"] == "json" and len(data["rows"]) == 4 and seen[-1] == "s3cret", data)
status, problem = js("POST", "/api/print-data/fetch", {"templateId": tid, "url": address + "/missing"})
check("a web address that answers an error is reported", status == 400 and "404" in json.dumps(problem), problem)

status, checked = js("POST", f"/api/templates/{tid}/check-rows", {"rows": [{"name": name} for name in names]})
check("check the rows before printing", status == 200 and len(checked["rows"]) == 30
      and all(not row["errors"] for row in checked["rows"]), checked)

before = len(received)
status, bulk = js("POST", "/api/print-jobs", {"templateId": tid, "printerId": pid, "printerName": None,
                                             "items": [{"fieldValues": {"name": name}, "quantity": 2 if i == 0 else 1}
                                                       for i, name in enumerate(names)]})
check("create a bulk print job", status in (200, 201, 202), bulk)
bulk = wait_job(bulk["id"])
check("the bulk job finishes with every row printed", bulk["status"] == "Completed"
      and all(item["status"] == "Completed" for item in bulk["items"]) and len(bulk["items"]) == 30, bulk["status"])
time.sleep(4)
check("its 31 labels went to the printer in two batches", len(received) - before == 2, len(received) - before)
check("a finished job has nothing to reprint", js("POST", f"/api/print-jobs/{bulk['id']}/reprint-unprinted")[0] == 400)
check("stopping a finished job leaves it as it is", js("POST", f"/api/print-jobs/{bulk['id']}/cancel")[1]["status"] == "Completed")

# ---- saved bulk print profiles ----------------------------------------------------------------
profile = {"fileName": "kisten.csv", "filePath": "/home/demo/kisten.csv", "separator": ";", "sheet": None, "hasHeader": True,
           "columns": [{"field": "name", "column": 0, "header": "Name"}], "quantityColumn": 1, "quantityHeader": "Anzahl",
           "printerId": pid, "printerName": None, "quality": "Standard", "cutMode": "AutoCut"}
status, saved = js("PUT", f"/api/templates/{tid}/bulk-print-profiles", {"name": "Kisten", "settings": profile})
check("save a bulk print profile", status == 200 and saved["name"] == "Kisten", saved)
replacement = {"name": "kisten", "settings": dict(profile, quantityColumn=None)}
check("a name that is taken is refused", js("PUT", f"/api/templates/{tid}/bulk-print-profiles", replacement)[0] == 409)
status, again = js("PUT", f"/api/templates/{tid}/bulk-print-profiles", dict(replacement, replace=True))
check("and replaced when asked to", status == 200 and again["id"] == saved["id"], again)
status, listed = js("GET", f"/api/templates/{tid}/bulk-print-profiles")
check("the profile comes back with its file path and matching", status == 200 and len(listed) == 1
      and listed[0]["settings"]["filePath"] == "/home/demo/kisten.csv" and listed[0]["settings"]["columns"][0]["header"] == "Name"
      and listed[0]["settings"]["quantityColumn"] is None, listed)
check("delete the profile", js("DELETE", f"/api/bulk-print-profiles/{saved['id']}")[0] == 204
      and js("GET", f"/api/templates/{tid}/bulk-print-profiles")[1] == [])
js("PUT", f"/api/templates/{tid}/bulk-print-profiles", {"name": "Kisten", "settings": profile})

# ---- statistics ---------------------------------------------------------------------------------
check("statistics count the labels", js("GET", "/api/stats")[1]["labelsPrinted"] >= 3)
check("reset statistics", js("POST", "/api/stats/reset")[1]["labelsPrinted"] == 0)
check("back to all-time statistics", js("DELETE", "/api/stats/reset")[1]["labelsPrinted"] >= 3)

# ---- backups ------------------------------------------------------------------------------------
for kind in ("database", "labels"):
    status, backup = js("POST", f"/api/backups/{kind}")
    check(f"create a {kind} backup", status == 201, backup)
    name = urllib.request.quote(backup["fileName"])
    check(f"download the {kind} backup", len(call("GET", f"/api/backups/{kind}/{name}")[1]) > 100)
    check(f"list {kind} backups", any(b["fileName"] == backup["fileName"] for b in js("GET", f"/api/backups/{kind}")[1]))
    check(f"restore the {kind} backup", js("POST", f"/api/backups/{kind}/{name}/restore")[0] == 200)
    templates = js("GET", "/api/templates")[1]
    check(f"templates are back after the {kind} restore", any(t["name"] == "Storage box" for t in templates))
    tid = next(t["id"] for t in templates if t["name"] == "Storage box")
    check(f"delete the {kind} backup", js("DELETE", f"/api/backups/{kind}/{name}")[0] == 204)

# ---- accounts: the sample users ---------------------------------------------------------------
status, state = js("POST", "/api/auth/first-user", {"userName": "demo", "displayName": "Demo User", "password": "Tapeory-Demo-2026"})
check("create the sample administrator 'demo'", status == 200 and state["user"]["userName"] == "demo", state)
status, created = js("POST", "/api/users", {"userName": "alex", "displayName": "Alex", "role": "User"})
check("create a second account", status in (200, 201), created)
alex_id = created["user"]["id"]
status, body = js("PUT", f"/api/users/{alex_id}", {"displayName": "Alex Doe", "role": "User", "disabled": False})
check("edit an account", status == 200 and body["displayName"] == "Alex Doe", body)
status, body = js("POST", f"/api/users/{alex_id}/reset-password")
check("reset a password", status == 200 and body["temporaryPassword"], body)
temporary = body["temporaryPassword"]
check("sign out", js("POST", "/api/auth/logout")[0] in (200, 204))
check("signed out: templates need signing in", js("GET", "/api/templates")[0] == 401)
check("a wrong password is refused", js("POST", "/api/auth/login", {"userName": "demo", "password": "wrong", "rememberMe": False})[0] in (400, 401))
status, state = js("POST", "/api/auth/login", {"userName": "alex", "password": temporary, "rememberMe": False})
check("sign in with the temporary password", status == 200 and state["user"]["mustChangePassword"], state)
status, state = js("PUT", "/api/auth/password", {"currentPassword": temporary, "newPassword": "Alex-Password-2026"})
check("change the password", status == 200 and not state["user"]["mustChangePassword"], state)
status, mine = js("POST", "/api/templates", dict(request, name="Alex's private label"))
check("a user's template is private", status == 201 and mine.get("isMine") and not mine.get("isPublic"), mine)
check("a user can't change someone else's template", js("PUT", f"/api/templates/{tid}", {"name": "Hacked"})[0] in (403, 404))
check("a user can't manage accounts", js("GET", "/api/users")[0] == 403)
js("POST", "/api/auth/logout")
status, state = js("POST", "/api/auth/login", {"userName": "demo", "password": "Tapeory-Demo-2026", "rememberMe": True})
check("sign in as demo", status == 200 and state["user"]["role"] == "Admin", state)
check("the administrator sees the user's private template", any(t["id"] == mine["id"] for t in js("GET", "/api/templates")[1]))
status, body = js("PUT", f"/api/templates/{mine['id']}/visibility", {"isPublic": True})
check("make a template public", status == 200 and body["isPublic"], body)

# ---- updates, stop ------------------------------------------------------------------------------
status, body = js("GET", "/api/updates")
check("the update check answers", status == 200, body)
engine.stdin.close()
try:
    engine.wait(15)
    check("the engine stops when its input closes", True)
except subprocess.TimeoutExpired:
    engine.kill()
    check("the engine stops when its input closes", False)

failed = [name for name, ok in results if not ok]
print(f"\n{len(results) - len(failed)}/{len(results)} passed")
for name in failed:
    print("  FAILED:", name)
print(f"\nSample data: {DATA}  (demo / Tapeory-Demo-2026, alex / Alex-Password-2026)")
sys.exit(1 if failed else 0)

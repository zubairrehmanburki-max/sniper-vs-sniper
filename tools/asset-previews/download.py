"""Downloads candidate assets so they can be inspected offline: Sketchfab GLBs (glbs.json {id: uid},
needs SKETCHFAB_API_TOKEN) and every .zip linked from the pages in pages.json {id: url}. Writes
OUT/<id>.glb, OUT/<id>__<name>.zip and OUT/download-report.json."""
import html, json, os, re, sys, time, urllib.parse, urllib.request

here, out = os.path.dirname(os.path.abspath(__file__)), sys.argv[1]
os.makedirs(out, exist_ok=True)
UA = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36"
report = {}


def get(url, headers=None):
    for attempt in range(5):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": UA, **(headers or {})})
            with urllib.request.urlopen(req, timeout=300) as r:
                return r.read()
        except urllib.error.HTTPError as e:
            if e.code != 429 or attempt == 4:
                raise
            time.sleep(30 * (attempt + 1))


token = os.environ.get("SKETCHFAB_API_TOKEN", "")
auth = {"Authorization": f"Token {token}"}
for mid, uid in json.load(open(os.path.join(here, "glbs.json"))).items():
    try:
        info = json.loads(get(f"https://api.sketchfab.com/v3/models/{uid}", auth))
        lic = (info.get("license") or {}).get("slug")
        if lic not in ("by", "cc0"):
            raise ValueError(f"licence {lic}")
        dl = json.loads(get(f"https://api.sketchfab.com/v3/models/{uid}/download", auth))
        if "glb" in dl:
            data = get(dl["glb"]["url"])
            open(os.path.join(out, mid + ".glb"), "wb").write(data)
        else:
            data = get(dl["gltf"]["url"])
            open(os.path.join(out, mid + "__gltf.zip"), "wb").write(data)
        report[mid] = {"ok": True, "mb": round(len(data) / 1e6, 1), "name": info.get("name"),
                       "author": (info.get("user") or {}).get("displayName"), "faces": info.get("faceCount"),
                       "url": info.get("viewerUrl"), "formats": sorted(dl)}
    except Exception as e:
        report[mid] = {"ok": False, "error": str(e)}
    print(mid, report[mid])

pages_path = os.path.join(here, "pages.json")
for pid, url in (json.load(open(pages_path)).items() if os.path.exists(pages_path) else []):
    try:
        page = get(url).decode("utf-8", "replace")
        links = sorted(set(urllib.parse.urljoin(url, html.unescape(h)) for h in re.findall(r'href=["\']([^"\']+\.zip)["\']', page)))
        got = []
        for link in links[:12]:
            data = get(link)
            name = re.sub(r"[^A-Za-z0-9._-]", "_", os.path.basename(urllib.parse.urlparse(link).path))
            open(os.path.join(out, f"{pid}__{name}"), "wb").write(data)
            got.append({"url": link, "mb": round(len(data) / 1e6, 1)})
        report[pid] = {"ok": bool(got), "zips": got, "links": links}
    except Exception as e:
        report[pid] = {"ok": False, "error": str(e)}
    print(pid, report[pid])
json.dump(report, open(os.path.join(out, "download-report.json"), "w"), indent=1)

#!/usr/bin/env python3
"""
Downloads the free third-party assets listed in unity/assets.json into an output folder, with a
CREDITS.md. Everything is CC0 (Poly Haven), OFL (Google Fonts) or CC-BY (Sketchfab, credited; needs
SKETCHFAB_API_TOKEN). Run by the unity-assets action on
a GitHub runner; the result is cached as a release so builds don't hit the sources every time.

usage: fetch.py unity/assets.json OUT_DIR
"""
import json, os, sys, time, urllib.request

API = "https://api.polyhaven.com"
UA = {"User-Agent": "sniper-vs-sniper-asset-fetch"}


def get(url, headers=None):
    for attempt in range(5):
        req = urllib.request.Request(url, headers={**UA, **(headers or {})})
        try:
            with urllib.request.urlopen(req, timeout=300) as r:
                return r.read()
        except urllib.error.HTTPError as e:
            if e.code != 429 or attempt == 4:
                raise
            time.sleep(30 * (attempt + 1))  # Sketchfab rate limit


def save(url, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    data = get(url)
    with open(path, "wb") as f:
        f.write(data)
    print(f"  {len(data) / 1e6:6.1f} MB  {path}")


def polyhaven(kind, item, out, credits, problems, max_mb):
    pid, res = item["id"], item.get("res", "2k")
    try:
        files = json.loads(get(f"{API}/files/{pid}"))
        info = json.loads(get(f"{API}/info/{pid}"))
    except Exception as e:  # unknown id: report and carry on so one typo doesn't sink the set
        problems.append(f"{pid}: {e}")
        return
    folder = os.path.join(out, item.get("dest", kind.capitalize() + "s"), pid)
    if kind == "hdri":
        fmt = item.get("format", "exr")
        save(files["hdri"][res][fmt]["url"], os.path.join(folder, f"{pid}_{res}.{fmt}"))
    elif kind == "texture":
        for m in item.get("maps", ["Diffuse", "nor_gl", "arm"]):
            if m not in files:
                problems.append(f"{pid}: no map {m} (has {sorted(files)})")
                continue
            f = files[m][res]
            f = f.get("jpg") or f.get("png") or next(iter(f.values()))
            save(f["url"], os.path.join(folder, os.path.basename(f["url"])))
    elif kind == "model":
        g = files["gltf"][res]["gltf"]
        mb = (g.get("size", 0) + sum(i.get("size", 0) for i in g.get("include", {}).values())) / 1e6
        if mb > max_mb:  # some scans (pine_tree_01) ship ~1 GB of geometry; not usable on a phone
            problems.append(f"{pid}: {mb:.0f} MB at {res}, over the {max_mb} MB limit; skipped")
            return
        save(g["url"], os.path.join(folder, os.path.basename(g["url"])))
        for rel, inc in g.get("include", {}).items():
            save(inc["url"], os.path.join(folder, rel))
    authors = ", ".join(info.get("authors", {}))
    credits.append(f"| {pid} | Poly Haven {kind} | {authors} | CC0 | https://polyhaven.com/a/{pid} |")


def sketchfab(item, out, credits, problems):
    """A CC-BY Sketchfab model as GLB, saved where Library.Model finds it (Models/<id>/<id>_1k.glb)."""
    token = os.environ.get("SKETCHFAB_API_TOKEN", "")
    if not token:
        problems.append(f"{item['id']}: SKETCHFAB_API_TOKEN not set; skipped")
        return
    uid, mid = item["uid"], item["id"]
    try:
        auth = {"Authorization": f"Token {token}"}
        info = json.loads(get(f"https://api.sketchfab.com/v3/models/{uid}", auth))
        lic = (info.get("license") or {}).get("slug")
        if lic not in ("by", "cc0"):
            problems.append(f"{mid}: licence {lic} not allowed; skipped")
            return
        dl = json.loads(get(f"https://api.sketchfab.com/v3/models/{uid}/download", auth))
        save(dl["glb"]["url"], os.path.join(out, "Models", mid, f"{mid}_1k.glb"))
    except Exception as e:
        problems.append(f"{mid}: {e}")
        return
    author = info.get("user", {}).get("displayName") or info.get("user", {}).get("username", "")
    credits.append(f"| {info.get('name', mid)} | Sketchfab model | {author} | CC-BY 4.0 | {info.get('viewerUrl', '')} |")


def main():
    manifest_path, out = sys.argv[1], sys.argv[2]
    manifest = json.load(open(manifest_path))
    credits = ["# Third-party assets", "", "Downloaded by tools/unity-assets/fetch.py from unity/assets.json.", "",
               "| Asset | Source | Author | Licence | Link |", "|---|---|---|---|---|"]
    problems = []
    for kind in ("hdri", "texture", "model"):
        for item in manifest.get(kind + "s", []):
            print(f"{kind}: {item['id']}")
            polyhaven(kind, item, out, credits, problems, manifest.get("maxModelMB", 1e9))
    for item in manifest.get("sketchfab", []):
        print(f"sketchfab: {item['id']}")
        sketchfab(item, out, credits, problems)
    for item in manifest.get("files", []):
        print(f"file: {item['dest']}")
        try:
            save(item["url"], os.path.join(out, item["dest"]))
            credits.append(f"| {os.path.basename(item['dest'])} | {item['source']} | {item.get('author', '')} | {item['license']} | {item['url']} |")
        except Exception as e:
            problems.append(f"{item['dest']}: {e}")
    with open(os.path.join(out, "CREDITS.md"), "w") as f:
        f.write("\n".join(credits) + "\n")
    # Catalogue of everything Poly Haven offers, so new picks can be made without guessing ids.
    for t in ("hdris", "textures", "models"):
        try:
            cat = json.loads(get(f"{API}/assets?t={t}"))
            with open(os.path.join(out, f"catalogue-{t}.txt"), "w") as f:
                for k, v in sorted(cat.items()):
                    f.write(f"{k}\t{','.join(v.get('categories', []))}\t{','.join(v.get('tags', []))}\n")
        except Exception as e:
            problems.append(f"catalogue {t}: {e}")
    if problems:
        print("\nProblems:\n  " + "\n  ".join(problems))
        with open(os.path.join(out, "PROBLEMS.txt"), "w") as f:
            f.write("\n".join(problems) + "\n")


if __name__ == "__main__":
    main()

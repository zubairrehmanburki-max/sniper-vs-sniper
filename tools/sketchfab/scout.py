#!/usr/bin/env python3
"""
Searches Sketchfab for downloadable CC-BY / CC0 models that could replace the game's soldier,
rifle and props, and writes a report with thumbnails so picks can be made by eye. For rigged
characters it also downloads the GLB and lists the skeleton, since the game poses figures by
Mixamo bone names.

usage: scout.py OUT_DIR   (needs SKETCHFAB_API_TOKEN)
"""
import json, os, struct, sys, urllib.parse, urllib.request

API = "https://api.sketchfab.com/v3"
TOKEN = os.environ.get("SKETCHFAB_API_TOKEN", "")
OK_LICENCES = {"by", "cc0"}

QUERIES = [
    # (label, query, rigged, max faces, inspect skeleton)
    ("soldier", "soldier", True, 120000, True),
    ("soldier", "sniper soldier", True, 120000, True),
    ("soldier", "ghillie suit", False, 150000, True),
    ("soldier", "military character", True, 120000, True),
    ("soldier", "special forces", True, 120000, True),
    ("rifle", "sniper rifle", False, 60000, False),
    ("rifle", "bolt action rifle", False, 60000, False),
    ("prop", "sandbags", False, 30000, False),
    ("prop", "military barrier", False, 30000, False),
    ("prop", "abandoned car", False, 60000, False),
]


def get(url, auth=True):
    req = urllib.request.Request(url, headers={"Authorization": f"Token {TOKEN}"} if auth else {})
    with urllib.request.urlopen(req, timeout=120) as r:
        return r.read()


def search(q, rigged, max_faces, count=24):
    params = {"type": "models", "q": q, "downloadable": "true", "count": count,
              "max_face_count": max_faces, "sort_by": "-likeCount"}
    if rigged:
        params["rigged"] = "true"
    return json.loads(get(f"{API}/search?" + urllib.parse.urlencode(params)))["results"]


def glb_skeleton(data):
    """Joint names and mesh/material counts from a GLB's JSON chunk."""
    length = struct.unpack_from("<I", data, 12)[0]
    doc = json.loads(data[20:20 + length])
    nodes = doc.get("nodes", [])
    joints = []
    for skin in doc.get("skins", []):
        joints += [nodes[j].get("name", "?") for j in skin.get("joints", [])]
    anims = [a.get("name", "?") for a in doc.get("animations", [])]
    return {"joints": joints, "meshes": len(doc.get("meshes", [])), "materials": len(doc.get("materials", [])),
            "images": len(doc.get("images", [])), "animations": anims}


def main():
    out = sys.argv[1]
    os.makedirs(os.path.join(out, "thumbs"), exist_ok=True)
    os.makedirs(os.path.join(out, "models"), exist_ok=True)
    seen, report = set(), []
    for label, q, rigged, max_faces, inspect in QUERIES:
        try:
            results = search(q, rigged, max_faces)
        except Exception as e:
            report.append({"query": q, "error": str(e)})
            continue
        for m in results:
            uid = m["uid"]
            if uid in seen:
                continue
            seen.add(uid)
            # Search results leave the licence out; the model's own page has it.
            lic = (m.get("license") or {}).get("slug") or ""
            if not lic:
                try:
                    lic = (json.loads(get(f"{API}/models/{uid}")).get("license") or {}).get("slug") or ""
                except Exception as e:
                    lic = f"? {e}"
            entry = {"label": label, "query": q, "uid": uid, "name": m.get("name"), "author": m["user"]["username"],
                     "license": lic, "faces": m.get("faceCount"), "verts": m.get("vertexCount"),
                     "animations": m.get("animationCount"), "likes": m.get("likeCount"),
                     "url": m.get("viewerUrl")}
            if lic not in OK_LICENCES:
                entry["skipped"] = "licence"
                report.append(entry)
                continue
            imgs = sorted((m.get("thumbnails") or {}).get("images", []), key=lambda i: abs(i.get("width", 0) - 512))
            if imgs:
                try:
                    with open(os.path.join(out, "thumbs", f"{label}-{uid}.jpg"), "wb") as f:
                        f.write(get(imgs[0]["url"], auth=False))
                except Exception as e:
                    entry["thumb_error"] = str(e)
            if inspect:
                try:
                    dl = json.loads(get(f"{API}/models/{uid}/download"))
                    g = dl.get("glb")
                    entry["glb_mb"] = round(g["size"] / 1e6, 1) if g else None
                    if g and g["size"] < 80e6:
                        data = get(g["url"], auth=False)
                        entry.update(glb_skeleton(data))
                        entry["mixamo"] = any("mixamorig" in j or j.endswith("Hips") for j in entry["joints"])
                        entry["joints"] = entry["joints"][:80]
                except Exception as e:
                    entry["download_error"] = str(e)
            report.append(entry)
            print(json.dumps({k: entry.get(k) for k in ("label", "uid", "name", "license", "faces", "mixamo", "glb_mb")}))
    with open(os.path.join(out, "report.json"), "w") as f:
        json.dump(report, f, indent=1)


if __name__ == "__main__":
    main()

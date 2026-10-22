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

# SCOUT_SET=graphics: the X-ray anatomy and the scenery the 2026-10-22 graphics review asks for.
if os.environ.get("SCOUT_SET") == "graphics":
    QUERIES = [
        ("skeleton", "human skeleton rigged", True, 150000, True),
        ("skeleton", "skeleton mixamo", True, 150000, True),
        ("skeleton", "human skeleton anatomy", False, 200000, False),
        ("anatomy", "human organs anatomy", False, 150000, False),
        ("anatomy", "heart lungs anatomy", False, 100000, False),
        ("soldier", "ghillie suit sniper", True, 150000, True),
        ("building", "apartment building low poly realistic", False, 40000, False),
        ("building", "office building pbr", False, 40000, False),
        ("prop", "oil rig platform", False, 150000, False),
        ("prop", "watchtower wooden", False, 60000, False),
    ]

# SCOUT_SET=organs: single organs for the X-ray, each fitted into the body on its own.
if os.environ.get("SCOUT_SET") == "organs":
    QUERIES = [
        ("organ", "human heart anatomy", False, 60000, True),
        ("organ", "human lungs anatomy", False, 60000, True),
        ("organ", "human liver", False, 60000, True),
        ("organ", "human stomach", False, 60000, True),
        ("organ", "human kidney", False, 60000, True),
        ("organ", "human organs", False, 80000, True),
    ]

# SCOUT_UIDS=uid,uid: also download these models and list their parts (nodes, meshes, sizes).
UIDS = [u.strip() for u in os.environ.get("SCOUT_UIDS", "").split(",") if u.strip()]


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


def glb_parts(data):
    """Nodes with their mesh, each mesh's primitives (material, size from the position bounds)."""
    length = struct.unpack_from("<I", data, 12)[0]
    doc = json.loads(data[20:20 + length])
    acc = doc.get("accessors", [])
    mats = [m.get("name", "?") for m in doc.get("materials", [])]
    meshes = []
    for m in doc.get("meshes", []):
        prims = []
        for p in m.get("primitives", []):
            a = acc[p["attributes"]["POSITION"]]
            lo, hi = a.get("min", [0, 0, 0]), a.get("max", [0, 0, 0])
            prims.append({"material": mats[p["material"]] if "material" in p else None, "verts": a.get("count"),
                          "min": [round(v, 3) for v in lo], "max": [round(v, 3) for v in hi]})
        meshes.append({"name": m.get("name"), "primitives": prims})
    nodes = [{"name": n.get("name"), "mesh": n.get("mesh"), "children": n.get("children", []),
              "t": n.get("translation"), "r": n.get("rotation"), "s": n.get("scale"), "m": n.get("matrix")}
             for n in doc.get("nodes", [])]
    return {"nodes": nodes, "meshes": meshes, "materials": mats}


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
    for uid in UIDS:
        entry = {"label": "inspect", "uid": uid}
        try:
            info = json.loads(get(f"{API}/models/{uid}"))
            entry.update({"name": info.get("name"), "author": (info.get("user") or {}).get("username"),
                          "license": (info.get("license") or {}).get("slug")})
            g = json.loads(get(f"{API}/models/{uid}/download")).get("glb")
            entry["glb_mb"] = round(g["size"] / 1e6, 1) if g else None
            if g:
                entry.update(glb_parts(get(g["url"], auth=False)))
        except Exception as e:
            entry["error"] = str(e)
        report.append(entry)
        print(json.dumps({k: entry.get(k) for k in ("uid", "name", "license", "glb_mb")}))
    with open(os.path.join(out, "report.json"), "w") as f:
        json.dump(report, f, indent=1)


if __name__ == "__main__":
    main()

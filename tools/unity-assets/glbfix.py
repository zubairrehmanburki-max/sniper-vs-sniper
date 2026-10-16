#!/usr/bin/env python3
"""
Readies a downloaded Sketchfab GLB for a phone, in place:
- drop: removes primitives whose material is named in the list (ground, grass and rifles baked
  into a character), and nodes left without a mesh;
- spec-gloss materials (KHR_materials_pbrSpecularGlossiness) become metal/roughness ones, which
  is what the game's material conversion reads;
- opaque: turns "BLEND" materials opaque (Sketchfab exports often mark whole bodies as blended);
- maxTexture: scales embedded images down so neither side exceeds it (needs Pillow);
- simplify: a triangle ratio for gltfpack (npx gltfpack), which keeps skins and UVs.

usage: glbfix.py FILE '{"drop": [...], "maxTexture": 1024, "simplify": 0.5}'
"""
import io, json, os, shutil, struct, subprocess, sys, tempfile


def read(path):
    d = open(path, "rb").read()
    n = struct.unpack_from("<I", d, 12)[0]
    doc = json.loads(d[20:20 + n])
    rest = d[20 + n:]
    blob = b""
    if len(rest) >= 8:
        bl = struct.unpack_from("<I", rest, 0)[0]
        blob = rest[8:8 + bl]
    return doc, blob


def write(path, doc, blob):
    blob += b"\0" * (-len(blob) % 4)
    if doc.get("buffers"):
        doc["buffers"][0]["byteLength"] = len(blob)
    j = json.dumps(doc, separators=(",", ":")).encode()
    j += b" " * (-len(j) % 4)
    total = 12 + 8 + len(j) + (8 + len(blob) if blob else 0)
    with open(path, "wb") as f:
        f.write(struct.pack("<III", 0x46546C67, 2, total))
        f.write(struct.pack("<II", len(j), 0x4E4F534A) + j)
        if blob:
            f.write(struct.pack("<II", len(blob), 0x004E4942) + blob)


def views(doc, blob):
    return [blob[v.get("byteOffset", 0):v.get("byteOffset", 0) + v["byteLength"]] for v in doc.get("bufferViews", [])]


def relayout(doc, parts):
    """Writes the bufferViews back one after another (8-byte aligned) and returns the new blob."""
    out = bytearray()
    for v, data in zip(doc.get("bufferViews", []), parts):
        out += b"\0" * (-len(out) % 8)
        v["byteOffset"] = len(out)
        v["byteLength"] = len(data)
        v["buffer"] = 0
        out += data
    return bytes(out)


def fix(path, ops):
    doc, blob = read(path)
    parts = views(doc, blob)
    mats = doc.get("materials", [])

    drop = set(ops.get("drop", []))
    if drop:
        names = [m.get("name", "") for m in mats]
        for m in doc.get("meshes", []):
            m["primitives"] = [p for p in m["primitives"] if names[p["material"]] not in drop] if all("material" in p for p in m["primitives"]) else m["primitives"]
        for nd in doc.get("nodes", []):
            if "mesh" in nd and not doc["meshes"][nd["mesh"]]["primitives"]:
                del nd["mesh"]
                nd.pop("skin", None)
        # A mesh with no primitives is invalid glTF: drop the empty ones and renumber the rest.
        keep = [i for i, m in enumerate(doc.get("meshes", [])) if m["primitives"]]
        renum = {old: new for new, old in enumerate(keep)}
        doc["meshes"] = [doc["meshes"][i] for i in keep]
        for nd in doc.get("nodes", []):
            if "mesh" in nd:
                nd["mesh"] = renum[nd["mesh"]]
        # Morph-weight tracks on nodes that lost their mesh point at nothing.
        nodes = doc.get("nodes", [])
        for an in doc.get("animations", []):
            an["channels"] = [c for c in an["channels"] if not (c["target"].get("path") == "weights" and "mesh" not in nodes[c["target"].get("node", 0)])]
        doc["animations"] = [an for an in doc.get("animations", []) if an["channels"]]
        if not doc["animations"]:
            del doc["animations"]

    for m in mats:
        sg = (m.get("extensions") or {}).pop("KHR_materials_pbrSpecularGlossiness", None)
        if sg is not None:
            mr = m.setdefault("pbrMetallicRoughness", {})
            if "diffuseTexture" in sg:
                mr["baseColorTexture"] = sg["diffuseTexture"]
            mr["baseColorFactor"] = sg.get("diffuseFactor", [1, 1, 1, 1])
            mr["metallicFactor"] = 0
            mr["roughnessFactor"] = round(1 - sg.get("glossinessFactor", 0.2), 3)
        if ops.get("opaque") and m.get("alphaMode") == "BLEND":
            m["alphaMode"] = "OPAQUE"
    used = {e for m in mats for e in (m.get("extensions") or {})}
    for key in ("extensionsUsed", "extensionsRequired"):
        if key in doc:
            doc[key] = [e for e in doc[key] if e != "KHR_materials_pbrSpecularGlossiness" or e in used]

    limit = ops.get("maxTexture")
    if limit:
        from PIL import Image
        for img in doc.get("images", []):
            if "bufferView" not in img:
                continue
            data = parts[img["bufferView"]]
            try:
                im = Image.open(io.BytesIO(data))
            except Exception:
                continue
            if max(im.size) <= limit:
                continue
            k = limit / max(im.size)
            im = im.resize((max(1, round(im.width * k)), max(1, round(im.height * k))), Image.LANCZOS)
            buf = io.BytesIO()
            if im.mode in ("RGBA", "LA") or (im.mode == "P" and "transparency" in im.info):
                im.save(buf, "PNG", optimize=True)
                img["mimeType"] = "image/png"
            else:
                im.convert("RGB").save(buf, "JPEG", quality=90)
                img["mimeType"] = "image/jpeg"
            parts[img["bufferView"]] = buf.getvalue()
    write(path, doc, relayout(doc, parts))

    ratio = ops.get("simplify")
    if ratio:
        tmp = tempfile.mkdtemp()
        out = os.path.join(tmp, "out.glb")
        npx = shutil.which("npx") or "npx"
        subprocess.run([npx, "--yes", "gltfpack@0.25.0", "-i", path, "-o", out, "-si", str(ratio), "-noq", "-kn"] + (["-km"] if ops.get("keepMaterials") else []), check=True)
        shutil.move(out, path)
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    fix(sys.argv[1], json.loads(sys.argv[2]) if len(sys.argv) > 2 else {})

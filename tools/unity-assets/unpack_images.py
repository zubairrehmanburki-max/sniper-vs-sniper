#!/usr/bin/env python3
"""
Moves the pictures embedded in each GLB under a folder out into image files beside it, the GLB
pointing at them by uri. Unity then imports them with its own texture importer, compressed for
the target GPU (ASTC on iPhone) at build time, instead of glTFast keeping them uncompressed inside
the model and the game converting them on the phone at every map load.

Each file is named after the GLB, the image index and what the materials use it for
(`_normal`, `_metal`, `_ao`, `_emissive`), so Build.ThirdPartyImport sets normal maps and linear
data maps correctly. Runs after the asset set is unpacked; a GLB with no embedded images is left
as it is.

usage: unpack_images.py FOLDER
"""
import json, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import glbfix

EXT = {"image/png": "png", "image/jpeg": "jpg"}


def roles(doc):
    """image index -> role suffix, from how the materials use each texture."""
    tex_image = [t.get("source") for t in doc.get("textures", [])]
    out = {}

    def mark(info, role):
        if not info or "index" not in info:
            return
        i = info["index"]
        if 0 <= i < len(tex_image) and tex_image[i] is not None:
            # A picture used as colour and as data keeps the colour reading.
            out.setdefault(tex_image[i], role)

    for m in doc.get("materials", []):
        mr = m.get("pbrMetallicRoughness") or {}
        mark(mr.get("baseColorTexture"), "")
        mark(m.get("emissiveTexture"), "_emissive")
        mark(m.get("normalTexture"), "_normal")
        mark(mr.get("metallicRoughnessTexture"), "_metal")
        mark(m.get("occlusionTexture"), "_ao")
    return out


def unpack(path):
    doc, blob = glbfix.read(path)
    images = doc.get("images", [])
    if not any("bufferView" in im for im in images):
        return 0
    parts = glbfix.views(doc, blob)
    role = roles(doc)
    base = os.path.splitext(os.path.basename(path))[0]
    folder = os.path.dirname(path)
    n = 0
    for i, im in enumerate(images):
        if "bufferView" not in im:
            continue
        data = parts[im["bufferView"]]
        mime = im.get("mimeType", "")
        ext = EXT.get(mime) or ("png" if data[:4] == b"\x89PNG" else "jpg" if data[:2] == b"\xff\xd8" else None)
        if ext is None:
            continue  # KTX2/WebP and the like: left embedded
        name = f"{base}_img{i}{role.get(i, '')}.{ext}"
        with open(os.path.join(folder, name), "wb") as f:
            f.write(data)
        del im["bufferView"]
        im.pop("mimeType", None)
        im["uri"] = name
        n += 1
    # The image bytes stay in the binary chunk, unreferenced: harmless, and the layout of the
    # geometry views is untouched.
    glbfix.write(path, doc, blob)
    return n


if __name__ == "__main__":
    total = 0
    for root, _, files in os.walk(sys.argv[1]):
        for f in sorted(files):
            if f.lower().endswith(".glb"):
                try:
                    k = unpack(os.path.join(root, f))
                except Exception as e:
                    print(f"  {f}: left as it is ({e})")
                    continue
                if k:
                    print(f"  {f}: {k} images unpacked")
                    total += k
    print(f"unpack_images: {total} images")

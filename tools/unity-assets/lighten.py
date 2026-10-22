#!/usr/bin/env python3
"""
Cuts the triangle count of the heaviest Poly Haven models to what a phone can draw by the
dozen, after the asset set is unpacked. Scans ship at hundreds of thousands of triangles each
(the road barrier in every firing-spot kit is ~61k; the Namaqualand boulders ~90k); at sniper
range their normal maps carry the detail, so a fraction of the triangles looks the same.

Each listed model's .gltf (with its .bin and textures) is replaced by one simplified GLB of the
same name (gltfpack, which keeps UVs, normals and node names). Its pictures are embedded at that
point; unpack_images.py, run next, moves them out again for Unity to compress.

usage: lighten.py MODELS_FOLDER
"""
import glob, os, shutil, subprocess, sys

# Model id -> share of triangles kept. Per-copy counts from the CI log are in the comments.
RATIOS = {
    "concrete_road_barrier": 0.05,   # 61k, in every spot kit (24-28 a map)
    "namaqualand_cliff_01": 0.15,    # 94k
    "namaqualand_boulder_02": 0.12,  # 98k
    "namaqualand_boulder_03": 0.15,  # 65k
    "namaqualand_boulder_04": 0.15,  # 59k
    "namaqualand_boulder_05": 0.12,  # 90k
    "namaqualand_boulders_01": 0.2,
    "dead_tree_trunk": 0.1,          # 100k
    "dead_tree_trunk_02": 0.1,       # 83k
    "tree_stump_01": 0.15,           # 41k
    "mountainside": 0.25,            # 153k, a backdrop
    "rock_moss_set_01": 0.3,
    "rock_moss_set_02": 0.3,
    "rock_face_01": 0.4,
    "old_military_crate": 0.25,      # 12.5k, many per kit
    "wooden_military_crate": 0.3,
    "binoculars": 0.2,
    "covered_car": 0.3,
    "exterior_aircon_unit": 0.3,
    "lateral_sea_marker": 0.3,
    "portable_searchlight": 0.25,
    "shrub_02": 0.4,
}


def lighten(folder, ratio):
    sources = glob.glob(os.path.join(folder, "*.gltf"))
    if len(sources) != 1:
        return f"skipped ({len(sources)} .gltf files)"
    src = sources[0]
    out = os.path.splitext(src)[0] + ".glb"
    tmp = out + ".tmp.glb"
    npx = shutil.which("npx") or "npx"
    try:
        subprocess.run([npx, "--yes", "gltfpack@0.25.0", "-i", src, "-o", tmp, "-si", str(ratio), "-noq", "-kn", "-km"],
                       check=True, stdout=subprocess.DEVNULL)
    except Exception:
        if os.path.exists(tmp):
            os.remove(tmp)
        raise
    # Only the simplified model stays: the originals would also be imported and shipped.
    for f in os.listdir(folder):
        p = os.path.join(folder, f)
        if p == tmp:
            continue
        shutil.rmtree(p) if os.path.isdir(p) else os.remove(p)
    os.replace(tmp, out)
    return f"{ratio:.0%} kept, {os.path.getsize(out) / 1e6:.1f} MB"


if __name__ == "__main__":
    root = sys.argv[1]
    for mid, ratio in RATIOS.items():
        folder = os.path.join(root, mid)
        if not os.path.isdir(folder):
            continue
        try:
            print(f"  {mid}: {lighten(folder, ratio)}")
        except Exception as e:
            print(f"  {mid}: left as it is ({e})")

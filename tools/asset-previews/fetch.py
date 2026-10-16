"""Download candidate asset preview images listed in a JSON map {key: url} and save them as
640 px wide JPEGs named <key>.jpg, plus report.json with each key's status. A URL that returns
an HTML page is followed to its og:image. A value "sketchfab:<uid>" is looked up through the
Sketchfab API (SKETCHFAB_API_TOKEN), and its licence, face count and downloadability are reported."""
import html, io, json, os, re, sys, urllib.parse, urllib.request
from PIL import Image

src, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
urls = json.load(open(src))
report = {}
meta = {}


def get(url, headers=None):
    req = urllib.request.Request(url, headers=headers or {
        "User-Agent": "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36",
        "Accept": "text/html,image/avif,image/webp,image/*,*/*"})
    return urllib.request.urlopen(req, timeout=30).read()


def sketchfab(uid):
    token = os.environ.get("SKETCHFAB_API_TOKEN", "")
    m = json.loads(get(f"https://api.sketchfab.com/v3/models/{uid}",
                       {"Authorization": f"Token {token}"} if token else {"User-Agent": "asset-previews"}))
    info = {"name": m.get("name"), "licence": (m.get("license") or {}).get("label"),
            "faces": m.get("faceCount"), "downloadable": m.get("isDownloadable"),
            "animations": m.get("animationCount"), "author": (m.get("user") or {}).get("displayName")}
    best = max(m["thumbnails"]["images"], key=lambda i: (i["width"] <= 1100, i["width"]))
    return best["url"], info


for key, url in urls.items():
    try:
        if url.startswith("sketchfab:"):
            url, info = sketchfab(url.split(":", 1)[1])
            meta[key] = info
        data = get(url)
        if data[:200].lstrip().lower().startswith((b"<!doctype", b"<html")):
            m = re.search(rb'property=["\']og:image["\'][^>]*content=["\']([^"\']+)', data) or \
                re.search(rb'content=["\']([^"\']+)["\'][^>]*property=["\']og:image', data)
            if not m:
                raise ValueError("page has no og:image")
            data = get(urllib.parse.urljoin(url, html.unescape(m.group(1).decode())))
        img = Image.open(io.BytesIO(data)).convert("RGB")
        if img.width > 640:
            img = img.resize((640, round(img.height * 640 / img.width)), Image.LANCZOS)
        img.save(os.path.join(out, key + ".jpg"), quality=82)
        report[key] = "ok"
    except Exception as e:  # keep going; the report says which failed
        report[key] = f"error: {e}"
    print(key, report[key])
json.dump({"status": report, "sketchfab": meta}, open(os.path.join(out, os.path.basename(src).replace(".json", "-report.json")), "w"), indent=1)


# Optional Sketchfab searches (searches.json next to the URL list): downloadable CC-BY / CC0 hits
# with thumbnails saved as sf-<uid>.jpg, for picking by eye.
searches_path = os.path.join(os.path.dirname(src), "searches.json")
if os.path.exists(searches_path) and os.environ.get("SKETCHFAB_API_TOKEN"):
    import urllib.parse
    auth = {"Authorization": "Token " + os.environ["SKETCHFAB_API_TOKEN"]}
    found = {}
    for s in json.load(open(searches_path)):
        params = {"type": "models", "q": s["q"], "downloadable": "true", "count": 24,
                  "sort_by": "-likeCount", "max_face_count": s.get("max_faces", 150000)}
        if s.get("rigged"):
            params["rigged"] = "true"
        if s.get("animated"):
            params["animated"] = "true"
        try:
            results = json.loads(get("https://api.sketchfab.com/v3/search?" + urllib.parse.urlencode(params), auth))["results"]
        except Exception as e:
            print("search", s["q"], e)
            continue
        kept = 0
        for m in results:
            uid = m["uid"]
            if uid in found or kept >= s.get("keep", 8):
                continue
            try:
                full = json.loads(get(f"https://api.sketchfab.com/v3/models/{uid}", auth))
                lic = (full.get("license") or {}).get("slug") or ""
                if lic not in ("by", "cc0"):
                    continue
                imgs = full["thumbnails"]["images"]
                best = max(imgs, key=lambda i: (i["width"] <= 1100, i["width"]))
                img = Image.open(io.BytesIO(get(best["url"]))).convert("RGB")
                if img.width > 640:
                    img = img.resize((640, round(img.height * 640 / img.width)), Image.LANCZOS)
                img.save(os.path.join(out, f"sf-{uid}.jpg"), quality=82)
            except Exception as e:
                print(uid, e)
                continue
            kept += 1
            found[uid] = {"label": s["label"], "query": s["q"], "name": full.get("name"), "licence": lic,
                          "faces": full.get("faceCount"), "animations": full.get("animationCount"),
                          "author": (full.get("user") or {}).get("displayName"), "likes": full.get("likeCount"),
                          "url": full.get("viewerUrl")}
    json.dump(found, open(os.path.join(out, "search.json"), "w"), indent=1)
    print("search hits", len(found))

"""Snapshot althamin.ae: SEO fields + cleaned body HTML for every sitemap URL, and download
every /wp-content/uploads/ image the content uses so it can be served at the same path."""
import json, os, re, sys, time
from urllib.parse import urljoin, urlparse
import requests
from bs4 import BeautifulSoup, Comment

OUT_JSON, UPLOAD_ROOT = sys.argv[1], sys.argv[2]
UA = {"User-Agent": "Mozilla/5.0 (AlThamin migration snapshot)"}
SITE = "https://althamin.ae"
SITEMAPS = ["page-sitemap.xml", "post-sitemap.xml"]
KEEP = {"h2", "h3", "h4", "p", "ul", "ol", "li", "a", "strong", "b", "em", "i", "img", "table",
        "thead", "tbody", "tr", "th", "td", "br", "blockquote", "figure", "figcaption"}

def get(url):
    for _ in range(3):
        try:
            r = requests.get(url, headers=UA, timeout=40)
            if r.ok:
                return r
        except requests.RequestException:
            time.sleep(2)
    raise RuntimeError(url)

def sitemap_urls():
    urls = []
    for sm in SITEMAPS:
        urls += re.findall(r"<loc>([^<]+)</loc>", get(f"{SITE}/{sm}").text)
    return urls

images = set()

def local_src(src):
    """Rewrite absolute upload URLs to root-relative ones and remember them for download."""
    if not src:
        return src
    u = urlparse(urljoin(SITE, src))
    if u.netloc.endswith("althamin.ae") and u.path.startswith("/wp-content/uploads/"):
        images.add(u.path)
        return u.path
    return src

def local_href(href):
    if not href:
        return href
    u = urlparse(urljoin(SITE, href))
    if u.netloc.endswith("althamin.ae"):
        if u.path.startswith("/wp-content/uploads/"):
            images.add(u.path)
        return u.path + (f"?{u.query}" if u.query else "") + (f"#{u.fragment}" if u.fragment else "")
    return href

def clean(nodes):
    """Reduce WordPress/Elementor markup to plain semantic HTML."""
    html = "".join(str(n) for n in nodes)
    soup = BeautifulSoup(html, "html.parser")
    for t in soup(["script", "style", "noscript", "svg", "form", "iframe", "button", "input"]):
        t.decompose()
    for c in soup.find_all(string=lambda s: isinstance(s, Comment)):
        c.extract()
    for el in soup.find_all(True):
        if el.name == "h1":
            el.name = "h2"
        if el.name in ("h5", "h6"):
            el.name = "h4"
        if el.name not in KEEP:
            el.unwrap()
            continue
        attrs = {}
        if el.name == "a":
            attrs["href"] = local_href(el.get("href"))
            if el.get("target") == "_blank":
                attrs["target"] = "_blank"
                attrs["rel"] = "noopener"
        if el.name == "img":
            src = el.get("data-src") or el.get("src")
            if not src or src.startswith("data:"):
                el.decompose()
                continue
            attrs["src"] = local_src(src)
            attrs["alt"] = el.get("alt", "")
            if el.get("width"): attrs["width"] = el["width"]
            if el.get("height"): attrs["height"] = el["height"]
            attrs["loading"] = "lazy"
        el.attrs = attrs
    # drop empty paragraphs / list items, and <p> duplicated inside <li> (Elementor icon lists)
    for p in soup.find_all("p"):
        if p.parent and p.parent.name == "li":
            p.unwrap()
    for el in soup.find_all(["p", "li", "h2", "h3", "h4"]):
        if not el.get_text(strip=True) and not el.find("img"):
            el.decompose()
    out = str(soup)
    out = re.sub(r"\n\s*\n+", "\n", out).strip()
    return out

def outline_main(s):
    """Return the content nodes between the site header and footer."""
    ec = s.select_one(".entry-content")
    if ec and len(ec.get_text(strip=True)) > 200:
        return ec
    body = s.body
    for t in body.select("header, footer, .site-header, .site-footer, [data-elementor-type=header], [data-elementor-type=footer]"):
        t.decompose()
    return body.select_one("[data-elementor-type=single-post], [data-elementor-type=single-page], [data-elementor-type=wp-post], main, #content") or body

def breadcrumbs_text(node):
    for p in node.find_all("p"):
        t = p.get_text(" ", strip=True)
        if t.startswith("Home /") or t.startswith("Home »"):
            p.decompose()

def faq(s):
    """Collect Yoast/Elementor FAQ pairs if the page has FAQPage JSON-LD."""
    items = []
    for sc in s.find_all("script", type="application/ld+json"):
        try:
            data = json.loads(sc.string or "")
        except Exception:
            continue
        graph = data.get("@graph", [data]) if isinstance(data, dict) else data
        for g in graph if isinstance(graph, list) else []:
            if isinstance(g, dict) and g.get("@type") == "FAQPage":
                for q in g.get("mainEntity", []):
                    a = q.get("acceptedAnswer", {}).get("text", "")
                    items.append({"q": q.get("name", ""), "a": BeautifulSoup(a, "html.parser").get_text(" ", strip=True)})
    return items

def meta(s, **kw):
    m = s.find("meta", attrs=kw)
    return m.get("content", "").strip() if m else ""

pages = []
for url in sitemap_urls():
    r = get(url)
    r.encoding = "utf-8"
    s = BeautifulSoup(r.text, "html.parser")
    title = (s.title.string or "").strip() if s.title else ""
    desc = meta(s, name="description")
    og_image = meta(s, property="og:image")
    published = meta(s, property="article:published_time")
    h1 = s.find("h1")
    h1 = h1.get_text(" ", strip=True) if h1 else ""
    tags = [a.get_text(strip=True) for a in s.select("a[rel~=tag]")]
    faqs = faq(s)
    main = outline_main(s)
    if main.find("h1"):
        main.find("h1").decompose()
    breadcrumbs_text(main)
    body = clean([main])
    path = urlparse(url).path
    pages.append({
        "path": path, "title": title, "metaDescription": desc, "h1": h1, "ogImage": local_src(og_image) if og_image else "",
        "published": published, "tags": tags, "faq": faqs, "body": body,
    })
    print(f"{path}  h1={h1[:50]!r}  body={len(body)}  faq={len(faqs)}", flush=True)

with open(OUT_JSON, "w", encoding="utf-8") as f:
    json.dump(pages, f, ensure_ascii=False, indent=1)

ok = 0
for p in sorted(images):
    dest = os.path.join(UPLOAD_ROOT, p.lstrip("/").replace("/", os.sep))
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    if not os.path.exists(dest):
        try:
            with open(dest, "wb") as f:
                f.write(get(SITE + p).content)
            ok += 1
        except RuntimeError:
            print("image failed:", p)
print(f"pages={len(pages)} images={len(images)} downloaded={ok}")

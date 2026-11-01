"""Turn the althamin.ae snapshot into the seed file the nopCommerce importer reads."""
import json, re, sys
from bs4 import BeautifulSoup

SNAP, OUT = sys.argv[1], sys.argv[2]
pages = {p["path"]: p for p in json.load(open(SNAP, encoding="utf-8"))}
LEGACY = set(pages)

def meta_title(path):
    """Old <title> minus the store suffix; nopCommerce appends ' - Al Thamin Technologies' again."""
    return re.sub(r"\s+-\s+Al Thamin Technologies$", "", pages[path]["title"]).strip()

def fix_links(html, drop_first_img=None, article=False):
    soup = BeautifulSoup(html, "html.parser")
    for a in soup.find_all("a"):
        href = (a.get("href") or "").strip()
        if not href:
            a.unwrap()
            continue
        if href.startswith("/cdn-cgi/l/email-protection"):
            a["href"] = "mailto:info@althamin.ae"
            a.string = "info@althamin.ae"
            continue
        path = href.split("#")[0].split("?")[0]
        if path.startswith("/") and not path.endswith("/") and "." not in path.rsplit("/", 1)[-1]:
            if path + "/" in LEGACY:
                a["href"] = href.replace(path, path + "/", 1)
    if drop_first_img:
        first = soup.find("img")
        if first is not None and first.get("src") == drop_first_img:
            first.decompose()
    out = str(soup).strip()
    return enhance(re.sub(r"(<br/>\s*){2,}", "<br/>", out), article)

def enhance(html, article=False):
    """Blog posts (article=True) keep their article flow: no lead/overline rewrites, no card grids.
    Give the flat WordPress markup structure the theme can style (wording is unchanged):
    eyebrows / lead paragraphs, check-lists, feature-card grids, CTA buttons and an accordion FAQ."""
    soup = BeautifulSoup(html, "html.parser")
    # WordPress emoji images and Cloudflare-mangled e-mail addresses
    for img in soup.find_all("img", src=re.compile(r"s\.w\.org/images/core/emoji")):
        img.replace_with(img.get("alt", ""))
    for text in soup.find_all(string=re.compile(r"\[email(\s|\xa0)protected\]")):
        mail = soup.new_tag("a", href="mailto:info@althamin.ae")
        mail.string = "info@althamin.ae"
        before, _, after = re.split(r"(\[email(?:\s|\xa0)protected\])", str(text), maxsplit=1)
        text.replace_with(before, mail, after)

    # h4 used as eyebrow ("WELCOME TO ALTHAMIN") or as an intro sentence → p.at-overline / p.at-lead
    seen_h2 = article  # articles: leave h4 headings alone
    for el in soup.find_all(["h2", "h4"]):
        if el.name == "h2":
            seen_h2 = True
            continue
        text = el.get_text(" ", strip=True)
        if text.isupper() and len(text) < 40:
            el.name, el["class"] = "p", "at-overline"
        elif not seen_h2 or len(text) > 80:
            el.name, el["class"] = "p", "at-lead"

    # bare "Contact us" links → buttons
    for a in soup.find_all("a", href="/contact/"):
        if a.parent is soup and a.get_text(strip=True).lower() == "contact us":
            a.string = "Contact us"
            a["class"] = "at-btn at-btn--primary"
            a.wrap(soup.new_tag("p", attrs={"class": "at-cta-line"}))

    # lists whose items all start with a bold label → check-list cards
    for ul in soup.find_all("ul"):
        items = ul.find_all("li", recursive=False)
        if len(items) >= 3 and all(li.find(["strong", "b"]) and li.get_text(strip=True).startswith(li.find(["strong", "b"]).get_text(strip=True)[:6]) for li in items):
            ul["class"] = "at-checklist"

    # plain lists of short items → compact tick list
    for ul in soup.find_all("ul"):
        items = ul.find_all("li", recursive=False)
        # items that start with their own emoji/symbol (✅ ❌ 📊) keep it instead of a tick
        starts_with_symbol = any(li.get_text(strip=True)[:1] and not li.get_text(strip=True)[0].isalnum() for li in items)
        if not ul.get("class") and len(items) >= 3 and not starts_with_symbol and all(len(li.get_text(strip=True)) <= 90 for li in items):
            ul["class"] = "at-ticklist"
        elif not ul.get("class") and starts_with_symbol:
            ul["class"] = "at-emojilist"

    # leftovers of the WordPress page header: breadcrumb text, "SERVICES" label, intro sentence, loose buttons
    if article:
        first = next((n for n in soup.children if not (isinstance(n, str) and not n.strip())), None)
        if getattr(first, "name", None) == "p" and not first.get("class"):
            first["class"] = "at-lead"
    def top_nodes():
        return [n for n in soup.children if not (isinstance(n, str) and not n.strip())]
    first_h2 = soup.find("h2")
    for node in ([] if article else top_nodes()):
        if node is first_h2:
            break
        if isinstance(node, str) or node.name in ("strong", "b") or (node.name == "a" and node.get("href") in ("/", "/service/")):
            node.extract()  # "Home / Service / …" breadcrumb text
    for node in ([] if article else top_nodes()):
        if getattr(node, "name", None) == "h2":
            break
        if node.name == "p" and not node.get("class"):
            text = node.get_text(" ", strip=True)
            node["class"] = "at-overline" if text.isupper() and len(text) < 40 else "at-lead"
    for node in top_nodes():
        if getattr(node, "name", None) == "a":
            node["class"] = "at-btn at-btn--primary"
            node.string = node.get_text(" ", strip=True)
            node.wrap(soup.new_tag("p", attrs={"class": "at-cta-line"}))

    # 3+ consecutive "<p><strong>Label</strong>: text</p>" (no line break) → check-list
    def is_label_p(node):
        if getattr(node, "name", None) != "p" or node.get("class") or node.find("br") or node.find("img"):
            return False
        first = next((c for c in node.children if not (isinstance(c, str) and not c.strip())), None)
        return getattr(first, "name", None) in ("strong", "b") and len(node.get_text(strip=True)) > len(first.get_text(strip=True)) + 3
    run = []
    def flush_labels():
        if len(run) >= 3:
            ul = soup.new_tag("ul", attrs={"class": "at-checklist"})
            run[0].insert_before(ul)
            for p in run:
                li = soup.new_tag("li")
                for child in list(p.children):
                    li.append(child.extract())
                ul.append(li)
                p.decompose()
        run.clear()
    for node in top_nodes():
        if is_label_p(node):
            run.append(node)
        else:
            flush_labels()
    flush_labels()

    # runs of <figure> → image gallery
    run = []
    def flush_figures():
        if len(run) >= 2:
            grid = soup.new_tag("div", attrs={"class": "at-gallery-grid"})
            run[0].insert_before(grid)
            for fig in run:
                grid.append(fig.extract())
        run.clear()
    for node in top_nodes():
        if node.name == "figure":
            run.append(node)
        else:
            flush_figures()
    flush_figures()

    # card runs: 3+ repeated blocks → feature cards (not in articles)
    #   "pbr":  [image] <p><strong>Title</strong><br/>text</p>
    #   "h3"/"h4": [image] <h3>Title</h3> <p>…</p> (+ more <p>/<ul>)
    def is_image(node):
        if node.name == "img":
            return True
        return node.name == "p" and node.find("img") is not None and not node.get_text(strip=True)
    def is_pbr(node):
        if node.name != "p" or node.get("class") or not node.find("br"):
            return False
        first = next((c for c in node.children if not (isinstance(c, str) and not c.strip())), None)
        return getattr(first, "name", None) in ("strong", "b") or (getattr(first, "name", None) == "a" and first.find(["strong", "b"]))
    def parse_card(nodes, i):
        start, image = i, None
        if i < len(nodes) and is_image(nodes[i]):
            image, i = nodes[i], i + 1
        if i >= len(nodes):
            return None
        node = nodes[i]
        if is_pbr(node):
            return {"kind": "pbr", "image": image, "head": node, "body": [], "end": i + 1}
        if node.name in ("h3", "h4"):
            body, j = [], i + 1
            while j < len(nodes) and nodes[j].name in ("p", "ul", "ol") and not is_image(nodes[j]) and not is_pbr(nodes[j]) and not nodes[j].get("class") in (["at-cta-line"], "at-cta-line"):
                body.append(nodes[j])
                j += 1
            if body:
                return {"kind": node.name, "image": image, "head": node, "body": body, "end": j}
        return None
    def build_card(card):
        img = None
        if card["image"]:
            img = card["image"] if card["image"].name == "img" else card["image"].find("img")
        # small pictures (≤ 320px wide) are icons, larger ones are photos
        is_icon = img is not None and img.get("width", "").isdigit() and int(img["width"]) <= 320 and img.get("src", "").lower().endswith(".png")
        el = soup.new_tag("div", attrs={"class": "at-feature" + (" at-feature--icon" if is_icon else " at-feature--media" if img else "")})
        if card["image"]:
            el.append(img.extract())
            if card["image"].name == "p":
                card["image"].decompose()
        h3 = soup.new_tag("h3")
        if card["kind"] == "pbr":
            # title = everything before the first <br/>, text = the rest
            html_in = card["head"].decode_contents()
            title_html, _, text_html = html_in.partition("<br/>")
            h3.append(BeautifulSoup(title_html, "html.parser"))
            el.append(h3)
            text = soup.new_tag("p")
            text.append(BeautifulSoup(text_html, "html.parser"))
            el.append(text)
            card["head"].decompose()
        else:
            for child in list(card["head"].children):
                h3.append(child.extract())
            card["head"].decompose()
            el.append(h3)
            for b in card["body"]:
                el.append(b.extract())
        return el
    i = 0
    while not article:
        nodes = [n for n in top_nodes() if not isinstance(n, str)]
        if i >= len(nodes):
            break
        first = parse_card(nodes, i)
        if first is None:
            i += 1
            continue
        cards, j = [first], first["end"]
        while j < len(nodes):
            nxt = parse_card(nodes, j)
            if nxt is None or nxt["kind"] != first["kind"]:
                break
            cards.append(nxt)
            j = nxt["end"]
        if len(cards) < 3:
            i += 1
            continue
        # drop the pictures when one image is reused across cards (WordPress placeholders)
        def card_src(card):
            if not card["image"]:
                return None
            img = card["image"] if card["image"].name == "img" else card["image"].find("img")
            return img.get("src") if img is not None else None
        srcs = [src for src in (card_src(card) for card in cards) if src]
        if len(set(srcs)) < len(srcs):
            for card in cards:
                if card["image"]:
                    card["image"].decompose()
                    card["image"] = None
        grid = soup.new_tag("div", attrs={"class": "at-feature-grid"})
        (cards[0]["image"] or cards[0]["head"]).insert_before(grid)
        for card in cards:
            grid.append(build_card(card))
        i = 0  # restart: the node list changed

    # closing "Ready to …?" heading and everything after it in that section → CTA panel
    for h3 in soup.find_all("h3"):
        if not h3.get_text(strip=True).lower().startswith("ready to"):
            continue
        panel = soup.new_tag("div", attrs={"class": "at-content-cta"})
        h3.insert_before(panel)
        node = h3
        while node is not None and getattr(node, "name", None) != "h2":
            nxt = node.next_sibling
            panel.append(node.extract())
            node = nxt

    # FAQ: after an <h2> containing "FAQ", bare text = question, following blocks = answer
    faq_h2 = next((h for h in soup.find_all("h2") if "faq" in h.get_text().lower()), None)
    if faq_h2 is not None:
        wrap = soup.new_tag("div", attrs={"class": "at-faq-list"})
        current = None
        for node in list(faq_h2.next_siblings):
            if getattr(node, "name", None) == "h2":
                break
            if isinstance(node, str):
                question = node.strip()
                node.extract()
                if question:
                    current = soup.new_tag("details")
                    summary = soup.new_tag("summary")
                    summary.string = question
                    current.append(summary)
                    current.append(soup.new_tag("div", attrs={"class": "at-faq-answer"}))
                    wrap.append(current)
                continue
            if current is not None:
                answer = node.extract()
                if answer.name == "p" and answer.get_text(strip=True).startswith("A: "):
                    answer.contents[0].replace_with(str(answer.contents[0]).replace("A: ", "", 1))
                current.find("div").append(answer)
        if len(wrap.contents):
            faq_h2.insert_after(wrap)
            faq_h2.string = "Frequently asked questions"

    out = re.sub(r"\n\s*\n+", "\n", str(soup)).strip()
    return out


# meta descriptions for pages that had none on the old site (or only an auto-generated tag line)
META = {
    "/service/": "Digital signage services in Dubai, Sharjah & across the UAE: LED walls, kiosks, menu boards, lift and hotel screens — supplied, installed and maintained.",
    "/service/hotel-digital-signage-solutions-in-uae/": "Hotel digital signage in the UAE: lobby welcome screens, wayfinding, digital menus, event boards and LED walls for hotels and resorts in Dubai, Abu Dhabi & Sharjah.",
    "/meeting-room-disply-in-uae/": "Meeting room displays in the UAE that show live room status and bookings, synced with Microsoft 365 / Outlook. Supplied and installed in Dubai and Sharjah.",
    "/service/led-wall-solutions/outdoor-flexible-led-screen/": "Outdoor flexible LED screens for curved façades, pillars and creative shopfronts in Dubai and the UAE — designed, supplied and installed by Al Thamin.",
    "/service/led-wall-solutions/curved-led-display-supplier-uae/": "Indoor flexible and curved LED displays for showrooms, retail and experience centres in Dubai and the UAE. Supply, installation and support by Al Thamin.",
    "/service/led-wall-solutions/indoor-rental-led-screen/": "Indoor LED screen rental in Dubai for conferences, launches, weddings and exhibitions — delivered, rigged and operated on site by the Al Thamin team.",
    "/service/led-wall-solutions/outdoor-rental-led-screen/": "Outdoor LED screen rental in Dubai and the UAE for concerts, sports, festivals and roadshows. Bright, weather-resistant screens with on-site technicians.",
    "/service/led-wall-solutions/signage-led-screen/": "LED signage screens for shopfronts, pharmacies, restaurants and showrooms in Dubai. Bright day and night, updated in seconds with Screendrive software.",
    "/service/led-wall-solutions/home-theatre-led-screen/": "Home theatre LED screens for villas and private cinemas in Dubai and the UAE — cinema-size, bright picture, installed and calibrated by Al Thamin.",
    "/service/digital-kiosks-uae/freestanding-digital-advertising-display/": "Freestanding digital advertising displays for malls, retail, hotels and lobbies in Dubai. Eye-catching indoor kiosk screens supplied and installed by Al Thamin.",
    "/blog/5-ways-digital-signage-is-transforming-uae-hotels-in-2025/": "How UAE hotels use digital signage in 2025: welcome screens, wayfinding, digital menus, event boards and in-room information for a better guest experience.",
}

def page(path, **kw):
    p = pages[path]
    picture = kw.pop("picture", None) or p["ogImage"] or ""
    body = kw.pop("body", None)
    if body is None:
        body = fix_links(p["body"], drop_first_img=picture or None)
    else:
        body = enhance(body)
    return {"legacyPath": path, "metaTitle": meta_title(path), "metaDescription": p["metaDescription"] or META.get(path, ""),
            "picture": picture, "body": body, **kw}

STARTER = {
    "home-theatre-led-screen": """<h2>Home Theatre LED Screens for Villas in Dubai &amp; the UAE</h2>
<p>A home theatre LED screen gives you a true cinema-size picture with no projector, no screen glare and no lamp to replace. Al Thamin designs, supplies and installs fine-pitch LED walls for private cinemas, majlis and living rooms across Dubai, Sharjah and the UAE.</p>
<h2>Why LED for your home cinema</h2>
<ul><li>Bright, high-contrast picture even with the lights on</li><li>Seamless large formats sized to your wall</li><li>Slim cabinets that mount flush to the wall</li><li>Installation, calibration and maintenance by our own team</li></ul>
<p>Tell us your room size and seating distance and we will recommend the right pixel pitch. <a href="/contact/">Contact Al Thamin</a> for a site visit.</p>""",
    "indoor-rental-led-screen": """<h2>Indoor LED Screen Rental in Dubai</h2>
<p>Rent an indoor LED screen for conferences, product launches, weddings, exhibitions and corporate events in Dubai, Sharjah and across the UAE. Al Thamin delivers, rigs, operates and dismantles the screen so you can focus on your event.</p>
<h2>What is included</h2>
<ul><li>LED panels sized to your stage or booth</li><li>Delivery, installation and dismantling</li><li>On-site technician during the event</li><li>Video processing and content playback</li></ul>
<p>Share your event date, venue and screen size and we will send a rental quote. <a href="/contact/">Contact us</a>.</p>""",
    "outdoor-rental-led-screen": """<h2>Outdoor LED Screen Rental in Dubai &amp; the UAE</h2>
<p>Outdoor rental LED screens for concerts, sports events, festivals, exhibitions and roadshows. Weather-resistant, high-brightness panels that stay readable in direct sunlight, supplied and operated by the Al Thamin team.</p>
<h2>Built for outdoor events</h2>
<ul><li>High-brightness, weather-resistant LED cabinets</li><li>Ground-stack or truss-mounted structures</li><li>Live camera feeds, sponsor loops and scoreboards</li><li>Technician on site for the whole event</li></ul>
<p>Planning an outdoor event? <a href="/contact/">Request a rental quote</a>.</p>""",
    "outdoor-flexible-led-screen": """<h2>Outdoor Flexible LED Screens in the UAE</h2>
<p>Flexible LED modules bend around columns, curved façades and unusual shapes, turning architecture into a display surface. Al Thamin supplies and installs outdoor flexible LED screens for retail, hospitality and landmark projects across Dubai, Sharjah and the UAE.</p>
<h2>Where flexible LED works best</h2>
<ul><li>Curved building façades and canopies</li><li>Round pillars and columns</li><li>Creative shopfronts and brand installations</li><li>Event and exhibition structures</li></ul>
<p>Send us your drawings or photos and we will propose a layout. <a href="/contact/">Contact Al Thamin</a>.</p>""",
    "signage-led-screen": """<h2>LED Signage Screens for Shops &amp; Businesses in Dubai</h2>
<p>Replace printed signs with bright LED signage screens that change with your offers. Ideal for shopfronts, pharmacies, restaurants and showrooms in Dubai, Sharjah and across the UAE, with content managed from our Screendrive software.</p>
<h2>Benefits of LED signage</h2>
<ul><li>Visible day and night</li><li>Update offers and prices in seconds</li><li>Indoor and outdoor versions</li><li>Supply, installation and maintenance from one team</li></ul>
<p><a href="/contact/">Get a quote</a> for your LED signage screen.</p>""",
}
MEETING_ROOM_BODY = """<h2>Meeting Room Displays in the UAE</h2>
<p>Meeting room displays show the room’s schedule at the door, let teams book rooms on the spot and stop double bookings. Al Thamin supplies and installs room booking displays and in-room screens for offices across Dubai, Sharjah and the UAE, with Microsoft Teams and Microsoft 365 integration.</p>
<h2>What our meeting room displays do</h2>
<ul><li>Show live room status: free, busy or starting soon</li><li>Book or extend a meeting from the panel</li><li>Sync with Microsoft 365 / Outlook calendars</li><li>Display company news between meetings</li></ul>
<p>See also our <a href="/service/office-communication-display-screens-in-dubai-uae/">office communication display screens</a>. <a href="/contact/">Contact us</a> for a demo.</p>"""

def homepage_text():
    soup = BeautifulSoup(pages["/"]["body"], "html.parser")
    keep = []
    for h in soup.find_all("h2"):
        title = h.get_text(" ", strip=True)
        if title.lower().startswith("our services"):
            break
        p = h.find_next_sibling("p")
        keep.append(f"<h2>{title.replace('Maintanance', 'Maintenance')}</h2>")
        if p:
            keep.append(f"<p>{p.get_text(' ', strip=True)}</p>")
    return fix_links("\n".join(keep))

L, K = "/service/led-wall-solutions/", "/service/digital-kiosks-uae/"
seed = {
    "categories": [
        page("/service/", slug="service", parent=None, name="Services", order=0, showOnHome=False),
        page(L, slug="led-wall-solutions", parent="service", name="LED Wall Solutions in Dubai", order=1, showOnHome=True),
        page(K, slug="digital-kiosks-uae", parent="service", name="Digital Signage Kiosks in Dubai", order=2, showOnHome=True),
        page("/service/digital-menu-solutions-dubai/", slug="digital-menu-solutions-dubai", parent="service", name="Digital Menu Solutions in Dubai", order=3, showOnHome=True),
        page("/service/hotel-digital-signage-solutions-in-uae/", picture="/wp-content/uploads/2025/12/Arjaan-rotana-1.jpg", slug="hotel-digital-signage-solutions-in-uae", parent="service", name="Hotel Digital Signage Solutions in UAE", order=4, showOnHome=True),
        page("/service/interactive-whiteboard-touch-board-supplier-in-dubai-uae/", slug="interactive-whiteboard-touch-board-supplier-in-dubai-uae", parent="service", name="Interactive Whiteboard & Touch Board Supplier in Dubai", order=5, showOnHome=True),
        page("/service/elevator-lobby-screens-installation-in-uae/", slug="elevator-lobby-screens-installation-in-uae", parent="service", name="Lift, Elevator & Lobby Screens in UAE", order=6, showOnHome=True),
        page("/service/campus-digital-signage-solutions-in-dubai/", slug="campus-digital-signage-solutions-in-dubai", parent="service", name="Campus Digital Signage Solutions in Dubai", order=7, showOnHome=True),
        page("/service/office-communication-display-screens-in-dubai-uae/", slug="office-communication-display-screens-in-dubai-uae", parent="service", name="Office Communication Display Screens in Dubai", order=8, showOnHome=True),
    ],
    "products": [
        page(L + "indoor-led-display-supplier-uae/", slug="indoor-led-display-supplier-uae", category="led-wall-solutions", name="Indoor Fixed LED Screen", shortDescription="MALLS · LOBBIES · STUDIOS", order=1, showOnHome=True),
        page(L + "outdoor-fixed-led-screen-in-dubai/", slug="outdoor-fixed-led-screen-in-dubai", category="led-wall-solutions", name="Outdoor LED Screen in Dubai", shortDescription="FAÇADES · BILLBOARDS", order=2, showOnHome=True),
        page(L + "outdoor-flexible-led-screen/", slug="outdoor-flexible-led-screen", category="led-wall-solutions", name="Outdoor Flexible LED Screen", shortDescription="CURVED FAÇADES · PILLARS", order=3, showOnHome=True, body=STARTER["outdoor-flexible-led-screen"]),
        page(L + "curved-led-display-supplier-uae/", slug="curved-led-display-supplier-uae", category="led-wall-solutions", name="Indoor Flexible & Curved LED Display", shortDescription="SHOWROOMS · EXPERIENCE CENTRES", order=4, showOnHome=True),
        page(L + "indoor-rental-led-screen/", slug="indoor-rental-led-screen", category="led-wall-solutions", name="Indoor Rental LED Screen", shortDescription="EVENTS · CONFERENCES", order=5, showOnHome=True, body=STARTER["indoor-rental-led-screen"]),
        page(L + "outdoor-rental-led-screen/", slug="outdoor-rental-led-screen", category="led-wall-solutions", name="Outdoor Rental LED Screen", shortDescription="CONCERTS · SPORTS · EXPOS", order=6, showOnHome=True, body=STARTER["outdoor-rental-led-screen"]),
        page(L + "signage-led-screen/", slug="signage-led-screen", category="led-wall-solutions", name="Signage LED Screen", shortDescription="SHOPFRONTS · PHARMACIES", order=7, showOnHome=True, body=STARTER["signage-led-screen"]),
        page(L + "menu-display-led-screen/", slug="menu-display-led-screen", category="led-wall-solutions", name="LED Menu Display Screen", shortDescription="RESTAURANTS · CAFÉS", order=8, showOnHome=True),
        page(L + "home-theatre-led-screen/", slug="home-theatre-led-screen", category="led-wall-solutions", name="Home Theatre LED Screen", shortDescription="VILLAS · PRIVATE CINEMAS", order=9, showOnHome=True, body=STARTER["home-theatre-led-screen"]),
        page(K + "freestanding-digital-advertising-display/", slug="freestanding-digital-advertising-display", category="digital-kiosks-uae", name="Freestanding Digital Advertising Display", shortDescription="RETAIL · LOBBIES · EVENTS", order=1, showOnHome=False),
        page(K + "led-poster-display-dubai/", slug="led-poster-display-dubai", category="digital-kiosks-uae", name="LED Poster Display in Dubai", shortDescription="MALLS · SHOPFRONTS", order=2, showOnHome=False),
    ],
    "topics": [
        page("/about/", systemName="AboutUs", slug="about", title="About Al Thamin Technologies", includeInSitemap=True),
        page("/digital-signage-software-in-dubai/", systemName="DigitalSignageSoftware", slug="digital-signage-software-in-dubai", title="Digital Signage Software in Dubai", includeInSitemap=True),
        page("/gym-advertising-and-promotion-screens-in-dubai-uae/", systemName="GymPromotionScreens", slug="gym-advertising-and-promotion-screens-in-dubai-uae", title="Gym Advertising and Promotion Screens in Dubai, UAE", includeInSitemap=True),
        page("/meeting-room-disply-in-uae/", systemName="MeetingRoomDisplay", slug="meeting-room-disply-in-uae", title="Meeting Room Displays in UAE", includeInSitemap=True, body=MEETING_ROOM_BODY),
        {"legacyPath": None, "systemName": "HomepageText", "slug": None, "title": "About Al Thamin", "metaTitle": "", "metaDescription": "", "picture": "", "includeInSitemap": False, "body": homepage_text()},
        {"legacyPath": None, "systemName": "ContactUs", "slug": None, "title": "Contact us", "metaTitle": meta_title("/contact/"), "metaDescription": "", "picture": "", "includeInSitemap": False,
         "body": "<p>Have a question or need a custom digital signage solution? Our team is ready to help — whether you’re planning a new project, need technical support or want to learn more about our services.</p>"},
    ],
    "blogPosts": [],
}
for path, p in pages.items():
    if not (path.startswith("/blog/") or path.startswith("/case-study/")) or path.count("/") != 3:
        continue
    tags = list(p["tags"])
    if path.startswith("/case-study/"):
        tags = ["case-study"] + tags
    seed["blogPosts"].append({
        "legacyPath": path, "slug": path.strip("/").split("/")[-1], "title": p["h1"] or meta_title(path),
        "metaTitle": meta_title(path), "metaDescription": p["metaDescription"] or META.get(path, ""), "picture": p["ogImage"],
        "tags": tags, "createdOnUtc": p["published"] or "2025-01-01T00:00:00+00:00", "body": fix_links(p["body"], article=True),
    })
json.dump(seed, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print({k: len(v) for k, v in seed.items()})

# Al Thamin Technologies — nopCommerce theme

Built for **nopCommerce 4.90.x** (checked against the 4.90.8 source); written to also run on 5.00.
Brand: green `#48E483`, ink `#0A0E13`, cream `#F3F1EC`. Fonts: Archivo + JetBrains Mono (Google Fonts).

## 1. Install

1. Copy the `AlThamin` folder into `/Themes/` of your nopCommerce site
   (next to `DefaultClean`). Works on a published site — nopCommerce compiles theme views at runtime.
2. Admin › Configuration › Settings › General settings › **Store theme** → select **Al Thamin Technologies**.
3. Restart the application (or clear cache) once.

## 2. How the site maps to nopCommerce

| What | nopCommerce item | Where it shows |
|---|---|---|
| Service pages (`/service/...`) | **Categories** (sub-categories for nested services) | Service template: dark hero, description, sub-service cards, product range, local block, offices |
| LED types, kiosks, LED posters | **Products** inside those categories | Product range grid on the service page; product page |
| Homepage "Solutions" cards | Categories with **Show on home page** ticked | Card text = first ~140 chars of the category description |
| Homepage "LED wall range" tiles | Products with **Show on home page** ticked | Caption = product **Short description** (e.g. `FAÇADES · BILLBOARDS`) |
| About, Contact intro, Resources, Screendrive, Gym, Meeting-room pages | **Topics** | Topic system names used by the theme: `HomepageText` (homepage SEO copy), `ContactUs` (contact intro) |
| Blog + case studies | **Blog** (tag case studies with `case-study`) | Latest 4 posts on the homepage |
| Main menu + footer columns | Admin › Content management › **Menus** | Enter each menu item URL exactly as the old site, e.g. `/service/led-wall-solutions/` |

For each migrated page, copy the old **title tag → Meta title**, **meta description → Meta description**
(also shown as the hero intro on service pages) and the **body → Description**.

## 3. Things to fill in before go-live

Search the theme for `[` to find every placeholder.

- `Views/Shared/_AlThamin.Offices.cshtml` — opening hours and the two Google Maps `<iframe>` embeds.
- `Views/Shared/_AlThamin.Schema.cshtml` — add `geo`, `openingHoursSpecification`, `hasMap` for each office.
  Keep name / address / phone identical to both Google Business Profiles.
- Homepage hero (`Views/Home/Index.cshtml`) — replace the LED "screen" mock with a real install photo or a muted video loop.
- Areas-served list (homepage) and coverage text (service template) — trim to areas you really serve.
- If you rename the theme folder, update `themeName` in `Shared/Head.cshtml`, `Components/Logo` and `Components/Footer`.
- Phone `+971 52 184 7186` appears in: `_Header`, `_Root` (WhatsApp button), `_AlThamin.Offices`,
  `_AlThamin.Schema`, `Components/Footer`, `Catalog/CategoryTemplate…`, `Common/ContactUs`, `Home/Index`.

## 4. Built-in local SEO

- Organization + 2× LocalBusiness JSON-LD on every page (Dubai, Sharjah).
- FAQPage JSON-LD on the homepage, generated from the same list that renders the FAQ.
- nopCommerce's own category JSON-LD is kept on service pages.
- NAP in the utility bar, footer, offices section and contact page; click-to-call and floating WhatsApp.
- One H1 per page, emirate + area names in headings.

## 5. Keeping the ranked URLs (done by the Misc.AlThamin plugin)

`src/Plugins/Nop.Plugin.Misc.AlThamin` (Admin › Configuration › Local plugins › "Al Thamin — legacy URLs & content"):

- serves every althamin.ae URL at its original path with a 200 (`/service/{category}/{product}/`, `/{topic}/`,
  `/blog/{post}/`, `/case-study/{post}/`, `/resources/`, `/tag/{tag}/`, `/contact/` …),
- generates those paths everywhere (menus, breadcrumbs, canonicals, JSON-LD) and serves `/sitemap.xml` with them,
- 301s nopCommerce-style URLs, missing trailing slashes, WordPress sitemaps, author and date archives,
- imports the site content from `Content/althamin-seed.json` (runs on install; re-run from the configure page).

Old images keep their URLs: they are in `wwwroot/wp-content/uploads/` and served as static files.
New pages keep the pattern automatically: put a service category under **Services**, map a product to its category.

## 6. Files

```
AlThamin/
  theme.json, preview.jpg
  Content/css/styles.css, styles.rtl.css, print.css   ← DefaultClean base (cart, checkout, account keep working)
  Content/css/althamin.css                            ← all brand styling (loads last)
  Content/images/logo.png (black text), logo-white.png (white text)
  Views/_ViewImports.cshtml
  Views/Shared/Head.cshtml, _Root.cshtml, _Header.cshtml
  Views/Shared/_AlThamin.Schema.cshtml, _AlThamin.Offices.cshtml
  Views/Shared/Components/Logo|Footer|HomepageCategories|HomepageProducts/Default.cshtml
  Views/Home/Index.cshtml
  Views/Catalog/CategoryTemplate.ProductsInGridOrLines.cshtml
  Views/Common/ContactUs.cshtml
  Views/Product/ProductTemplate.Simple.cshtml    ← sub-service pages
  Views/Topic/TopicDetails.cshtml                ← About, Software, Gym, Meeting room
  Views/Blog/List.cshtml, Views/Blog/BlogPost.cshtml ← Resources hub, blog posts, case studies
```

## 7. nopCommerce 5.00

The views avoid APIs that moved between 4.90 and 5.00 (`IWebHelper`, `IThemeContext`, the News widget zone),
so the same theme folder is meant to work on both. One difference: 5.00 adds custom **contact form attributes**;
this theme's contact page doesn't render them. If you use them on 5.00, add this inside the contact form, after the Enquiry field:

```cshtml
@if (Model.ContactFormAttributes.Count > 0)
{
    @await Html.PartialAsync("_ContactFormAttributes", Model.ContactFormAttributes)
}
```

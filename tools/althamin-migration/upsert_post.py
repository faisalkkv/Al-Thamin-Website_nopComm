"""Create or update one blog post (and its URL record) in a nopCommerce database, straight from the content seed.

Usage:
  python upsert_post.py <althamin-seed.json> <slug> local
  python upsert_post.py <althamin-seed.json> <slug> remote      (needs env REMOTE_DB_CONNECTION = full ODBC connection string)
"""
import json, os, sys
from datetime import datetime, timezone
import pyodbc

LOCAL = r"DRIVER={ODBC Driver 17 for SQL Server};SERVER=LAPTOP-CSP4MIR5\SQLEXPRESS;DATABASE=althamin_nopCommerse;Trusted_Connection=yes;TrustServerCertificate=yes"

def utc(value):
    if not value:
        return None
    return datetime.fromisoformat(value).astimezone(timezone.utc).replace(tzinfo=None)

seed_file, slug, target = sys.argv[1], sys.argv[2], sys.argv[3]
post = next(p for p in json.load(open(seed_file, encoding="utf-8"))["blogPosts"] if p["slug"] == slug)
db = pyodbc.connect(LOCAL if target == "local" else os.environ["REMOTE_DB_CONNECTION"])
cur = db.cursor()

values = {
    "Title": post["title"], "Body": post["body"], "BodyOverview": post["metaDescription"],
    "MetaTitle": post["metaTitle"], "MetaDescription": post["metaDescription"], "Tags": ", ".join(post["tags"]),
    "StartDateUtc": utc(post.get("startDateUtc")),
}
row = cur.execute("select EntityId from UrlRecord where Slug = ? and EntityName = 'BlogPost'", slug).fetchone()
if row:
    cur.execute(f"update BlogPost set {', '.join(k + ' = ?' for k in values)} where Id = ?", *values.values(), row[0])
    post_id, action = row[0], "updated"
else:
    language_id = cur.execute("select top 1 Id from Language order by DisplayOrder, Id").fetchone()[0]
    cols = list(values) + ["LanguageId", "IncludeInSitemap", "AllowComments", "LimitedToStores", "CreatedOnUtc"]
    params = list(values.values()) + [language_id, 1, 0, 0, utc(post.get("createdOnUtc")) or datetime.utcnow()]
    post_id = cur.execute(f"insert into BlogPost ({', '.join(cols)}) output inserted.Id values ({', '.join('?' * len(cols))})", *params).fetchone()[0]
    # language 0 = default slug (what nopCommerce admin saves; needed for links and sitemap)
    cur.execute("insert into UrlRecord (EntityId, EntityName, Slug, IsActive, LanguageId) values (?, 'BlogPost', ?, 1, 0)", post_id, slug)
    action = "created"
db.commit()
check = cur.execute("select Id, Title, StartDateUtc, len(Body) from BlogPost where Id = ?", post_id).fetchone()
print(f"{target}: {action} post {post_id} | start (UTC) {check[2]} | body {check[3]} chars")

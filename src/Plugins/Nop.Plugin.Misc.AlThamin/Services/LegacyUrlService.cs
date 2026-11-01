using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Nop.Core;
using Nop.Core.Domain.Blogs;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Seo;
using Nop.Core.Domain.Topics;
using Nop.Services.Blogs;
using Nop.Services.Catalog;
using Nop.Services.Seo;
using Nop.Services.Topics;

namespace Nop.Plugin.Misc.AlThamin.Services;

/// <summary>
/// Maps nopCommerce entities to the URLs althamin.ae used under WordPress, and back.
/// Rules:
///   category  → /{ancestor slugs}/{slug}/   (e.g. /service/led-wall-solutions/)
///   product   → {first category path}{slug}/ (e.g. /service/led-wall-solutions/signage-led-screen/)
///   blog post → /blog/{slug}/, or /case-study/{slug}/ when tagged "case-study"
///   topic     → /{slug}/
/// </summary>
public partial class LegacyUrlService
{
    #region Fields

    protected static readonly string[] _supportedEntities = [nameof(Category), nameof(Product), nameof(BlogPost), nameof(Topic)];
    protected static readonly Regex _wpDateArchive = new(@"^/\d{4}(/\d{2}){0,2}/?$", RegexOptions.Compiled);

    /// <summary>
    /// Legacy WordPress listing pages served by a nopCommerce route (rewritten, URL kept)
    /// </summary>
    protected static readonly Dictionary<string, string> _staticRewrites = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/contact/"] = "/contactus",
        ["/resources/"] = "/blog",
        ["/blog-2/"] = "/blog",
        ["/category/blog/"] = "/blog",
        ["/case-study/"] = "/blog/tag/case-study",
        ["/category/case-study/"] = "/blog/tag/case-study"
    };

    /// <summary>
    /// nopCommerce pages that have a legacy address (GET requests are 301'd to it)
    /// </summary>
    protected static readonly Dictionary<string, string> _nativeToLegacy = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/contactus"] = "/contact/",
        ["/blog"] = "/resources/",
        ["/blog/"] = "/resources/"
    };

    /// <summary>
    /// Thin or technical WordPress URLs that are 301'd
    /// </summary>
    protected static readonly Dictionary<string, string> _wordpressRedirects = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/sitemap_index.xml"] = "/sitemap.xml",
        ["/wp-sitemap.xml"] = "/sitemap.xml",
        ["/page-sitemap.xml"] = "/sitemap.xml",
        ["/post-sitemap.xml"] = "/sitemap.xml",
        ["/category-sitemap.xml"] = "/sitemap.xml",
        ["/post_tag-sitemap.xml"] = "/sitemap.xml",
        ["/author-sitemap.xml"] = "/sitemap.xml",
        ["/sitemap-misc.xml"] = "/sitemap.xml",
        ["/llms-sitemap.xml"] = "/sitemap.xml",
        ["/sitemap.html"] = "/sitemap",
        ["/home/"] = "/",
        ["/feed/"] = "/resources/",
        ["/comments/feed/"] = "/resources/"
    };

    protected readonly IBlogService _blogService;
    protected readonly ICategoryService _categoryService;
    protected readonly IProductService _productService;
    protected readonly IStoreContext _storeContext;
    protected readonly ITopicService _topicService;
    protected readonly IUrlRecordService _urlRecordService;
    protected readonly IWorkContext _workContext;

    #endregion

    #region Ctor

    public LegacyUrlService(IBlogService blogService,
        ICategoryService categoryService,
        IProductService productService,
        IStoreContext storeContext,
        ITopicService topicService,
        IUrlRecordService urlRecordService,
        IWorkContext workContext)
    {
        _blogService = blogService;
        _categoryService = categoryService;
        _productService = productService;
        _storeContext = storeContext;
        _topicService = topicService;
        _urlRecordService = urlRecordService;
        _workContext = workContext;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Convert a blog tag to its WordPress tag slug ("Healthcare Digital SIgnage" → "healthcare-digital-signage")
    /// </summary>
    public static string TagSlug(string tag)
    {
        return Regex.Replace((tag ?? string.Empty).Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    }

    /// <summary>
    /// Get the legacy URL of a blog tag listing
    /// </summary>
    public static string TagPath(string tag)
    {
        var slug = TagSlug(tag);
        return slug == AlThaminDefaults.CaseStudyTag ? "/category/case-study/" : $"/tag/{slug}/";
    }

    protected virtual async Task<string> GetCategoryPathAsync(Category category)
    {
        var breadcrumb = await _categoryService.GetCategoryBreadCrumbAsync(category, showHidden: true);
        var sb = new StringBuilder("/");
        foreach (var item in breadcrumb)
            sb.Append(await _urlRecordService.GetSeNameAsync(item)).Append('/');

        return sb.ToString();
    }

    protected virtual async Task<bool> IsCaseStudyAsync(BlogPost post)
    {
        var tags = await _blogService.ParseTagsAsync(post);
        return tags.Any(tag => TagSlug(tag) == AlThaminDefaults.CaseStudyTag);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Get the legacy path of an entity
    /// </summary>
    /// <param name="entityName">Entity name (Category, Product, BlogPost, Topic)</param>
    /// <param name="entityId">Entity identifier</param>
    /// <param name="slug">Active slug of the entity</param>
    /// <returns>The path, or null when the entity type is not handled</returns>
    public virtual async Task<string> GetEntityPathAsync(string entityName, int entityId, string slug)
    {
        if (string.IsNullOrEmpty(slug))
            return null;

        switch (entityName)
        {
            case nameof(Category):
                var category = await _categoryService.GetCategoryByIdAsync(entityId);
                return category is null || category.Deleted ? null : await GetCategoryPathAsync(category);

            case nameof(Product):
                var productCategory = (await _categoryService.GetProductCategoriesByProductIdAsync(entityId, true))
                    .OrderBy(pc => pc.DisplayOrder)
                    .FirstOrDefault();
                var parent = await _categoryService.GetCategoryByIdAsync(productCategory?.CategoryId ?? 0);
                return (parent is null || parent.Deleted ? "/" : await GetCategoryPathAsync(parent)) + slug + "/";

            case nameof(BlogPost):
                var post = await _blogService.GetBlogPostByIdAsync(entityId);
                if (post is null)
                    return null;
                return (await IsCaseStudyAsync(post) ? "/case-study/" : "/blog/") + slug + "/";

            case nameof(Topic):
                return "/" + slug + "/";

            default:
                return null;
        }
    }

    /// <summary>
    /// Get the legacy path for an entity type and slug (used when generating links)
    /// </summary>
    public virtual async Task<string> GetPathBySlugAsync(string entityName, string slug)
    {
        if (string.IsNullOrEmpty(slug) || !_supportedEntities.Contains(entityName))
            return null;

        var urlRecord = await _urlRecordService.GetBySlugAsync(slug);
        if (urlRecord is null || !urlRecord.EntityName.Equals(entityName, StringComparison.InvariantCultureIgnoreCase))
            return null;

        return await GetEntityPathAsync(entityName, urlRecord.EntityId, slug);
    }

    /// <summary>
    /// Resolve an incoming request path
    /// </summary>
    /// <param name="path">Request path (unescaped, as ASP.NET Core exposes it)</param>
    /// <returns>Where to send the request: rewrite to a nopCommerce path, 301 to the canonical legacy path, or nothing</returns>
    public virtual async Task<(string RewriteTo, string RedirectTo)> ResolveAsync(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
            return default;

        if (_wordpressRedirects.TryGetValue(path, out var wpTarget))
            return (null, wpTarget);

        if (path.StartsWith("/author/", StringComparison.OrdinalIgnoreCase) || _wpDateArchive.IsMatch(path))
            return (null, "/resources/");

        if (_staticRewrites.TryGetValue(path, out var rewrite))
            return (rewrite, null);

        //no trailing slash on a listing page
        if (_staticRewrites.ContainsKey(path + "/"))
            return (null, path + "/");

        if (_nativeToLegacy.TryGetValue(path, out var legacy))
            return (null, legacy);

        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        //blog tags: /tag/{slug}/ ↔ /blog/tag/{tag}
        if (segments.Length == 2 && segments[0].Equals("tag", StringComparison.OrdinalIgnoreCase))
        {
            var store = await _storeContext.GetCurrentStoreAsync();
            var language = await _workContext.GetWorkingLanguageAsync();
            var tag = (await _blogService.GetAllBlogPostTagsAsync(store.Id, language.Id))
                .FirstOrDefault(t => TagSlug(t.Name) == segments[1].ToLowerInvariant());
            if (tag is null)
                return default;

            var tagPath = TagPath(tag.Name);
            return tagPath.Equals(path, StringComparison.Ordinal) ? ("/blog/tag/" + tag.Name, null) : (null, tagPath);
        }
        if (segments.Length == 3 && segments[0].Equals("blog", StringComparison.OrdinalIgnoreCase) && segments[1].Equals("tag", StringComparison.OrdinalIgnoreCase))
            return (null, TagPath(segments[2]));

        //entities: one segment (nopCommerce-style or a legacy topic), or a nested legacy path
        if (segments.Length is 0 or > 4)
            return default;
        if (segments.Length > 1 && !path.EndsWith('/') &&
            !segments[0].Equals(AlThaminDefaults.ServicesSlug, StringComparison.OrdinalIgnoreCase) &&
            !segments[0].Equals("blog", StringComparison.OrdinalIgnoreCase) &&
            !segments[0].Equals("case-study", StringComparison.OrdinalIgnoreCase))
        {
            //nopCommerce's own multi-segment routes never end with a slash; leave them alone
            return default;
        }

        var urlRecord = await _urlRecordService.GetBySlugAsync(segments[^1]);
        if (urlRecord is null || !_supportedEntities.Contains(urlRecord.EntityName, StringComparer.InvariantCultureIgnoreCase))
            return default;

        var slug = urlRecord.IsActive
            ? urlRecord.Slug
            : await _urlRecordService.GetActiveSlugAsync(urlRecord.EntityId, urlRecord.EntityName, urlRecord.LanguageId);
        var entityName = _supportedEntities.First(name => name.Equals(urlRecord.EntityName, StringComparison.InvariantCultureIgnoreCase));
        var canonical = await GetEntityPathAsync(entityName, urlRecord.EntityId, slug);
        if (canonical is null)
            return default;

        return canonical.Equals(path, StringComparison.Ordinal) ? ("/" + slug, null) : (null, canonical);
    }

    /// <summary>
    /// Build sitemap.xml with the legacy URL of every public page
    /// </summary>
    /// <param name="baseUrl">Site URL without a trailing slash, e.g. https://althamin.ae</param>
    public virtual async Task<string> BuildSitemapXmlAsync(string baseUrl)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var language = await _workContext.GetWorkingLanguageAsync();
        var urls = new List<(string Path, DateTime? Modified)> { ("/", null) };

        foreach (var category in await _categoryService.GetAllCategoriesAsync(store.Id))
            urls.Add((await GetCategoryPathAsync(category), category.UpdatedOnUtc));

        foreach (var product in await _productService.SearchProductsAsync(storeId: store.Id, visibleIndividuallyOnly: true))
            urls.Add((await GetEntityPathAsync(nameof(Product), product.Id, await _urlRecordService.GetSeNameAsync(product)), product.UpdatedOnUtc));

        foreach (var topic in (await _topicService.GetAllTopicsAsync(store.Id)).Where(t => t.IncludeInSitemap))
            urls.Add(("/" + await _urlRecordService.GetSeNameAsync(topic) + "/", null));

        foreach (var post in (await _blogService.GetAllBlogPostsAsync(store.Id, language.Id)).Where(p => p.IncludeInSitemap))
            urls.Add((await GetEntityPathAsync(nameof(BlogPost), post.Id, await _urlRecordService.GetSeNameAsync(post)), post.CreatedOnUtc));

        urls.AddRange(_staticRewrites.Keys.Select(path => (path, (DateTime?)null)));
        foreach (var tag in await _blogService.GetAllBlogPostTagsAsync(store.Id, language.Id))
        {
            var tagPath = TagPath(tag.Name);
            if (!_staticRewrites.ContainsKey(tagPath))
                urls.Add((tagPath, null));
        }

        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, Async = true };
        await using var stream = new MemoryStream();
        await using (var writer = XmlWriter.Create(stream, settings))
        {
            await writer.WriteStartDocumentAsync();
            writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");
            foreach (var (path, modified) in urls.Where(u => u.Path is not null).DistinctBy(u => u.Path))
            {
                writer.WriteStartElement("url");
                writer.WriteElementString("loc", baseUrl + path);
                if (modified.HasValue)
                    writer.WriteElementString("lastmod", modified.Value.ToString("yyyy-MM-dd"));
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
            await writer.WriteEndDocumentAsync();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    #endregion
}

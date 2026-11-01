using Microsoft.AspNetCore.Http;
using Nop.Data;

namespace Nop.Plugin.Misc.AlThamin.Services;

/// <summary>
/// Runs before routing: serves legacy WordPress paths with a 200 by rewriting them to the nopCommerce path,
/// 301s every other address of the same page to its legacy path, and answers /sitemap.xml
/// </summary>
public partial class LegacyUrlMiddleware
{
    #region Fields

    protected static readonly string[] _skippedPrefixes =
        ["/admin", "/api", "/install", "/js/", "/css/", "/lib", "/bundles/", "/images/", "/icons/", "/files/", "/themes/", "/plugins/", "/wp-content/", "/.well-known/"];

    protected readonly RequestDelegate _next;

    #endregion

    #region Ctor

    public LegacyUrlMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    #endregion

    #region Utilities

    protected static bool IsSkipped(string path)
    {
        if (_skippedPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            return true;

        //static files (anything with an extension), except the WordPress sitemaps we redirect
        var last = path[(path.LastIndexOf('/') + 1)..];
        return last.Contains('.') && !last.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !last.EndsWith(".html", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Methods

    public async Task InvokeAsync(HttpContext context, LegacyUrlService legacyUrlService)
    {
        var path = context.Request.Path.Value;
        if (string.IsNullOrEmpty(path) || path == "/" || !DataSettingsManager.IsDatabaseInstalled() || IsSkipped(path))
        {
            await _next(context);
            return;
        }

        var isRead = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);

        if (isRead && path.Equals("/sitemap.xml", StringComparison.OrdinalIgnoreCase))
        {
            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}";
            context.Response.ContentType = "application/xml; charset=utf-8";
            await context.Response.WriteAsync(await legacyUrlService.BuildSitemapXmlAsync(baseUrl));
            return;
        }

        var (rewriteTo, redirectTo) = await legacyUrlService.ResolveAsync(path);

        if (redirectTo is not null)
        {
            //never redirect form posts; let nopCommerce handle them at the address they were posted to
            if (isRead)
            {
                context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
                context.Response.Headers.Location = $"{context.Request.PathBase}{redirectTo}{context.Request.QueryString}";
                return;
            }
        }
        else if (rewriteTo is not null)
        {
            context.Items[AlThaminDefaults.OriginalPathItemKey] = context.Request.Path;
            context.Request.Path = new PathString(rewriteTo);
        }

        await _next(context);
    }

    #endregion
}

/// <summary>
/// Runs after routing: puts the legacy path back so links built from the current URL (canonical, paging) keep it
/// </summary>
public partial class LegacyPathRestoreMiddleware
{
    protected readonly RequestDelegate _next;

    public LegacyPathRestoreMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Items.TryGetValue(AlThaminDefaults.OriginalPathItemKey, out var original) && original is PathString path)
            context.Request.Path = path;

        await _next(context);
    }
}

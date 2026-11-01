using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;
using Nop.Services.Seo;
using Nop.Services.Topics;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.AlThamin.Services;

/// <summary>
/// URL helper that generates the legacy althamin.ae path for categories, products, topics and blog posts,
/// so menus, breadcrumbs, canonical tags and JSON-LD all point at the URLs Google already ranks
/// </summary>
public partial class LegacyNopUrlHelper : NopUrlHelper
{
    #region Fields

    protected readonly IHttpContextAccessor _httpContextAccessor;
    protected readonly LegacyUrlService _legacyUrlService;

    #endregion

    #region Ctor

    public LegacyNopUrlHelper(CatalogSettings catalogSettings,
        IActionContextAccessor actionContextAccessor,
        ICategoryService categoryService,
        IHttpContextAccessor httpContextAccessor,
        IManufacturerService manufacturerService,
        IStoreContext storeContext,
        ITopicService topicService,
        IUrlHelperFactory urlHelperFactory,
        IUrlRecordService urlRecordService,
        LegacyUrlService legacyUrlService)
        : base(catalogSettings, actionContextAccessor, categoryService, manufacturerService, storeContext, topicService, urlHelperFactory, urlRecordService)
    {
        _httpContextAccessor = httpContextAccessor;
        _legacyUrlService = legacyUrlService;
    }

    #endregion

    #region Methods

    public override async Task<string> RouteGenericUrlAsync<TEntity>(object values = null, string protocol = null, string host = null, string fragment = null)
    {
        var routeValues = new RouteValueDictionary(values);
        if (!routeValues.TryGetValue(NopRoutingDefaults.RouteValue.SeName, out var seName) || string.IsNullOrEmpty(seName?.ToString()))
            return await base.RouteGenericUrlAsync<TEntity>(values, protocol, host, fragment);

        var path = await _legacyUrlService.GetPathBySlugAsync(typeof(TEntity).Name, seName.ToString());
        if (path is null)
            return await base.RouteGenericUrlAsync<TEntity>(values, protocol, host, fragment);

        //any other route values become the query string, as the default route would do
        routeValues.Remove(NopRoutingDefaults.RouteValue.SeName);
        var query = QueryString.Create(routeValues
            .Where(pair => pair.Value is not null)
            .Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value.ToString())));

        var request = _httpContextAccessor.HttpContext?.Request;
        var url = $"{request?.PathBase}{path}{query}";
        if (!string.IsNullOrEmpty(fragment))
            url += "#" + fragment;

        if (!string.IsNullOrEmpty(protocol) && request is not null)
            url = $"{protocol}://{host ?? request.Host.Value}{url}";

        return url;
    }

    #endregion
}

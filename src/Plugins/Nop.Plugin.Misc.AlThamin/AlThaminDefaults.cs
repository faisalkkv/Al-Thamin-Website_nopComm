namespace Nop.Plugin.Misc.AlThamin;

/// <summary>
/// Represents plugin constants
/// </summary>
public static class AlThaminDefaults
{
    /// <summary>
    /// Gets the plugin system name
    /// </summary>
    public static string SystemName => "Misc.AlThamin";

    /// <summary>
    /// Gets the configuration route name
    /// </summary>
    public static string ConfigurationRouteName => "Plugin.Misc.AlThamin.Configure";

    /// <summary>
    /// Gets the slug of the root "Services" category; its sub-categories and their products live under /service/...
    /// </summary>
    public static string ServicesSlug => "service";

    /// <summary>
    /// Gets the blog tag that turns a blog post into a case study (/case-study/{slug}/)
    /// </summary>
    public static string CaseStudyTag => "case-study";

    /// <summary>
    /// Gets the HttpContext item key holding the legacy path while the request is routed by its nopCommerce path
    /// </summary>
    public static string OriginalPathItemKey => "AlThamin.OriginalPath";

    /// <summary>
    /// Gets the path of the content seed file, relative to the plugin folder
    /// </summary>
    public static string SeedFilePath => "~/Plugins/Misc.AlThamin/Content/althamin-seed.json";
}

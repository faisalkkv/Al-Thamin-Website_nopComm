using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.AlThamin.Services;
using Nop.Services.Common;
using Nop.Services.Plugins;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.AlThamin;

/// <summary>
/// Al Thamin site plugin: legacy WordPress URLs and content import
/// </summary>
public class AlThaminPlugin : BasePlugin, IMiscPlugin
{
    #region Fields

    protected readonly INopUrlHelper _nopUrlHelper;

    #endregion

    #region Ctor

    public AlThaminPlugin(INopUrlHelper nopUrlHelper)
    {
        _nopUrlHelper = nopUrlHelper;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets a configuration page URL
    /// </summary>
    public override string GetConfigurationPageUrl()
    {
        return _nopUrlHelper.RouteUrl(AlThaminDefaults.ConfigurationRouteName);
    }

    /// <summary>
    /// Install the plugin: imports the althamin.ae content so the legacy URLs resolve straight away
    /// </summary>
    public override async Task InstallAsync()
    {
        await EngineContext.Current.Resolve<ContentImporter>().ImportAsync();
        await base.InstallAsync();
    }

    #endregion
}

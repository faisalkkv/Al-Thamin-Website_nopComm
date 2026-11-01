using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.AlThamin.Services;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.AlThamin.Controllers;

[Area(AreaNames.ADMIN)]
[AuthorizeAdmin]
[AutoValidateAntiforgeryToken]
public class AlThaminController : BasePluginController
{
    #region Fields

    protected readonly ContentImporter _contentImporter;
    protected readonly ILogger _logger;
    protected readonly INotificationService _notificationService;

    #endregion

    #region Ctor

    public AlThaminController(ContentImporter contentImporter,
        ILogger logger,
        INotificationService notificationService)
    {
        _contentImporter = contentImporter;
        _logger = logger;
        _notificationService = notificationService;
    }

    #endregion

    #region Methods

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public IActionResult Configure()
    {
        return View("~/Plugins/Misc.AlThamin/Views/Configure.cshtml", TempData["AlThamin.ImportLog"] as string);
    }

    [HttpPost, ActionName("Configure")]
    [FormValueRequired("import")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Import()
    {
        try
        {
            var log = await _contentImporter.ImportAsync();
            TempData["AlThamin.ImportLog"] = string.Join(Environment.NewLine, log);
            _notificationService.SuccessNotification($"Import finished: {log.Count} steps.");
        }
        catch (Exception exception)
        {
            await _logger.ErrorAsync("Al Thamin content import failed", exception);
            _notificationService.ErrorNotification($"Import failed: {exception.Message}");
        }

        return RedirectToAction("Configure");
    }

    #endregion
}

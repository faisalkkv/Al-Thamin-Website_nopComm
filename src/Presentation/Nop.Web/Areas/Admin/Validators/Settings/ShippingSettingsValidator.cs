using Nop.Core.Domain.Common;
using Nop.Services.Localization;
using Nop.Web.Areas.Admin.Models.Settings;
using Nop.Web.Areas.Admin.Validators.Common;
using Nop.Web.Framework.Validators;

namespace Nop.Web.Areas.Admin.Validators.Settings;

public partial class ShippingSettingsValidator : BaseNopValidator<ShippingSettingsModel>
{
    public ShippingSettingsValidator(AddressSettings addressSettings, ILocalizationService localizationService)
    {
        RuleFor(model => model.ShippingOriginAddress).SetValidator(new AddressValidator(addressSettings, localizationService));
    }
}
using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.Platron.Components
{
    [ViewComponent(Name = "PaymentPlatron")]
    public class PaymentPlatronViewComponent : NopViewComponent
    {
        public IViewComponentResult Invoke()
        {
            return View("~/Plugins/Payments.Platron/Views/PaymentInfo.cshtml");
        }
    }
}

using System.Web.Mvc;
using System.Web.Routing;
using Nop.Web.Framework.Mvc.Routes;

namespace Nop.Plugin.Payments.Platron
{
    public partial class RouteProvider : IRouteProvider
    {
        public void RegisterRoutes(RouteCollection routes)
        {
            //confirm pay
            routes.MapRoute("Plugin.Payments.Platron.ConfirmPay",
                 "Plugins/Platron/ConfirmPay",
                 new { controller = "PaymentPlatron", action = "ConfirmPay" },
                 new[] { "Nop.Plugin.Payments.Platron.Controllers" }
            );
            //cancel
            routes.MapRoute("Plugin.Payments.Platron.CancelOrder",
                 "Plugins/Platron/CancelOrder",
                 new { controller = "PaymentPlatron", action = "CancelOrder" },
                 new[] { "Nop.Plugin.Payments.Platron.Controllers" }
            );
            //success
            routes.MapRoute("Plugin.Payments.Platron.Success",
                 "Plugins/Platron/Success",
                 new { controller = "PaymentPlatron", action = "Success" },
                 new[] { "Nop.Plugin.Payments.Platron.Controllers" }
            );
        }
        public int Priority
        {
            get
            {
                return 0;
            }
        }
    }
}

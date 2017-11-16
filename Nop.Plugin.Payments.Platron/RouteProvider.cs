using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Payments.Platron
{
    public partial class RouteProvider : IRouteProvider
    {
        public void RegisterRoutes(IRouteBuilder routeBuilder)
        {
            //confirm pay
            routeBuilder.MapRoute("Plugin.Payments.Platron.ConfirmPay",
                 "Plugins/Platron/ConfirmPay",
                 new { controller = "PaymentPlatron", action = "ConfirmPay" });
            //cancel
            routeBuilder.MapRoute("Plugin.Payments.Platron.CancelOrder",
                 "Plugins/Platron/CancelOrder",
                 new { controller = "PaymentPlatron", action = "CancelOrder" });
            //success
            routeBuilder.MapRoute("Plugin.Payments.Platron.Success",
                 "Plugins/Platron/Success",
                 new { controller = "PaymentPlatron", action = "Success" });
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

using System;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.Platron.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Services.Stores;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.Platron.Controllers
{
    public class PaymentPlatronController : BasePaymentController
    {
        private const string ORDER_DESCRIPTION = "Payment order #$orderId";
        private readonly IWorkContext _workContext;
        private readonly IStoreService _storeService;
        private readonly ISettingService _settingService;
        private readonly IPaymentService _paymentService;
        private readonly IOrderService _orderService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly ILogger _logger;
        private readonly PaymentSettings _paymentSettings;
        private readonly ILocalizationService _localizationService;
        private readonly IWebHelper _webHelper;
        private readonly IPermissionService _permissionService;
        
        public PaymentPlatronController(IWorkContext workContext,
            IStoreService storeService, 
            ISettingService settingService, 
            IPaymentService paymentService, 
            IOrderService orderService, 
            IOrderProcessingService orderProcessingService, 
            ILogger logger,
            PaymentSettings paymentSettings, 
            ILocalizationService localizationService, 
            IWebHelper webHelper,
            IPermissionService permissionService)
        {
            this._workContext = workContext;
            this._storeService = storeService;
            this._settingService = settingService;
            this._paymentService = paymentService;
            this._orderService = orderService;
            this._orderProcessingService = orderProcessingService;
            this._logger = logger;
            this._paymentSettings = paymentSettings;
            this._localizationService = localizationService;
            this._webHelper = webHelper;
            this._permissionService = permissionService;
        }

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult Configure()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //load settings for a chosen store scope
            var storeScope = GetActiveStoreScopeConfiguration(_storeService, _workContext);
            var platronPaymentSettings = _settingService.LoadSetting<PlatronPaymentSettings>(storeScope);

            if (!platronPaymentSettings.DescriptionTemplate.Any())
                platronPaymentSettings.DescriptionTemplate = ORDER_DESCRIPTION;

            var model = new ConfigurationModel
            {
                MerchantId = platronPaymentSettings.MerchantId,
                SecretKey = platronPaymentSettings.SecretKey,
                TestingMode = platronPaymentSettings.TestingMode,
                DescriptionTemplate = platronPaymentSettings.DescriptionTemplate,
                AdditionalFee = platronPaymentSettings.AdditionalFee,
                AdditionalFeePercentage = platronPaymentSettings.AdditionalFeePercentage,
                ActiveStoreScopeConfiguration = storeScope
            };

            if (storeScope > 0)
            {
                model.MerchantIdOverrideForStore = _settingService.SettingExists(platronPaymentSettings, x => x.MerchantId, storeScope);
                model.SecretKeyOverrideForStore = _settingService.SettingExists(platronPaymentSettings, x => x.SecretKey, storeScope);
                model.TestingModeOverrideForStore = _settingService.SettingExists(platronPaymentSettings, x => x.TestingMode, storeScope);
                model.DescriptionTemplateOverrideForStore = _settingService.SettingExists(platronPaymentSettings, x => x.DescriptionTemplate, storeScope);
                model.AdditionalFeeOverrideForStore = _settingService.SettingExists(platronPaymentSettings, x => x.AdditionalFee, storeScope);
                model.AdditionalFeePercentageOverrideForStore = _settingService.SettingExists(platronPaymentSettings, x => x.AdditionalFeePercentage, storeScope);
            }

            return View("~/Plugins/Payments.Platron/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult Configure(ConfigurationModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return Configure();

            //load settings for a chosen store scope
            var storeScope = GetActiveStoreScopeConfiguration(_storeService, _workContext);
            var platronPaymentSettings = _settingService.LoadSetting<PlatronPaymentSettings>(storeScope);

            //save settings
            platronPaymentSettings.MerchantId = model.MerchantId;
            platronPaymentSettings.SecretKey = model.SecretKey;
            platronPaymentSettings.TestingMode = model.TestingMode;
            platronPaymentSettings.DescriptionTemplate = model.DescriptionTemplate;
            platronPaymentSettings.AdditionalFee = model.AdditionalFee;
            platronPaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared 
             * and loaded from database after each update */
            _settingService.SaveSettingOverridablePerStore(platronPaymentSettings, x => x.MerchantId, model.MerchantIdOverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(platronPaymentSettings, x => x.SecretKey, model.SecretKeyOverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(platronPaymentSettings, x => x.TestingMode, model.TestingModeOverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(platronPaymentSettings, x => x.DescriptionTemplate, model.DescriptionTemplateOverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(platronPaymentSettings, x => x.AdditionalFee, model.AdditionalFeeOverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(platronPaymentSettings, x => x.AdditionalFeePercentage, model.AdditionalFeePercentageOverrideForStore, storeScope, false);

            //now clear settings cache
            _settingService.ClearCache();

            SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));

            return Configure();
        }
        
        private ContentResult GetResponse(string textToResponse, PlatronPaymentProcessor processor, bool success = false)
        {
            var status = success ? "ok" : "error";
            if (!success)
                _logger.Error($"Platron. {textToResponse}");

            var postData = new NameValueCollection
            {
                { "pg_status", status },
                { "pg_salt", CommonHelper.GenerateRandomDigitCode(8) }
            };
            if (!success)
                postData.Add("pg_error_description", textToResponse);
           
            postData.Add("pg_sig", processor.GetSignature(processor.GetScriptName(Request.Path), postData));

            const string rez = "<?xml version=\"1.0\" encoding=\"utf - 8\"?><response>{0}</response>";

            var content = postData.AllKeys.Select(key => string.Format("<{0}>{1}</{0}>", key, postData[key])).Aggregate(string.Empty, (all, curent) => all + curent);

            return Content(string.Format(rez, content), "text/xml", Encoding.UTF8);
        }

        private string GetValue(string key, IFormCollection form)
        {
            return (form.Keys.Contains(key) ? form[key].ToString() : _webHelper.QueryString<string>(key)) ?? string.Empty;
        }

        private void UpdateOrderStatus(Order order, string status)
        {
            status = status.ToLower();
           
            switch (status)
            {
                case "failed":
                case "revoked":
                    {
                        //mark order as canceled
                        if ((order.PaymentStatus == PaymentStatus.Paid || order.PaymentStatus == PaymentStatus.Authorized) &&
                            _orderProcessingService.CanCancelOrder(order))
                            _orderProcessingService.CancelOrder(order, true);
                    }
                    break;
                case "ok":
                    {
                        //mark order as paid
                        if (_orderProcessingService.CanMarkOrderAsPaid(order) && status.ToUpper() == "PAID")
                            _orderProcessingService.MarkOrderAsPaid(order);
                    }
                    break;
            }
        }

        public ActionResult ConfirmPay()
        {
            var form = Request.Form;
            var processor = GetPaymentProcessor();

            const string orderIdKey = "pg_order_id";
            const string signatureKey = "pg_sig";
            const string resultKey = "pg_result";

            var orderId = GetValue(orderIdKey, form);
            var signature = GetValue(signatureKey, form);
            var result = GetValue(resultKey, form);

            Order order = null;

            if (Guid.TryParse(orderId, out Guid orderGuid))
            {
                order = _orderService.GetOrderByGuid(orderGuid);
            }

            if (order == null)
                return GetResponse("Order cannot be loaded", processor);

            var sb = new StringBuilder();
            sb.AppendLine("Platron:");
            foreach (var key in form.Keys)
            {
                sb.AppendLine(key + ": " + form[key]);
            }
           
            //order note
            order.OrderNotes.Add(new OrderNote
            {
                Note = sb.ToString(),
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });
            _orderService.UpdateOrder(order);
           
            var postData = new NameValueCollection();
            foreach (var keyValuePair in form.Where(pair => !pair.Key.Equals(signatureKey, StringComparison.InvariantCultureIgnoreCase)))
            {
                postData.Add(keyValuePair.Key, keyValuePair.Value);
            }
            
            var checkDataString = processor.GetSignature(processor.GetScriptName(Url.ToString()), postData);

            if (checkDataString != signature)
                return GetResponse("Invalid order data", processor);

            if (result == "0")
                return GetResponse("The payment has been canceled", processor, true);

            //mark order as paid
            if (_orderProcessingService.CanMarkOrderAsPaid(order))
            {
                _orderProcessingService.MarkOrderAsPaid(order);
            }

            return GetResponse("The order has been paid", processor, true);
        }

        private PlatronPaymentProcessor GetPaymentProcessor()
        {
            var processor =
                _paymentService.LoadPaymentMethodBySystemName("Payments.Platron") as PlatronPaymentProcessor;
            if (processor == null ||
                !processor.IsPaymentMethodActive(_paymentSettings) || !processor.PluginDescriptor.Installed)
                throw new NopException("Platron module cannot be loaded");
            return processor;
        }

        public ActionResult Success(FormCollection form)
        {
            var orderId = _webHelper.QueryString<string>("pg_order_id");
            Order order = null;

            if (Guid.TryParse(orderId, out Guid orderGuid))
                order = _orderService.GetOrderByGuid(orderGuid);

            if (order == null)
                return RedirectToAction("Index", "Home", new { area = string.Empty });

            //update payment status if need
            if (order.PaymentStatus == PaymentStatus.Paid)
            {
                var status = GetPaymentProcessor().GetPaymentStatus(orderId);
                if (status[0].ToLower() == "ok")
                    UpdateOrderStatus(order, status[1]);
            }

            return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
        }

        public ActionResult CancelOrder(FormCollection form)
        {
            var orderId = _webHelper.QueryString<string>("pg_order_id");
            Order order = null;

            if (Guid.TryParse(orderId, out Guid orderGuid))
                order = _orderService.GetOrderByGuid(orderGuid);

            if (order == null)
                return RedirectToAction("Index", "Home", new { area = string.Empty });

            //update payment status if need
            if (order.PaymentStatus != PaymentStatus.Voided)
            {
                var status = GetPaymentProcessor().GetPaymentStatus(orderId);
                if (status[0].ToLower() == "ok")
                    UpdateOrderStatus(order, status[1]);
            }

            return RedirectToRoute("OrderDetails", new { orderId = order.Id });
        }
    }
}
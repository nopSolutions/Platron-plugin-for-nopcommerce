using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.Platron.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Stores;
using Nop.Web.Framework.Controllers;

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
        
        public PaymentPlatronController(IWorkContext workContext,
            IStoreService storeService, 
            ISettingService settingService, 
            IPaymentService paymentService, 
            IOrderService orderService, 
            IOrderProcessingService orderProcessingService, 
            ILogger logger,
            PaymentSettings paymentSettings, 
            ILocalizationService localizationService, IWebHelper webHelper)
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
        }

        [AdminAuthorize]
        [ChildActionOnly]
        public ActionResult Configure()
        {
            //load settings for a chosen store scope
            var storeScope = this.GetActiveStoreScopeConfiguration(_storeService, _workContext);
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
                model.DescriptionTamplateOverrideForStore = _settingService.SettingExists(platronPaymentSettings, x => x.DescriptionTemplate, storeScope);
                model.AdditionalFeeOverrideForStore = _settingService.SettingExists(platronPaymentSettings, x => x.AdditionalFee, storeScope);
                model.AdditionalFeePercentageOverrideForStore = _settingService.SettingExists(platronPaymentSettings, x => x.AdditionalFeePercentage, storeScope);
            }

            return View("~/Plugins/Payments.Platron/Views/PaymentPlatron/Configure.cshtml", model);
        }

        [HttpPost]
        [AdminAuthorize]
        [ChildActionOnly]
        public ActionResult Configure(ConfigurationModel model)
        {
            if (!ModelState.IsValid)
                return Configure();

            //load settings for a chosen store scope
            var storeScope = this.GetActiveStoreScopeConfiguration(_storeService, _workContext);
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
            _settingService.SaveSettingOverridablePerStore(platronPaymentSettings, x => x.DescriptionTemplate, model.DescriptionTamplateOverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(platronPaymentSettings, x => x.AdditionalFee, model.AdditionalFeeOverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(platronPaymentSettings, x => x.AdditionalFeePercentage, model.AdditionalFeePercentageOverrideForStore, storeScope, false);

            //now clear settings cache
            _settingService.ClearCache();

            SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));

            return Configure();
        }

        [ChildActionOnly]
        public ActionResult PaymentInfo()
        {
            return View("~/Plugins/Payments.Platron/Views/PaymentPlatron/PaymentInfo.cshtml");
        }
        
        private ContentResult GetResponse(string textToResponse, PlatronPaymentProcessor processor, bool success = false)
        {
            var status = success ? "ok" : "error";
            if (!success)
                _logger.Error(String.Format("Platron. {0}", textToResponse));

            var postData = new NameValueCollection
            {
                { "pg_status", status },
                { "pg_salt", CommonHelper.GenerateRandomDigitCode(8) }
            };
            if (!success)
                postData.Add("pg_error_description", textToResponse);
           
            postData.Add("pg_sig", processor.GetSignature(processor.GetScriptName(Request.Url.LocalPath), postData));

            const string rez = "<?xml version=\"1.0\" encoding=\"utf - 8\"?><response>{0}</response>";

            var content = postData.AllKeys.Select(key => String.Format("<{0}>{1}</{0}>", key, postData[key])).Aggregate(String.Empty, (all, curent) => all + curent);

            return Content(String.Format(rez, content), "text/xml", Encoding.UTF8);
        }

        private string GetValue(string key, FormCollection form)
        {
            return (form.AllKeys.Contains(key) ? form[key] : _webHelper.QueryString<string>(key)) ?? String.Empty;
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

        public ActionResult ConfirmPay(FormCollection form)
        {
            var processor = GetPaymentProcessor();

            const string orderIdKey = "pg_order_id";
            const string signatureKey = "pg_sig";
            const string resultKey = "pg_result";

            var orderId = GetValue(orderIdKey, form);
            var signature = GetValue(signatureKey, form);
            var result = GetValue(resultKey, form);

            Order order = null;

            Guid orderGuid;
            if (Guid.TryParse(orderId, out orderGuid))
            {
                order = _orderService.GetOrderByGuid(orderGuid);
            }
            
            if (order == null)
                return GetResponse("Order cannot be loaded", processor);

            var sb = new StringBuilder();
            sb.AppendLine("Platron:");
            foreach (var key in form.AllKeys)
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
            
            form.Remove(signatureKey);

            var checkDataString = processor.GetSignature(processor.GetScriptName(Request.Url.LocalPath), form);

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

            Guid orderGuid;
            if (Guid.TryParse(orderId, out orderGuid))
                order = _orderService.GetOrderByGuid(orderGuid);

            if (order == null)
                return RedirectToAction("Index", "Home", new { area = String.Empty });

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

            Guid orderGuid;
            if (Guid.TryParse(orderId, out orderGuid))
                order = _orderService.GetOrderByGuid(orderGuid);

            if (order == null)
                return RedirectToAction("Index", "Home", new { area = String.Empty });

            //update payment status if need
            if (order.PaymentStatus != PaymentStatus.Voided)
            {
                var status = GetPaymentProcessor().GetPaymentStatus(orderId);
                if (status[0].ToLower() == "ok")
                    UpdateOrderStatus(order, status[1]);
            }

            return RedirectToRoute("OrderDetails", new { orderId = order.Id });
        }

        public override IList<string> ValidatePaymentForm(FormCollection form)
        {
            return new List<string>();
        }

        public override ProcessPaymentRequest GetPaymentInfo(FormCollection form)
        {
            return new ProcessPaymentRequest();
        }
    }
}
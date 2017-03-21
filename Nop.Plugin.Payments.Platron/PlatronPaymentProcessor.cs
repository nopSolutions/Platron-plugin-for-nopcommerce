using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Routing;
using System.Xml.Linq;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Plugins;
using Nop.Plugin.Payments.Platron.Controllers;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Web.Framework;

namespace Nop.Plugin.Payments.Platron
{
    /// <summary>
    /// Platron payment method
    /// </summary>
    public class PlatronPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly PlatronPaymentSettings _platronPaymentSettings;
        private readonly ISettingService _settingService;
        private readonly ICurrencyService _currencyService;
        private readonly CurrencySettings _currencySettings;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly ILocalizationService _localizationService;
        private readonly IWebHelper _webHelper;

        private const string PLATRON_URL = "https://www.platron.ru/payment.php";
        private const string PLATRON_RESULTS_URL = "https://www.platron.ru/get_status.php";

        #endregion

        #region Ctor

        public PlatronPaymentProcessor(PlatronPaymentSettings platronPaymentSettings,
            ISettingService settingService,
            ICurrencyService currencyService,
            CurrencySettings currencySettings,
            IOrderTotalCalculationService orderTotalCalculationService,
            ILocalizationService localizationService,
            IWebHelper webHelper)
        {
            this._platronPaymentSettings = platronPaymentSettings;
            this._settingService = settingService;
            this._currencyService = currencyService;
            this._currencySettings = currencySettings;
            this._orderTotalCalculationService = orderTotalCalculationService;
            this._localizationService = localizationService;
            this._webHelper = webHelper;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessPayment(ProcessPaymentRequest processPaymentRequest)
        {
            return new ProcessPaymentResult { NewPaymentStatus = PaymentStatus.Pending };
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public void PostProcessPayment(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            var orderGuid = postProcessPaymentRequest.Order.OrderGuid;
            var orderTotal = postProcessPaymentRequest.Order.OrderTotal;
            var amount = String.Format(CultureInfo.InvariantCulture, "{0:0.00}", orderTotal);
            var orderId = orderGuid.ToString();

            //create and send post data
            var post = new RemotePost
            {
                FormName = "PayPoint",
                Url = PLATRON_URL
            };
            post.Add("pg_merchant_id", _platronPaymentSettings.MerchantId);
            post.Add("pg_order_id", orderId);
            post.Add("pg_currency", _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId).CurrencyCode);
            post.Add("pg_amount", amount);
            post.Add("pg_description", _platronPaymentSettings.DescriptionTemplate.Replace("$orderId", postProcessPaymentRequest.Order.Id.ToString()));
            post.Add("pg_salt", CommonHelper.GenerateRandomDigitCode(8));
            post.Add("pg_request_method", "POST");
            post.Add("pg_success_url_method", "GET");
            post.Add("pg_failure_url_method", "GET");
            post.Add("pg_testing_mode", (_platronPaymentSettings.TestingMode ? 1 : 0).ToString());
            //suppression check payment
            post.Add("pg_check_url", String.Empty);
            var siteUrl = _webHelper.GetStoreLocation();
            var failUrl = String.Format("{0}{1}", siteUrl, "Plugins/Platron/CancelOrder");
            var successUrl = String.Format("{0}{1}", siteUrl, "Plugins/Platron/Success");
            var confirmPay = String.Format("{0}{1}", siteUrl, "Plugins/Platron/ConfirmPay");

            post.Add("pg_site_url", siteUrl);
            post.Add("pg_failure_url", failUrl);
            post.Add("pg_success_url", successUrl);
            post.Add("pg_result_url", confirmPay);
            
            //code to identify the sender and check integrity of files
            post.Add("pg_sig", GetSignature(GetScriptName(PLATRON_URL), post.Params));

            post.Post();
        }

        /// <summary>
        /// Get the status of the order in the system Platron 
        /// </summary>
        /// <param name="orderId">Order ID</param>
        /// <returns>An array of three elements. 
        /// First: status of the request (ok or error)
        /// Second: Payment status (partial, pending, ok, failed, revoked)
        /// Third: query error if the status of the request "error", or empty
        /// </returns>
        public string[] GetPaymentStatus(string orderId)
        {
            //create and send post data
            var postData = new NameValueCollection
            {
                { "pg_merchant_id", _platronPaymentSettings.MerchantId },
                { "pg_order_id", orderId },
                { "pg_salt", CommonHelper.GenerateRandomDigitCode(8) }
            };

            postData.Add("pg_sig", GetSignature(GetScriptName(PLATRON_RESULTS_URL), postData));

            byte[] data;
            using (var client = new WebClient())
            {
                data = client.UploadValues(PLATRON_RESULTS_URL, postData);
            }

            using (var ms = new MemoryStream(data))
            {
                using (var sr = new StreamReader(ms))
                {
                    var rez = sr.ReadToEnd();

                    if (!rez.Contains("?xml"))
                        return new string[3];

                    try
                    {
                        var doc = XDocument.Parse(rez);

                        var root = doc.Root;

                        var status = root.Element("pg_status").Value;
                        var paymentStatusElement = root.Element("pg_transaction_status");
                        var paymentStatus = paymentStatusElement == null ? String.Empty : paymentStatusElement.Value;
                        var errorElement = root.Element("pg_error_description");
                        var error = errorElement == null ? String.Empty : errorElement.Value;

                        return new[] { status, paymentStatus, error };
                    }
                    catch (NullReferenceException)
                    {
                        return new string[3];
                    }
                }
            }
        }

        /// <summary>
        /// Get the signarure
        /// </summary>
        /// <param name="scriptName">Script name</param>
        /// <param name="postData"></param>
        /// <returns>List of query parameters</returns>
        public string GetSignature(string scriptName, NameValueCollection postData)
        { 
            var signature = postData.AllKeys.OrderBy(s => s).Aggregate(scriptName + ";", (current, key) => current + postData[key] + ";");
            signature += _platronPaymentSettings.SecretKey;
           
            return GetMD5(signature).ToLower();
        }

        /// <summary>
        /// Returns only the name of the script
        /// </summary>
        /// <param name="scriptUrl">Script URL</param>
        /// <returns></returns>
        public string GetScriptName(string scriptUrl)
        {
            return scriptUrl.Split('/').Last().Split('?').First();
        }

        /// <summary>
        /// Creates an MD5 hash sum from string
        /// </summary>
        /// <param name="strToMD5">String to create an MD5 hash sum</param>
        /// <returns>MD5 hash sum</returns>
        private static string GetMD5(string strToMD5)
        {
            var enc = Encoding.Default.GetEncoder();
            var length = strToMD5.Length;
            var data = new byte[length];
            enc.GetBytes(strToMD5.ToCharArray(), 0, length, data, 0, true);
            byte[] result;

            using (var md5 = new MD5CryptoServiceProvider())
            {
                result = md5.ComputeHash(data);
            }

            return BitConverter.ToString(result)
                .Replace("-", string.Empty).ToLower();
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>true - hide; false - display.</returns>
        public bool HidePaymentMethod(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country
            return false;
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>Additional handling fee</returns>
        public decimal GetAdditionalHandlingFee(IList<ShoppingCartItem> cart)
        {
            var result = this.CalculateAdditionalFee(_orderTotalCalculationService, cart,
                _platronPaymentSettings.AdditionalFee, _platronPaymentSettings.AdditionalFeePercentage);
            return result;
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>Result</returns>
        public bool CanRePostProcessPayment(Order order)
        {
            //let's ensure that at least 5 seconds passed after order is placed
            //P.S. there's no any particular reason for that. we just do it
            return !((DateTime.UtcNow - order.CreatedOnUtc).TotalSeconds < 5);
        }

        /// <summary>
        /// Gets a route for provider configuration
        /// </summary>
        /// <param name="actionName">Action name</param>
        /// <param name="controllerName">Controller name</param>
        /// <param name="routeValues">Route values</param>
        public void GetConfigurationRoute(out string actionName, out string controllerName, out RouteValueDictionary routeValues)
        {
            actionName = "Configure";
            controllerName = "PaymentPlatron";
            routeValues = new RouteValueDictionary { { "Namespaces", "Nop.Plugin.Payments.Platron.Controllers" }, { "area", null } };
        }

        /// <summary>
        /// Gets a route for payment info
        /// </summary>
        /// <param name="actionName">Action name</param>
        /// <param name="controllerName">Controller name</param>
        /// <param name="routeValues">Route values</param>
        public void GetPaymentInfoRoute(out string actionName, out string controllerName, out RouteValueDictionary routeValues)
        {
            actionName = "PaymentInfo";
            controllerName = "PaymentPlatron";
            routeValues = new RouteValueDictionary { { "Namespaces", "Nop.Plugin.Payments.Platron.Controllers" }, { "area", null } };
        }

        /// <summary>
        /// Get controller type
        /// </summary>
        /// <returns>Controller type</returns>
        public Type GetControllerType()
        {
            return typeof(PaymentPlatronController);
        }
 
        /// <summary>
        /// Install plugin method
        /// </summary>
        public override void Install()
        {
            //settings
            var settings = new PlatronPaymentSettings();
            _settingService.SaveSetting(settings);

            //locales
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.Platron.Fields.MerchantId", "The Platron Merchan ID");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.Platron.Fields.MerchantId.Hint", "Specify the Platron Merchan ID of your store on the website Platron.ru.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.Platron.Fields.SecretKey", "Secret key");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.Platron.Fields.SecretKey.Hint", "Set the secret key.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.Platron.Fields.TestingMode", "Test mode");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.Platron.Fields.TestingMode.Hint", "Check to enable test mode.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.Platron.Fields.DescriptionTamplate", "Order description template");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.Platron.Fields.DescriptionTamplate.Hint", "Template text transmitted in the description on the website. There should not be empty. $orderId - Order number.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.Platron.Fields.AdditionalFee", "Additional fee");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.Platron.Fields.AdditionalFee.Hint", "Enter additional fee to charge your customers.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.Platron.Fields.AdditionalFeePercentage", "Additional fee. Use percentage");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.Platron.Fields.AdditionalFeePercentage.Hint", "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.Platron.Fields.RedirectionTip", "For payment you will be redirected to the website Platron.ru.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.Platron.Fields.PaymentMethodDescription", "For payment you will be redirected to the website Platron.ru.");

            base.Install();
        }

        /// <summary>
        /// Uninstall plugin method
        /// </summary>
        public override void Uninstall()
        {
            //settings
            _settingService.DeleteSetting<PlatronPaymentSettings>();

            //locales
            this.DeletePluginLocaleResource("Plugins.Payments.Platron.Fields.MerchantId");
            this.DeletePluginLocaleResource("Plugins.Payments.Platron.Fields.MerchantId.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.Platron.Fields.SecretKey");
            this.DeletePluginLocaleResource("Plugins.Payments.Platron.Fields.SecretKey.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.Platron.Fields.TestingMode");
            this.DeletePluginLocaleResource("Plugins.Payments.Platron.Fields.TestingMode.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.Platron.Fields.DescriptionTamplate");
            this.DeletePluginLocaleResource("Plugins.Payments.Platron.Fields.DescriptionTamplate.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.Platron.Fields.AdditionalFee");
            this.DeletePluginLocaleResource("Plugins.Payments.Platron.Fields.AdditionalFee.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.Platron.Fields.AdditionalFeePercentage");
            this.DeletePluginLocaleResource("Plugins.Payments.Platron.Fields.AdditionalFeePercentage.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.Platron.Fields.RedirectionTip");
            this.DeletePluginLocaleResource("Plugins.Payments.Platron.Fields.PaymentMethodDescription");

            base.Uninstall();
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>Capture payment result</returns>
        public CapturePaymentResult Capture(CapturePaymentRequest capturePaymentRequest)
        {
            var result = new CapturePaymentResult();
            result.AddError("Capture method not supported");
            return result;
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public RefundPaymentResult Refund(RefundPaymentRequest refundPaymentRequest)
        {
            var result = new RefundPaymentResult();
            result.AddError("Refund method not supported");
            return result;
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public VoidPaymentResult Void(VoidPaymentRequest voidPaymentRequest)
        {
            var result = new VoidPaymentResult();
            result.AddError("Void method not supported");
            return result;
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessRecurringPayment(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();
            result.AddError("Recurring payment not supported");
            return result;
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public CancelRecurringPaymentResult CancelRecurringPayment(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            var result = new CancelRecurringPaymentResult();
            result.AddError("Recurring payment not supported");
            return result;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType
        {
            get { return RecurringPaymentType.NotSupported; }
        }

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType
        {
            get { return PaymentMethodType.Redirection; }
        }

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        public string PaymentMethodDescription
        {
            get { return _localizationService.GetResource("Plugins.Payments.Platron.Fields.PaymentMethodDescription"); }
        }

        #endregion
    }
}
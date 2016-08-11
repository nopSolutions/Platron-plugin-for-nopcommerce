using Nop.Web.Framework;
using Nop.Web.Framework.Mvc;

namespace Nop.Plugin.Payments.Platron.Models
{
    public class ConfigurationModel : BaseNopModel
    {
        public int ActiveStoreScopeConfiguration { get; set; }

        /// <summary>
        /// The Platron Merchan ID
        /// </summary>
        [NopResourceDisplayName("Plugins.Payments.Platron.Fields.MerchantId")]
        public string MerchantId { get; set; }
        public bool MerchantIdOverrideForStore { get; set; }

        /// <summary>
        /// Secret key
        /// </summary>
        [NopResourceDisplayName("Plugins.Payments.Platron.Fields.SecretKey")]
        public string SecretKey { get; set; }
        public bool SecretKeyOverrideForStore { get; set; }

        /// <summary>
        /// Indicates that the request is sent in a test mode
        /// </summary>
        [NopResourceDisplayName("Plugins.Payments.Platron.Fields.TestingMode")]
        public bool TestingMode { get; set; }
        public bool TestingModeOverrideForStore { get; set; }

        /// <summary>
        /// Template description of the order for the Platron system
        /// </summary>
        [NopResourceDisplayName("Plugins.Payments.Platron.Fields.DescriptionTamplate")]
        public string DescriptionTemplate { get; set; }
        public bool DescriptionTamplateOverrideForStore { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to "additional fee" is specified as percentage. true - percentage, false - fixed value
        /// </summary>
        [NopResourceDisplayName("Plugins.Payments.Platron.Fields.AdditionalFeePercentage")]
        public bool AdditionalFeePercentage { get; set; }
        public bool AdditionalFeePercentageOverrideForStore { get; set; }

        /// <summary>
        /// Additional fee
        /// </summary>
        [NopResourceDisplayName("Plugins.Payments.Platron.Fields.AdditionalFee")]
        public decimal AdditionalFee { get; set; }
        public bool AdditionalFeeOverrideForStore { get; set; }
    }
}
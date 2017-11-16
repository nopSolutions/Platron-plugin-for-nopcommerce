using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.Platron
{
    public class PlatronPaymentSettings : ISettings
    {
        /// <summary>
        /// The Platron Merchan ID
        /// </summary>
        public string MerchantId { get; set; }

        /// <summary>
        /// Secret key
        /// </summary>
        public string SecretKey { get; set; }

        /// <summary>
        /// Indicates that the request is sent in a test mode
        /// </summary>
        public bool TestingMode { get; set; }

        /// <summary>
        /// Template description of the order for the Platron system
        /// </summary>
        public string DescriptionTemplate { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to "additional fee" is specified as percentage. true - percentage, false - fixed value
        /// </summary>
        public bool AdditionalFeePercentage { get; set; }

        /// <summary>
        /// Additional fee
        /// </summary>
        public decimal AdditionalFee { get; set; }
    }
}

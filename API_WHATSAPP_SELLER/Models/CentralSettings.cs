using API_WHATSAPP_SELLER.Models.Enum;

namespace API_WHATSAPP_SELLER.Models
{
    public class CentralSettings
    {
        public CanalEnvio Canal { get; set; } = CanalEnvio.Twilio;
        public string ApiUrl { get; set; } = string.Empty;
        public string ApiToken { get; set; } = string.Empty;
        public string SharedSecret { get; set; } = string.Empty;

        // Com Canal=Central as três chaves são obrigatórias; checado no boot
        // (ValidateOnStart em Program.cs).
        public bool EhValido()
        {
            if (Canal != CanalEnvio.Central)
                return true;

            return Uri.TryCreate(ApiUrl, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && !string.IsNullOrWhiteSpace(ApiToken)
                && !string.IsNullOrWhiteSpace(SharedSecret);
        }
    }
}

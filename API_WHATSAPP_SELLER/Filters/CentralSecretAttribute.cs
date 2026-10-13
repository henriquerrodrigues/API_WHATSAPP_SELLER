using System.Security.Cryptography;
using System.Text;
using API_WHATSAPP_SELLER.Models;
using API_WHATSAPP_SELLER.Models.Enum;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace API_WHATSAPP_SELLER.Filters
{
    // Com Canal=Central, só a central chama webhook/reset: exige o header
    // X-Central-Secret igual a Central__SharedSecret. Com Canal=Twilio não checa
    // nada (a Twilio chama o webhook direto, como antes).
    public class CentralSecretAttribute : ActionFilterAttribute
    {
        public const string Header = "X-Central-Secret";

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var settings = context.HttpContext.RequestServices
                .GetRequiredService<IOptions<CentralSettings>>().Value;

            if (settings.Canal != CanalEnvio.Central)
                return;

            var recebido = context.HttpContext.Request.Headers[Header].ToString();
            if (string.IsNullOrEmpty(settings.SharedSecret) || !SegredoConfere(recebido, settings.SharedSecret))
                context.Result = new UnauthorizedResult();
        }

        private static bool SegredoConfere(string recebido, string esperado) =>
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(recebido),
                Encoding.UTF8.GetBytes(esperado));
    }
}

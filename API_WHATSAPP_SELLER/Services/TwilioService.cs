using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;
using Microsoft.Extensions.Options;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace API_WHATSAPP_SELLER.Services
{
    public class TwilioService : IEnvioMensagemService
    {
        private readonly TwilioSettings _settings;

        public TwilioService(IOptions<TwilioSettings> twilioOptions)
        {
            _settings = twilioOptions.Value;
        }

        public async Task EnviarMensagem( string telefone, string mensagem)
        {
            TwilioClient.Init(_settings.AccountSid, _settings.AuthToken );

            await MessageResource.CreateAsync(
                from: new PhoneNumber(_settings.WhatsappNumber),
                to: new PhoneNumber(telefone),
                body: mensagem
            );
        }
    }
}

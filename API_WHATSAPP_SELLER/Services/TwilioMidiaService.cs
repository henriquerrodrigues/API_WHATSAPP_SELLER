using System.Net.Http.Headers;
using System.Text;
using API_WHATSAPP_SELLER.Exceptions;
using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;
using Microsoft.Extensions.Options;

namespace API_WHATSAPP_SELLER.Services
{
    public class TwilioMidiaService : ITwilioMidiaService
    {
        public const long TamanhoMaximoBytes = 5 * 1024 * 1024;

        private readonly HttpClient _httpClient;
        private readonly TwilioSettings _settings;

        public TwilioMidiaService(HttpClient httpClient, IOptions<TwilioSettings> twilioOptions)
        {
            _httpClient = httpClient;
            _settings = twilioOptions.Value;
        }

        // MediaUrl do Twilio exige Basic Auth (AccountSid:AuthToken). O Twilio
        // responde com redirect pro storage; o HttpClient segue e descarta o
        // header Authorization ao trocar de host, o que é o esperado.
        public async Task<MidiaBaixada> BaixarAsync(string url, CancellationToken ct = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var credenciais = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credenciais);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength > TamanhoMaximoBytes)
                throw new MidiaMuitoGrandeException(TamanhoMaximoBytes);

            // Content-Length pode faltar: lê em blocos e aborta ao passar do limite
            // em vez de carregar tudo na memória primeiro.
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var bloco = new byte[81920];
            int lidos;
            while ((lidos = await stream.ReadAsync(bloco, ct)) > 0)
            {
                if (buffer.Length + lidos > TamanhoMaximoBytes)
                    throw new MidiaMuitoGrandeException(TamanhoMaximoBytes);

                buffer.Write(bloco, 0, lidos);
            }

            return new MidiaBaixada
            {
                Conteudo = buffer.ToArray(),
                ContentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty
            };
        }
    }
}

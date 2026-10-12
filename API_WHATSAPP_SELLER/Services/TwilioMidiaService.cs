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
        public static readonly TimeSpan TempoMaximo = TimeSpan.FromSeconds(10);
        private const string HostTwilio = "api.twilio.com";

        private readonly HttpClient _httpClient;
        private readonly TwilioSettings _settings;

        public TwilioMidiaService(HttpClient httpClient, IOptions<TwilioSettings> twilioOptions)
        {
            _httpClient = httpClient;
            _settings = twilioOptions.Value;
        }

        // MediaUrl do Twilio exige Basic Auth (AccountSid:AuthToken). O Twilio
        // responde 307 pra uma URL pré-assinada de storage que não precisa de
        // auth; o .NET limpa o header Authorization em todo redirect automático
        // e recusa redirect de https para http.
        public async Task<MidiaBaixada> BaixarAsync(string url, CancellationToken ct = default)
        {
            // Credenciais só vão pro Twilio: a URL vem do webhook (MediaUrl0),
            // que não é autenticado, então qualquer outro destino é rejeitado.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(uri.Host, HostTwilio, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"A URL da mídia deve ser https://{HostTwilio}/...", nameof(url));
            }

            // HttpClient.Timeout só cobre até os headers (ResponseHeadersRead);
            // este limite cobre também a leitura do corpo.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TempoMaximo);

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var credenciais = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credenciais);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength > TamanhoMaximoBytes)
                throw new MidiaMuitoGrandeException(TamanhoMaximoBytes);

            // Content-Length pode faltar: lê em blocos e aborta ao passar do limite
            // em vez de carregar tudo na memória primeiro.
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var buffer = new MemoryStream();
            var bloco = new byte[81920];
            int lidos;
            while ((lidos = await stream.ReadAsync(bloco, cts.Token)) > 0)
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

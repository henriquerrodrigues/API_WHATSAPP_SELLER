using System.Net.Http.Headers;
using System.Net.Http.Json;
using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;
using Microsoft.Extensions.Options;

namespace API_WHATSAPP_SELLER.Services
{
    // Resposta do bot pela API externa da central (POST /v1/api/external/:apiId),
    // para a mensagem aparecer no ticket. A central normaliza "whatsapp:+55...".
    public class CentralMensagemService : IEnvioMensagemService
    {
        private readonly HttpClient _httpClient;
        private readonly CentralSettings _settings;

        public CentralMensagemService(HttpClient httpClient, IOptions<CentralSettings> centralOptions)
        {
            _httpClient = httpClient;
            _settings = centralOptions.Value;
        }

        public async Task EnviarMensagem(string telefone, string mensagem)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _settings.ApiUrl)
            {
                Content = JsonContent.Create(new
                {
                    number = telefone,
                    body = mensagem,
                    externalKey = Guid.NewGuid().ToString()
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiToken);

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var corpo = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Central respondeu {(int)response.StatusCode}: {corpo}");
            }
        }
    }
}

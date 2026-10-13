using System.Net;
using System.Text;
using System.Text.Json;
using API_WHATSAPP_SELLER.Models;
using API_WHATSAPP_SELLER.Models.Enum;
using API_WHATSAPP_SELLER.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace API_WHATSAPP_SELLER.Tests.Services
{
    public class CentralMensagemServiceTests
    {
        private const string ApiUrl = "https://central.example/v1/api/external/api-1";
        private const string Token = "tok-123";

        private static CentralMensagemService CriarSut(FakeHandler handler)
        {
            var settings = Options.Create(new CentralSettings
            {
                Canal = CanalEnvio.Central,
                ApiUrl = ApiUrl,
                ApiToken = Token,
                SharedSecret = "segredo"
            });
            return new CentralMensagemService(new HttpClient(handler), settings);
        }

        private static HttpResponseMessage Ok() => new(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"message\":\"Message sent\"}", Encoding.UTF8, "application/json")
        };

        [Fact]
        public async Task EnviarMensagem_PostJsonComNumberBodyEExternalKey()
        {
            var handler = new FakeHandler(_ => Ok());

            await CriarSut(handler).EnviarMensagem("whatsapp:+5511999999999", "Olá!");

            var requisicao = handler.Requisicoes.Single();
            requisicao.Method.Should().Be(HttpMethod.Post);
            requisicao.RequestUri!.ToString().Should().Be(ApiUrl);
            requisicao.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");

            using var json = JsonDocument.Parse(handler.Corpos.Single());
            json.RootElement.GetProperty("number").GetString().Should().Be("whatsapp:+5511999999999");
            json.RootElement.GetProperty("body").GetString().Should().Be("Olá!");
            Guid.TryParse(json.RootElement.GetProperty("externalKey").GetString(), out _).Should().BeTrue();
        }

        [Fact]
        public async Task EnviarMensagem_EnviaBearerComApiToken()
        {
            var handler = new FakeHandler(_ => Ok());

            await CriarSut(handler).EnviarMensagem("whatsapp:+5511999999999", "Olá!");

            var auth = handler.Requisicoes.Single().Headers.Authorization!;
            auth.Scheme.Should().Be("Bearer");
            auth.Parameter.Should().Be(Token);
        }

        [Fact]
        public async Task EnviarMensagem_ExternalKeyDiferenteACadaEnvio()
        {
            var handler = new FakeHandler(_ => Ok());
            var sut = CriarSut(handler);

            await sut.EnviarMensagem("whatsapp:+5511999999999", "um");
            await sut.EnviarMensagem("whatsapp:+5511999999999", "dois");

            var chaves = handler.Corpos
                .Select(c => JsonDocument.Parse(c).RootElement.GetProperty("externalKey").GetString())
                .ToList();
            chaves.Should().OnlyHaveUniqueItems();
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.BadGateway)]
        public async Task EnviarMensagem_StatusNaoSucesso_LancaComStatusECorpoSemToken(HttpStatusCode status)
        {
            var handler = new FakeHandler(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent("{\"error\":\"ERR_TWILIO_SEND: 63016\"}")
            });

            var act = () => CriarSut(handler).EnviarMensagem("whatsapp:+5511999999999", "Olá!");

            var erro = await act.Should().ThrowAsync<InvalidOperationException>();
            erro.Which.Message.Should().Contain(((int)status).ToString());
            erro.Which.Message.Should().Contain("ERR_TWILIO_SEND: 63016");
            erro.Which.Message.Should().NotContain(Token);
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

            public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                _responder = responder;
            }

            public List<HttpRequestMessage> Requisicoes { get; } = new();
            public List<string> Corpos { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requisicoes.Add(request);
                // O serviço descarta a requisição ao sair; o corpo é lido aqui.
                Corpos.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
                return _responder(request);
            }
        }
    }
}

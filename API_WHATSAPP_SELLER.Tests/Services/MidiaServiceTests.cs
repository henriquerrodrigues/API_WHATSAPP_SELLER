using System.Net;
using System.Net.Http.Headers;
using System.Text;
using API_WHATSAPP_SELLER.Exceptions;
using API_WHATSAPP_SELLER.Models;
using API_WHATSAPP_SELLER.Models.Enum;
using API_WHATSAPP_SELLER.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace API_WHATSAPP_SELLER.Tests.Services
{
    public class MidiaServiceTests
    {
        private const string Url = "https://api.twilio.com/2010-04-01/Accounts/AC123/Messages/MM1/Media/ME1";
        private const int Limite = 5 * 1024 * 1024;

        private const string ApiUrlCentral = "https://central.example/v1/api/external/api-1";
        private const string UrlCentral = "https://central.example/public/1700000000000.jpeg";

        private static MidiaService CriarSut(FakeHandler handler, string apiUrlCentral = ApiUrlCentral)
        {
            var twilio = Options.Create(new TwilioSettings { AccountSid = "AC123", AuthToken = "segredo" });
            var central = Options.Create(new CentralSettings { Canal = CanalEnvio.Central, ApiUrl = apiUrlCentral });
            return new MidiaService(new HttpClient(handler), twilio, central);
        }

        private static HttpResponseMessage RespostaImagem(byte[] conteudo, string contentType = "image/jpeg")
        {
            var content = new ByteArrayContent(conteudo);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }

        [Fact]
        public async Task BaixarAsync_EnviaBasicAuthComAccountSidEAuthToken()
        {
            var handler = new FakeHandler(_ => RespostaImagem(new byte[] { 1 }));

            await CriarSut(handler).BaixarAsync(Url);

            var esperado = Convert.ToBase64String(Encoding.ASCII.GetBytes("AC123:segredo"));
            handler.Requisicao!.Headers.Authorization!.Scheme.Should().Be("Basic");
            handler.Requisicao.Headers.Authorization.Parameter.Should().Be(esperado);
            handler.Requisicao.RequestUri!.ToString().Should().Be(Url);
        }

        [Fact]
        public async Task BaixarAsync_200_RetornaBytesEContentType()
        {
            var bytes = new byte[] { 1, 2, 3 };
            var handler = new FakeHandler(_ => RespostaImagem(bytes, "image/png"));

            var midia = await CriarSut(handler).BaixarAsync(Url);

            midia.Conteudo.Should().Equal(bytes);
            midia.ContentType.Should().Be("image/png");
        }

        [Fact]
        public async Task BaixarAsync_ExatamenteNoLimite_Aceita()
        {
            var handler = new FakeHandler(_ => RespostaImagem(new byte[Limite]));

            var midia = await CriarSut(handler).BaixarAsync(Url);

            midia.Conteudo.Length.Should().Be(Limite);
        }

        [Fact]
        public async Task BaixarAsync_ContentLengthAcimaDoLimite_LancaMidiaMuitoGrande()
        {
            var handler = new FakeHandler(_ =>
            {
                // Corpo minúsculo, mas Content-Length declarado acima do limite:
                // só a checagem do header pode barrar.
                var content = new ByteArrayContent(new byte[] { 1 });
                content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                content.Headers.ContentLength = Limite + 1;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            });

            var act = () => CriarSut(handler).BaixarAsync(Url);

            await act.Should().ThrowAsync<MidiaMuitoGrandeException>();
        }

        [Fact]
        public async Task BaixarAsync_SemContentLengthPassandoDoLimite_LancaMidiaMuitoGrande()
        {
            var handler = new FakeHandler(_ =>
            {
                // StreamContent sobre stream não-buscável não informa Content-Length,
                // forçando o caminho de leitura incremental.
                var content = new StreamContent(new StreamNaoBuscavel(new byte[Limite + 1]));
                content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            });

            var act = () => CriarSut(handler).BaixarAsync(Url);

            await act.Should().ThrowAsync<MidiaMuitoGrandeException>();
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public async Task BaixarAsync_StatusNaoSucesso_LancaHttpRequestException(HttpStatusCode status)
        {
            var handler = new FakeHandler(_ => new HttpResponseMessage(status));

            var act = () => CriarSut(handler).BaixarAsync(Url);

            await act.Should().ThrowAsync<HttpRequestException>();
        }

        [Theory]
        [InlineData("https://evil.example/x")]
        [InlineData("http://api.twilio.com/2010-04-01/x")]
        [InlineData("https://api.twilio.com.evil.example/x")]
        [InlineData("nao-e-url")]
        [InlineData("http://central.example/public/x.jpeg")]
        [InlineData("https://central.example.evil.example/public/x.jpeg")]
        public async Task BaixarAsync_HostNaoPermitido_LancaArgumentExceptionSemChamarRede(string url)
        {
            var handler = new FakeHandler(_ => RespostaImagem(new byte[] { 1 }));

            var act = () => CriarSut(handler).BaixarAsync(url);

            await act.Should().ThrowAsync<ArgumentException>();
            handler.Requisicao.Should().BeNull();
        }

        [Fact]
        public async Task BaixarAsync_HostDaCentral_BaixaSemAuthorization()
        {
            var bytes = new byte[] { 7, 8 };
            var handler = new FakeHandler(_ => RespostaImagem(bytes));

            var midia = await CriarSut(handler).BaixarAsync(UrlCentral);

            midia.Conteudo.Should().Equal(bytes);
            handler.Requisicao!.Headers.Authorization.Should().BeNull();
            handler.Requisicao.RequestUri!.ToString().Should().Be(UrlCentral);
        }

        [Fact]
        public async Task BaixarAsync_HostDaCentralSemApiUrlConfigurada_Rejeita()
        {
            var handler = new FakeHandler(_ => RespostaImagem(new byte[] { 1 }));

            var act = () => CriarSut(handler, apiUrlCentral: "").BaixarAsync(UrlCentral);

            await act.Should().ThrowAsync<ArgumentException>();
            handler.Requisicao.Should().BeNull();
        }

        [Fact]
        public async Task BaixarAsync_HostDaCentralAcimaDoLimite_LancaMidiaMuitoGrande()
        {
            var handler = new FakeHandler(_ => RespostaImagem(new byte[Limite + 1]));

            var act = () => CriarSut(handler).BaixarAsync(UrlCentral);

            await act.Should().ThrowAsync<MidiaMuitoGrandeException>();
        }

        [Fact]
        public async Task BaixarAsync_TokenJaCancelado_LancaOperationCanceled()
        {
            var handler = new FakeHandler(_ => RespostaImagem(new byte[] { 1 }));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var act = () => CriarSut(handler).BaixarAsync(Url, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

            public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                _responder = responder;
            }

            public HttpRequestMessage? Requisicao { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requisicao = request;
                return Task.FromResult(_responder(request));
            }
        }

        private sealed class StreamNaoBuscavel : MemoryStream
        {
            public StreamNaoBuscavel(byte[] buffer) : base(buffer) { }
            public override bool CanSeek => false;
        }
    }
}

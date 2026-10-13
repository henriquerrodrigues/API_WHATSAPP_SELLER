using API_WHATSAPP_SELLER.Controllers;
using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;
using API_WHATSAPP_SELLER.Models.Enum;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace API_WHATSAPP_SELLER.Tests.Controllers
{
    public class WhatsappControllerTests
    {
        private readonly Mock<IBotService> _botServiceMock = new();
        private readonly Mock<IEnvioMensagemService> _envioServiceMock = new();
        private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
        private readonly CentralSettings _central = new();
        private MensagemRecebida? _recebida;

        public WhatsappControllerTests()
        {
            _botServiceMock
                .Setup(b => b.ProcessarMensagem(It.IsAny<MensagemRecebida>()))
                .Callback<MensagemRecebida>(m => _recebida = m)
                .Returns(Task.CompletedTask);
        }

        private WhatsappController CriarController(Dictionary<string, StringValues> campos)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.ContentType = "application/x-www-form-urlencoded";
            httpContext.Request.Form = new FormCollection(campos);

            return new WhatsappController(_botServiceMock.Object, _envioServiceMock.Object, _cache, Options.Create(_central))
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext }
            };
        }

        private Task<IActionResult> ReceberComForm(Dictionary<string, StringValues> campos)
        {
            return CriarController(campos).ReceberMensagem();
        }

        private static Dictionary<string, StringValues> Form(string? messageSid)
        {
            var campos = new Dictionary<string, StringValues>
            {
                ["From"] = "whatsapp:+5511999999999",
                ["Body"] = "oi",
                ["NumMedia"] = "0"
            };
            if (messageSid != null)
                campos["MessageSid"] = messageSid;
            return campos;
        }

        [Fact]
        public async Task ReceberMensagem_NumMediaZero_MidiaNula()
        {
            await ReceberComForm(new()
            {
                ["From"] = "whatsapp:+5511999999999",
                ["Body"] = "oi",
                ["NumMedia"] = "0"
            });

            _recebida!.Telefone.Should().Be("whatsapp:+5511999999999");
            _recebida.Texto.Should().Be("oi");
            _recebida.Midia.Should().BeNull();
        }

        [Fact]
        public async Task ReceberMensagem_NumMediaUm_PreencheMidiaComPrimeiraMidia()
        {
            await ReceberComForm(new()
            {
                ["From"] = "whatsapp:+5511999999999",
                ["Body"] = "",
                ["NumMedia"] = "1",
                ["MediaUrl0"] = "https://api.twilio.com/media/ME1",
                ["MediaContentType0"] = "image/jpeg"
            });

            _recebida!.Midia.Should().NotBeNull();
            _recebida.Midia!.Url.Should().Be("https://api.twilio.com/media/ME1");
            _recebida.Midia.ContentType.Should().Be("image/jpeg");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("abc")]
        public async Task ReceberMensagem_NumMediaAusenteOuInvalido_MidiaNula(string? numMedia)
        {
            var campos = new Dictionary<string, StringValues>
            {
                ["From"] = "whatsapp:+5511999999999",
                ["Body"] = "oi",
                ["MediaUrl0"] = "https://api.twilio.com/media/ME1",
                ["MediaContentType0"] = "image/jpeg"
            };
            if (numMedia != null)
                campos["NumMedia"] = numMedia;

            await ReceberComForm(campos);

            _recebida!.Midia.Should().BeNull();
        }

        [Fact]
        public async Task ReceberMensagem_MessageSidRepetido_ProcessaUmaVezERespondeOk()
        {
            await ReceberComForm(Form("SM1"));
            var segunda = await ReceberComForm(Form("SM1"));

            segunda.Should().BeOfType<OkResult>();
            _botServiceMock.Verify(b => b.ProcessarMensagem(It.IsAny<MensagemRecebida>()), Times.Once);
        }

        [Fact]
        public async Task ReceberMensagem_MessageSidsDiferentes_ProcessaAmbos()
        {
            await ReceberComForm(Form("SM1"));
            await ReceberComForm(Form("SM2"));

            _botServiceMock.Verify(b => b.ProcessarMensagem(It.IsAny<MensagemRecebida>()), Times.Exactly(2));
        }

        [Fact]
        public async Task ReceberMensagem_SemMessageSid_ProcessaSempre()
        {
            await ReceberComForm(Form(null));
            await ReceberComForm(Form(null));

            _botServiceMock.Verify(b => b.ProcessarMensagem(It.IsAny<MensagemRecebida>()), Times.Exactly(2));
        }

        [Fact]
        public async Task ReceberMensagem_MarcaMessageSidAntesDeProcessar()
        {
            _botServiceMock
                .Setup(b => b.ProcessarMensagem(It.IsAny<MensagemRecebida>()))
                .ThrowsAsync(new InvalidOperationException("banco fora"));

            var primeira = () => ReceberComForm(Form("SM1"));
            await primeira.Should().ThrowAsync<InvalidOperationException>();

            var segunda = await ReceberComForm(Form("SM1"));

            segunda.Should().BeOfType<OkResult>();
            _botServiceMock.Verify(b => b.ProcessarMensagem(It.IsAny<MensagemRecebida>()), Times.Once);
        }

        [Fact]
        public async Task Resetar_CanalCentral_EncerraConversaERespondeOk()
        {
            _central.Canal = CanalEnvio.Central;

            var resultado = await CriarController(new() { ["telefone"] = "whatsapp:+5511999999999" }).Resetar();

            resultado.Should().BeOfType<OkResult>();
            _botServiceMock.Verify(b => b.EncerrarConversa("whatsapp:+5511999999999"), Times.Once);
        }

        [Fact]
        public async Task Resetar_CanalTwilio_404SemEncerrar()
        {
            _central.Canal = CanalEnvio.Twilio;

            var resultado = await CriarController(new() { ["telefone"] = "whatsapp:+5511999999999" }).Resetar();

            resultado.Should().BeOfType<NotFoundResult>();
            _botServiceMock.Verify(b => b.EncerrarConversa(It.IsAny<string>()), Times.Never);
        }
    }
}

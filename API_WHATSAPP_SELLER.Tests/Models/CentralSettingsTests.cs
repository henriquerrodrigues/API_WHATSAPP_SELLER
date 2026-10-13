using API_WHATSAPP_SELLER.Models;
using API_WHATSAPP_SELLER.Models.Enum;
using FluentAssertions;
using Xunit;

namespace API_WHATSAPP_SELLER.Tests.Models
{
    public class CentralSettingsTests
    {
        private static CentralSettings Completa() => new()
        {
            Canal = CanalEnvio.Central,
            ApiUrl = "https://central.example/v1/api/external/api-1",
            ApiToken = "tok",
            SharedSecret = "segredo"
        };

        [Fact]
        public void Padrao_CanalTwilio_Valido()
        {
            var settings = new CentralSettings();

            settings.Canal.Should().Be(CanalEnvio.Twilio);
            settings.EhValido().Should().BeTrue();
        }

        [Fact]
        public void CanalCentral_Completo_Valido()
        {
            Completa().EhValido().Should().BeTrue();
        }

        [Theory]
        [InlineData("ApiUrl")]
        [InlineData("ApiToken")]
        [InlineData("SharedSecret")]
        public void CanalCentral_FaltandoChave_Invalido(string chave)
        {
            var settings = Completa();
            switch (chave)
            {
                case "ApiUrl": settings.ApiUrl = ""; break;
                case "ApiToken": settings.ApiToken = " "; break;
                case "SharedSecret": settings.SharedSecret = ""; break;
            }

            settings.EhValido().Should().BeFalse();
        }

        [Theory]
        [InlineData("http://central.example/v1/api/external/api-1")]
        [InlineData("nao-e-url")]
        public void CanalCentral_ApiUrlNaoHttps_Invalido(string apiUrl)
        {
            var settings = Completa();
            settings.ApiUrl = apiUrl;

            settings.EhValido().Should().BeFalse();
        }
    }
}

using API_WHATSAPP_SELLER.Filters;
using API_WHATSAPP_SELLER.Models;
using API_WHATSAPP_SELLER.Models.Enum;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace API_WHATSAPP_SELLER.Tests.Filters
{
    public class CentralSecretAttributeTests
    {
        private const string Segredo = "segredo-compartilhado";

        private static ActionExecutingContext Executar(CanalEnvio canal, string? header)
        {
            var settings = new CentralSettings
            {
                Canal = canal,
                ApiUrl = "https://central.example/v1/api/external/api-1",
                ApiToken = "tok",
                SharedSecret = Segredo
            };
            var services = new ServiceCollection()
                .AddSingleton<IOptions<CentralSettings>>(Options.Create(settings))
                .BuildServiceProvider();

            var httpContext = new DefaultHttpContext { RequestServices = services };
            if (header != null)
                httpContext.Request.Headers[CentralSecretAttribute.Header] = header;

            var context = new ActionExecutingContext(
                new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>(),
                new Dictionary<string, object?>(),
                controller: new object());

            new CentralSecretAttribute().OnActionExecuting(context);
            return context;
        }

        [Fact]
        public void CanalCentral_SemHeader_401()
        {
            Executar(CanalEnvio.Central, header: null).Result.Should().BeOfType<UnauthorizedResult>();
        }

        [Theory]
        [InlineData("")]
        [InlineData("errado")]
        [InlineData("segredo-compartilhadoX")]
        public void CanalCentral_SegredoErrado_401(string header)
        {
            Executar(CanalEnvio.Central, header).Result.Should().BeOfType<UnauthorizedResult>();
        }

        [Fact]
        public void CanalCentral_SegredoCerto_Segue()
        {
            Executar(CanalEnvio.Central, Segredo).Result.Should().BeNull();
        }

        [Fact]
        public void CanalTwilio_SemHeader_Segue()
        {
            Executar(CanalEnvio.Twilio, header: null).Result.Should().BeNull();
        }
    }
}

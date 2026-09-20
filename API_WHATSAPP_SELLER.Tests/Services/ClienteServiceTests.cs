using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;
using API_WHATSAPP_SELLER.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace API_WHATSAPP_SELLER.Tests.Services
{
    public class ClienteServiceTests
    {
        private readonly Mock<IClienteRepository> _clienteRepositoryMock = new();
        private readonly ClienteService _sut;

        public ClienteServiceTests()
        {
            _sut = new ClienteService(_clienteRepositoryMock.Object);
        }

        [Fact]
        public void TryNormalizarCpfCnpj_Cpf11Digitos_Aceito()
        {
            var resultado = _sut.TryNormalizarCpfCnpj("12345678900", out var normalizado);

            resultado.Should().BeTrue();
            normalizado.Should().Be("12345678900");
        }

        [Fact]
        public void TryNormalizarCpfCnpj_Cnpj14Digitos_Aceito()
        {
            var resultado = _sut.TryNormalizarCpfCnpj("12345678000199", out var normalizado);

            resultado.Should().BeTrue();
            normalizado.Should().Be("12345678000199");
        }

        [Theory]
        [InlineData("1234567890")]
        [InlineData("123456789012")]
        public void TryNormalizarCpfCnpj_TamanhoErrado_Rejeitado(string valor)
        {
            var resultado = _sut.TryNormalizarCpfCnpj(valor, out _);

            resultado.Should().BeFalse();
        }

        [Fact]
        public void TryNormalizarCpfCnpj_ComLetra_Rejeitado()
        {
            var resultado = _sut.TryNormalizarCpfCnpj("1234567890A", out _);

            resultado.Should().BeFalse();
        }

        [Fact]
        public void TryNormalizarCpfCnpj_ComMascara_NormalizadoEAceito()
        {
            var resultado = _sut.TryNormalizarCpfCnpj("123.456.789-00", out var normalizado);

            resultado.Should().BeTrue();
            normalizado.Should().Be("12345678900");
        }

        [Fact]
        public void EmailValido_FormatoCorreto_Aceito()
        {
            _sut.EmailValido("nome@dominio.com").Should().BeTrue();
        }

        [Fact]
        public void EmailValido_SemArroba_Rejeitado()
        {
            _sut.EmailValido("nomedominio.com").Should().BeFalse();
        }

        [Fact]
        public void EmailValido_SemDominio_Rejeitado()
        {
            _sut.EmailValido("nome@").Should().BeFalse();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void NomeValido_VazioOuEspaco_Rejeitado(string nome)
        {
            _sut.NomeValido(nome).Should().BeFalse();
        }

        [Fact]
        public async Task CreateClienteAsync_ClienteNulo_LancaArgumentNullException()
        {
            Func<Task> act = () => _sut.CreateClienteAsync(null!);

            await act.Should().ThrowAsync<ArgumentNullException>();
        }
    }
}

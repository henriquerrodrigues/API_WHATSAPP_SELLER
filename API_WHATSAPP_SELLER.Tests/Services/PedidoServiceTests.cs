using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;
using API_WHATSAPP_SELLER.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace API_WHATSAPP_SELLER.Tests.Services
{
    public class PedidoServiceTests
    {
        private readonly Mock<IPedidoRepository> _pedidoRepositoryMock = new();
        private readonly Mock<IClienteRepository> _clienteRepositoryMock = new();
        private readonly PedidoService _sut;

        public PedidoServiceTests()
        {
            _sut = new PedidoService(_pedidoRepositoryMock.Object, _clienteRepositoryMock.Object);
        }

        [Fact]
        public async Task CreatePedidoAsync_MultiplasLinhasValidas_ViramItensFormatados()
        {
            _clienteRepositoryMock
                .Setup(r => r.GetByCodigoAsync(1))
                .ReturnsAsync(new Cliente { CLI_CODIGO = 1 });

            _pedidoRepositoryMock
                .Setup(r => r.CreatePedidoAsync(It.IsAny<Pedido>()))
                .ReturnsAsync(10);

            var pedido = new Pedido
            {
                PED_CLICODIGO = 1,
                PED_CORPO = "2x chocolate\n3x morango"
            };

            await _sut.CreatePedidoAsync(pedido);

            pedido.PED_CORPO.Should().Be("2X CHOCOLATE;\n3X MORANGO");
        }

        [Fact]
        public async Task CreatePedidoAsync_EspacoAntesDoX_NormalizaCorretamente()
        {
            _clienteRepositoryMock
                .Setup(r => r.GetByCodigoAsync(1))
                .ReturnsAsync(new Cliente { CLI_CODIGO = 1 });

            _pedidoRepositoryMock
                .Setup(r => r.CreatePedidoAsync(It.IsAny<Pedido>()))
                .ReturnsAsync(10);

            var pedido = new Pedido { PED_CLICODIGO = 1, PED_CORPO = "2 x produto" };

            await _sut.CreatePedidoAsync(pedido);

            pedido.PED_CORPO.Should().Be("2X PRODUTO");
        }

        [Fact]
        public async Task CreatePedidoAsync_LinhaMaiuscula_NormalizaCorretamente()
        {
            _clienteRepositoryMock
                .Setup(r => r.GetByCodigoAsync(1))
                .ReturnsAsync(new Cliente { CLI_CODIGO = 1 });

            _pedidoRepositoryMock
                .Setup(r => r.CreatePedidoAsync(It.IsAny<Pedido>()))
                .ReturnsAsync(10);

            var pedido = new Pedido { PED_CLICODIGO = 1, PED_CORPO = "3X PISTACHE" };

            await _sut.CreatePedidoAsync(pedido);

            pedido.PED_CORPO.Should().Be("3X PISTACHE");
        }

        [Fact]
        public async Task CreatePedidoAsync_LinhaVaziaEntreItens_Ignorada()
        {
            _clienteRepositoryMock
                .Setup(r => r.GetByCodigoAsync(1))
                .ReturnsAsync(new Cliente { CLI_CODIGO = 1 });

            _pedidoRepositoryMock
                .Setup(r => r.CreatePedidoAsync(It.IsAny<Pedido>()))
                .ReturnsAsync(10);

            var pedido = new Pedido { PED_CLICODIGO = 1, PED_CORPO = "2x chocolate\n\n3x morango" };

            await _sut.CreatePedidoAsync(pedido);

            pedido.PED_CORPO.Should().Be("2X CHOCOLATE;\n3X MORANGO");
        }

        [Fact]
        public async Task CreatePedidoAsync_CorpoVazio_LancaArgumentException()
        {
            _clienteRepositoryMock
                .Setup(r => r.GetByCodigoAsync(1))
                .ReturnsAsync(new Cliente { CLI_CODIGO = 1 });

            var pedido = new Pedido { PED_CLICODIGO = 1, PED_CORPO = "   " };

            Func<Task> act = () => _sut.CreatePedidoAsync(pedido);

            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public void ValidarLinhaItem_QuantidadeZero_Rejeitada()
        {
            var resultado = _sut.ValidarLinhaItem("0x produto");

            resultado.Resultado.Should().Be(ItemPedidoValidacaoResultado.QuantidadeZero);
        }

        [Fact]
        public void ValidarLinhaItem_QuantidadeAcimaDe100_Rejeitada()
        {
            var resultado = _sut.ValidarLinhaItem("101x produto");

            resultado.Resultado.Should().Be(ItemPedidoValidacaoResultado.QuantidadeMuitoAlta);
        }

        [Fact]
        public async Task CreatePedidoAsync_ClienteInexistente_LancaExcecaoENaoChamaRepositorio()
        {
            _clienteRepositoryMock
                .Setup(r => r.GetByCodigoAsync(It.IsAny<int>()))
                .ReturnsAsync((Cliente?)null);

            var pedido = new Pedido { PED_CLICODIGO = 999, PED_CORPO = "2x chocolate" };

            Func<Task> act = () => _sut.CreatePedidoAsync(pedido);

            await act.Should().ThrowAsync<InvalidOperationException>();
            _pedidoRepositoryMock.Verify(r => r.CreatePedidoAsync(It.IsAny<Pedido>()), Times.Never);
        }
    }
}

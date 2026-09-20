using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;
using API_WHATSAPP_SELLER.Models.Enum;
using API_WHATSAPP_SELLER.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace API_WHATSAPP_SELLER.Tests.Services
{
    public class BotServiceTests
    {
        private readonly Mock<ITwilioService> _twilioServiceMock = new();
        private readonly Mock<IConversaRepository> _conversaRepositoryMock = new();
        private readonly Mock<IClienteService> _clienteServiceMock = new();
        private readonly Mock<IPedidoService> _pedidoServiceMock = new();
        private readonly Mock<IClienteRepository> _clienteRepositoryMock = new();
        private readonly BotService _sut;

        public BotServiceTests()
        {
            _sut = new BotService(
                _twilioServiceMock.Object,
                _conversaRepositoryMock.Object,
                _clienteServiceMock.Object,
                _pedidoServiceMock.Object,
                _clienteRepositoryMock.Object);

            _clienteServiceMock
                .Setup(s => s.TryNormalizarCpfCnpj(It.IsAny<string>(), out It.Ref<string>.IsAny))
                .Returns((string input, out string normalizado) =>
                {
                    normalizado = new string(input.Where(char.IsDigit).ToArray());
                    return normalizado.Length == 11 || normalizado.Length == 14;
                });

            _clienteServiceMock.Setup(s => s.EmailValido(It.IsAny<string>()))
                .Returns<string>(email => !string.IsNullOrWhiteSpace(email) && email.Contains('@') && email.Contains('.'));

            _clienteServiceMock.Setup(s => s.NomeValido(It.IsAny<string>()))
                .Returns<string>(nome => !string.IsNullOrWhiteSpace(nome));
        }

        private static Conversa NovaConversa(EtapaConversa etapa, string telefone = "11999999999")
        {
            return new Conversa { CON_TELEFONE = telefone, CON_ETAPA = etapa };
        }

        [Fact]
        public async Task ProcessarMensagem_TelefoneNovo_IniciaConversaAguardandoCpf()
        {
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone("11999999999")).ReturnsAsync((Conversa?)null);

            await _sut.ProcessarMensagem("11999999999", "oi");

            _conversaRepositoryMock.Verify(r => r.Criar(It.Is<Conversa>(c => c.CON_ETAPA == EtapaConversa.AguardandoCpf)), Times.Once);
        }

        [Fact]
        public async Task ProcessarMensagem_CpfJaCadastrado_PulaDireitoParaMenuCliente()
        {
            var conversa = NovaConversa(EtapaConversa.AguardandoCpf);
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);
            _clienteServiceMock.Setup(s => s.BuscaPorCpf("12345678900"))
                .ReturnsAsync(new Cliente { CLI_CODIGO = 1, CLI_NOME = "Fulano", CLI_CPFCNPJ = "12345678900" });

            await _sut.ProcessarMensagem(conversa.CON_TELEFONE, "12345678900");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.MenuCliente);
            _conversaRepositoryMock.Verify(r => r.Atualizar(It.Is<Conversa>(c => c.CON_ETAPA == EtapaConversa.MenuCliente)), Times.Once);
        }

        [Fact]
        public async Task ProcessarMensagem_CpfNaoEncontradoMasTelefoneCadastrado_UsaDadosDoClienteExistente()
        {
            var conversa = NovaConversa(EtapaConversa.AguardandoCpf, telefone: "whatsapp:+5511999999999");
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);
            _clienteServiceMock.Setup(s => s.BuscaPorCpf("98765432100")).ReturnsAsync((Cliente?)null);
            _clienteRepositoryMock.Setup(r => r.GetByTelefoneAsync("11999999999"))
                .ReturnsAsync(new Cliente { CLI_CODIGO = 5, CLI_NOME = "Ciclana", CLI_CPFCNPJ = "11122233344" });

            await _sut.ProcessarMensagem(conversa.CON_TELEFONE, "98765432100");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.MenuCliente);
            conversa.CON_CPF.Should().Be("11122233344");
            conversa.CON_CLICODIGO.Should().Be(5);
            _clienteRepositoryMock.Verify(r => r.GetByTelefoneAsync("11999999999"), Times.Once);
        }

        [Fact]
        public async Task ProcessarMensagem_CpfETelefoneNaoEncontrados_SegueParaAguardandoNome()
        {
            var conversa = NovaConversa(EtapaConversa.AguardandoCpf);
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);
            _clienteServiceMock.Setup(s => s.BuscaPorCpf("98765432100")).ReturnsAsync((Cliente?)null);
            _clienteRepositoryMock.Setup(r => r.GetByTelefoneAsync(It.IsAny<string>())).ReturnsAsync((Cliente?)null);

            await _sut.ProcessarMensagem(conversa.CON_TELEFONE, "98765432100");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoNome);
        }

        [Fact]
        public async Task ProcessarMensagem_CpfInvalido_PermaneceAguardandoCpf()
        {
            var conversa = NovaConversa(EtapaConversa.AguardandoCpf);
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await _sut.ProcessarMensagem(conversa.CON_TELEFONE, "123");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoCpf);
            _conversaRepositoryMock.Verify(r => r.Atualizar(It.IsAny<Conversa>()), Times.Never);
        }

        [Fact]
        public async Task ProcessarMensagem_EmailInvalido_PermaneceAguardandoEmail()
        {
            var conversa = NovaConversa(EtapaConversa.AguardandoEmail);
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await _sut.ProcessarMensagem(conversa.CON_TELEFONE, "emailinvalido");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoEmail);
            _clienteServiceMock.Verify(s => s.CreateClienteAsync(It.IsAny<Cliente>()), Times.Never);
        }

        [Fact]
        public async Task ProcessarMensagem_ItemFormatoNaoReconhecido_NaoAlteraUltimaMensagem()
        {
            var conversa = NovaConversa(EtapaConversa.NovoPedido);
            conversa.CON_CLICODIGO = 1;
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);
            _pedidoServiceMock.Setup(s => s.ValidarLinhaItem("produto sem quantidade"))
                .Returns(new ItemPedidoValidacao { Resultado = ItemPedidoValidacaoResultado.FormatoInvalido });

            await _sut.ProcessarMensagem(conversa.CON_TELEFONE, "produto sem quantidade");

            conversa.CON_ULTIMA_MENSAGEM.Should().BeNullOrEmpty();
            conversa.CON_ETAPA.Should().Be(EtapaConversa.NovoPedido);
        }

        [Fact]
        public async Task ProcessarMensagem_ItemQuantidadeZeroOuAcimaDe100_CarrinhoNaoMuda()
        {
            var conversa = NovaConversa(EtapaConversa.NovoPedido);
            conversa.CON_CLICODIGO = 1;
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);
            _pedidoServiceMock.Setup(s => s.ValidarLinhaItem("101x produto"))
                .Returns(new ItemPedidoValidacao { Resultado = ItemPedidoValidacaoResultado.QuantidadeMuitoAlta });

            await _sut.ProcessarMensagem(conversa.CON_TELEFONE, "101x produto");

            conversa.CON_ULTIMA_MENSAGEM.Should().BeNullOrEmpty();
        }

        [Fact]
        public async Task ProcessarMensagem_TerminarSemItens_NaoCriaPedido()
        {
            var conversa = NovaConversa(EtapaConversa.NovoPedido);
            conversa.CON_CLICODIGO = 1;
            conversa.CON_ULTIMA_MENSAGEM = string.Empty;
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await _sut.ProcessarMensagem(conversa.CON_TELEFONE, "terminar");

            _pedidoServiceMock.Verify(s => s.CreatePedidoAsync(It.IsAny<Pedido>()), Times.Never);
        }

        [Fact]
        public async Task ProcessarMensagem_TerminarComClienteCodigoNulo_ReiniciaConversa()
        {
            var conversa = NovaConversa(EtapaConversa.NovoPedido);
            conversa.CON_CLICODIGO = null;
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await _sut.ProcessarMensagem(conversa.CON_TELEFONE, "terminar");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoCpf);
        }

        [Fact]
        public async Task ProcessarMensagem_OpcaoMenuInvalida_EtapaNaoMuda()
        {
            var conversa = NovaConversa(EtapaConversa.MenuCliente);
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await _sut.ProcessarMensagem(conversa.CON_TELEFONE, "9");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.MenuCliente);
            _conversaRepositoryMock.Verify(r => r.Atualizar(It.IsAny<Conversa>()), Times.Never);
        }

        [Fact]
        public async Task ProcessarMensagem_AcompanhamentoCodigoValidoExistente_RetornaStatus()
        {
            var conversa = NovaConversa(EtapaConversa.AcompanhandoPedido);
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);
            _pedidoServiceMock.Setup(s => s.BuscarPorId(15)).ReturnsAsync(new Pedido { PED_CODIGO = "15", PED_STATUS = "Em preparo", PED_DATA_PEDIDO = DateTime.Now });

            await _sut.ProcessarMensagem(conversa.CON_TELEFONE, "15");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.MenuCliente);
            _twilioServiceMock.Verify(t => t.EnviarMensagem(conversa.CON_TELEFONE, It.Is<string>(m => m.Contains("Em preparo"))), Times.Once);
        }

        [Fact]
        public async Task ProcessarMensagem_AcompanhamentoCodigoInexistente_RetornaNaoEncontrado()
        {
            var conversa = NovaConversa(EtapaConversa.AcompanhandoPedido);
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);
            _pedidoServiceMock.Setup(s => s.BuscarPorId(99)).ReturnsAsync((Pedido?)null);

            await _sut.ProcessarMensagem(conversa.CON_TELEFONE, "99");

            _twilioServiceMock.Verify(t => t.EnviarMensagem(conversa.CON_TELEFONE, It.Is<string>(m => m.Contains("Não encontramos"))), Times.Once);
        }

        [Fact]
        public async Task ProcessarMensagem_AcompanhamentoZero_VoltaMenu()
        {
            var conversa = NovaConversa(EtapaConversa.AcompanhandoPedido);
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await _sut.ProcessarMensagem(conversa.CON_TELEFONE, "0");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.MenuCliente);
        }

        [Fact]
        public async Task ProcessarMensagem_AcompanhamentoTextoNaoNumerico_PedeNovamente()
        {
            var conversa = NovaConversa(EtapaConversa.AcompanhandoPedido);
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await _sut.ProcessarMensagem(conversa.CON_TELEFONE, "abc");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.AcompanhandoPedido);
            _pedidoServiceMock.Verify(s => s.BuscarPorId(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task FluxoFeliz_TelefoneNovoAteConfirmacaoDePedido()
        {
            var telefone = "11988887777";

            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(telefone)).ReturnsAsync((Conversa?)null);
            await _sut.ProcessarMensagem(telefone, "oi");
            var conversa = new Conversa { CON_TELEFONE = telefone, CON_ETAPA = EtapaConversa.AguardandoCpf };
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(telefone)).ReturnsAsync(conversa);

            _clienteServiceMock.Setup(s => s.BuscaPorCpf("12345678900")).ReturnsAsync((Cliente?)null);
            _clienteRepositoryMock.Setup(r => r.GetByTelefoneAsync(It.IsAny<string>())).ReturnsAsync((Cliente?)null);
            await _sut.ProcessarMensagem(telefone, "12345678900");
            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoNome);

            await _sut.ProcessarMensagem(telefone, "Fulano de Tal");
            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoEmail);

            _clienteServiceMock.Setup(s => s.CreateClienteAsync(It.IsAny<Cliente>())).ReturnsAsync(42);
            await _sut.ProcessarMensagem(telefone, "fulano@teste.com");
            conversa.CON_ETAPA.Should().Be(EtapaConversa.MenuCliente);
            conversa.CON_CLICODIGO.Should().Be(42);

            await _sut.ProcessarMensagem(telefone, "1");
            conversa.CON_ETAPA.Should().Be(EtapaConversa.NovoPedido);

            _pedidoServiceMock.Setup(s => s.ValidarLinhaItem("2x pizza"))
                .Returns(new ItemPedidoValidacao { Resultado = ItemPedidoValidacaoResultado.Valido, LinhaNormalizada = "2X PIZZA" });
            await _sut.ProcessarMensagem(telefone, "2x pizza");
            conversa.CON_ULTIMA_MENSAGEM.Should().Be("2x pizza");

            _pedidoServiceMock.Setup(s => s.CreatePedidoAsync(It.IsAny<Pedido>())).ReturnsAsync(777);
            await _sut.ProcessarMensagem(telefone, "terminar");
            conversa.CON_ETAPA.Should().Be(EtapaConversa.MenuCliente);
            conversa.CON_ULTIMA_MENSAGEM.Should().BeNullOrEmpty();
            _twilioServiceMock.Verify(t => t.EnviarMensagem(telefone, It.Is<string>(m => m.Contains("777"))), Times.Once);
        }
    }
}

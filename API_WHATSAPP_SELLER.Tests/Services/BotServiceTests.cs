using API_WHATSAPP_SELLER.Exceptions;
using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;
using API_WHATSAPP_SELLER.Models.Enum;
using API_WHATSAPP_SELLER.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace API_WHATSAPP_SELLER.Tests.Services
{
    public class BotServiceTests
    {
        private readonly Mock<IEnvioMensagemService> _envioServiceMock = new();
        private readonly Mock<IConversaRepository> _conversaRepositoryMock = new();
        private readonly Mock<IClienteService> _clienteServiceMock = new();
        private readonly Mock<IPedidoService> _pedidoServiceMock = new();
        private readonly Mock<IClienteRepository> _clienteRepositoryMock = new();
        private readonly Mock<IMidiaService> _midiaServiceMock = new();
        private readonly BotService _sut;

        public BotServiceTests()
        {
            _sut = new BotService(
                _envioServiceMock.Object,
                _conversaRepositoryMock.Object,
                _clienteServiceMock.Object,
                _pedidoServiceMock.Object,
                _clienteRepositoryMock.Object,
                _midiaServiceMock.Object,
                NullLogger<BotService>.Instance);

            // Fakes simplificados (mais permissivos que as regras reais em ClienteService,
            // que tem cobertura propria em ClienteServiceTests.cs) — aqui so precisamos de
            // um comportamento previsivel para testar a orquestracao do BotService.
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

        private Task Enviar(string telefone, string texto, MidiaRecebida? midia = null)
        {
            return _sut.ProcessarMensagem(new MensagemRecebida { Telefone = telefone, Texto = texto, Midia = midia });
        }

        private const string UrlFoto = "https://api.twilio.com/media/ME1";

        private static MidiaRecebida Foto(string contentType = "image/jpeg")
        {
            return new MidiaRecebida { Url = UrlFoto, ContentType = contentType };
        }

        private Conversa ConversaAguardandoFoto()
        {
            var conversa = NovaConversa(EtapaConversa.AguardandoFotoFachada);
            conversa.CON_CPF = "12345678900";
            conversa.CON_NOME = "Fulano";
            conversa.CON_EMAIL = "fulano@teste.com";
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);
            return conversa;
        }

        [Fact]
        public async Task ProcessarMensagem_TelefoneNovo_IniciaConversaAguardandoCpf()
        {
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone("11999999999")).ReturnsAsync((Conversa?)null);

            await Enviar("11999999999", "oi");

            _conversaRepositoryMock.Verify(r => r.Criar(It.Is<Conversa>(c => c.CON_ETAPA == EtapaConversa.AguardandoCpf)), Times.Once);
        }

        [Fact]
        public async Task ProcessarMensagem_CpfJaCadastrado_PulaDireitoParaMenuCliente()
        {
            var conversa = NovaConversa(EtapaConversa.AguardandoCpf);
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);
            _clienteServiceMock.Setup(s => s.BuscaPorCpf("12345678900"))
                .ReturnsAsync(new Cliente { CLI_CODIGO = 1, CLI_NOME = "Fulano", CLI_CPFCNPJ = "12345678900" });

            await Enviar(conversa.CON_TELEFONE, "12345678900");

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

            await Enviar(conversa.CON_TELEFONE, "98765432100");

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

            await Enviar(conversa.CON_TELEFONE, "98765432100");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoNome);
        }

        [Fact]
        public async Task ProcessarMensagem_CpfInvalido_PermaneceAguardandoCpf()
        {
            var conversa = NovaConversa(EtapaConversa.AguardandoCpf);
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await Enviar(conversa.CON_TELEFONE, "123");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoCpf);
            _conversaRepositoryMock.Verify(r => r.Atualizar(It.IsAny<Conversa>()), Times.Never);
        }

        [Fact]
        public async Task ProcessarMensagem_EmailInvalido_PermaneceAguardandoEmail()
        {
            var conversa = NovaConversa(EtapaConversa.AguardandoEmail);
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await Enviar(conversa.CON_TELEFONE, "emailinvalido");

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

            await Enviar(conversa.CON_TELEFONE, "produto sem quantidade");

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

            await Enviar(conversa.CON_TELEFONE, "101x produto");

            conversa.CON_ULTIMA_MENSAGEM.Should().BeNullOrEmpty();
        }

        [Fact]
        public async Task ProcessarMensagem_TerminarSemItens_NaoCriaPedido()
        {
            var conversa = NovaConversa(EtapaConversa.NovoPedido);
            conversa.CON_CLICODIGO = 1;
            conversa.CON_ULTIMA_MENSAGEM = string.Empty;
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await Enviar(conversa.CON_TELEFONE, "terminar");

            _pedidoServiceMock.Verify(s => s.CreatePedidoAsync(It.IsAny<Pedido>()), Times.Never);
        }

        [Fact]
        public async Task ProcessarMensagem_TerminarComClienteCodigoNulo_ReiniciaConversa()
        {
            var conversa = NovaConversa(EtapaConversa.NovoPedido);
            conversa.CON_CLICODIGO = null;
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await Enviar(conversa.CON_TELEFONE, "terminar");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoCpf);
        }

        [Fact]
        public async Task ProcessarMensagem_OpcaoMenuInvalida_EtapaNaoMuda()
        {
            var conversa = NovaConversa(EtapaConversa.MenuCliente);
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await Enviar(conversa.CON_TELEFONE, "9");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.MenuCliente);
            _conversaRepositoryMock.Verify(r => r.Atualizar(It.IsAny<Conversa>()), Times.Never);
        }

        [Fact]
        public async Task ProcessarMensagem_AcompanhamentoCodigoValidoExistente_RetornaStatus()
        {
            var conversa = NovaConversa(EtapaConversa.AcompanhandoPedido);
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);
            _pedidoServiceMock.Setup(s => s.BuscarPorId(15)).ReturnsAsync(new Pedido { PED_CODIGO = "15", PED_STATUS = "Em preparo", PED_DATA_PEDIDO = DateTime.Now });

            await Enviar(conversa.CON_TELEFONE, "15");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.MenuCliente);
            _envioServiceMock.Verify(t => t.EnviarMensagem(conversa.CON_TELEFONE, It.Is<string>(m => m.Contains("Em preparo"))), Times.Once);
        }

        [Fact]
        public async Task ProcessarMensagem_AcompanhamentoCodigoInexistente_RetornaNaoEncontrado()
        {
            var conversa = NovaConversa(EtapaConversa.AcompanhandoPedido);
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);
            _pedidoServiceMock.Setup(s => s.BuscarPorId(99)).ReturnsAsync((Pedido?)null);

            await Enviar(conversa.CON_TELEFONE, "99");

            _envioServiceMock.Verify(t => t.EnviarMensagem(conversa.CON_TELEFONE, It.Is<string>(m => m.Contains("Não encontramos"))), Times.Once);
        }

        [Fact]
        public async Task ProcessarMensagem_AcompanhamentoZero_VoltaMenu()
        {
            var conversa = NovaConversa(EtapaConversa.AcompanhandoPedido);
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await Enviar(conversa.CON_TELEFONE, "0");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.MenuCliente);
        }

        [Fact]
        public async Task ProcessarMensagem_AcompanhamentoTextoNaoNumerico_PedeNovamente()
        {
            var conversa = NovaConversa(EtapaConversa.AcompanhandoPedido);
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await Enviar(conversa.CON_TELEFONE, "abc");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.AcompanhandoPedido);
            _pedidoServiceMock.Verify(s => s.BuscarPorId(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task ProcessarMensagem_EmailValido_VaiParaAguardandoFotoSemCriarCliente()
        {
            var conversa = NovaConversa(EtapaConversa.AguardandoEmail);
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await Enviar(conversa.CON_TELEFONE, "fulano@teste.com");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoFotoFachada);
            conversa.CON_EMAIL.Should().Be("fulano@teste.com");
            _conversaRepositoryMock.Verify(r => r.Atualizar(conversa), Times.Once);
            _clienteServiceMock.Verify(s => s.CreateClienteAsync(It.IsAny<Cliente>()), Times.Never);
            _clienteServiceMock.Verify(s => s.CreateClienteComFotoAsync(It.IsAny<Cliente>(), It.IsAny<FotoCliente>()), Times.Never);
            _envioServiceMock.Verify(t => t.EnviarMensagem(conversa.CON_TELEFONE, MensagensBot.PedirFotoFachada), Times.Once);
        }

        [Fact]
        public async Task ProcessarMensagem_FotoSemMidia_PedeFotoENaoBaixa()
        {
            var conversa = ConversaAguardandoFoto();

            await Enviar(conversa.CON_TELEFONE, "não tenho foto");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoFotoFachada);
            _midiaServiceMock.Verify(m => m.BaixarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _conversaRepositoryMock.Verify(r => r.Atualizar(It.IsAny<Conversa>()), Times.Never);
            _envioServiceMock.Verify(t => t.EnviarMensagem(conversa.CON_TELEFONE, MensagensBot.FotoFormatoInvalido), Times.Once);
        }

        [Theory]
        [InlineData("application/pdf")]
        [InlineData("video/mp4")]
        public async Task ProcessarMensagem_FotoTipoNaoAceito_RejeitaSemBaixar(string contentType)
        {
            var conversa = ConversaAguardandoFoto();

            await Enviar(conversa.CON_TELEFONE, "", Foto(contentType));

            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoFotoFachada);
            _midiaServiceMock.Verify(m => m.BaixarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _envioServiceMock.Verify(t => t.EnviarMensagem(conversa.CON_TELEFONE, MensagensBot.FotoFormatoInvalido), Times.Once);
        }

        [Fact]
        public async Task ProcessarMensagem_FotoDownloadFalha_PedeReenvioENaoCriaCliente()
        {
            var conversa = ConversaAguardandoFoto();
            _midiaServiceMock.Setup(m => m.BaixarAsync(UrlFoto, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("500"));

            await Enviar(conversa.CON_TELEFONE, "", Foto());

            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoFotoFachada);
            _clienteServiceMock.Verify(s => s.CreateClienteComFotoAsync(It.IsAny<Cliente>(), It.IsAny<FotoCliente>()), Times.Never);
            _conversaRepositoryMock.Verify(r => r.Atualizar(It.IsAny<Conversa>()), Times.Never);
            _envioServiceMock.Verify(t => t.EnviarMensagem(conversa.CON_TELEFONE, MensagensBot.FotoNaoRecebida), Times.Once);
        }

        [Fact]
        public async Task ProcessarMensagem_FotoUrlForaDoTwilio_PedeReenvioENaoCriaCliente()
        {
            var conversa = ConversaAguardandoFoto();
            _midiaServiceMock.Setup(m => m.BaixarAsync(UrlFoto, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ArgumentException("URL de mídia inválida.", "url"));

            await Enviar(conversa.CON_TELEFONE, "", Foto());

            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoFotoFachada);
            _clienteServiceMock.Verify(s => s.CreateClienteComFotoAsync(It.IsAny<Cliente>(), It.IsAny<FotoCliente>()), Times.Never);
            _conversaRepositoryMock.Verify(r => r.Atualizar(It.IsAny<Conversa>()), Times.Never);
            _envioServiceMock.Verify(t => t.EnviarMensagem(conversa.CON_TELEFONE, MensagensBot.FotoNaoRecebida), Times.Once);
        }

        [Fact]
        public async Task ProcessarMensagem_FotoDownloadVazio_PedeReenvioENaoCriaCliente()
        {
            var conversa = ConversaAguardandoFoto();
            _midiaServiceMock.Setup(m => m.BaixarAsync(UrlFoto, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MidiaBaixada { Conteudo = Array.Empty<byte>(), ContentType = "image/jpeg" });

            await Enviar(conversa.CON_TELEFONE, "", Foto());

            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoFotoFachada);
            _clienteServiceMock.Verify(s => s.CreateClienteComFotoAsync(It.IsAny<Cliente>(), It.IsAny<FotoCliente>()), Times.Never);
            _conversaRepositoryMock.Verify(r => r.Atualizar(It.IsAny<Conversa>()), Times.Never);
            _envioServiceMock.Verify(t => t.EnviarMensagem(conversa.CON_TELEFONE, MensagensBot.FotoNaoRecebida), Times.Once);
        }

        [Fact]
        public async Task ProcessarMensagem_FotoComClienteJaCriadoPorCpf_VinculaSemBaixarNemCriar()
        {
            var conversa = ConversaAguardandoFoto();
            _clienteServiceMock.Setup(s => s.BuscaPorCpf("12345678900"))
                .ReturnsAsync(new Cliente { CLI_CODIGO = 77, CLI_NOME = "Fulano" });

            await Enviar(conversa.CON_TELEFONE, "", Foto());

            conversa.CON_CLICODIGO.Should().Be(77);
            conversa.CON_ETAPA.Should().Be(EtapaConversa.MenuCliente);
            _conversaRepositoryMock.Verify(r => r.Atualizar(conversa), Times.Once);
            _midiaServiceMock.Verify(m => m.BaixarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _clienteServiceMock.Verify(s => s.CreateClienteComFotoAsync(It.IsAny<Cliente>(), It.IsAny<FotoCliente>()), Times.Never);
            _envioServiceMock.Verify(t => t.EnviarMensagem(conversa.CON_TELEFONE, MensagensBot.CadastroConcluido), Times.Once);
        }

        [Fact]
        public async Task ProcessarMensagem_FotoContentTypeMaiusculo_Aceita()
        {
            var conversa = ConversaAguardandoFoto();
            _midiaServiceMock.Setup(m => m.BaixarAsync(UrlFoto, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MidiaBaixada { Conteudo = new byte[] { 1 }, ContentType = "image/jpeg" });
            _clienteServiceMock.Setup(s => s.CreateClienteComFotoAsync(It.IsAny<Cliente>(), It.IsAny<FotoCliente>()))
                .ReturnsAsync(42);

            await Enviar(conversa.CON_TELEFONE, "", Foto("IMAGE/JPEG"));

            conversa.CON_CLICODIGO.Should().Be(42);
            conversa.CON_ETAPA.Should().Be(EtapaConversa.MenuCliente);
        }

        [Fact]
        public async Task ProcessarMensagem_FotoMuitoGrande_AvisaENaoCriaCliente()
        {
            var conversa = ConversaAguardandoFoto();
            _midiaServiceMock.Setup(m => m.BaixarAsync(UrlFoto, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new MidiaMuitoGrandeException(MidiaService.TamanhoMaximoBytes));

            await Enviar(conversa.CON_TELEFONE, "", Foto());

            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoFotoFachada);
            _clienteServiceMock.Verify(s => s.CreateClienteComFotoAsync(It.IsAny<Cliente>(), It.IsAny<FotoCliente>()), Times.Never);
            _conversaRepositoryMock.Verify(r => r.Atualizar(It.IsAny<Conversa>()), Times.Never);
            _envioServiceMock.Verify(t => t.EnviarMensagem(conversa.CON_TELEFONE, MensagensBot.FotoMuitoGrande), Times.Once);
        }

        [Fact]
        public async Task ProcessarMensagem_FotoCriacaoClienteFalha_AvisaEMantemEtapa()
        {
            var conversa = ConversaAguardandoFoto();
            _midiaServiceMock.Setup(m => m.BaixarAsync(UrlFoto, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MidiaBaixada { Conteudo = new byte[] { 1 }, ContentType = "image/jpeg" });
            _clienteServiceMock.Setup(s => s.CreateClienteComFotoAsync(It.IsAny<Cliente>(), It.IsAny<FotoCliente>()))
                .ThrowsAsync(new InvalidOperationException("db fora"));

            await Enviar(conversa.CON_TELEFONE, "", Foto());

            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoFotoFachada);
            conversa.CON_CLICODIGO.Should().BeNull();
            _conversaRepositoryMock.Verify(r => r.Atualizar(It.IsAny<Conversa>()), Times.Never);
            _envioServiceMock.Verify(t => t.EnviarMensagem(conversa.CON_TELEFONE, MensagensBot.ErroAoFinalizarCadastro), Times.Once);
        }

        [Fact]
        public async Task ProcessarMensagem_FotoValida_CriaClienteComFotoEVaiParaMenu()
        {
            var conversa = ConversaAguardandoFoto();
            var bytes = new byte[] { 1, 2, 3 };
            _midiaServiceMock.Setup(m => m.BaixarAsync(UrlFoto, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MidiaBaixada { Conteudo = bytes, ContentType = "image/jpeg" });
            _clienteServiceMock.Setup(s => s.CreateClienteComFotoAsync(It.IsAny<Cliente>(), It.IsAny<FotoCliente>()))
                .ReturnsAsync(42);

            await Enviar(conversa.CON_TELEFONE, "", Foto("image/png"));

            _clienteServiceMock.Verify(s => s.CreateClienteComFotoAsync(
                It.Is<Cliente>(c =>
                    c.CLI_NOME == "Fulano" &&
                    c.CLI_EMAIL == "fulano@teste.com" &&
                    c.CLI_CPFCNPJ == "12345678900" &&
                    c.CLI_TELEFONE == "11999999999"),
                It.Is<FotoCliente>(f =>
                    f.FOT_CONTEUDO == bytes &&
                    f.FOT_CONTENT_TYPE == "image/png" &&
                    f.FOT_TAMANHO == 3)), Times.Once);
            conversa.CON_CLICODIGO.Should().Be(42);
            conversa.CON_ETAPA.Should().Be(EtapaConversa.MenuCliente);
            _conversaRepositoryMock.Verify(r => r.Atualizar(conversa), Times.Once);
            _envioServiceMock.Verify(t => t.EnviarMensagem(conversa.CON_TELEFONE, MensagensBot.CadastroConcluido), Times.Once);
        }

        [Fact]
        public async Task FluxoFeliz_TelefoneNovoAteConfirmacaoDePedido()
        {
            var telefone = "11988887777";

            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(telefone)).ReturnsAsync((Conversa?)null);
            await Enviar(telefone, "oi");
            var conversa = new Conversa { CON_TELEFONE = telefone, CON_ETAPA = EtapaConversa.AguardandoCpf };
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(telefone)).ReturnsAsync(conversa);

            _clienteServiceMock.Setup(s => s.BuscaPorCpf("12345678900")).ReturnsAsync((Cliente?)null);
            _clienteRepositoryMock.Setup(r => r.GetByTelefoneAsync(It.IsAny<string>())).ReturnsAsync((Cliente?)null);
            await Enviar(telefone, "12345678900");
            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoNome);

            await Enviar(telefone, "Fulano de Tal");
            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoEmail);

            await Enviar(telefone, "fulano@teste.com");
            conversa.CON_ETAPA.Should().Be(EtapaConversa.AguardandoFotoFachada);

            _midiaServiceMock.Setup(m => m.BaixarAsync(UrlFoto, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MidiaBaixada { Conteudo = new byte[] { 1 }, ContentType = "image/jpeg" });
            _clienteServiceMock.Setup(s => s.CreateClienteComFotoAsync(It.IsAny<Cliente>(), It.IsAny<FotoCliente>())).ReturnsAsync(42);
            await Enviar(telefone, "", Foto());
            conversa.CON_ETAPA.Should().Be(EtapaConversa.MenuCliente);
            conversa.CON_CLICODIGO.Should().Be(42);

            await Enviar(telefone, "1");
            conversa.CON_ETAPA.Should().Be(EtapaConversa.NovoPedido);

            _pedidoServiceMock.Setup(s => s.ValidarLinhaItem("2x pizza"))
                .Returns(new ItemPedidoValidacao { Resultado = ItemPedidoValidacaoResultado.Valido, LinhaNormalizada = "2X PIZZA" });
            await Enviar(telefone, "2x pizza");
            conversa.CON_ULTIMA_MENSAGEM.Should().Be("2x pizza");

            _pedidoServiceMock.Setup(s => s.CreatePedidoAsync(It.IsAny<Pedido>())).ReturnsAsync(777);
            await Enviar(telefone, "terminar");
            conversa.CON_ETAPA.Should().Be(EtapaConversa.MenuCliente);
            conversa.CON_ULTIMA_MENSAGEM.Should().BeNullOrEmpty();
            _envioServiceMock.Verify(t => t.EnviarMensagem(telefone, It.Is<string>(m => m.Contains("777"))), Times.Once);
        }

        [Fact]
        public async Task ProcessarMensagem_FalhaNoEnvio_NaoPropagaEGravaConversa()
        {
            var conversa = NovaConversa(EtapaConversa.AguardandoNome);
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);
            _envioServiceMock
                .Setup(t => t.EnviarMensagem(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("Central respondeu 502"));

            var act = () => Enviar(conversa.CON_TELEFONE, "Fulano");

            await act.Should().NotThrowAsync();
            _conversaRepositoryMock.Verify(r => r.Atualizar(It.Is<Conversa>(c => c.CON_ETAPA == EtapaConversa.AguardandoEmail)), Times.Once);
        }

        [Fact]
        public async Task EncerrarConversa_ConversaAtiva_MarcaFinalizadaSemMudarEtapa()
        {
            var conversa = NovaConversa(EtapaConversa.NovoPedido, telefone: "whatsapp:+5511999999999");
            conversa.CON_CLICODIGO = 7;
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await _sut.EncerrarConversa(conversa.CON_TELEFONE);

            _conversaRepositoryMock.Verify(r => r.Atualizar(It.Is<Conversa>(c =>
                c.CON_STATUS == StatusConversa.Finalizada &&
                c.CON_ETAPA == EtapaConversa.NovoPedido &&
                c.CON_CLICODIGO == 7)), Times.Once);
            _envioServiceMock.Verify(t => t.EnviarMensagem(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task EncerrarConversa_ConversaInexistente_NaoFazNada()
        {
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(It.IsAny<string>())).ReturnsAsync((Conversa?)null);

            await _sut.EncerrarConversa("whatsapp:+5511999999999");

            _conversaRepositoryMock.Verify(r => r.Atualizar(It.IsAny<Conversa>()), Times.Never);
            _conversaRepositoryMock.Verify(r => r.Criar(It.IsAny<Conversa>()), Times.Never);
        }

        [Fact]
        public async Task EncerrarConversa_JaFinalizada_NaoGravaDeNovo()
        {
            var conversa = NovaConversa(EtapaConversa.MenuCliente);
            conversa.CON_STATUS = StatusConversa.Finalizada;
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await _sut.EncerrarConversa(conversa.CON_TELEFONE);

            _conversaRepositoryMock.Verify(r => r.Atualizar(It.IsAny<Conversa>()), Times.Never);
        }

        [Fact]
        public async Task ProcessarMensagem_FinalizadaComCliente_ReabreNoMenuEIgnoraTexto()
        {
            var conversa = NovaConversa(EtapaConversa.NovoPedido);
            conversa.CON_STATUS = StatusConversa.Finalizada;
            conversa.CON_CLICODIGO = 7;
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await Enviar(conversa.CON_TELEFONE, "1x sorvete de chocolate");

            _conversaRepositoryMock.Verify(r => r.Atualizar(It.Is<Conversa>(c =>
                c.CON_STATUS == StatusConversa.Ativa &&
                c.CON_ETAPA == EtapaConversa.MenuCliente)), Times.Once);
            _envioServiceMock.Verify(t => t.EnviarMensagem(
                conversa.CON_TELEFONE,
                It.Is<string>(m => m.EndsWith(MensagensBot.Menu))), Times.Once);
            _pedidoServiceMock.Verify(s => s.ValidarLinhaItem(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ProcessarMensagem_FinalizadaSemCliente_ReabrePedindoCpf()
        {
            var conversa = NovaConversa(EtapaConversa.AguardandoEmail);
            conversa.CON_STATUS = StatusConversa.Finalizada;
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await Enviar(conversa.CON_TELEFONE, "fulano@teste.com");

            _conversaRepositoryMock.Verify(r => r.Atualizar(It.Is<Conversa>(c =>
                c.CON_STATUS == StatusConversa.Ativa &&
                c.CON_ETAPA == EtapaConversa.AguardandoCpf)), Times.Once);
            _envioServiceMock.Verify(t => t.EnviarMensagem(
                conversa.CON_TELEFONE,
                It.Is<string>(m => m.EndsWith(MensagensBot.PedidoCpf))), Times.Once);
        }

        [Fact]
        public async Task ProcessarMensagem_Cancelada_TambemReabre()
        {
            var conversa = NovaConversa(EtapaConversa.MenuCliente);
            conversa.CON_STATUS = StatusConversa.Cancelada;
            conversa.CON_CLICODIGO = 7;
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await Enviar(conversa.CON_TELEFONE, "2");

            conversa.CON_STATUS.Should().Be(StatusConversa.Ativa);
            conversa.CON_ETAPA.Should().Be(EtapaConversa.MenuCliente);
        }

        [Fact]
        public async Task ProcessarMensagem_NovoPedidoCancelar_LimpaCarrinhoEVoltaMenu()
        {
            var conversa = NovaConversa(EtapaConversa.NovoPedido);
            conversa.CON_CLICODIGO = 7;
            conversa.CON_ULTIMA_MENSAGEM = "2x pizza";
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);

            await Enviar(conversa.CON_TELEFONE, " Cancelar ");

            conversa.CON_ETAPA.Should().Be(EtapaConversa.MenuCliente);
            conversa.CON_ULTIMA_MENSAGEM.Should().BeEmpty();
            _pedidoServiceMock.Verify(s => s.CreatePedidoAsync(It.IsAny<Pedido>()), Times.Never);
            _envioServiceMock.Verify(t => t.EnviarMensagem(conversa.CON_TELEFONE, MensagensBot.PedidoCancelado), Times.Once);
        }

        [Fact]
        public async Task ProcessarMensagem_ItemValido_MostraCarrinhoAcumulado()
        {
            var conversa = NovaConversa(EtapaConversa.NovoPedido);
            conversa.CON_ULTIMA_MENSAGEM = "2x pizza";
            _conversaRepositoryMock.Setup(r => r.BuscaPorTelefone(conversa.CON_TELEFONE)).ReturnsAsync(conversa);
            _pedidoServiceMock.Setup(s => s.ValidarLinhaItem("1x refrigerante"))
                .Returns(new ItemPedidoValidacao { Resultado = ItemPedidoValidacaoResultado.Valido });

            await Enviar(conversa.CON_TELEFONE, "1x refrigerante");

            _envioServiceMock.Verify(t => t.EnviarMensagem(
                conversa.CON_TELEFONE,
                It.Is<string>(m => m.Contains("• 2x pizza\n• 1x refrigerante"))), Times.Once);
        }

        [Theory]
        [InlineData(5, "Bom dia")]
        [InlineData(11, "Bom dia")]
        [InlineData(12, "Boa tarde")]
        [InlineData(17, "Boa tarde")]
        [InlineData(18, "Boa noite")]
        [InlineData(0, "Boa noite")]
        [InlineData(4, "Boa noite")]
        public void Saudacao_DependeDaHora(int hora, string esperado)
        {
            MensagensBot.Saudacao(hora).Should().Be(esperado);
        }
    }
}

using API_WHATSAPP_SELLER.Exceptions;
using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;
using API_WHATSAPP_SELLER.Models.Enum;
using System.Linq;

namespace API_WHATSAPP_SELLER.Services
{
    public class BotService : IBotService
    {
        private static readonly HashSet<string> TiposFotoAceitos = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

        private readonly IEnvioMensagemService _envioService;
        private readonly IConversaRepository _conversaRepository;
        private readonly IClienteService _clienteService;
        private readonly IPedidoService _pedidoService;
        private readonly IClienteRepository _clienteRepository;
        private readonly IMidiaService _midiaService;
        private readonly ILogger<BotService> _logger;

        public BotService(
            IEnvioMensagemService envioService,
            IConversaRepository conversaRepository,
            IClienteService clienteService,
            IPedidoService pedidoService,
            IClienteRepository clienteRepository,
            IMidiaService midiaService,
            ILogger<BotService> logger)
        {
            _envioService = envioService;
            _conversaRepository = conversaRepository;
            _clienteService = clienteService;
            _pedidoService = pedidoService;
            _clienteRepository = clienteRepository;
            _midiaService = midiaService;
            _logger = logger;
        }

        // Falha de envio não interrompe o fluxo: a conversa já foi gravada e o
        // webhook precisa responder 200 para a central não repetir o repasse.
        private async Task Responder(string telefone, string mensagem)
        {
            try
            {
                await _envioService.EnviarMensagem(telefone, mensagem);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao enviar mensagem para {Telefone}", telefone);
            }
        }

        public async Task ProcessarMensagem(MensagemRecebida mensagem)
        {
            var conversa = await _conversaRepository.BuscaPorTelefone(mensagem.Telefone);

            if (conversa == null)
            {
                await IniciarConversa(mensagem.Telefone);
                return;
            }

            if (conversa.CON_STATUS != StatusConversa.Ativa)
            {
                await ReabrirConversa(conversa);
                return;
            }

            var texto = mensagem.Texto;

            switch (conversa.CON_ETAPA)
            {
                case EtapaConversa.AguardandoCpf:
                    await ProcessarCpf(conversa, texto);
                    break;

                case EtapaConversa.AguardandoNome:
                    await ProcessarNome(conversa, texto);
                    break;

                case EtapaConversa.AguardandoEmail:
                    await ProcessarEmail(conversa, texto);
                    break;

                case EtapaConversa.AguardandoFotoFachada:
                    await ProcessarFotoFachada(conversa, mensagem.Midia);
                    break;

                case EtapaConversa.MenuCliente:
                    await ProcessarMenu(conversa, texto);
                    break;

                case EtapaConversa.NovoPedido:
                    await ProcessarNovoPedido(conversa, texto);
                    break;

                case EtapaConversa.AcompanhandoPedido:
                    await ProcessarAcompanhamentoPedido(conversa, texto);
                    break;
            }
        }

        // Ticket fechado na central. Só muda o status; a próxima mensagem do
        // cliente reabre a conversa (ver ReabrirConversa).
        public async Task EncerrarConversa(string telefone)
        {
            var conversa = await _conversaRepository.BuscaPorTelefone(telefone);
            if (conversa == null || conversa.CON_STATUS == StatusConversa.Finalizada)
                return;

            conversa.CON_STATUS = StatusConversa.Finalizada;
            await _conversaRepository.Atualizar(conversa);
        }

        private async Task IniciarConversa(string telefone)
        {
            var conversa = new Conversa
            {
                CON_TELEFONE = telefone,
                CON_ETAPA = EtapaConversa.AguardandoCpf
            };

            await _conversaRepository.Criar(conversa);

            await Responder(telefone, MensagensBot.BoasVindas());
        }

        // Ticket fechado na central (reset): a primeira mensagem depois só reabre
        // a conversa e o conteúdo dela é ignorado.
        private async Task ReabrirConversa(Conversa conversa)
        {
            conversa.CON_STATUS = StatusConversa.Ativa;

            if (conversa.CON_CLICODIGO.HasValue)
            {
                conversa.CON_ETAPA = EtapaConversa.MenuCliente;
                await _conversaRepository.Atualizar(conversa);

                await Responder(conversa.CON_TELEFONE, MensagensBot.BoasVindasDeVolta());
                return;
            }

            conversa.CON_ETAPA = EtapaConversa.AguardandoCpf;
            await _conversaRepository.Atualizar(conversa);

            await Responder(conversa.CON_TELEFONE, MensagensBot.BoasVindas());
        }

        private async Task ProcessarCpf(Conversa conversa, string cpf)
        {
            if (!_clienteService.TryNormalizarCpfCnpj(cpf, out var cpfNormalizado))
            {
                await Responder(conversa.CON_TELEFONE, MensagensBot.CpfInvalido);
                return;
            }

            var cliente = await _clienteService.BuscaPorCpf(cpfNormalizado);

            cliente ??= await _clienteRepository.GetByTelefoneAsync(NormalizarTelefoneCliente(conversa.CON_TELEFONE));

            if (cliente != null)
            {
                conversa.CON_CPF = cliente.CLI_CPFCNPJ;
                conversa.CON_CLICODIGO = cliente.CLI_CODIGO;
                conversa.CON_ETAPA = EtapaConversa.MenuCliente;

                await _conversaRepository.Atualizar(conversa);

                await Responder(conversa.CON_TELEFONE, MensagensBot.ClienteEncontrado(cliente.CLI_NOME));

                return;
            }

            conversa.CON_CPF = cpfNormalizado;
            conversa.CON_ETAPA = EtapaConversa.AguardandoNome;

            await _conversaRepository.Atualizar(conversa);

            await Responder(conversa.CON_TELEFONE, MensagensBot.CadastroNaoEncontrado);
        }

        private async Task ProcessarNome(Conversa conversa, string nome)
        {
            if (!_clienteService.NomeValido(nome))
            {
                await Responder(conversa.CON_TELEFONE, MensagensBot.NomeInvalido);
                return;
            }

            conversa.CON_NOME = nome.Trim();
            conversa.CON_ETAPA = EtapaConversa.AguardandoEmail;

            await _conversaRepository.Atualizar(conversa);

            await Responder(conversa.CON_TELEFONE, MensagensBot.PedirEmail(conversa.CON_NOME));
        }

        private async Task ProcessarEmail(Conversa conversa, string email)
        {
            if (!_clienteService.EmailValido(email))
            {
                await Responder(conversa.CON_TELEFONE, MensagensBot.EmailInvalido);
                return;
            }

            conversa.CON_EMAIL = email;
            conversa.CON_ETAPA = EtapaConversa.AguardandoFotoFachada;

            await _conversaRepository.Atualizar(conversa);

            await Responder(conversa.CON_TELEFONE, MensagensBot.PedirFotoFachada);
        }

        // Foto é obrigatória: em qualquer falha a conversa fica em
        // AguardandoFotoFachada (sem Atualizar) e o cliente reenvia.
        private async Task ProcessarFotoFachada(Conversa conversa, MidiaRecebida? midia)
        {
            // Cliente já gravado numa tentativa anterior cujo Atualizar da conversa
            // falhou: só vincula, pra não duplicar o cadastro. Envio simultâneo de
            // várias fotos ainda pode duplicar — só UNIQUE em CLI_CPFCNPJ resolve.
            var existente = await _clienteService.BuscaPorCpf(conversa.CON_CPF!);
            if (existente != null)
            {
                conversa.CON_CLICODIGO = existente.CLI_CODIGO;
                conversa.CON_ETAPA = EtapaConversa.MenuCliente;

                await _conversaRepository.Atualizar(conversa);

                await Responder(conversa.CON_TELEFONE, MensagensBot.CadastroConcluido);
                return;
            }

            if (midia == null || !TiposFotoAceitos.Contains(midia.ContentType))
            {
                await Responder(conversa.CON_TELEFONE, MensagensBot.FotoFormatoInvalido);
                return;
            }

            MidiaBaixada baixada;
            try
            {
                baixada = await _midiaService.BaixarAsync(midia.Url);
            }
            catch (MidiaMuitoGrandeException)
            {
                await Responder(conversa.CON_TELEFONE, MensagensBot.FotoMuitoGrande);
                return;
            }
            catch (ArgumentException)
            {
                // Host fora do Twilio/central só chega por webhook forjado; não loga a URL.
                _logger.LogWarning("MediaUrl de host não permitido recebida para {Telefone}", conversa.CON_TELEFONE);
                await Responder(conversa.CON_TELEFONE, MensagensBot.FotoNaoRecebida);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao baixar foto da fachada de {Telefone}", conversa.CON_TELEFONE);
                await Responder(conversa.CON_TELEFONE, MensagensBot.FotoNaoRecebida);
                return;
            }

            if (baixada.Conteudo.Length == 0)
            {
                await Responder(conversa.CON_TELEFONE, MensagensBot.FotoNaoRecebida);
                return;
            }

            // O telefone já é conhecido de forma confiável via CON_TELEFONE (o
            // "From" do Twilio, capturado desde a primeira mensagem) — pedir
            // pro cliente digitar de novo só introduzia divergência (erro de
            // digitação, número sem o 9 do celular etc.) entre CLI_TELEFONE e
            // CON_TELEFONE, o que duplicava o cadastro do lado da sincronização
            // com o Bling.
            var cliente = new Cliente
            {
                CLI_NOME = conversa.CON_NOME,
                CLI_EMAIL = conversa.CON_EMAIL,
                CLI_TELEFONE = NormalizarTelefoneCliente(conversa.CON_TELEFONE),
                CLI_CPFCNPJ = conversa.CON_CPF
            };

            // Content-type da mensagem do Twilio, já validado contra a lista aceita.
            var foto = new FotoCliente
            {
                FOT_CONTEUDO = baixada.Conteudo,
                FOT_CONTENT_TYPE = midia.ContentType,
                FOT_TAMANHO = baixada.Conteudo.Length,
                FOT_CREATED_AT = DateTime.Now
            };

            int codigoCliente;
            try
            {
                codigoCliente = await _clienteService.CreateClienteComFotoAsync(cliente, foto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao criar cliente com foto para {Telefone}", conversa.CON_TELEFONE);
                await Responder(conversa.CON_TELEFONE, MensagensBot.ErroAoFinalizarCadastro);
                return;
            }

            conversa.CON_CLICODIGO = codigoCliente;
            conversa.CON_ETAPA = EtapaConversa.MenuCliente;

            await _conversaRepository.Atualizar(conversa);

            await Responder(conversa.CON_TELEFONE, MensagensBot.CadastroConcluido);
        }

        // Espelha internal/clienteconversa/domain/telefone.go (NormalizarTelefoneCliente)
        // do lado bling-go: extrai só dígitos e remove o DDI 55 quando presente,
        // pra CLI_TELEFONE ficar no mesmo formato usado na sincronização com o
        // Bling ("11952793798", sem "whatsapp:+55" e sem exceder o varchar(20)
        // da coluna).
        private static string NormalizarTelefoneCliente(string numero)
        {
            var digitos = new string(numero.Where(char.IsDigit).ToArray());
            if ((digitos.Length == 12 || digitos.Length == 13) && digitos.StartsWith("55"))
            {
                return digitos[2..];
            }
            return digitos;
        }

        private async Task ProcessarMenu(Conversa conversa, string opcao)
        {
            switch (opcao.Trim())
            {
                case "1":
                    conversa.CON_ETAPA = EtapaConversa.NovoPedido;
                    conversa.CON_ULTIMA_MENSAGEM = string.Empty; // Limpa o acumulador
                    await _conversaRepository.Atualizar(conversa);

                    await Responder(conversa.CON_TELEFONE, MensagensBot.NovoPedidoIniciado);
                    break;

                case "2":
                    conversa.CON_ETAPA = EtapaConversa.AcompanhandoPedido;
                    await _conversaRepository.Atualizar(conversa);

                    await Responder(conversa.CON_TELEFONE, MensagensBot.PedirNumeroPedido);
                    break;

                default:
                    await Responder(conversa.CON_TELEFONE, MensagensBot.OpcaoInvalida);
                    break;
            }
        }

        private async Task ProcessarNovoPedido(Conversa conversa, string mensagem)
        {
            var textoDigitado = mensagem.Trim();

            // Desiste do pedido: descarta o carrinho e volta ao menu.
            if (textoDigitado.Equals("cancelar", StringComparison.OrdinalIgnoreCase))
            {
                conversa.CON_ETAPA = EtapaConversa.MenuCliente;
                conversa.CON_ULTIMA_MENSAGEM = string.Empty;
                await _conversaRepository.Atualizar(conversa);

                await Responder(conversa.CON_TELEFONE, MensagensBot.PedidoCancelado);
                return;
            }

            // Se o cliente digitou "terminar", processamos o carrinho atual sem incluir a palavra "terminar"
            if (textoDigitado.Equals("terminar", StringComparison.OrdinalIgnoreCase))
            {
                if (!conversa.CON_CLICODIGO.HasValue)
                {
                    await Responder(conversa.CON_TELEFONE, MensagensBot.ErroIdentificacaoCliente);
                    conversa.CON_ETAPA = EtapaConversa.AguardandoCpf;
                    conversa.CON_ULTIMA_MENSAGEM = string.Empty;
                    await _conversaRepository.Atualizar(conversa);
                    return;
                }

                var corpoPedido = conversa.CON_ULTIMA_MENSAGEM ?? string.Empty;

                if (string.IsNullOrWhiteSpace(corpoPedido))
                {
                    await Responder(conversa.CON_TELEFONE, MensagensBot.PedidoSemItens);
                    return;
                }

                var pedido = new Pedido
                {
                    PED_CLICODIGO = conversa.CON_CLICODIGO.Value,
                    PED_CORPO = corpoPedido, // Contém apenas os produtos puros enviados anteriormente
                    // Horário de Brasília: o container roda em UTC.
                    PED_DATA_PEDIDO = DateTimeOffset.UtcNow.ToOffset(MensagensBot.FusoBrasilia).DateTime,
                    PED_TOTAL = 0
                };

                int idPedidoGerado;
                try
                {
                    idPedidoGerado = await _pedidoService.CreatePedidoAsync(pedido);
                }
                catch (Exception ex)
                {
                    // Carrinho e etapa ficam como estão: o cliente pode enviar "terminar" de novo.
                    _logger.LogError(ex, "Falha ao criar pedido para {Telefone}", conversa.CON_TELEFONE);
                    await Responder(conversa.CON_TELEFONE, MensagensBot.ErroAoRegistrarPedido);
                    return;
                }

                conversa.CON_ETAPA = EtapaConversa.MenuCliente;
                conversa.CON_ULTIMA_MENSAGEM = string.Empty; // Limpa o carrinho
                await _conversaRepository.Atualizar(conversa);

                await Responder(conversa.CON_TELEFONE, MensagensBot.PedidoFinalizado(idPedidoGerado, corpoPedido));
                return;
            }

            // Se NÃO digitou terminar, valida o item antes de acumular.
            var validacao = _pedidoService.ValidarLinhaItem(textoDigitado);

            switch (validacao.Resultado)
            {
                case ItemPedidoValidacaoResultado.QuantidadeZero:
                    await Responder(conversa.CON_TELEFONE, MensagensBot.ItemQuantidadeZero);
                    return;

                case ItemPedidoValidacaoResultado.QuantidadeMuitoAlta:
                    await Responder(conversa.CON_TELEFONE, MensagensBot.ItemQuantidadeMuitoAlta);
                    return;

                case ItemPedidoValidacaoResultado.Valido:
                    break;

                default:
                    await Responder(conversa.CON_TELEFONE, MensagensBot.ItemFormatoInvalido);
                    return;
            }

            if (string.IsNullOrEmpty(conversa.CON_ULTIMA_MENSAGEM))
            {
                conversa.CON_ULTIMA_MENSAGEM = textoDigitado;
            }
            else
            {
                conversa.CON_ULTIMA_MENSAGEM += "\n" + textoDigitado;
            }

            await _conversaRepository.Atualizar(conversa);

            await Responder(conversa.CON_TELEFONE, MensagensBot.ItemAdicionado(conversa.CON_ULTIMA_MENSAGEM));
        }

        private async Task ProcessarAcompanhamentoPedido(Conversa conversa, string mensagem)
        {
            var textoDigitado = mensagem.Trim();

            // Tenta converter o que o usuário digitou em um número de ID válido
            if (!int.TryParse(textoDigitado, out var codigoPedido))
            {
                await Responder(conversa.CON_TELEFONE, MensagensBot.NumeroPedidoInvalido);
                return;
            }

            if (codigoPedido == 0)
            {
                conversa.CON_ETAPA = EtapaConversa.MenuCliente;
                await _conversaRepository.Atualizar(conversa);
                await Responder(conversa.CON_TELEFONE, MensagensBot.Menu);
                return;
            }

            // Busca o pedido no banco pelo ID informado
            var pedido = await _pedidoService.BuscarPorId(codigoPedido); // (Ou chame direto o repositório se preferir)

            // Pedido de outro cliente recebe a mesma resposta de inexistente, para não revelar que o número existe.
            // CON_CLICODIGO nulo nunca casa com PED_CLICODIGO.
            if (pedido == null || pedido.PED_CLICODIGO != conversa.CON_CLICODIGO)
            {
                if (pedido != null)
                {
                    _logger.LogWarning(
                        "Consulta de pedido {Pedido} de outro cliente por {Telefone}",
                        codigoPedido, conversa.CON_TELEFONE);
                }

                await Responder(conversa.CON_TELEFONE, MensagensBot.PedidoNaoEncontrado(codigoPedido));
                return;
            }

            // Retorna o status encontrado para o usuário e devolve ele para o menu
            conversa.CON_ETAPA = EtapaConversa.MenuCliente;
            await _conversaRepository.Atualizar(conversa);

            await Responder(
                conversa.CON_TELEFONE,
                MensagensBot.StatusPedido(pedido.PED_CODIGO, pedido.PED_STATUS, pedido.PED_DATA_PEDIDO));
        }
    }
}

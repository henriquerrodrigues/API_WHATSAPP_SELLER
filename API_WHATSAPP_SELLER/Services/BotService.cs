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

        private const string MensagemBoasVindas =
            "Olá! Seja bem-vindo.\n\nInforme seu CPF ou CNPJ para continuarmos.";

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

            await Responder(telefone, MensagemBoasVindas);
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

                await Responder(
                    conversa.CON_TELEFONE,
                    "Olá! Que bom te ver de novo.\n\n" +
                    "1 - Novo Pedido\n" +
                    "2 - Acompanhar Pedido");
                return;
            }

            conversa.CON_ETAPA = EtapaConversa.AguardandoCpf;
            await _conversaRepository.Atualizar(conversa);

            await Responder(conversa.CON_TELEFONE, MensagemBoasVindas);
        }

        private async Task ProcessarCpf(Conversa conversa, string cpf)
        {
            if (!_clienteService.TryNormalizarCpfCnpj(cpf, out var cpfNormalizado))
            {
                await Responder(
                    conversa.CON_TELEFONE,
                    "CPF/CNPJ inválido, digite novamente.");
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

                await Responder(
                    conversa.CON_TELEFONE,
                    $"Olá {cliente.CLI_NOME}!\n\n" +
                    "1 - Novo Pedido\n" +
                    "2 - Acompanhar Pedido");

                return;
            }

            conversa.CON_CPF = cpfNormalizado;
            conversa.CON_ETAPA = EtapaConversa.AguardandoNome;

            await _conversaRepository.Atualizar(conversa);

            await Responder(
                conversa.CON_TELEFONE,
                "Não encontramos seu cadastro.\n\nQual seu nome?");
        }

        private async Task ProcessarNome(Conversa conversa, string nome)
        {
            if (!_clienteService.NomeValido(nome))
            {
                await Responder(
                    conversa.CON_TELEFONE,
                    "Nome inválido, digite novamente.");
                return;
            }

            conversa.CON_NOME = nome.Trim();
            conversa.CON_ETAPA = EtapaConversa.AguardandoEmail;

            await _conversaRepository.Atualizar(conversa);

            await Responder(
                conversa.CON_TELEFONE,
                "Informe seu e-mail.");
        }

        private async Task ProcessarEmail(Conversa conversa, string email)
        {
            if (!_clienteService.EmailValido(email))
            {
                await Responder(
                    conversa.CON_TELEFONE,
                    "E-mail inválido, digite novamente.");
                return;
            }

            conversa.CON_EMAIL = email;
            conversa.CON_ETAPA = EtapaConversa.AguardandoFotoFachada;

            await _conversaRepository.Atualizar(conversa);

            await Responder(
                conversa.CON_TELEFONE,
                "Envie uma foto da fachada do seu estabelecimento.");
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

                await Responder(
                    conversa.CON_TELEFONE,
                    "Cadastro realizado!\n\n" +
                    "1 - Novo Pedido\n" +
                    "2 - Acompanhar Pedido");
                return;
            }

            if (midia == null || !TiposFotoAceitos.Contains(midia.ContentType))
            {
                await Responder(
                    conversa.CON_TELEFONE,
                    "Envie uma foto (JPG, PNG ou WEBP) da fachada do seu estabelecimento.");
                return;
            }

            MidiaBaixada baixada;
            try
            {
                baixada = await _midiaService.BaixarAsync(midia.Url);
            }
            catch (MidiaMuitoGrandeException)
            {
                await Responder(
                    conversa.CON_TELEFONE,
                    "Foto muito grande, envie outra.");
                return;
            }
            catch (ArgumentException)
            {
                // Host fora do Twilio/central só chega por webhook forjado; não loga a URL.
                _logger.LogWarning("MediaUrl de host não permitido recebida para {Telefone}", conversa.CON_TELEFONE);
                await Responder(
                    conversa.CON_TELEFONE,
                    "Não consegui receber a foto, envie novamente.");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao baixar foto da fachada de {Telefone}", conversa.CON_TELEFONE);
                await Responder(
                    conversa.CON_TELEFONE,
                    "Não consegui receber a foto, envie novamente.");
                return;
            }

            if (baixada.Conteudo.Length == 0)
            {
                await Responder(
                    conversa.CON_TELEFONE,
                    "Não consegui receber a foto, envie novamente.");
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
                await Responder(
                    conversa.CON_TELEFONE,
                    "Erro ao finalizar cadastro, envie a foto novamente.");
                return;
            }

            conversa.CON_CLICODIGO = codigoCliente;
            conversa.CON_ETAPA = EtapaConversa.MenuCliente;

            await _conversaRepository.Atualizar(conversa);

            await Responder(
                conversa.CON_TELEFONE,
                "Cadastro realizado!\n\n" +
                "1 - Novo Pedido\n" +
                "2 - Acompanhar Pedido");
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

                    await Responder(
                        conversa.CON_TELEFONE,
                        "🛒 *Novo Pedido Iniciado*\n\n" +
                        "Digite a quantidade e o nome do produto que deseja adicionar (Ex: `1x sorvete de chocolate`).");
                    break;

                case "2":
                    conversa.CON_ETAPA = EtapaConversa.AcompanhandoPedido;
                    await _conversaRepository.Atualizar(conversa);

                    await Responder(
                        conversa.CON_TELEFONE,
                        "Informe o número do pedido.");
                    break;

                default:
                    await Responder(
                        conversa.CON_TELEFONE,
                        "Opção inválida.\n\n" +
                        "1 - Novo Pedido\n" +
                        "2 - Acompanhar Pedido");
                    break;
            }
        }

        private async Task ProcessarNovoPedido(Conversa conversa, string mensagem)
        {
            var textoDigitado = mensagem.Trim();

            // Se o cliente digitou "terminar", processamos o carrinho atual sem incluir a palavra "terminar"
            if (textoDigitado.Equals("terminar", StringComparison.OrdinalIgnoreCase))
            {
                if (!conversa.CON_CLICODIGO.HasValue)
                {
                    await Responder(conversa.CON_TELEFONE, "Erro de identificação do cliente. Reiniciando...");
                    conversa.CON_ETAPA = EtapaConversa.AguardandoCpf;
                    conversa.CON_ULTIMA_MENSAGEM = string.Empty;
                    await _conversaRepository.Atualizar(conversa);
                    return;
                }

                var corpoPedido = conversa.CON_ULTIMA_MENSAGEM ?? string.Empty;

                if (string.IsNullOrWhiteSpace(corpoPedido))
                {
                    await Responder(
                        conversa.CON_TELEFONE,
                        "Nenhum item foi adicionado ao pedido. Envie pelo menos um item ou digite o menu.");
                    return;
                }

                var pedido = new Pedido
                {
                    PED_CLICODIGO = conversa.CON_CLICODIGO.Value,
                    PED_CORPO = corpoPedido, // Contém apenas os produtos puros enviados anteriormente
                    PED_DATA_PEDIDO = DateTime.Now,
                    PED_TOTAL = 0
                };

                var idPedidoGerado = await _pedidoService.CreatePedidoAsync(pedido);

                conversa.CON_ETAPA = EtapaConversa.MenuCliente;
                conversa.CON_ULTIMA_MENSAGEM = string.Empty; // Limpa o carrinho
                await _conversaRepository.Atualizar(conversa);

                await Responder(
                    conversa.CON_TELEFONE,
                    $"✅ *Pedido finalizado com sucesso!*\n\n" +
                    $"📦 *Código do seu pedido:* `{idPedidoGerado}`\n" +
                    $"Guarde esse número para acompanhar seu pedido futuramente.\n\n" +
                    "1 - Novo Pedido\n" +
                    "2 - Acompanhar Pedido");
                return;
            }

            // Se NÃO digitou terminar, valida o item antes de acumular.
            var validacao = _pedidoService.ValidarLinhaItem(textoDigitado);

            switch (validacao.Resultado)
            {
                case ItemPedidoValidacaoResultado.FormatoInvalido:
                    await Responder(
                        conversa.CON_TELEFONE,
                        "Não entendi esse item, use o formato: 2x produto");
                    return;

                case ItemPedidoValidacaoResultado.QuantidadeZero:
                    await Responder(
                        conversa.CON_TELEFONE,
                        "Não entendi esse item, use o formato: 2x produto");
                    return;

                case ItemPedidoValidacaoResultado.QuantidadeMuitoAlta:
                    await Responder(
                        conversa.CON_TELEFONE,
                        "Quantidade muito alta, confirme o valor.");
                    return;

                case ItemPedidoValidacaoResultado.Valido:
                    break;

                default:
                    await Responder(
                        conversa.CON_TELEFONE,
                        "Não entendi esse item, use o formato: 2x produto");
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

            await Responder(
                conversa.CON_TELEFONE,
                "➕ Item adicionado ao carrinho!\n\n" +
                "Envie o próximo produto ou digite *terminar* para concluir o pedido.");
        }

        private async Task ProcessarAcompanhamentoPedido(Conversa conversa, string mensagem)
        {
            var textoDigitado = mensagem.Trim();

            // Tenta converter o que o usuário digitou em um número de ID válido
            if (!int.TryParse(textoDigitado, out var codigoPedido))
            {
                await Responder(
                    conversa.CON_TELEFONE,
                    "⚠️ Código inválido. Por favor, digite apenas o **número** do código do pedido (ex: `15`) ou envie `0` para voltar ao menu.");
                return;
            }

            if (codigoPedido == 0)
            {
                conversa.CON_ETAPA = EtapaConversa.MenuCliente;
                await _conversaRepository.Atualizar(conversa);
                await Responder(
                    conversa.CON_TELEFONE,
                    "Menu Principal:\n\n1 - Novo Pedido\n2 - Acompanhar Pedido");
                return;
            }

            // Busca o pedido no banco pelo ID informado
            var pedido = await _pedidoService.BuscarPorId(codigoPedido); // (Ou chame direto o repositório se preferir)

            if (pedido == null)
            {
                await Responder(
                    conversa.CON_TELEFONE,
                    $"❌ Não encontramos nenhum pedido com o código **{codigoPedido}**.\n\nVerifique o número e tente novamente ou digite `0` para voltar ao menu.");
                return;
            }

            // Retorna o status encontrado para o usuário e devolve ele para o menu
            conversa.CON_ETAPA = EtapaConversa.MenuCliente;
            await _conversaRepository.Atualizar(conversa);

            await Responder(
                conversa.CON_TELEFONE,
                $"📦 *Consulta de Pedido*\n\n" +
                $"• *Código:* `{pedido.PED_CODIGO}`\n" +
                $"• *Status atual:* *{pedido.PED_STATUS ?? "Recebido"}*\n" +
                $"• *Data:* {pedido.PED_DATA_PEDIDO:dd/MM/yyyy HH:mm}\n\n" +
                "1 - Novo Pedido\n" +
                "2 - Acompanhar Pedido");
        }
    }
}
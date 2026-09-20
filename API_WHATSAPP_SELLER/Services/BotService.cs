using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;
using API_WHATSAPP_SELLER.Models.Enum;
using System.Linq;

namespace API_WHATSAPP_SELLER.Services
{
    public class BotService : IBotService
    {
        private readonly ITwilioService _twilioService;
        private readonly IConversaRepository _conversaRepository;
        private readonly IClienteService _clienteService;
        private readonly IPedidoService _pedidoService;
        private readonly IClienteRepository _clienteRepository;

        public BotService(
            ITwilioService twilioService,
            IConversaRepository conversaRepository,
            IClienteService clienteService,
            IPedidoService pedidoService,
            IClienteRepository clienteRepository)
        {
            _twilioService = twilioService;
            _conversaRepository = conversaRepository;
            _clienteService = clienteService;
            _pedidoService = pedidoService;
            _clienteRepository = clienteRepository;
        }

        public async Task ProcessarMensagem(string telefone, string mensagem)
        {
            var conversa = await _conversaRepository.BuscaPorTelefone(telefone);

            if (conversa == null)
            {
                await IniciarConversa(telefone);
                return;
            }

            switch (conversa.CON_ETAPA)
            {
                case EtapaConversa.AguardandoCpf:
                    await ProcessarCpf(conversa, mensagem);
                    break;

                case EtapaConversa.AguardandoNome:
                    await ProcessarNome(conversa, mensagem);
                    break;

                case EtapaConversa.AguardandoEmail:
                    await ProcessarEmail(conversa, mensagem);
                    break;

                case EtapaConversa.MenuCliente:
                    await ProcessarMenu(conversa, mensagem);
                    break;

                case EtapaConversa.NovoPedido:
                    await ProcessarNovoPedido(conversa, mensagem);
                    break;

                case EtapaConversa.AcompanhandoPedido:
                    await ProcessarAcompanhamentoPedido(conversa, mensagem);
                    break;
            }
        }

        private async Task IniciarConversa(string telefone)
        {
            var conversa = new Conversa
            {
                CON_TELEFONE = telefone,
                CON_ETAPA = EtapaConversa.AguardandoCpf
            };

            await _conversaRepository.Criar(conversa);

            await _twilioService.EnviarMensagem(
                telefone,
                "Olá! Seja bem-vindo.\n\nInforme seu CPF ou CNPJ para continuarmos.");
        }

        private async Task ProcessarCpf(Conversa conversa, string cpf)
        {
            if (!_clienteService.TryNormalizarCpfCnpj(cpf, out var cpfNormalizado))
            {
                await _twilioService.EnviarMensagem(
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

                await _twilioService.EnviarMensagem(
                    conversa.CON_TELEFONE,
                    $"Olá {cliente.CLI_NOME}!\n\n" +
                    "1 - Novo Pedido\n" +
                    "2 - Acompanhar Pedido");

                return;
            }

            conversa.CON_CPF = cpfNormalizado;
            conversa.CON_ETAPA = EtapaConversa.AguardandoNome;

            await _conversaRepository.Atualizar(conversa);

            await _twilioService.EnviarMensagem(
                conversa.CON_TELEFONE,
                "Não encontramos seu cadastro.\n\nQual seu nome?");
        }

        private async Task ProcessarNome(Conversa conversa, string nome)
        {
            if (!_clienteService.NomeValido(nome))
            {
                await _twilioService.EnviarMensagem(
                    conversa.CON_TELEFONE,
                    "Nome inválido, digite novamente.");
                return;
            }

            conversa.CON_NOME = nome.Trim();
            conversa.CON_ETAPA = EtapaConversa.AguardandoEmail;

            await _conversaRepository.Atualizar(conversa);

            await _twilioService.EnviarMensagem(
                conversa.CON_TELEFONE,
                "Informe seu e-mail.");
        }

        private async Task ProcessarEmail(Conversa conversa, string email)
        {
            if (!_clienteService.EmailValido(email))
            {
                await _twilioService.EnviarMensagem(
                    conversa.CON_TELEFONE,
                    "E-mail inválido, digite novamente.");
                return;
            }

            conversa.CON_EMAIL = email;

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

            var codigoCliente = await _clienteService.CreateClienteAsync(cliente);

            conversa.CON_CLICODIGO = codigoCliente;
            conversa.CON_ETAPA = EtapaConversa.MenuCliente;

            await _conversaRepository.Atualizar(conversa);

            await _twilioService.EnviarMensagem(
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

                    await _twilioService.EnviarMensagem(
                        conversa.CON_TELEFONE,
                        "🛒 *Novo Pedido Iniciado*\n\n" +
                        "Digite a quantidade e o nome do produto que deseja adicionar (Ex: `1x sorvete de chocolate`).");
                    break;

                case "2":
                    conversa.CON_ETAPA = EtapaConversa.AcompanhandoPedido;
                    await _conversaRepository.Atualizar(conversa);

                    await _twilioService.EnviarMensagem(
                        conversa.CON_TELEFONE,
                        "Informe o número do pedido.");
                    break;

                default:
                    await _twilioService.EnviarMensagem(
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
                    await _twilioService.EnviarMensagem(conversa.CON_TELEFONE, "Erro de identificação do cliente. Reiniciando...");
                    conversa.CON_ETAPA = EtapaConversa.AguardandoCpf;
                    conversa.CON_ULTIMA_MENSAGEM = string.Empty;
                    await _conversaRepository.Atualizar(conversa);
                    return;
                }

                var corpoPedido = conversa.CON_ULTIMA_MENSAGEM ?? string.Empty;

                if (string.IsNullOrWhiteSpace(corpoPedido))
                {
                    await _twilioService.EnviarMensagem(
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

                await _twilioService.EnviarMensagem(
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
                    await _twilioService.EnviarMensagem(
                        conversa.CON_TELEFONE,
                        "Não entendi esse item, use o formato: 2x produto");
                    return;

                case ItemPedidoValidacaoResultado.QuantidadeZero:
                    await _twilioService.EnviarMensagem(
                        conversa.CON_TELEFONE,
                        "Não entendi esse item, use o formato: 2x produto");
                    return;

                case ItemPedidoValidacaoResultado.QuantidadeMuitoAlta:
                    await _twilioService.EnviarMensagem(
                        conversa.CON_TELEFONE,
                        "Quantidade muito alta, confirme o valor.");
                    return;

                case ItemPedidoValidacaoResultado.Valido:
                    break;

                default:
                    await _twilioService.EnviarMensagem(
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

            await _twilioService.EnviarMensagem(
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
                await _twilioService.EnviarMensagem(
                    conversa.CON_TELEFONE,
                    "⚠️ Código inválido. Por favor, digite apenas o **número** do código do pedido (ex: `15`) ou envie `0` para voltar ao menu.");
                return;
            }

            if (codigoPedido == 0)
            {
                conversa.CON_ETAPA = EtapaConversa.MenuCliente;
                await _conversaRepository.Atualizar(conversa);
                await _twilioService.EnviarMensagem(
                    conversa.CON_TELEFONE,
                    "Menu Principal:\n\n1 - Novo Pedido\n2 - Acompanhar Pedido");
                return;
            }

            // Busca o pedido no banco pelo ID informado
            var pedido = await _pedidoService.BuscarPorId(codigoPedido); // (Ou chame direto o repositório se preferir)

            if (pedido == null)
            {
                await _twilioService.EnviarMensagem(
                    conversa.CON_TELEFONE,
                    $"❌ Não encontramos nenhum pedido com o código **{codigoPedido}**.\n\nVerifique o número e tente novamente ou digite `0` para voltar ao menu.");
                return;
            }

            // Retorna o status encontrado para o usuário e devolve ele para o menu
            conversa.CON_ETAPA = EtapaConversa.MenuCliente;
            await _conversaRepository.Atualizar(conversa);

            await _twilioService.EnviarMensagem(
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
using System.Linq;

namespace API_WHATSAPP_SELLER.Services
{
    // Textos enviados ao cliente pelo WhatsApp. Formatação do WhatsApp:
    // *negrito* e _itálico_ (não usa ** nem crases).
    public static class MensagensBot
    {
        // Brasil não tem horário de verão desde 2019: Brasília é sempre UTC-3,
        // independente do fuso do servidor/container.
        private static readonly TimeSpan FusoBrasilia = TimeSpan.FromHours(-3);

        public static string Saudacao() => Saudacao(DateTimeOffset.UtcNow.ToOffset(FusoBrasilia).Hour);

        public static string Saudacao(int hora) => hora switch
        {
            >= 5 and < 12 => "Bom dia",
            >= 12 and < 18 => "Boa tarde",
            _ => "Boa noite"
        };

        public const string Menu =
            "*Como podemos te ajudar?* Responda com o número da opção desejada:\n\n" +
            "*1* - Fazer um novo pedido\n" +
            "*2* - Acompanhar um pedido";

        public const string PedidoCpf =
            "Para começar, por favor informe o seu *CPF* (pessoa física) ou *CNPJ* (empresa).\n" +
            "Pode enviar só os números ou com pontos e traço, como preferir.";

        public static string BoasVindas() =>
            $"{Saudacao()}! 😊 Seja bem-vindo(a) ao nosso atendimento.\n\n" + PedidoCpf;

        public static string BoasVindasDeVolta() =>
            $"{Saudacao()}! Que bom ter você de volta 😊\n\n" + Menu;

        // ---------- Identificação / cadastro ----------

        public const string CpfInvalido =
            "Hmm, não consegui reconhecer esse documento. 🤔\n\n" +
            "O *CPF* tem 11 números e o *CNPJ* tem 14. Confira e envie novamente, por favor.\n" +
            "Exemplo: _123.456.789-00_";

        public static string ClienteEncontrado(string? nome) =>
            $"Que bom falar com você{(string.IsNullOrWhiteSpace(nome) ? "" : $", *{nome.Trim()}*")}! " +
            "Encontramos o seu cadastro. ✅\n\n" + Menu;

        public const string CadastroNaoEncontrado =
            "Não encontramos nenhum cadastro com esse documento, mas não se preocupe: " +
            "vamos fazer o seu agora mesmo, é rapidinho! São só 3 passos: nome, e-mail e uma foto da fachada.\n\n" +
            "*Passo 1 de 3:* qual é o seu nome ou o nome da sua empresa?";

        public const string NomeInvalido =
            "Não recebi o nome. 🙂\n\n" +
            "*Passo 1 de 3:* por favor, digite o seu nome ou o nome da sua empresa.";

        public static string PedirEmail(string? nome) =>
            $"Obrigado{(string.IsNullOrWhiteSpace(nome) ? "" : $", *{nome.Trim()}*")}! 😊\n\n" +
            "*Passo 2 de 3:* qual é o seu e-mail?\n" +
            "Exemplo: _nome@email.com_";

        public const string EmailInvalido =
            "Esse e-mail parece estar incompleto. 🤔\n\n" +
            "Confira se ele tem o *@* e o domínio (exemplo: _nome@email.com_) e envie novamente, por favor.";

        public const string PedirFotoFachada =
            "Perfeito! *Passo 3 de 3:* envie uma *foto da fachada* do seu estabelecimento. 📸\n\n" +
            "Ela ajuda nossa equipe a identificar o seu estabelecimento. " +
            "Você pode tirar a foto agora mesmo pela câmera do WhatsApp.";

        public const string FotoFormatoInvalido =
            "Para concluir o cadastro, precisamos de uma *foto* da fachada do seu estabelecimento " +
            "(formato JPG, PNG ou WEBP). 📸\n\n" +
            "Toque no ícone da câmera ou do clipe 📎 aqui no WhatsApp e envie a imagem, por favor.";

        public const string FotoMuitoGrande =
            "Essa foto ficou grande demais para recebermos. 😕\n\n" +
            "Por favor, envie outra, de preferência tirada direto pela câmera do WhatsApp " +
            "(assim ela já vai em um tamanho menor).";

        public const string FotoNaoRecebida =
            "Não conseguimos receber a sua foto. 😕\n\n" +
            "Por favor, tente enviá-la novamente.";

        public const string ErroAoFinalizarCadastro =
            "Tivemos um problema ao concluir o seu cadastro. Pedimos desculpas! 🙏\n\n" +
            "Por favor, envie a foto da fachada novamente para tentarmos de novo.";

        public const string CadastroConcluido =
            "🎉 *Cadastro concluído com sucesso!* Seja bem-vindo(a)!\n\n" + Menu;

        public const string ErroIdentificacaoCliente =
            "Não conseguimos identificar o seu cadastro. Pedimos desculpas pelo transtorno! 🙏\n\n" +
            "Para continuarmos, por favor informe novamente o seu *CPF* ou *CNPJ*.";

        // ---------- Menu ----------

        public const string OpcaoInvalida =
            "Desculpe, não entendi a sua resposta. 🙂\n\n" + Menu;

        // ---------- Novo pedido ----------

        private const string ExemploItem = "Exemplo: _2x sorvete de chocolate_";

        private const string ComoConcluirOuCancelar =
            "Quando terminar, envie *terminar* para concluir o pedido.\n" +
            "Se quiser desistir, envie *cancelar*.";

        public const string NovoPedidoIniciado =
            "🛒 *Novo pedido*\n\n" +
            "Envie *um item por mensagem*, com a quantidade e o nome do produto.\n" +
            ExemploItem + "\n\n" +
            ComoConcluirOuCancelar;

        public static string ItemAdicionado(string carrinho) =>
            "✅ Item anotado!\n\n" +
            "🛒 *Seu pedido até agora:*\n" + ListarItens(carrinho) + "\n\n" +
            "Pode enviar o próximo item.\n" +
            ComoConcluirOuCancelar;

        public const string ItemFormatoInvalido =
            "Não consegui entender esse item. 🤔\n\n" +
            "Envie a *quantidade* seguida do *nome do produto*, um item por mensagem.\n" +
            ExemploItem + "\n\n" +
            ComoConcluirOuCancelar;

        public const string ItemQuantidadeZero =
            "A quantidade precisa ser de pelo menos *1 unidade*. Por favor, envie o item novamente.\n" +
            ExemploItem;

        public const string ItemQuantidadeMuitoAlta =
            "A quantidade máxima por item é de *100 unidades*. Por favor, confira e envie o item novamente.\n" +
            ExemploItem;

        public const string PedidoSemItens =
            "Seu pedido ainda não tem nenhum item. 🛒\n\n" +
            "Envie pelo menos um item (exemplo: _2x sorvete de chocolate_) " +
            "ou envie *cancelar* para voltar ao menu.";

        public const string PedidoCancelado =
            "Tudo bem, o pedido foi cancelado e nada foi enviado. 👍\n\n" + Menu;

        public static string PedidoFinalizado(int numeroPedido, string carrinho) =>
            "✅ *Pedido recebido com sucesso!* Obrigado pela preferência! 🙏\n\n" +
            $"📦 *Número do pedido:* {numeroPedido}\n\n" +
            "*Itens:*\n" + ListarItens(carrinho) + "\n\n" +
            "Guarde esse número: com ele você acompanha o seu pedido a qualquer momento pela opção *2* do menu.\n\n" +
            Menu;

        // ---------- Acompanhar pedido ----------

        private const string VoltarAoMenu = "Para voltar ao menu, envie *0*.";

        public const string PedirNumeroPedido =
            "📦 *Acompanhar pedido*\n\n" +
            "Por favor, envie o *número do pedido* (ele foi informado quando você finalizou o pedido).\n" +
            "Exemplo: _15_\n\n" +
            VoltarAoMenu;

        public const string NumeroPedidoInvalido =
            "Por favor, envie apenas o *número* do pedido, sem letras ou símbolos.\n" +
            "Exemplo: _15_\n\n" +
            VoltarAoMenu;

        public static string PedidoNaoEncontrado(int numeroPedido) =>
            $"Não encontramos nenhum pedido com o número *{numeroPedido}*. 🤔\n\n" +
            "Confira o número e envie novamente, ou envie *0* para voltar ao menu.";

        public static string StatusPedido(string? codigo, string? status, DateTime data) =>
            $"📦 *Pedido {codigo}*\n\n" +
            $"• *Status:* {status ?? "Recebido"}\n" +
            $"• *Feito em:* {data:dd/MM/yyyy} às {data:HH:mm}\n\n" +
            "Posso ajudar em mais alguma coisa?\n\n" + Menu;

        private static string ListarItens(string carrinho) =>
            string.Join("\n", carrinho
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item => $"• {item}"));
    }
}

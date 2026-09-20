using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;
using System.Text.RegularExpressions;

namespace API_WHATSAPP_SELLER.Services
{
    public class PedidoService : IPedidoService
    {
        private static readonly Regex ItemRegex = new(@"^(\d+)\s*(?:[xX])?\s*(.+)$", RegexOptions.Compiled);

        private readonly IPedidoRepository _pedidoRepository;
        private readonly IClienteRepository _clienteRepository;

        public PedidoService(IPedidoRepository pedidoRepository, IClienteRepository clienteRepository)
        {
            _pedidoRepository = pedidoRepository;
            _clienteRepository = clienteRepository;
        }

        public ItemPedidoValidacao ValidarLinhaItem(string linha)
        {
            var texto = (linha ?? string.Empty).Trim();

            var match = ItemRegex.Match(texto);

            if (!match.Success)
            {
                return new ItemPedidoValidacao { Resultado = ItemPedidoValidacaoResultado.FormatoInvalido };
            }

            if (!int.TryParse(match.Groups[1].Value, out var quantidade))
            {
                return new ItemPedidoValidacao { Resultado = ItemPedidoValidacaoResultado.FormatoInvalido };
            }

            var produto = match.Groups[2].Value.Trim();

            if (quantidade == 0)
            {
                return new ItemPedidoValidacao { Resultado = ItemPedidoValidacaoResultado.QuantidadeZero };
            }

            if (quantidade > 100)
            {
                return new ItemPedidoValidacao { Resultado = ItemPedidoValidacaoResultado.QuantidadeMuitoAlta };
            }

            return new ItemPedidoValidacao
            {
                Resultado = ItemPedidoValidacaoResultado.Valido,
                LinhaNormalizada = $"{quantidade}X {produto.ToUpperInvariant()}"
            };
        }

        public async Task<int> CreatePedidoAsync(Pedido newPedido)
        {
            var cliente = await _clienteRepository.GetByCodigoAsync(newPedido.PED_CLICODIGO);

            if (cliente == null)
                throw new InvalidOperationException($"Cliente {newPedido.PED_CLICODIGO} não encontrado.");

            newPedido.PED_CORPO = NormalizarPedido(newPedido.PED_CORPO);

            return await _pedidoRepository.CreatePedidoAsync(newPedido);
        }

        #region Função para normalizar o texto do pedido
        private string NormalizarPedido(string corpo)
        {
            if (string.IsNullOrWhiteSpace(corpo))
                throw new ArgumentException("O corpo do pedido não pode estar vazio.");

            var linhas = corpo
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            var itens = new List<string>();

            foreach (var linha in linhas)
            {
                var validacao = ValidarLinhaItem(linha);

                if (validacao.Resultado != ItemPedidoValidacaoResultado.Valido)
                    continue;

                itens.Add(validacao.LinhaNormalizada!);
            }

            if (itens.Count == 0)
                throw new ArgumentException("Nenhum item válido foi encontrado no pedido.");

            return string.Join(";\n", itens);
        }
        #endregion

        public async Task<List<Pedido>> GetPedidosAsync()
        {
            return await _pedidoRepository.GetPedidosAsync();
        }

        public async Task<Pedido> BuscarPorId(int id)
        {
            return await _pedidoRepository.BuscarPorId(id);
        }
    }
}

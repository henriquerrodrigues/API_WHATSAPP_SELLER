using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;
using System.Text.RegularExpressions;

namespace API_WHATSAPP_SELLER.Services
{
    public class PedidoService : IPedidoService
    {
        private readonly IPedidoRepository _pedidoRepository;

        public PedidoService(IPedidoRepository pedidoRepository)
        {
            _pedidoRepository = pedidoRepository;
        }

        public async Task<int> CreatePedidoAsync(Pedido newPedido)
        {
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
                var texto = linha.Trim();

                // Procura algo como:
                // 1x Chocolate
                // 2 x Morango
                // 3X Pistache
                var match = Regex.Match(
                    texto,
                    @"^(\d+)\s*(?:[xX])?\s*(.+)$"
                );

                if (!match.Success)
                    continue;

                var quantidade = match.Groups[1].Value;
                var produto = match.Groups[2].Value.Trim();

                produto = produto.ToUpperInvariant();

                itens.Add($"{quantidade}X {produto}");
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

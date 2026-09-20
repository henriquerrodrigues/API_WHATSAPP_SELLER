using API_WHATSAPP_SELLER.Models;

namespace API_WHATSAPP_SELLER.Interfaces
{
    public interface IPedidoService
    {
        Task<int> CreatePedidoAsync(Pedido newPedido);
        Task<List<Pedido>> GetPedidosAsync();
        Task<Pedido> BuscarPorId(int id);
        ItemPedidoValidacao ValidarLinhaItem(string linha);
    }

    public enum ItemPedidoValidacaoResultado
    {
        Valido,
        FormatoInvalido,
        QuantidadeZero,
        QuantidadeMuitoAlta
    }

    public class ItemPedidoValidacao
    {
        public ItemPedidoValidacaoResultado Resultado { get; set; }
        public string? LinhaNormalizada { get; set; }
    }
}

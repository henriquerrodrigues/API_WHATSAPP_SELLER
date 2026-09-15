using API_WHATSAPP_SELLER.Models;

namespace API_WHATSAPP_SELLER.Interfaces
{
    public interface IPedidoService
    {
        Task<int> CreatePedidoAsync(Pedido newPedido);
        Task<List<Pedido>> GetPedidosAsync();
        Task<Pedido> BuscarPorId(int id);
    }
}

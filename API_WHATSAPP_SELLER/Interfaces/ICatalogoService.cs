using API_WHATSAPP_SELLER.Models;

namespace API_WHATSAPP_SELLER.Interfaces
{
    public interface ICatalogoService
    {
        Task<List<Produto>> GetProdutosAsync();
    }
}

using API_WHATSAPP_SELLER.Models;

namespace API_WHATSAPP_SELLER.Interfaces
{
    public interface IProdutoRepository
    {
        Task<List<Produto>> GetProdutosAsync();
    }
}

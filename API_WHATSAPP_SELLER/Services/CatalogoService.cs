using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;

namespace API_WHATSAPP_SELLER.Services
{
    public class CatalogoService : ICatalogoService
    {
        private readonly IProdutoRepository _produtoRepository;
        public CatalogoService(IProdutoRepository produtoRepository)
        {
            _produtoRepository = produtoRepository;
        }
        public async Task<List<Produto>> GetProdutosAsync()
        {
            return await _produtoRepository.GetProdutosAsync();
        }
    }
}

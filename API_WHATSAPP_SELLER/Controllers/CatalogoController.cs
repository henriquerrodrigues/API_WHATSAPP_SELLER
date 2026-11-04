using API_WHATSAPP_SELLER.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace API_WHATSAPP_SELLER.Controllers
{

    [ApiController]
    [Route("api/[controller]")]
    public class CatalogoController : ControllerBase
    {
        private readonly ICatalogoService _catalogoService;

        public CatalogoController(ICatalogoService catalogoService)
        {
            _catalogoService = catalogoService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var produtos = await _catalogoService.GetProdutosAsync();
            return Ok(produtos);
        }
    }
}

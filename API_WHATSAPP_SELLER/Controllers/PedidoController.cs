using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;
using Microsoft.AspNetCore.Mvc;

namespace API_WHATSAPP_SELLER.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PedidoController : ControllerBase
    {
        private readonly IPedidoService _pedidoService;

        public PedidoController(IPedidoService pedidoService)
        {
            _pedidoService = pedidoService;
        }

        [HttpPost]
        public async Task<IActionResult> Create(Pedido newPedido)
        {
            var id = await _pedidoService.CreatePedidoAsync(newPedido);
            return Ok(id);
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var pedidos = await _pedidoService.GetPedidosAsync();
            return Ok(pedidos);
        }

    }


}

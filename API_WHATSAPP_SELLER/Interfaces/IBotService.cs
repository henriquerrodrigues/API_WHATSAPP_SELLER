using API_WHATSAPP_SELLER.Models;

namespace API_WHATSAPP_SELLER.Interfaces
{
    public interface IBotService
    {
        Task ProcessarMensagem(MensagemRecebida mensagem);
        Task EncerrarConversa(string telefone);
    }
}

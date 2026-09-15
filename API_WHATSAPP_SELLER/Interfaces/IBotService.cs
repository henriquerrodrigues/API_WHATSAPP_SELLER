namespace API_WHATSAPP_SELLER.Interfaces
{
    public interface IBotService
    {
        Task ProcessarMensagem(string telefone, string mensagem);
    }
}

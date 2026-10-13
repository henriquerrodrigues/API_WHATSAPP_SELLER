namespace API_WHATSAPP_SELLER.Interfaces
{
    public interface IEnvioMensagemService
    {
        Task EnviarMensagem(string telefone, string mensagem);
    }
}

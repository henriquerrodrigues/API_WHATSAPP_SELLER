namespace API_WHATSAPP_SELLER.Models
{
    public class MensagemRecebida
    {
        public string Telefone { get; set; } = string.Empty;
        public string Texto { get; set; } = string.Empty;
        public MidiaRecebida? Midia { get; set; }
    }

    public class MidiaRecebida
    {
        public string Url { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
    }
}

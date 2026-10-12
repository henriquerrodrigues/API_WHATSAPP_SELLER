namespace API_WHATSAPP_SELLER.Models
{
    public class FotoCliente
    {
        public int FOT_CODIGO { get; set; }
        public int FOT_CLICODIGO { get; set; }
        public byte[] FOT_CONTEUDO { get; set; } = Array.Empty<byte>();
        public string FOT_CONTENT_TYPE { get; set; } = string.Empty;
        public int FOT_TAMANHO { get; set; }
        public bool FOT_SINCRONIZADO_BLING { get; set; }
        public DateTime FOT_CREATED_AT { get; set; } = DateTime.Now;
    }
}

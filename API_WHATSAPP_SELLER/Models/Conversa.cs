using API_WHATSAPP_SELLER.Models.Enum;

namespace API_WHATSAPP_SELLER.Models
{
    public class Conversa
    {
        public int CON_CODIGO { get; set; }
        public string CON_TELEFONE { get; set; } = string.Empty;
        public StatusConversa CON_STATUS { get; set; } = StatusConversa.Ativa;
        public EtapaConversa CON_ETAPA { get; set; }
        public string? CON_CPF { get; set; }
        public int? CON_CLICODIGO { get; set; }
        public string? CON_NOME { get; set; }
        public string? CON_EMAIL { get; set; }
        public DateTime CON_ULTIMA_INTERACAO { get; set; }
        public string? CON_ULTIMA_MENSAGEM { get; set; }

    }
}

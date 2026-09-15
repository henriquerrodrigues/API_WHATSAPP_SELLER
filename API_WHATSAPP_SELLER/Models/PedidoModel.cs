namespace API_WHATSAPP_SELLER.Models
{
    public class Pedido
    {
        public string PED_CODIGO { get; set; }
        public string PED_NUMERO { get; set; }
        public int PED_CLICODIGO { get; set; }
        public DateTime PED_DATA_PEDIDO { get; set; }
        public decimal PED_TOTAL { get; set; }
        public string PED_CORPO { get; set; }
        public string? PED_STATUS { get; set; }
        public DateTime PED_CRIADO_EM { get; set; } = DateTime.Now;
        public DateTime PED_ATUALIZADO_EM { get; set; } = DateTime.Now;
    }
}

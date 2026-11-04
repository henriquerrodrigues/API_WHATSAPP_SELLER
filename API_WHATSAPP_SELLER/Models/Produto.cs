namespace API_WHATSAPP_SELLER.Models
{
    public class Produto
    {
        public int PRO_CODIGO { get; set; }
        public string PRO_SKU { get; set; } = string.Empty;
        public string PRO_NOME { get; set; } = string.Empty;
        public float PRO_PRECO { get; set; } = 0.0f;
        public int PRO_ESTOQUE_ATUAL { get; set; } = 0;
        public int PRO_SALDO_VIRTUAL { get; set; } = 0;
        public int PRO_ESTOQUE_MINIMO { get; set; } = 0;
        public DateTime PRO_CRIADO_EM { get; set; } = DateTime.Now;
        public DateTime PRO_ATUALIZADO_EM { get; set; } = DateTime.Now;
        public string PRO_SITUACAO { get; set; } = string.Empty;
        public string PRO_TIPO { get; set; } = string.Empty;
        public int PRO_PRODUTO_PAI { get; set; } = 0;
        public int PRO_CATEGORIA_CODIGO { get; set; } = 0;
        public string PRO_CATEGORIA_NOME { get; set; } = string.Empty;
        public string PRO_DESCRICAO_CURTA { get; set; } = string.Empty;
        public string PRO_UNIDADE { get; set; } = string.Empty;
        public int PRO_VISIVEL_CARDAPIO { get; set; } = 0;
        public DateTime PRO_ESTOQUE_SINCRONIZADO_EM { get; set; } = DateTime.Now;
        public DateTime PRO_MIDIA_SINCRONIZADA_EM { get; set; } = DateTime.Now;
    }
}

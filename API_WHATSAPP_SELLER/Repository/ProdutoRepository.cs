using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;
using Dapper;
using MySqlConnector;
using System.Data;

namespace API_WHATSAPP_SELLER.Repository
{
    public class ProdutoRepository : IProdutoRepository
    {
        private readonly IConfiguration _configuration;

        public ProdutoRepository(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IDbConnection Connection()
        {
            return new MySqlConnection(_configuration.GetConnectionString("WppSellerDB"));
        }

        public async Task<List<Produto>> GetProdutosAsync()
        {
            const string sql = @"
                SELECT
                    PRO_CODIGO,
                    PRO_SKU,
                    PRO_NOME,
                    PRO_PRECO,
                    PRO_ESTOQUE_ATUAL,
                    PRO_SALDO_VIRTUAL,
                    PRO_ESTOQUE_MINIMO,
                    PRO_CRIADO_EM,
                    PRO_ATUALIZADO_EM,
                    PRO_SITUACAO,
                    PRO_TIPO,
                    PRO_PRODUTO_PAI,
                    PRO_CATEGORIA_CODIGO,
                    PRO_CATEGORIA_NOME,
                    PRO_DESCRICAO_CURTA,
                    PRO_UNIDADE,
                    PRO_VISIVEL_CARDAPIO,
                    PRO_ESTOQUE_SINCRONIZADO_EM,
                    PRO_MIDIA_SINCRONIZADA_EM
                FROM tb_produto;
            ";

            using var connection = Connection();

            var produtos = await connection.QueryAsync<Produto>(sql);

            return produtos.ToList();
        }
    }
}

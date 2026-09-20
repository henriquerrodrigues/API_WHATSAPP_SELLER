using Dapper;
using MySqlConnector;
using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;

namespace API_WHATSAPP_SELLER.Repository
{
    public class PedidoRepository : IPedidoRepository
    {
        private readonly IConfiguration _configuration;

        public PedidoRepository(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<int> CreatePedidoAsync(Pedido newPedido)
        {
            using var connection = new MySqlConnection(
                _configuration.GetConnectionString("WppSellerDB")
            );

            var sql = @"
                    INSERT INTO tb_pedido
                    (
                        PED_CLICODIGO,
                        PED_DATA_PEDIDO,
                        PED_TOTAL,
                        PED_CORPO
                    )
                    VALUES
                    (
                        @PED_CLICODIGO,
                        @PED_DATA_PEDIDO,
                        @PED_TOTAL,
                        @PED_CORPO
                    );

                    SET @novo_id = LAST_INSERT_ID();

                    UPDATE tb_pedido SET PED_NUMERO = @novo_id WHERE PED_CODIGO = @novo_id;

                    SELECT @novo_id;
                ";

            // Executa insert + update do PED_NUMERO + select do id na mesma conexão/transação implícita
            return await connection.ExecuteScalarAsync<int>(sql, newPedido);
        }
        public async Task<List<Pedido>> GetPedidosAsync()
        {
            using var connection = new MySqlConnection(
                _configuration.GetConnectionString("WppSellerDB")
            );

            var sql = @"
                SELECT
                    PED_CODIGO,
                    PED_NUMERO,
                    PED_CLICODIGO,
                    PED_DATA_PEDIDO,
                    PED_TOTAL,
                    PED_CORPO,
                    PED_CRIADO_EM,
                    PED_ATUALIZADO_EM
                FROM tb_pedido;
            ";

            var pedidos = await connection.QueryAsync<Pedido>(sql);

            return pedidos.ToList();
        }

        public async Task<Pedido> BuscarPorId(int id)
        {
            using var connection = new MySqlConnection(
                _configuration.GetConnectionString("WppSellerDB")
            );

            const string sql = @"
                SELECT 
                    PED_CODIGO, 
                    PED_NUMERO, 
                    PED_CLICODIGO, 
                    PED_DATA_PEDIDO, 
                    PED_TOTAL, 
                    PED_CORPO, 
                    PED_STATUS 
                FROM tb_pedido 
                WHERE PED_CODIGO = @PedidoId;
            ";

            return await connection.QueryFirstOrDefaultAsync<Pedido>(sql, new { PedidoId = id });
        }
    }
}
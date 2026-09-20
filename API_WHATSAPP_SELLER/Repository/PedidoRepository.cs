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

                    UPDATE tb_pedido SET PED_NUMERO = LAST_INSERT_ID() WHERE PED_CODIGO = LAST_INSERT_ID();

                    SELECT LAST_INSERT_ID();
                ";

            // LAST_INSERT_ID() é escopado à conexão/sessão MySQL e não muda entre as chamadas
            // acima, então repeti-lo evita depender de variável de usuário (@novo_id), que
            // exigiria "Allow User Variables=True" na connection string do MySqlConnector.
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
using Dapper;
using System.Data;
using MySqlConnector;
using API_WHATSAPP_SELLER.Models;
using API_WHATSAPP_SELLER.Interfaces;

namespace API_WHATSAPP_SELLER.Repository
{
    public class ClienteRepository : IClienteRepository
    {

        private readonly IConfiguration _configuration;

        public ClienteRepository(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IDbConnection Connection()
        {
            return new MySqlConnection(_configuration.GetConnectionString("WppSellerDB"));
        }

        public async Task<int> CreateClienteAsync(Cliente newCliente)
        {
            const string sql = @"
                INSERT INTO tb_cliente
                (
                    CLI_NOME,
                    CLI_EMAIL,
                    CLI_TELEFONE,
                    CLI_CPFCNPJ,
                    CLI_CREATED_AT,
                    CLI_LAST_UPDATED_AT
                )
                VALUES
                (
                    @CLI_NOME,
                    @CLI_EMAIL,
                    @CLI_TELEFONE,
                    @CLI_CPFCNPJ,
                    @CLI_CREATED_AT,
                    @CLI_LAST_UPDATED_AT
                );

                SELECT LAST_INSERT_ID();
            ";

            using var conn = Connection();

            var id = await conn.ExecuteScalarAsync<int>(sql, newCliente);

            return id;
        }

        public async Task<List<Cliente>> GetClientesAsync()
        {
            const string sql = @"
                SELECT
                    CLI_CODIGO,
                    CLI_NOME,
                    CLI_EMAIL,
                    CLI_TELEFONE,
                    CLI_CPFCNPJ,
                    CLI_CREATED_AT,
                    CLI_LAST_UPDATED_AT
               FROM
                    TB_CLIENTE;                    
            ";

            using var conn = Connection();

            var clientes = await conn.QueryAsync<Cliente>(sql);

            return clientes.ToList();
        }

        public async Task<Cliente?> GetByTelefoneAsync(string telefone)
        {
            const string sql = @"
                SELECT
                    CLI_CODIGO,
                    CLI_NOME,
                    CLI_EMAIL,
                    CLI_TELEFONE,
                    CLI_CPFCNPJ,
                    CLI_CREATED_AT,
                    CLI_LAST_UPDATED_AT
               FROM
                    tb_cliente
               WHERE 
                    CLI_TELEFONE = @CLI_TELEFONE;
            ";

            using var conn = Connection();

            // Use QueryFirstOrDefaultAsync: se encontrar, retorna o Cliente. Se não, retorna null.
            return await conn.QueryFirstOrDefaultAsync<Cliente>(sql, new { Telefone = telefone });
        }

        public async Task<Cliente?> GetByCodigoAsync(int codigo)
        {
            const string sql = @"
                SELECT
                    CLI_CODIGO,
                    CLI_NOME,
                    CLI_EMAIL,
                    CLI_TELEFONE,
                    CLI_CPFCNPJ,
                    CLI_CREATED_AT,
                    CLI_LAST_UPDATED_AT
               FROM
                    tb_cliente
               WHERE
                    CLI_CODIGO = @CLI_CODIGO;
            ";

            using var conn = Connection();

            return await conn.QueryFirstOrDefaultAsync<Cliente>(sql, new { CLI_CODIGO = codigo });
        }

        public async Task<Cliente?> BuscarPorCpf(string cpf)
        {
            const string sql = @"
                SELECT
                    CLI_CODIGO,
                    CLI_NOME,
                    CLI_EMAIL,
                    CLI_TELEFONE,
                    CLI_CPFCNPJ,
                    CLI_CREATED_AT,
                    CLI_LAST_UPDATED_AT
               FROM
                    tb_cliente
               WHERE 
                    CLI_CPFCNPJ = @CLI_CPFCNPJ;
            ";

            using var conn = Connection();

            
            return await conn.QueryFirstOrDefaultAsync<Cliente>(sql, new { CLI_CPFCNPJ = cpf });
        }
    }
}

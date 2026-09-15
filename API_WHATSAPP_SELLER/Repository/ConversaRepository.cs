using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;
using Dapper;
using Microsoft.AspNetCore.Http.HttpResults;
using MySqlConnector;
using System.Data;

namespace API_WHATSAPP_SELLER.Repository
{
    public class ConversaRepository : IConversaRepository
    {

        private readonly IConfiguration _configuration;

        public ConversaRepository(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IDbConnection Connection()
        {
            return new MySqlConnection(_configuration.GetConnectionString("WppSellerDB"));
        }


        public async Task<Conversa?> BuscaPorTelefone(string telefone)
        {
            const string sql = @"
                SELECT
                    CON_CODIGO,
                    CON_TELEFONE,
                    CON_STATUS,
                    CON_ETAPA,
                    CON_CPF,
                    CON_CLICODIGO,
                    CON_NOME,
                    CON_EMAIL,
                    CON_ULTIMA_INTERACAO,
                    CON_ULTIMA_MENSAGEM
                FROM
                    tb_conversa
                WHERE
                    CON_TELEFONE = @CON_TELEFONE
            ";

            using var conn = Connection();

            return await conn.QueryFirstOrDefaultAsync<Conversa>(sql, new { CON_TELEFONE = telefone });

        }

        public async Task<int> Criar(Conversa conversa)
        {
            const string sql = @"
                INSERT INTO
                    tb_conversa(
                        CON_TELEFONE, 
                        CON_STATUS, 
                        CON_ETAPA, 
                        CON_CPF, 
                        CON_NOME, 
                        CON_EMAIL, 
                        CON_ULTIMA_INTERACAO
                    ) 
                    VALUES (
                        @CON_TELEFONE, 
                        @CON_STATUS, 
                        @CON_ETAPA, 
                        @CON_CPF, 
                        @CON_NOME, 
                        @CON_EMAIL, 
                        NOW() -- O MySQL já grava a data/hora atual automaticamente
                    );
            ";

            using var conn = Connection();

            return await conn.ExecuteAsync(sql, conversa);
        }

        public async Task<int> Atualizar(Conversa conversa)
        {
            const string sql = @"
                UPDATE tb_conversa 
                SET 
                    CON_STATUS = @CON_STATUS,
                    CON_ETAPA = @CON_ETAPA,
                    CON_CPF = @CON_CPF,
                    CON_CLICODIGO = @CON_CLICODIGO,
                    CON_NOME = @CON_NOME,
                    CON_EMAIL = @CON_EMAIL,
                    CON_ULTIMA_INTERACAO = NOW(),
                    CON_ULTIMA_MENSAGEM = @CON_ULTIMA_MENSAGEM
                WHERE CON_CODIGO = @CON_CODIGO;";

            using var conn = Connection();
            return await conn.ExecuteAsync(sql, conversa);
        }


    }
}

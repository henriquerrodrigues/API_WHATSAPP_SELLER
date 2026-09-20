using API_WHATSAPP_SELLER.Models;

namespace API_WHATSAPP_SELLER.Interfaces
{
    public interface IClienteService
    {
        Task<int> CreateClienteAsync(Cliente newCliente);
        Task<List<Cliente>> GetClientesAsync();
        Task<Cliente> BuscaPorCpf(string cpf);
        bool TryNormalizarCpfCnpj(string cpfCnpj, out string normalizado);
        bool EmailValido(string email);
        bool NomeValido(string nome);
    }
}

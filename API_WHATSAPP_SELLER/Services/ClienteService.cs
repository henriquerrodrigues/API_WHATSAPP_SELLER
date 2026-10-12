using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;
using System.Text.RegularExpressions;

namespace API_WHATSAPP_SELLER.Services
{
    public class ClienteService : IClienteService
    {
        private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        private readonly IClienteRepository _clienteRepository;

        public ClienteService(IClienteRepository clienteRepository)
        {
           _clienteRepository = clienteRepository;
        }

        public async Task<int> CreateClienteAsync(Cliente newCliente)
        {
            if(newCliente == null)
                throw new ArgumentNullException(nameof(newCliente));

            return await _clienteRepository.CreateClienteAsync(newCliente);
        }

        public async Task<int> CreateClienteComFotoAsync(Cliente newCliente, FotoCliente foto)
        {
            if (newCliente == null)
                throw new ArgumentNullException(nameof(newCliente));

            if (foto == null)
                throw new ArgumentNullException(nameof(foto));

            return await _clienteRepository.CreateClienteComFotoAsync(newCliente, foto);
        }

        public async Task<List<Cliente>> GetClientesAsync()
        {
            return await _clienteRepository.GetClientesAsync();
        }

        public async Task<Cliente> BuscaPorCpf(string cpf)
        {
            return await _clienteRepository.BuscarPorCpf(cpf);
        }

        public bool TryNormalizarCpfCnpj(string cpfCnpj, out string normalizado)
        {
            normalizado = string.Empty;

            if (string.IsNullOrWhiteSpace(cpfCnpj))
                return false;

            var semMascara = cpfCnpj.Replace(".", "").Replace("-", "").Replace("/", "");

            if (!semMascara.All(char.IsDigit))
                return false;

            if (semMascara.Length != 11 && semMascara.Length != 14)
                return false;

            normalizado = semMascara;
            return true;
        }

        public bool EmailValido(string email)
        {
            return !string.IsNullOrWhiteSpace(email) && EmailRegex.IsMatch(email);
        }

        public bool NomeValido(string nome)
        {
            return !string.IsNullOrWhiteSpace(nome);
        }
    }
}

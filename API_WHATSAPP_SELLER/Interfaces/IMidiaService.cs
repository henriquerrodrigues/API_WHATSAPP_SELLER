using API_WHATSAPP_SELLER.Models;

namespace API_WHATSAPP_SELLER.Interfaces
{
    public interface IMidiaService
    {
        Task<MidiaBaixada> BaixarAsync(string url, CancellationToken ct = default);
    }
}

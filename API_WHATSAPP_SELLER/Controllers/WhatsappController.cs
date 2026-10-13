using Microsoft.AspNetCore.Mvc;
using API_WHATSAPP_SELLER.Services;
using Microsoft.IdentityModel.Tokens;
using API_WHATSAPP_SELLER.Models;
using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Filters;
using API_WHATSAPP_SELLER.Models.Enum;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace API_WHATSAPP_SELLER.Controllers;

[ApiController]
[Route("api/whatsapp")]
public class WhatsappController : ControllerBase
{
    // A central desiste do repasse em 15 s e tenta de novo por alguns minutos.
    private static readonly TimeSpan JanelaDuplicata = TimeSpan.FromMinutes(10);

    private readonly IBotService _botservice;
    private readonly IEnvioMensagemService _envioService;
    private readonly IMemoryCache _cache;
    private readonly CentralSettings _central;

    public WhatsappController(
        IBotService botservice,
        IEnvioMensagemService envioService,
        IMemoryCache cache,
        IOptions<CentralSettings> centralOptions)
    {
        _botservice = botservice;
        _envioService = envioService;
        _cache = cache;
        _central = centralOptions.Value;
    }

    [HttpPost("webhook")]
    [CentralSecret]
    public async Task<IActionResult> ReceberMensagem()
    {
        var form = Request.Form;

        // Marca antes de processar: a repetição que chega enquanto a primeira
        // ainda roda (ex.: download da foto) não executa o bot de novo.
        var messageSid = form["MessageSid"].ToString();
        string? chave = null;
        if (!string.IsNullOrEmpty(messageSid))
        {
            chave = $"MessageSid:{messageSid}";
            if (_cache.TryGetValue(chave, out _))
                return Ok();

            _cache.Set(chave, true, JanelaDuplicata);
        }

        var mensagem = new MensagemRecebida
        {
            Telefone = form["From"].ToString(),
            Texto = form["Body"].ToString(),
            Midia = LerPrimeiraMidia(form)
        };

        try
        {
            await _botservice.ProcessarMensagem(mensagem);
        }
        catch
        {
            // Libera o SID para que o retry da central não seja engolido como duplicata.
            if (chave != null)
                _cache.Remove(chave);
            throw;
        }

        return Ok();
    }

    // Central avisa que o ticket foi fechado. Idempotente: sempre 200.
    [HttpPost("reset")]
    [CentralSecret]
    public async Task<IActionResult> Resetar()
    {
        if (_central.Canal != CanalEnvio.Central)
            return NotFound();

        var telefone = Request.Form["telefone"].ToString();
        if (!string.IsNullOrWhiteSpace(telefone))
            await _botservice.EncerrarConversa(telefone);

        return Ok();
    }

    // Twilio manda NumMedia + MediaUrl{N}/MediaContentType{N}; só a primeira
    // mídia interessa ao bot.
    private static MidiaRecebida? LerPrimeiraMidia(IFormCollection form)
    {
        if (!int.TryParse(form["NumMedia"].ToString(), out var numMedia) || numMedia <= 0)
            return null;

        return new MidiaRecebida
        {
            Url = form["MediaUrl0"].ToString(),
            ContentType = form["MediaContentType0"].ToString()
        };
    }

    [HttpPost("enviar")]
    public IActionResult EnviarMensagem([FromBody] MensagemRequestModel request)
    {
        if(string.IsNullOrEmpty(request.Telefone) || string.IsNullOrEmpty(request.Mensagem))
        {
            return BadRequest("Telefone e Mensagem são obrigatórios.");
        }

        _envioService.EnviarMensagem(request.Telefone, request.Mensagem);
        return Ok(new { status = "Mensagem enviada com sucesso" });
    }
}
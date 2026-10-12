using Microsoft.AspNetCore.Mvc;
using API_WHATSAPP_SELLER.Services;
using Microsoft.IdentityModel.Tokens;
using API_WHATSAPP_SELLER.Models;
using API_WHATSAPP_SELLER.Interfaces;

namespace API_WHATSAPP_SELLER.Controllers;

[ApiController]
[Route("api/whatsapp")]
public class WhatsappController : ControllerBase
{
    private readonly IBotService _botservice;
    private readonly ITwilioService _twilioService;


    public WhatsappController(IBotService botservice, ITwilioService twilioService)
    {
        _botservice = botservice;
        _twilioService = twilioService;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> ReceberMensagem()
    {
        var form = Request.Form;

        var mensagem = new MensagemRecebida
        {
            Telefone = form["From"].ToString(),
            Texto = form["Body"].ToString(),
            Midia = LerPrimeiraMidia(form)
        };

        await _botservice.ProcessarMensagem(mensagem);

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

        _twilioService.EnviarMensagem(request.Telefone, request.Mensagem);
        return Ok(new { status = "Mensagem enviada com sucesso" });
    }
}
using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;
using API_WHATSAPP_SELLER.Models.Enum;
using API_WHATSAPP_SELLER.Repository;
using API_WHATSAPP_SELLER.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuração explícita para desativar o monitoramento automático de arquivos (evita o erro de inotify no Render)
builder.Configuration.Sources.Clear();
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();

builder.Services.AddScoped<IConversaRepository, ConversaRepository>();
builder.Services.AddScoped<IClienteRepository, ClienteRepository>();
builder.Services.AddScoped<IPedidoRepository, PedidoRepository>();

builder.Services.AddScoped<IClienteService, ClienteService>();
builder.Services.AddScoped<IPedidoService, PedidoService>();

builder.Services.Configure<TwilioSettings>(
    builder.Configuration.GetSection("Twilio")
);

builder.Services.AddOptions<CentralSettings>()
    .Bind(builder.Configuration.GetSection("Central"))
    .Validate(s => s.EhValido(),
        "Central__Canal=Central exige Central__ApiUrl (https), Central__ApiToken e Central__SharedSecret.")
    .ValidateOnStart();

// Central__Canal escolhe por onde o bot responde; Twilio continua como
// rollback sem deploy.
var central = builder.Configuration.GetSection("Central").Get<CentralSettings>() ?? new CentralSettings();
if (central.Canal == CanalEnvio.Central)
{
    builder.Services.AddHttpClient<IEnvioMensagemService, CentralMensagemService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(20);
    });
}
else
{
    builder.Services.AddScoped<IEnvioMensagemService, TwilioService>();
}

// Timeout de 10 s cobre só até os headers; o serviço limita o tempo total
// do download (o webhook do Twilio desiste em 15 s).
builder.Services.AddHttpClient<IMidiaService, MidiaService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddScoped<IBotService, BotService>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();
app.Run();
using API_WHATSAPP_SELLER.Interfaces;
using API_WHATSAPP_SELLER.Models;
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

builder.Services.AddScoped<IConversaRepository, ConversaRepository>();
builder.Services.AddScoped<IClienteRepository, ClienteRepository>();
builder.Services.AddScoped<IPedidoRepository, PedidoRepository>();

builder.Services.AddScoped<IClienteService, ClienteService>();
builder.Services.AddScoped<IPedidoService, PedidoService>();

builder.Services.AddScoped<ITwilioService, TwilioService>();
builder.Services.Configure<TwilioSettings>(
    builder.Configuration.GetSection("Twilio")
);

// Timeout de 10 s cobre só até os headers; o serviço limita o tempo total
// do download (o webhook do Twilio desiste em 15 s).
builder.Services.AddHttpClient<ITwilioMidiaService, TwilioMidiaService>(client =>
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
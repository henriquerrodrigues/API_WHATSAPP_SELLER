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

// ==========================================
// 1. REGISTRO DE SERVIÇOS (Antes do Build)
// ==========================================
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configuração do CORS para o Front-end (Angular na porta 4200)
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy
            .WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

builder.Services.AddScoped<IConversaRepository, ConversaRepository>();
builder.Services.AddScoped<IClienteRepository, ClienteRepository>();
builder.Services.AddScoped<IPedidoRepository, PedidoRepository>();
builder.Services.AddScoped<IProdutoRepository, ProdutoRepository>();
builder.Services.AddScoped<IClienteService, ClienteService>();
builder.Services.AddScoped<IPedidoService, PedidoService>();
builder.Services.AddScoped<ICatalogoService, CatalogoService>();
builder.Services.AddScoped<ITwilioService, TwilioService>();

builder.Services.Configure<TwilioSettings>(
    builder.Configuration.GetSection("Twilio")
);

builder.Services.AddHttpClient<ITwilioMidiaService, TwilioMidiaService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddScoped<IBotService, BotService>();

// Constrói a aplicação
var app = builder.Build();

// ==========================================
// 2. PIPELINE DE REQUISIÇÕES (Depois do Build)
// ==========================================
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Ativa a política de CORS criada acima
app.UseCors("Frontend");

app.UseAuthorization();
app.MapControllers();

app.Run();
using BannerlordMcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// stdout é reservado para o protocolo MCP; todo log vai para stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// Pasta do Bannerlord: variável de ambiente > 1º argumento > caminho padrão do Steam.
Bannerlord.GameDir =
    Environment.GetEnvironmentVariable("BANNERLORD_DIR")
    ?? (args.Length > 0 ? args[0] : null)
    ?? @"C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord";

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

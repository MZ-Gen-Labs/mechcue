using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
var builder=Host.CreateApplicationBuilder(args.Where(a=>a is not ("--allow-solidedge" or "--allow-solidedge-write")).ToArray());
builder.Logging.ClearProviders();builder.Logging.AddConsole(o=>o.LogToStandardErrorThreshold=LogLevel.Trace);
builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();
await builder.Build().RunAsync();

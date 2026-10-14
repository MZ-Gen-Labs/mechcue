using MechCue;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
if(args.Contains("--physics-self-test")){Environment.ExitCode=MechCue.Physics.PhysicsSelfTest.Run();return;}
if(McpUpdate.Updating()) return;
McpAccessSettings.StartTray();
var builder=Host.CreateApplicationBuilder(args.Where(a=>a is not ("--allow-solidedge" or "--allow-solidedge-write")).ToArray());
builder.Logging.ClearProviders();builder.Logging.AddConsole(o=>o.LogToStandardErrorThreshold=LogLevel.Trace);
var traffic = new McpTrafficJournal();
builder.Services.AddMcpServer(o => o.ServerInstructions = McpWorkflowRules.Instructions).WithStreamServerTransport(new McpTrafficStream(Console.OpenStandardInput(), traffic, "in"), new McpTrafficStream(Console.OpenStandardOutput(), traffic, "out")).WithToolsFromAssembly()
    .WithRequestFilters(filters=>filters.AddCallToolFilter(next=>(context,ct)=>StrictToolArguments.Invoke(context,c=>next(c,ct))));
using var host=builder.Build();
using var shutdown=new McpShutdownSignal(()=>host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication());
await host.RunAsync();

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

// Azure Functions .NET 8 isolated worker host for the Claims adjudication Durable orchestration.
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddHttpClient();
    })
    .Build();

host.Run();

using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Hartonomous.App.Shared.Services;
using Hartonomous.App.Web.Client.Services;

namespace Hartonomous.App.Web.Client;

class Program
{
    static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);

        // Add device-specific services used by the Hartonomous.App.Shared project
        builder.Services.AddSingleton<IFormFactor, FormFactor>();

        await builder.Build().RunAsync();
    }
}

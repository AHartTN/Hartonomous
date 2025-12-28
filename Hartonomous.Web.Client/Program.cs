using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Hartonomous.UI.Services;
using Hartonomous.Web.Client.Services;

namespace Hartonomous.Web.Client;

sealed class Program
{
    static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);

        // Add device-specific services used by the Hartonomous.App.Shared project
        builder.Services.AddSingleton<IFormFactor, FormFactor>();

        await builder.Build().RunAsync();
    }
}

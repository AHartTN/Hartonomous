using Hartonomous.Maui.Services;
using Hartonomous.Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        var apiBaseUrl = DeviceInfo.Platform == DevicePlatform.Android
            ? "http://10.0.2.2:5000"
            : "http://localhost:5000";

        builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
        builder.Services.AddScoped<IAtomService, ApiAtomService>();
        builder.Services.AddScoped<ITensorService, ApiTensorService>();
        builder.Services.AddScoped<IIngestionService, ApiIngestionService>();

        return builder.Build();
    }
}

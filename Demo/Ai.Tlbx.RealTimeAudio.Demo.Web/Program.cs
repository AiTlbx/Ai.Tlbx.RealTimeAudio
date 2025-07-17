using Ai.Tlbx.RealTimeAudio.Demo.Web.Components;
using Ai.Tlbx.RealTimeAudio.Hardware.Web;
using Ai.Tlbx.RealTimeAudio.OpenAi;
using Ai.Tlbx.RealTimeAudio.OpenAi.Models;
using Ai.Tlbx.RealTime.WebUi;

namespace Ai.Tlbx.RealTimeAudio.Demo.Web;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();


        builder.Services.AddScoped<IAudioHardwareAccess, WebAudioAccess>();

        // Register OpenAiRealTimeApiAccess with hardware access and direct console logging
        builder.Services.AddScoped(sp =>
        {
            var hardwareAccess = sp.GetRequiredService<IAudioHardwareAccess>();
            
            // Create log action that writes directly to console (bypassing ASP.NET Core logging)
            Action<Ai.Tlbx.RealTimeAudio.OpenAi.Models.LogLevel, string> logAction = (level, message) =>
            {
                var levelPrefix = level switch
                {
                    Ai.Tlbx.RealTimeAudio.OpenAi.Models.LogLevel.Error => "[Error]",
                    Ai.Tlbx.RealTimeAudio.OpenAi.Models.LogLevel.Warn => "[Warn]",
                    Ai.Tlbx.RealTimeAudio.OpenAi.Models.LogLevel.Info => "[Info]",
                    _ => "[Info]"
                };
                Console.WriteLine($"{levelPrefix} {message}");
            };
            
            return new OpenAiRealTimeApiAccess(hardwareAccess, logAction);
        });
        
        // Register IAudioInteropService
        builder.Services.AddScoped<IAudioInteropService, AudioInteropService>();

        builder.Services.AddSignalR(options =>
        {
            options.MaximumReceiveMessageSize = 1024 * 1024; // 1 MB, adjust as needed
            options.StreamBufferCapacity = 100; // Buffer for streaming
        });

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}
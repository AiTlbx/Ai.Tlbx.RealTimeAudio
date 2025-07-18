using Ai.Tlbx.RealTimeAudio.Demo.Web.Components;
using Ai.Tlbx.RealTimeAudio.Hardware.Web;
using Ai.Tlbx.RealTimeAudio.OpenAi;
using Ai.Tlbx.RealTime.WebUi;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

            return new OpenAiRealTimeApiAccess(hardwareAccess, (level, message) => Debug.WriteLine($"[{level}] {message}"));
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
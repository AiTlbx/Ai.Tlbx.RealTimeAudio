using Ai.Tlbx.RealTimeAudio.Hardware.Linux;
using Ai.Tlbx.RealTimeAudio.OpenAi;
using Ai.Tlbx.RealTimeAudio.OpenAi.Models;
using System.Diagnostics;
using System.Text.Json;

namespace Ai.Tlbx.RealTimeAudio.Demo.Linux
{
    class Program
    {
        private static LinuxAudioDevice? _audioDevice;
        private static OpenAiRealTimeApiAccess? _apiAccess;
        private static string _openAiApiKey = string.Empty;
        private static string _openAiModel = "gpt-4o";
        private static readonly ManualResetEvent _exitEvent = new ManualResetEvent(false);
        private static readonly CancellationTokenSource _cts = new CancellationTokenSource();

        static async Task Main(string[] args)
        {
            Console.WriteLine("Linux Real-Time Audio Demo");
            Console.WriteLine("==========================");

            // Load configuration
            LoadConfiguration();

            // Set up audio
            try
            {
                _audioDevice = new LinuxAudioDevice();
                await _audioDevice.InitAudio();
                Console.WriteLine("Audio device initialized successfully");

                // Set up logging - direct console output only
                Action<LogLevel, string> logAction = (level, message) => 
                {
                    var logPrefix = level switch
                    {
                        LogLevel.Error => "[Error]",
                        LogLevel.Warn => "[Warn]",
                        LogLevel.Info => "[Info]",
                        _ => "[Info]"
                    };
                    Console.WriteLine($"{logPrefix} {message}");
                };

                // Create API access
                if (string.IsNullOrEmpty(_openAiApiKey))
                {
                    Console.WriteLine("No OpenAI API key found. Please update the appsettings.json file.");
                    return;
                }

                // Set the API key as an environment variable for OpenAiRealTimeApiAccess
                Environment.SetEnvironmentVariable("OPENAI_API_KEY", _openAiApiKey);
                
                _apiAccess = new OpenAiRealTimeApiAccess(_audioDevice, logAction);
                
                // Apply settings for the model
                var settings = OpenAiRealTimeSettings.CreateDefault();
                
                // Override the model if specified
                if (!string.IsNullOrEmpty(_openAiModel))
                {
                    settings.Instructions = $"You are a helpful AI assistant using the {_openAiModel} model. Be friendly, conversational, helpful, and engaging. When the user speaks interrupt your answer and listen and then answer the new question.";
                }
                
                // Subscribe to status updates
                _apiAccess.StatusUpdated += (sender, e) =>
                {
                    Console.WriteLine($"Status: {e.Category} - {e.Code}: {e.Message}");
                };
                
                // UI and controls
                Console.WriteLine("\nCommands:");
                Console.WriteLine(" 's' - Start conversation");
                Console.WriteLine(" 'p' - Pause/Resume");
                Console.WriteLine(" 'q' - Quit");

                // Start input handling in a separate task
                _ = Task.Run(() => HandleUserInput());
                
                // Wait for exit signal
                _exitEvent.WaitOne();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Details: {ex}");
            }
            finally
            {
                // Clean up
                if (_apiAccess != null)
                {
                    await _apiAccess.DisposeAsync();
                }
                
                if (_audioDevice != null) 
                {
                    await _audioDevice.DisposeAsync();
                }
            }
        }

        private static void LoadConfiguration()
        {
            try
            {
                if (File.Exists("appsettings.json"))
                {
                    var config = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText("appsettings.json"));
                    
                    if (config.TryGetProperty("OpenAI", out var openAi))
                    {
                        if (openAi.TryGetProperty("ApiKey", out var apiKey))
                        {
                            _openAiApiKey = apiKey.GetString() ?? string.Empty;
                        }
                        
                        if (openAi.TryGetProperty("Model", out var model))
                        {
                            _openAiModel = model.GetString() ?? "gpt-4o";
                        }
                    }
                }
                else
                {
                    Console.WriteLine("Warning: appsettings.json not found.");
                }
                
                // Try to load from environment variables (useful for containers)
                var envApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                if (!string.IsNullOrEmpty(envApiKey))
                {
                    _openAiApiKey = envApiKey;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading configuration: {ex.Message}");
                Console.WriteLine($"Details: {ex}");
            }
        }

        private static async void HandleUserInput()
        {
            try
            {
                bool isRunning = false;
                
                while (!_cts.Token.IsCancellationRequested)
                {
                    var key = Console.ReadKey(true);
                    
                    switch (key.KeyChar)
                    {
                        case 's':
                            if (!isRunning)
                            {
                                isRunning = true;
                                Console.WriteLine("Starting conversation...");
                                var apiSettings = OpenAiRealTimeSettings.CreateDefault();
                                // Override the model if specified
                                if (!string.IsNullOrEmpty(_openAiModel))
                                {
                                    apiSettings.Instructions = $"You are a helpful AI assistant using the {_openAiModel} model. Be friendly, conversational, helpful, and engaging. When the user speaks interrupt your answer and listen and then answer the new question.";
                                }
                                await _apiAccess!.Start(apiSettings);
                            }
                            break;
                            
                        case 'p':
                            if (isRunning)
                            {
                                // Note: The current API doesn't seem to have direct pause/resume functionality
                                Console.WriteLine("Pause/Resume functionality would be here if available in the API");
                            }
                            break;
                            
                        case 'q':
                            Console.WriteLine("Exiting...");
                            _cts.Cancel();
                            _exitEvent.Set();
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Input handling error: {ex.Message}");
                Console.WriteLine($"Details: {ex}");
                _exitEvent.Set();
            }
        }
    }
}

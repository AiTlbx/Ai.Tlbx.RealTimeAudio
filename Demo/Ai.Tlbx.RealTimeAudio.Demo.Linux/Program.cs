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
                Debug.WriteLine($"{logPrefix} {message}");
            };

            logAction(LogLevel.Info, "Linux Real-Time Audio Demo");
            logAction(LogLevel.Info, "==========================");

            // Load configuration
            LoadConfiguration(logAction);

            // Set up audio
            try
            {
                _audioDevice = new LinuxAudioDevice();
                await _audioDevice.InitAudio();
                logAction(LogLevel.Info, "Audio device initialized successfully");


                // Create API access
                if (string.IsNullOrEmpty(_openAiApiKey))
                {
                    logAction(LogLevel.Error, "No OpenAI API key found. Please update the appsettings.json file.");
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
                    logAction(LogLevel.Info, $"Status: {e.Category} - {e.Code}: {e.Message}");
                };
                
                // UI and controls
                logAction(LogLevel.Info, "\nCommands:");
                logAction(LogLevel.Info, " 's' - Start conversation");
                logAction(LogLevel.Info, " 'p' - Pause/Resume");
                logAction(LogLevel.Info, " 'q' - Quit");

                // Start input handling in a separate task
                _ = Task.Run(() => HandleUserInput(logAction));
                
                // Wait for exit signal
                _exitEvent.WaitOne();
            }
            catch (Exception ex)
            {
                logAction(LogLevel.Error, $"Error: {ex.Message}");
                logAction(LogLevel.Error, $"Details: {ex}");
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

        private static void LoadConfiguration(Action<LogLevel, string> logAction)
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
                    logAction(LogLevel.Warn, "Warning: appsettings.json not found.");
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
                logAction(LogLevel.Error, $"Error loading configuration: {ex.Message}");
                logAction(LogLevel.Error, $"Details: {ex}");
            }
        }

        private static async void HandleUserInput(Action<LogLevel, string> logAction)
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
                                logAction(LogLevel.Info, "Starting conversation...");
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
                                logAction(LogLevel.Info, "Pause/Resume functionality would be here if available in the API");
                            }
                            break;
                            
                        case 'q':
                            logAction(LogLevel.Info, "Exiting...");
                            _cts.Cancel();
                            _exitEvent.Set();
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                logAction(LogLevel.Error, $"Input handling error: {ex.Message}");
                logAction(LogLevel.Error, $"Details: {ex}");
                _exitEvent.Set();
            }
        }
    }
}

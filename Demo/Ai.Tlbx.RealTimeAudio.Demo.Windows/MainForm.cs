using Ai.Tlbx.RealTimeAudio.Hardware.Windows;
using Ai.Tlbx.RealTimeAudio.OpenAi;
using Ai.Tlbx.RealTimeAudio.OpenAi.Models;
using System.Diagnostics;

namespace Ai.Tlbx.RealTimeAudio.Demo.Windows
{
    public partial class MainForm : Form
    {
        private readonly IAudioHardwareAccess _audioHardware;
        private readonly OpenAiRealTimeApiAccess _audioService;
        private bool _isRecording = false;
        
        public MainForm()
        {
            InitializeComponent();
            
            // Create the audio hardware instance for Windows
            _audioHardware = new WindowsAudioHardware();
            
            // Hook up audio error events directly
            _audioHardware.AudioError += OnAudioError;
            
            // Create the OpenAI service with logging
            _audioService = new OpenAiRealTimeApiAccess(_audioHardware, LogMessage);
            
            // Hook up events
            _audioService.MessageAdded += OnMessageAdded;
            _audioService.ConnectionStatusChanged += OnConnectionStatusChanged;
            
            // Set default voice
            _audioService.CurrentVoice = AssistantVoice.Alloy;
            
            // Initial UI state
            UpdateUIState();
            
            LogMessage(LogLevel.Info, "MainForm initialized");
        }
        
        private void OnAudioError(object? sender, string errorMessage)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => OnAudioError(sender, errorMessage)));
                return;
            }
            
            // Display the error message in the UI
            lblStatus.Text = $"Audio Error: {errorMessage}";
            LogMessage(LogLevel.Error, $"Audio Error: {errorMessage}");
            MessageBox.Show(errorMessage, "Audio Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        
        private void OnMessageAdded(object? sender, OpenAiChatMessage message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => OnMessageAdded(sender, message)));
                return;
            }
            
            // Add the message to the transcript
            string rolePrefix = message.Role == "user" ? "You: " : "AI: ";
            txtTranscription.AppendText($"{rolePrefix}{message.Content}\r\n\r\n");
        }
        
        private void OnConnectionStatusChanged(object? sender, string status)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => OnConnectionStatusChanged(sender, status)));
                return;
            }
            
            lblStatus.Text = status;
            LogMessage(LogLevel.Info, $"Connection status: {status}");
            UpdateUIState();
        }
        
        
        private async void btnTestMic_Click(object sender, EventArgs e)
        {
            try
            {
                btnTestMic.Enabled = false;
                lblStatus.Text = "Testing microphone...";
                LogMessage(LogLevel.Info, "Starting microphone test");
                
                // Use only the OpenAiRealTimeApiAccess for microphone testing
                bool success = await _audioService.TestMicrophone();
                
                if (!success)
                {
                    lblStatus.Text = "Microphone test failed";
                    LogMessage(LogLevel.Error, "Microphone test failed");
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Error testing microphone: {ex.Message}";
                LogMessage(LogLevel.Error, $"Microphone test error: {ex.Message}\nStackTrace: {ex.StackTrace}");
                MessageBox.Show($"Error testing microphone: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnTestMic.Enabled = true;
                UpdateUIState();
            }
        }
        
        private async void btnStart_Click(object sender, EventArgs e)
        {
            if (_isRecording)
            {
                return;
            }
            
            try
            {
                btnStart.Enabled = false;
                lblStatus.Text = "Starting...";
                LogMessage(LogLevel.Info, "Starting recording session");
                
                await _audioService.Start();
                _isRecording = true;
                lblStatus.Text = "Recording in progress...";
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Error starting: {ex.Message}";
                LogMessage(LogLevel.Error, $"Start recording error: {ex.Message}\nStackTrace: {ex.StackTrace}");
                MessageBox.Show($"Error starting recording: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UpdateUIState();
            }
        }
        
        private async void btnEnd_Click(object sender, EventArgs e)
        {
            if (!_isRecording)
            {
                return;
            }
            
            try
            {
                btnEnd.Enabled = false;
                lblStatus.Text = "Ending recording...";
                LogMessage(LogLevel.Info, "Ending recording session");
                
                await _audioService.Stop();
                _isRecording = false;
                lblStatus.Text = "Recording ended";
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Error ending recording: {ex.Message}";
                LogMessage(LogLevel.Error, $"End recording error: {ex.Message}\nStackTrace: {ex.StackTrace}");
                MessageBox.Show($"Error ending recording: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UpdateUIState();
            }
        }
        
        private async void btnInterrupt_Click(object sender, EventArgs e)
        {
            if (!_isRecording)
            {
                return;
            }
            
            try
            {
                btnInterrupt.Enabled = false;
                lblStatus.Text = "Interrupting...";
                LogMessage(LogLevel.Info, "Interrupting audio session");
                
                await _audioService.Interrupt();
                lblStatus.Text = "Interrupted";
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Error interrupting: {ex.Message}";
                LogMessage(LogLevel.Error, $"Interrupt error: {ex.Message}\nStackTrace: {ex.StackTrace}");
                MessageBox.Show($"Error interrupting: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UpdateUIState();
            }
        }
        
        private void LogMessage(LogLevel level, string message)
        {
            var logPrefix = level switch
            {
                LogLevel.Error => "[Error]",
                LogLevel.Warn => "[Warn]",
                LogLevel.Info => "[Info]",
                _ => "[Info]"
            };
            Console.WriteLine($"{logPrefix} {message}");
        }

        private void UpdateUIState()
        {
            bool isConnecting = _audioService?.IsConnecting ?? false;
            bool isInitialized = _audioService?.IsInitialized ?? false;
            bool isMicTesting = _audioService?.IsMicrophoneTesting ?? false;
            
            btnTestMic.Enabled = !_isRecording && !isMicTesting && !isConnecting;
            btnStart.Enabled = !_isRecording && !isMicTesting && !isConnecting;
            btnInterrupt.Enabled = isInitialized && !isConnecting;
            btnEnd.Enabled = _isRecording && !isConnecting;
        }
        
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Cleanup
            if (_audioService != null)
            {
                _audioService.MessageAdded -= OnMessageAdded;
                _audioService.ConnectionStatusChanged -= OnConnectionStatusChanged;
            }
            
            if (_audioHardware != null)
            {
                _audioHardware.AudioError -= OnAudioError;
            }
            
            base.OnFormClosing(e);
        }
    }
}

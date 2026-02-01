using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AETHRA
{
    public partial class MainWindow : Window
    {
        private TextBox _editor = null!;
        private TextBlock _statusText = null!;

        public MainWindow()
        {
            InitializeComponent();
            _editor = this.FindControl<TextBox>("Editor")!;
            _statusText = this.FindControl<TextBlock>("StatusText")!;
        }

        private async void RunButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _statusText.Text = "Generating audio...";
                string script = _editor.Text ?? "";
                string tempPath = Path.Combine(Path.GetTempPath(), "aethra_preview.wav");
                
                await Task.Run(() => Interpreter.Run(script, tempPath));
                
                _statusText.Text = "Playing...";
                PlayWavFile(tempPath);
                _statusText.Text = "Done.";
            }
            catch (Exception ex)
            {
                _statusText.Text = $"Error: {ex.Message}";
            }
        }

        private async void ExportButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var topLevel = GetTopLevel(this);
                if (topLevel == null)
                {
                    return;
                }

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export WAV",
                    DefaultExtension = "wav",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("WAV Audio") { Patterns = new[] { "*.wav" } }
                    },
                    SuggestedFileName = "aethra_output.wav"
                });

                if (file != null)
                {
                    _statusText.Text = "Exporting...";
                    string script = _editor.Text ?? "";
                    string path = file.Path.LocalPath;
                    
                    await Task.Run(() => Interpreter.Run(script, path));
                    
                    _statusText.Text = $"Exported to {Path.GetFileName(path)}";
                }
            }
            catch (Exception ex)
            {
                _statusText.Text = $"Error: {ex.Message}";
            }
        }

        private void PlayWavFile(string filePath)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // Try various Linux audio players in order of preference
                    string[] players = { "paplay", "aplay", "ffplay", "mpv", "vlc" };
                    foreach (var player in players)
                    {
                        try
                        {
                            var startInfo = new ProcessStartInfo
                            {
                                FileName = player,
                                Arguments = player == "ffplay" ? $"-nodisp -autoexit \"{filePath}\"" : $"\"{filePath}\"",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardError = true
                            };
                            Process.Start(startInfo);
                            return;
                        }
                        catch
                        {
                            // Player not found, try next
                        }
                    }
                    _statusText.Text = "No audio player found. Install pulseaudio-utils or alsa-utils.";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("afplay", $"\"{filePath}\"");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                }
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _statusText.Text = $"Playback error: {ex.Message}";
                });
            }
        }
    }
}

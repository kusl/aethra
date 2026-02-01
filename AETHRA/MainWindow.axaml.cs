using System.Diagnostics;
using System.Runtime.InteropServices;
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
                
                // Verify the file was created and has content
                if (!File.Exists(tempPath))
                {
                    _statusText.Text = "Error: WAV file was not created.";
                    return;
                }
                
                var fileInfo = new FileInfo(tempPath);
                if (fileInfo.Length < 100) // A valid WAV with any audio should be larger than just the header
                {
                    _statusText.Text = "Error: Generated WAV file appears to be empty.";
                    return;
                }
                
                _statusText.Text = "Playing...";
                bool playbackStarted = await PlayWavFileAsync(tempPath);
                
                if (playbackStarted)
                {
                    _statusText.Text = "Done.";
                }
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

        private async Task<bool> PlayWavFileAsync(string filePath)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // Try various Linux audio players in order of preference
                    // paplay (PulseAudio), aplay (ALSA), pw-play (PipeWire), ffplay, mpv, vlc
                    var players = new (string name, string[] args)[]
                    {
                        ("paplay", new[] { filePath }),
                        ("pw-play", new[] { filePath }),
                        ("aplay", new[] { filePath }),
                        ("ffplay", new[] { "-nodisp", "-autoexit", filePath }),
                        ("mpv", new[] { "--no-video", filePath }),
                        ("vlc", new[] { "--intf", "dummy", "--play-and-exit", filePath })
                    };
                    
                    foreach (var (player, args) in players)
                    {
                        try
                        {
                            var startInfo = new ProcessStartInfo
                            {
                                FileName = player,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardError = true,
                                RedirectStandardOutput = true
                            };
                            
                            foreach (var arg in args)
                            {
                                startInfo.ArgumentList.Add(arg);
                            }
                            
                            var process = Process.Start(startInfo);
                            if (process != null)
                            {
                                // Give the process a moment to fail if the file can't be played
                                await Task.Delay(100);
                                
                                if (!process.HasExited)
                                {
                                    // Process is running, playback likely started
                                    return true;
                                }
                                
                                // Process exited quickly - check if it was successful
                                // Exit code 0 typically means success (file played completely if very short)
                                if (process.ExitCode == 0)
                                {
                                    return true;
                                }
                                
                                // Non-zero exit, try next player
                            }
                        }
                        catch
                        {
                            // Player not found or failed to start, try next
                        }
                    }
                    
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _statusText.Text = "No audio player found. Install pulseaudio-utils, pipewire, or alsa-utils.";
                    });
                    return false;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "afplay",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    startInfo.ArgumentList.Add(filePath);
                    
                    var process = Process.Start(startInfo);
                    return process != null;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    };
                    var process = Process.Start(startInfo);
                    return process != null;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _statusText.Text = $"Playback error: {ex.Message}";
                });
                return false;
            }
        }
    }
}

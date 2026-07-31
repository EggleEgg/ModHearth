using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ModHearth.Utilities.Steam;

namespace ModHearth.UI
{
    public partial class SteamCmdSetupDialog : Window
    {
        private CancellationTokenSource? _cts;
        private readonly ISteamCmdService _steamCmdService = new SteamCmdService();

        public SteamCmdSetupDialog()
        {
            InitializeComponent();
            WindowThemeManager.Register(this);

            BtnInstall.Click += async (_, _) => await InstallSteamCmdAsync();
            BtnBrowse.Click += async (_, _) => await BrowseExistingAsync();
            BtnCancel.Click += (_, _) =>
            {
                _cts?.Cancel();
                Close(false);
            };
        }

        private async Task InstallSteamCmdAsync()
        {
            BtnInstall.IsEnabled = false;
            BtnBrowse.IsEnabled = false;
            ProgressPanel.IsVisible = true;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            string installDir = Path.Combine(AppContext.BaseDirectory, "steamcmd");
            var progress = new Progress<string>(msg =>
            {
                Dispatcher.UIThread.Post(() => StatusTextBlock.Text = msg);
            });

            try
            {
                bool success = await _steamCmdService.InstallAsync(installDir, progress, _cts.Token);
                if (success)
                {
                    if (IsLoaded)
                    {
                        await DialogService.ShowMessageAsync(this, "SteamCMD was successfully installed and verified.", "Success");
                    }
                    Close(true);
                }
                else
                {
                    if (IsLoaded)
                    {
                        await DialogService.ShowMessageAsync(this, "SteamCMD installation could not be verified.", "Error");
                    }
                    ResetUI();
                }
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Installation cancelled.";
                ResetUI();
            }
            catch (Exception ex)
            {
                if (IsLoaded)
                {
                    await DialogService.ShowMessageAsync(this, $"Installation failed: {ex.Message}", "Error");
                }
                ResetUI();
            }
        }

        private async Task BrowseExistingAsync()
        {
            var fileTypes = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("SteamCMD Executable")
                {
                    Patterns = OperatingSystem.IsWindows() ? new[] { "steamcmd.exe" } : new[] { "steamcmd.sh", "steamcmd" }
                }
            };

            string? path = await DialogService.PickFileAsync(this, "Select SteamCMD Executable", fileTypes);
            if (!string.IsNullOrEmpty(path))
            {
                if (await _steamCmdService.ValidateAsync(path))
                {
                    ConfigManager.Config.SteamCmdPath = path;
                    ConfigManager.SaveConfigFile("SteamCmdPath selected");
                    await DialogService.ShowMessageAsync(this, "SteamCMD location validated and saved.", "Success");
                    Close(true);
                }
                else
                {
                    await DialogService.ShowMessageAsync(this, "Selected file is not a valid SteamCMD installation.", "Validation Failed");
                }
            }
        }

        private void ResetUI()
        {
            BtnInstall.IsEnabled = true;
            BtnBrowse.IsEnabled = true;
            ProgressPanel.IsVisible = false;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _cts?.Dispose();
            _cts = null;
        }

        public static async Task<bool> ShowAsync(Window owner)
        {
            var dialog = new SteamCmdSetupDialog();
            var result = await dialog.ShowDialog<bool?>(owner);
            return result == true;
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace GamePivot;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var diagnoseIndex = Array.IndexOf(e.Args, "--diagnose");
        if (diagnoseIndex >= 0)
        {
            await RunHeadlessDiagnosisAsync(e.Args, diagnoseIndex);
            return;
        }

        new MainWindow().Show();
    }

    private async Task RunHeadlessDiagnosisAsync(string[] args, int diagnoseIndex)
    {
        var appId = diagnoseIndex + 1 < args.Length ? args[diagnoseIndex + 1] : string.Empty;
        var outputIndex = Array.IndexOf(args, "--output");
        var outputPath = outputIndex >= 0 && outputIndex + 1 < args.Length
            ? args[outputIndex + 1]
            : Path.Combine(Environment.CurrentDirectory, "gamepivot-diagnosis.json");

        try
        {
            var steam = new SteamService();
            var game = steam.DiscoverGames().FirstOrDefault(item => item.AppId == appId)
                ?? throw new InvalidOperationException($"Steam app {appId} is not installed.");
            var diagnosis = await new DiagnosticEngine(steam).DiagnoseAsync(game);
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            };
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(diagnosis, options));
            Shutdown(diagnosis.Status == DiagnosticStatus.Healthy ? 0 : 2);
        }
        catch (Exception ex)
        {
            var errorJson = JsonSerializer.Serialize(new
            {
                error = ex.Message,
                type = ex.GetType().Name
            }, new JsonSerializerOptions { WriteIndented = true });
            try
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(outputPath, errorJson);
            }
            catch
            {
                var fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "gamepivot-error.json");
                await File.WriteAllTextAsync(fallback, errorJson);
            }

            Shutdown(1);
        }
    }
}

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace GamePivot;

public partial class MainWindow : Window
{
    private readonly SteamService _steam = new();
    private readonly DiagnosticEngine _engine;
    private IReadOnlyList<SteamGame> _games = [];
    private GameDiagnosis? _diagnosis;
    private bool _loadingSelection;

    public MainWindow()
    {
        InitializeComponent();
        _engine = new DiagnosticEngine(_steam);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshLibrary();
        var preferred = _games.FirstOrDefault(game => game.AppId == "393080") ?? _games.FirstOrDefault();
        if (preferred is not null)
        {
            GameList.SelectedItem = preferred;
            await DiagnoseSelectedAsync();
        }
    }

    private void RefreshLibrary()
    {
        _games = _steam.DiscoverGames();
        ApplyFilter();
        FooterText.Text = _steam.SteamPath is null
            ? "Steam was not found."
            : $"{_games.Count} installed Steam games found.";
    }

    private void ApplyFilter()
    {
        var filter = SearchBox.Text.Trim();
        GameList.ItemsSource = string.IsNullOrWhiteSpace(filter)
            ? _games
            : _games.Where(game =>
                    game.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                    game.AppId.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToArray();
    }

    private SteamGame? SelectedGame => GameList.SelectedItem as SteamGame;

    private async Task DiagnoseSelectedAsync()
    {
        var game = SelectedGame;
        if (game is null || _loadingSelection)
        {
            return;
        }

        _loadingSelection = true;
        SetBusy(true, $"Scanning {game.Name}...");
        try
        {
            _diagnosis = await Task.Run(() => _engine.DiagnoseAsync(game));
            RenderDiagnosis(_diagnosis);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "GamePivot could not finish the scan",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            FooterText.Text = "Scan failed.";
        }
        finally
        {
            SetBusy(false, _diagnosis is null ? "Ready" : $"Scanned at {_diagnosis.ScannedAt:t}");
            _loadingSelection = false;
        }
    }

    private void RenderSelection(SteamGame? game)
    {
        _diagnosis = null;
        GameNameText.Text = game?.Name ?? "Select a Steam game";
        GamePathText.Text = game?.InstallDirectory ?? string.Empty;
        StatusText.Text = "NOT SCANNED";
        StatusBadge.Background = new SolidColorBrush(Color.FromRgb(37, 50, 77));
        SummaryText.Text = game is null
            ? "Choose a game and run a diagnosis."
            : "Ready to inspect executable dependencies, Steam manifests, logs, and Defender history.";
        RecommendationText.Text = string.Empty;
        FindingsText.Text = string.Empty;
        MissingDependenciesList.ItemsSource = null;
        NoMissingText.Visibility = Visibility.Collapsed;
        EvidenceText.Text = string.Empty;
        SetActionState(game, null);
    }

    private void RenderDiagnosis(GameDiagnosis diagnosis)
    {
        GameNameText.Text = diagnosis.Game.Name;
        GamePathText.Text = $"{diagnosis.Game.InstallDirectory}  |  App {diagnosis.Game.AppId}  |  Build {diagnosis.Game.BuildId}";
        SummaryText.Text = diagnosis.Summary;
        RecommendationText.Text = diagnosis.RecommendedAction;
        StatusText.Text = diagnosis.Status switch
        {
            DiagnosticStatus.Healthy => "HEALTHY",
            DiagnosticStatus.NeedsAttention => "REPAIR NEEDED",
            _ => "STEAM BLOCKED"
        };
        StatusBadge.Background = new SolidColorBrush(diagnosis.Status switch
        {
            DiagnosticStatus.Healthy => Color.FromRgb(28, 83, 69),
            DiagnosticStatus.NeedsAttention => Color.FromRgb(111, 77, 22),
            _ => Color.FromRgb(110, 43, 52)
        });

        var missing = diagnosis.Dependencies
            .Where(item => !item.IsPresent)
            .DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => $"{item.Name}  [{FormatKind(item.Kind)}]")
            .ToArray();
        MissingDependenciesList.ItemsSource = missing;
        NoMissingText.Visibility = missing.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        FindingsText.Text = diagnosis.Findings.Count == 0
            ? "No actionable problems found."
            : string.Join(
                "\n\n",
                diagnosis.Findings.Select(item =>
                    $"{item.Severity.ToString().ToUpperInvariant()}  {item.Title}\n{item.Detail}"));

        var evidence = new StringBuilder();
        evidence.AppendLine($"Manifest: {diagnosis.Game.ManifestPath}");
        evidence.AppendLine($"Installed depots: {string.Join(", ", diagnosis.Game.InstalledDepots.Keys)}");
        evidence.AppendLine($"Shared depots: {string.Join(", ", diagnosis.Game.SharedDepots.Keys)}");
        evidence.AppendLine();
        evidence.AppendLine("Steam content log:");
        if (diagnosis.SteamLogEvidence.Count == 0)
        {
            evidence.AppendLine("No matching errors found.");
        }
        else
        {
            foreach (var line in diagnosis.SteamLogEvidence)
            {
                evidence.AppendLine(line);
            }
        }

        evidence.AppendLine();
        evidence.AppendLine("Windows Security:");
        if (diagnosis.DefenderEvidence.Count == 0)
        {
            evidence.AppendLine("No detections matched this game folder.");
        }
        else
        {
            foreach (var line in diagnosis.DefenderEvidence)
            {
                evidence.AppendLine(line);
            }
        }

        EvidenceText.Text = evidence.ToString();
        SetActionState(diagnosis.Game, diagnosis);
    }

    private static string FormatKind(DependencyKind kind) => kind switch
    {
        DependencyKind.GameFile => "game file",
        DependencyKind.VisualCppRuntime => "Visual C++",
        DependencyKind.LegacyDirectX => "DirectX",
        _ => "Windows"
    };

    private void SetActionState(SteamGame? game, GameDiagnosis? diagnosis)
    {
        var hasGame = game is not null;
        VerifyButton.IsEnabled = hasGame;
        SteamHelpButton.IsEnabled = hasGame;
        LaunchButton.IsEnabled = hasGame;
        OpenFolderButton.IsEnabled = hasGame;
        ExportButton.IsEnabled = diagnosis is not null;
        InstallCompanionButton.IsEnabled =
            diagnosis?.Rule?.CompanionAppId is not null;
        InstallCompanionButton.Content = diagnosis?.Rule?.CompanionName is null
            ? "Install required Steam component"
            : $"Install {diagnosis.Rule.CompanionName}";
    }

    private void SetBusy(bool busy, string footer)
    {
        ScanProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ScanButton.IsEnabled = !busy && SelectedGame is not null;
        GameList.IsEnabled = !busy;
        FooterText.Text = footer;
    }

    private async void GameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSelection)
        {
            return;
        }

        RenderSelection(SelectedGame);
        if (SelectedGame is not null && IsLoaded)
        {
            await DiagnoseSelectedAsync();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
        {
            ApplyFilter();
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
        => await DiagnoseSelectedAsync();

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedAppId = SelectedGame?.AppId;
        RefreshLibrary();
        GameList.SelectedItem = _games.FirstOrDefault(game => game.AppId == selectedAppId)
            ?? _games.FirstOrDefault();
    }

    private void InstallCompanionButton_Click(object sender, RoutedEventArgs e)
    {
        var rule = _diagnosis?.Rule;
        if (rule?.CompanionAppId is null)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Steam will install {rule.CompanionName}. This title can require tens of gigabytes because its shared files live under the companion app.\n\nContinue in Steam?",
            "Install required component",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (result == MessageBoxResult.Yes)
        {
            _steam.InstallGame(rule.CompanionAppId);
        }
    }

    private void VerifyButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGame is { } game)
        {
            _steam.VerifyGame(game.AppId);
        }
    }

    private void SteamHelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGame is { } game)
        {
            _steam.OpenGameSupport(game.AppId);
        }
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGame is { } game)
        {
            _steam.LaunchGame(game.AppId);
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGame is not { } game)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{game.InstallDirectory}\"",
            UseShellExecute = true
        });
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_diagnosis is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export GamePivot report",
            FileName = $"gamepivot-{_diagnosis.Game.AppId}-{DateTime.Now:yyyyMMdd-HHmm}.json",
            Filter = "JSON report (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(_diagnosis, options));
        FooterText.Text = $"Report saved to {dialog.FileName}";
    }

    private void VcRuntimeButton_Click(object sender, RoutedEventArgs e)
        => SteamService.OpenUri("https://aka.ms/vs/17/release/vc_redist.x64.exe");

    private void DirectXButton_Click(object sender, RoutedEventArgs e)
        => SteamService.OpenUri("https://www.microsoft.com/en-us/download/details.aspx?id=8109");
}

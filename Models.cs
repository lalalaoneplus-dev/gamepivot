namespace GamePivot;

public enum DiagnosticStatus
{
    Healthy,
    NeedsAttention,
    Blocked
}

public enum FindingSeverity
{
    Info,
    Warning,
    Critical
}

public enum DependencyKind
{
    GameFile,
    VisualCppRuntime,
    LegacyDirectX,
    WindowsComponent
}

public sealed class SteamGame
{
    public required string AppId { get; init; }
    public required string Name { get; init; }
    public required string InstallDirectory { get; init; }
    public required string ManifestPath { get; init; }
    public required string LibraryPath { get; init; }
    public string BuildId { get; init; } = string.Empty;
    public string Language { get; init; } = "english";
    public IReadOnlyDictionary<string, string> InstalledDepots { get; init; }
        = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> SharedDepots { get; init; }
        = new Dictionary<string, string>();

    public override string ToString() => Name;
}

public sealed record DependencyCheck(
    string Name,
    string ImportedBy,
    DependencyKind Kind,
    bool IsPresent);

public sealed record DiagnosticFinding(
    FindingSeverity Severity,
    string Title,
    string Detail);

public sealed class GameRule
{
    public required string AppId { get; init; }
    public string? CompanionAppId { get; init; }
    public string? CompanionName { get; init; }
    public string[] RequiredSharedDepots { get; init; } = [];
    public IReadOnlyDictionary<string, string> LanguageSharedDepots { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public string Explanation { get; init; } = string.Empty;
}

public sealed class GameDiagnosis
{
    public required SteamGame Game { get; init; }
    public required DiagnosticStatus Status { get; init; }
    public required string Summary { get; init; }
    public required string RecommendedAction { get; init; }
    public required IReadOnlyList<DependencyCheck> Dependencies { get; init; }
    public required IReadOnlyList<DiagnosticFinding> Findings { get; init; }
    public required IReadOnlyList<string> SteamLogEvidence { get; init; }
    public required IReadOnlyList<string> DefenderEvidence { get; init; }
    public GameRule? Rule { get; init; }
    public string? PrimaryExecutable { get; init; }
    public DateTimeOffset ScannedAt { get; init; } = DateTimeOffset.Now;
}

namespace GamePivot;

public sealed class DiagnosticEngine(SteamService steam)
{
    private readonly IReadOnlyList<GameRule> _rules = KnowledgeBase.Load();

    public async Task<GameDiagnosis> DiagnoseAsync(SteamGame game)
    {
        var findings = new List<DiagnosticFinding>();
        var dependencies = new List<DependencyCheck>();
        var executables = Directory
            .EnumerateFiles(game.InstallDirectory, "*.exe", SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => new FileInfo(path).Length)
            .Take(8)
            .ToArray();
        var primaryExecutable = executables.FirstOrDefault();

        foreach (var executable in executables)
        {
            try
            {
                dependencies.AddRange(DependencyResolver.Inspect(executable));
            }
            catch (Exception ex)
            {
                findings.Add(new DiagnosticFinding(
                    FindingSeverity.Warning,
                    $"Could not inspect {Path.GetFileName(executable)}",
                    ex.Message));
            }
        }

        var missing = dependencies
            .Where(item => !item.IsPresent)
            .DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missing.Length > 0)
        {
            findings.Add(new DiagnosticFinding(
                FindingSeverity.Critical,
                $"{missing.Length} imported DLL{(missing.Length == 1 ? "" : "s")} missing",
                string.Join(", ", missing.Select(item => item.Name))));
        }

        var rule = _rules.FirstOrDefault(item => item.AppId == game.AppId);
        if (rule is not null)
        {
            var requiredDepots = rule.RequiredSharedDepots.AsEnumerable();
            if (rule.LanguageSharedDepots.TryGetValue(game.Language, out var languageDepot))
            {
                requiredDepots = requiredDepots.Append(languageDepot);
            }

            var absentDepots = requiredDepots
                .Where(depot => !game.SharedDepots.ContainsKey(depot))
                .ToArray();
            if (absentDepots.Length > 0)
            {
                findings.Add(new DiagnosticFinding(
                    FindingSeverity.Critical,
                    "Required shared Steam content is not mounted",
                    rule.Explanation));
            }

            if (!string.IsNullOrWhiteSpace(rule.CompanionAppId))
            {
                var companionInstalled = steam.DiscoverGames()
                    .Any(item => item.AppId == rule.CompanionAppId);
                if (!companionInstalled)
                {
                    findings.Add(new DiagnosticFinding(
                        FindingSeverity.Warning,
                        "Companion Steam component is not installed",
                        $"{rule.CompanionName} ({rule.CompanionAppId}) owns shared files used by this game."));
                }
            }
        }

        var logEvidence = steam.ReadContentLogEvidence(game.AppId);
        var ownershipBlocked = logEvidence.Any(line =>
            line.Contains("ownership ticket", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("access denied", StringComparison.OrdinalIgnoreCase));
        if (ownershipBlocked)
        {
            findings.Add(new DiagnosticFinding(
                FindingSeverity.Critical,
                "Steam is rejecting the ownership or depot request",
                "Steam's content log contains Access Denied entries. Validation cannot restore files until Steam refreshes the license for the purchasing account."));
        }

        var defenderEvidence = await DefenderService.FindEvidenceAsync(game.InstallDirectory);
        if (defenderEvidence.Count > 0)
        {
            findings.Add(new DiagnosticFinding(
                FindingSeverity.Critical,
                "Windows Security quarantined game content",
                "Restore the detected item only if Steam verifies it as an official game file, then verify the game again."));
        }

        var status = ownershipBlocked
            ? DiagnosticStatus.Blocked
            : findings.Any(item => item.Severity == FindingSeverity.Critical)
                ? DiagnosticStatus.NeedsAttention
                : DiagnosticStatus.Healthy;

        return new GameDiagnosis
        {
            Game = game,
            Status = status,
            Summary = BuildSummary(status, missing.Length, rule),
            RecommendedAction = BuildRecommendation(ownershipBlocked, missing, rule),
            Dependencies = dependencies,
            Findings = findings,
            SteamLogEvidence = logEvidence,
            DefenderEvidence = defenderEvidence,
            Rule = rule,
            PrimaryExecutable = primaryExecutable
        };
    }

    private static string BuildSummary(DiagnosticStatus status, int missingCount, GameRule? rule)
    {
        return status switch
        {
            DiagnosticStatus.Healthy => "No missing imported dependencies or Steam repair blockers were found.",
            DiagnosticStatus.Blocked when rule?.CompanionAppId is not null =>
                $"The install is incomplete and Steam is currently blocking depot access. {missingCount} required DLLs are missing.",
            _ => $"The game needs repair. {missingCount} imported DLLs are missing."
        };
    }

    private static string BuildRecommendation(
        bool ownershipBlocked,
        IReadOnlyList<DependencyCheck> missing,
        GameRule? rule)
    {
        if (ownershipBlocked)
        {
            return rule?.CompanionAppId is not null
                ? $"Sign out of Steam and sign back into the purchasing account. Install {rule.CompanionName}, then verify the original game. If Steam still denies it, open Steam Support because the account entitlement must be corrected."
                : "Sign out of Steam, sign back into the purchasing account, then run Steam verification again.";
        }

        if (rule?.CompanionAppId is not null)
        {
            return $"Install {rule.CompanionName} through Steam, then verify this game.";
        }

        if (missing.Any(item => item.Kind == DependencyKind.VisualCppRuntime))
        {
            return "Install Microsoft's current Visual C++ 2015-2022 redistributables, then verify the game.";
        }

        if (missing.Any(item => item.Kind == DependencyKind.LegacyDirectX))
        {
            return "Install Microsoft's DirectX End-User Runtimes (June 2010), then verify the game.";
        }

        return missing.Count > 0
            ? "Use the official launcher to verify or repair the game files."
            : "No repair is currently recommended.";
    }
}

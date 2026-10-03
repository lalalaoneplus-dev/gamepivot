using System.Text.RegularExpressions;

namespace GamePivot;

internal static partial class DependencyResolver
{
    public static IReadOnlyList<DependencyCheck> Inspect(string executablePath)
    {
        var pe = PeImportReader.Read(executablePath);
        var executableDirectory = Path.GetDirectoryName(executablePath)
            ?? throw new InvalidOperationException("Executable directory is unavailable.");

        return pe.Imports
            .Select(name => new DependencyCheck(
                name,
                Path.GetFileName(executablePath),
                Classify(name),
                IsAvailable(name, executableDirectory, pe.Is64Bit)))
            .ToArray();
    }

    private static bool IsAvailable(string name, string executableDirectory, bool is64Bit)
    {
        if (name.StartsWith("api-ms-win-", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("ext-ms-win-", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (File.Exists(Path.Combine(executableDirectory, name)))
        {
            return true;
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var systemDirectory = is64Bit
            ? Path.Combine(windows, "System32")
            : Path.Combine(windows, "SysWOW64");
        if (File.Exists(Path.Combine(systemDirectory, name)))
        {
            return true;
        }

        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return pathDirectories.Any(directory =>
        {
            try
            {
                return File.Exists(Path.Combine(directory, name));
            }
            catch
            {
                return false;
            }
        });
    }

    private static DependencyKind Classify(string name)
    {
        if (VisualCppPattern().IsMatch(name))
        {
            return DependencyKind.VisualCppRuntime;
        }

        if (DirectXPattern().IsMatch(name))
        {
            return DependencyKind.LegacyDirectX;
        }

        if (GameFilePattern().IsMatch(name))
        {
            return DependencyKind.GameFile;
        }

        return DependencyKind.WindowsComponent;
    }

    [GeneratedRegex(@"^(msvcp|msvcr|vcruntime|concrt)\d+(_\d+)?\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex VisualCppPattern();

    [GeneratedRegex(@"^(d3dx(9|10|11)_\d+|xinput1_3|xaudio2_[0-9]+|xactengine[0-9_]+)\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex DirectXPattern();

    [GeneratedRegex(@"^(steam_api(64)?|bink\w*|amd_ags\w*|discord_game_sdk|eossdk\w*|physx\w*|openal32)\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex GameFilePattern();
}

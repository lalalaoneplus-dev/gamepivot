using System.Diagnostics;
using Microsoft.Win32;

namespace GamePivot;

public sealed class SteamService
{
    public string? SteamPath { get; } = LocateSteamPath();

    public IReadOnlyList<SteamGame> DiscoverGames()
    {
        if (SteamPath is null)
        {
            return [];
        }

        var libraries = DiscoverLibraries(SteamPath);
        var games = new List<SteamGame>();

        foreach (var library in libraries)
        {
            var steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps))
            {
                continue;
            }

            foreach (var manifestPath in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
            {
                try
                {
                    var root = VdfParser.Parse(File.ReadAllText(manifestPath));
                    var appState = root.GetObject("AppState");
                    if (appState is null)
                    {
                        continue;
                    }

                    var appId = appState.GetString("appid");
                    if (appId == "228980")
                    {
                        continue;
                    }

                    var installDirectory = Path.Combine(
                        steamApps,
                        "common",
                        appState.GetString("installdir"));
                    if (!Directory.Exists(installDirectory))
                    {
                        continue;
                    }

                    games.Add(new SteamGame
                    {
                        AppId = appId,
                        Name = appState.GetString("name", Path.GetFileName(installDirectory)),
                        InstallDirectory = installDirectory,
                        ManifestPath = manifestPath,
                        LibraryPath = library,
                        BuildId = appState.GetString("buildid"),
                        Language = appState.GetObject("UserConfig")?.GetString("language", "english")
                            ?? "english",
                        InstalledDepots = ReadStringMap(appState.GetObject("InstalledDepots"), "manifest"),
                        SharedDepots = ReadFlatStringMap(appState.GetObject("SharedDepots"))
                    });
                }
                catch
                {
                    // A malformed manifest should not hide every other installed game.
                }
            }
        }

        return games
            .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> ReadContentLogEvidence(string appId)
    {
        if (SteamPath is null)
        {
            return [];
        }

        var path = Path.Combine(SteamPath, "logs", "content_log.txt");
        if (!File.Exists(path))
        {
            return [];
        }

        string tail;
        using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var length = (int)Math.Min(stream.Length, 2 * 1024 * 1024);
            stream.Seek(-length, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            tail = reader.ReadToEnd();
        }

        var markers = new[]
        {
            "access denied",
            "failed downloading",
            "failed to get manifest",
            "ownership ticket",
            "no connection",
            "corrupt",
            "missing"
        };

        return tail
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Where(line =>
                (line.Contains($"AppID {appId}", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains($"App: {appId}", StringComparison.OrdinalIgnoreCase)) &&
                markers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .TakeLast(30)
            .ToArray();
    }

    public static void OpenUri(string uri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true
        });
    }

    public void VerifyGame(string appId) => OpenUri($"steam://validate/{appId}");

    public void InstallGame(string appId) => OpenUri($"steam://install/{appId}");

    public void LaunchGame(string appId) => OpenUri($"steam://run/{appId}");

    public void OpenGameSupport(string appId)
        => OpenUri($"https://help.steampowered.com/en/wizard/HelpWithGame/?appid={appId}");

    private static string? LocateSteamPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var path = key?.GetValue("SteamPath") as string;
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        var commonPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam");
        return Directory.Exists(commonPath) ? commonPath : null;
    }

    private static IReadOnlyList<string> DiscoverLibraries(string steamPath)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            steamPath
        };
        var file = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(file))
        {
            return libraries.ToArray();
        }

        try
        {
            var root = VdfParser.Parse(File.ReadAllText(file));
            var folders = root.GetObject("libraryfolders");
            if (folders is null)
            {
                return libraries.ToArray();
            }

            foreach (var value in folders.Values.OfType<VdfObject>())
            {
                var path = value.GetString("path");
                if (Directory.Exists(path))
                {
                    libraries.Add(Path.GetFullPath(path));
                }
            }
        }
        catch
        {
            // The default Steam library remains usable.
        }

        return libraries.ToArray();
    }

    private static IReadOnlyDictionary<string, string> ReadStringMap(VdfObject? source, string nestedKey)
    {
        if (source is null)
        {
            return new Dictionary<string, string>();
        }

        return source.ToDictionary(
            pair => pair.Key,
            pair => pair.Value is VdfObject nested
                ? nested.GetString(nestedKey)
                : pair.Value.ToString() ?? "",
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> ReadFlatStringMap(VdfObject? source)
    {
        if (source is null)
        {
            return new Dictionary<string, string>();
        }

        return source.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToString() ?? "",
            StringComparer.OrdinalIgnoreCase);
    }
}

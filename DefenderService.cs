using System.Diagnostics;

namespace GamePivot;

internal static class DefenderService
{
    public static async Task<IReadOnlyList<string>> FindEvidenceAsync(string gameDirectory)
    {
        var escapedDirectory = gameDirectory.Replace("'", "''");
        var command =
            "$ErrorActionPreference='SilentlyContinue'; " +
            "Get-MpThreatDetection | ForEach-Object { $_.Resources } | " +
            $"Where-Object {{ $_ -like '*{escapedDirectory}*' }}";

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -Command \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return [];
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }
}

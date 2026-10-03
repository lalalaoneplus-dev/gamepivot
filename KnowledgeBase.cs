using System.Text.Json;

namespace GamePivot;

internal static class KnowledgeBase
{
    public static IReadOnlyList<GameRule> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rules.json");
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<GameRule[]>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch
        {
            return [];
        }
    }
}

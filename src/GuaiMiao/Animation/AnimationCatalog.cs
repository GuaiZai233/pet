using System.Reflection;
using System.Text.Json;

namespace GuaiMiao.Animation;

internal sealed class AnimationCatalog
{
    public int CellWidth { get; init; }
    public int CellHeight { get; init; }
    public Dictionary<string, AnimationDefinition> States { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static AnimationCatalog Load()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("GuaiMiao.Assets.animations.json")
            ?? throw new InvalidOperationException("Missing animation manifest.");
        return JsonSerializer.Deserialize<AnimationCatalog>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Invalid animation manifest.");
    }

    public AnimationDefinition Get(string state) =>
        States.TryGetValue(state, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown animation state: {state}");
}

internal sealed class AnimationDefinition
{
    public string Source { get; init; } = "main";
    public int Row { get; init; }
    public int Frames { get; init; }
    public int[] DurationsMs { get; init; } = [];

    public int DurationFor(int frame) => DurationsMs.Length == 0
        ? 160
        : DurationsMs[Math.Clamp(frame, 0, DurationsMs.Length - 1)];
}

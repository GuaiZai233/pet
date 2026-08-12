using System.Reflection;
using System.Text;
using System.Text.Json;
using GuaiMiao.Infrastructure;

namespace GuaiMiao.Services;

internal static class CodexPetSyncService
{
    private const string PetId = "emberwhisk";

    public static string PetDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "pets", PetId);

    public static void Sync()
    {
        Directory.CreateDirectory(PetDirectory);
        var atlasPath = Path.Combine(PetDirectory, "spritesheet.webp");
        var atlasTemp = atlasPath + ".new";
        using (var resource = Assembly.GetExecutingAssembly()
                   .GetManifestResourceStream("GuaiMiao.Assets.codex-spritesheet.webp")
               ?? throw new InvalidOperationException("缺少 Codex 宠物图集资源。"))
        using (var output = File.Create(atlasTemp))
            resource.CopyTo(output);
        File.Move(atlasTemp, atlasPath, true);

        var manifest = new
        {
            id = PetId,
            displayName = AppInfo.ProductName,
            description = "An updated fluffy brown-and-cream furry cat with orange goggles, a peach bandana, bold ear markings, and an adventurous friendly spirit.",
            spriteVersionNumber = 2,
            spritesheetPath = "spritesheet.webp"
        };
        var manifestPath = Path.Combine(PetDirectory, "pet.json");
        var manifestTemp = manifestPath + ".new";
        File.WriteAllText(manifestTemp, JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        }), new UTF8Encoding(false));
        File.Move(manifestTemp, manifestPath, true);
        LocalLog.Info("codex-pet-synchronized");
    }
}

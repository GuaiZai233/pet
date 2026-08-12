using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GuaiMiao.Animation;

internal sealed class SpriteLibrary
{
    private readonly AnimationCatalog _catalog;
    private readonly BitmapSource _main;
    private readonly BitmapSource _paw;
    private readonly Dictionary<string, BitmapSource[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    public SpriteLibrary(AnimationCatalog catalog)
    {
        _catalog = catalog;
        _main = LoadBitmap("GuaiMiao.Assets.spritesheet.png");
        _paw = LoadBitmap("GuaiMiao.Assets.paw-glass.png");
    }

    public IReadOnlyList<BitmapSource> GetFrames(string state)
    {
        if (_cache.TryGetValue(state, out var cached))
            return cached;

        var definition = _catalog.Get(state);
        var source = definition.Source.Equals("paw", StringComparison.OrdinalIgnoreCase) ? _paw : _main;
        var frames = new BitmapSource[definition.Frames];
        for (var frame = 0; frame < frames.Length; frame++)
        {
            var rect = new Int32Rect(frame * _catalog.CellWidth, definition.Row * _catalog.CellHeight,
                _catalog.CellWidth, _catalog.CellHeight);
            if (rect.X + rect.Width > source.PixelWidth || rect.Y + rect.Height > source.PixelHeight)
                throw new InvalidDataException($"Animation '{state}' exceeds its sprite source.");
            var crop = new CroppedBitmap(source, rect);
            crop.Freeze();
            frames[frame] = crop;
        }
        _cache[state] = frames;
        return frames;
    }

    private static BitmapSource LoadBitmap(string resourceName)
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing sprite resource: {resourceName}");
        using var memory = new MemoryStream();
        resource.CopyTo(memory);
        memory.Position = 0;
        var decoder = new PngBitmapDecoder(memory, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        BitmapSource converted = frame.Format == PixelFormats.Bgra32
            ? frame
            : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }
}

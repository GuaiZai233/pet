using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GuaiMiao.Animation;

internal sealed class PetAnimator : IDisposable
{
    private readonly AnimationCatalog _catalog;
    private readonly SpriteLibrary _sprites;
    private readonly Action<BitmapSource> _showFrame;
    private readonly DispatcherTimer _timer;
    private IReadOnlyList<BitmapSource> _frames = [];
    private AnimationDefinition? _definition;
    private Action? _completed;
    private int _frameIndex;
    private int _loopsCompleted;
    private int? _loopLimit;

    public PetAnimator(AnimationCatalog catalog, SpriteLibrary sprites, Action<BitmapSource> showFrame)
    {
        _catalog = catalog;
        _sprites = sprites;
        _showFrame = showFrame;
        _timer = new DispatcherTimer(DispatcherPriority.Background);
        _timer.Tick += OnTick;
    }

    public string CurrentState { get; private set; } = string.Empty;

    public void Play(string state, int? loops = null, Action? completed = null)
    {
        _timer.Stop();
        CurrentState = state;
        _definition = _catalog.Get(state);
        _frames = _sprites.GetFrames(state);
        _frameIndex = 0;
        _loopsCompleted = 0;
        _loopLimit = loops;
        _completed = completed;
        ShowCurrentFrame();
        ScheduleCurrentFrame();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        _frameIndex++;
        if (_frameIndex >= _frames.Count)
        {
            _frameIndex = 0;
            _loopsCompleted++;
            if (_loopLimit is int loopLimit && _loopsCompleted >= loopLimit)
            {
                var callback = _completed;
                _completed = null;
                callback?.Invoke();
                return;
            }
        }
        ShowCurrentFrame();
        ScheduleCurrentFrame();
    }

    private void ShowCurrentFrame() => _showFrame(_frames[_frameIndex]);

    private void ScheduleCurrentFrame()
    {
        if (_definition is null)
            return;
        _timer.Interval = TimeSpan.FromMilliseconds(_definition.DurationFor(_frameIndex));
        _timer.Start();
    }

    public void Dispose() => _timer.Stop();
}

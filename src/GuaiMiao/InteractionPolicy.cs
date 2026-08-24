namespace GuaiMiao;

internal static class PetInteractionPolicy
{
    public static readonly int HoverPounceLoops = 3;
    public static readonly int HoverExitSamples = 3;
    public static readonly double HoverExitMargin = 8.0;
    public static readonly double DragDirectionThreshold = 2.0;
    public static readonly int AutomaticActionLoops = 2;
    public static readonly int InitialAutoRunDelaySeconds = 6;
    public static readonly int AutomaticDelayMinSeconds = 45;
    public static readonly int AutomaticDelayMaxExclusiveSeconds = 76;
}

internal enum HoverExitResult
{
    None,
    ActiveHoverEnded,
    SuppressionCleared
}

internal sealed class HoverInteractionGate
{
    private int _outsideSamples;

    public bool Active { get; private set; }
    public bool SuppressedUntilExit { get; private set; }
    public bool Monitoring => Active || SuppressedUntilExit;

    public bool TryEnter()
    {
        if (Monitoring)
            return false;
        Active = true;
        _outsideSamples = 0;
        return true;
    }

    public void SuppressUntilExit()
    {
        Active = false;
        SuppressedUntilExit = true;
        _outsideSamples = 0;
    }

    public HoverExitResult ObservePointer(bool withinExitBounds)
    {
        if (!Monitoring)
            return HoverExitResult.None;
        if (withinExitBounds)
        {
            _outsideSamples = 0;
            return HoverExitResult.None;
        }
        _outsideSamples++;
        if (_outsideSamples < PetInteractionPolicy.HoverExitSamples)
            return HoverExitResult.None;

        var activeHoverEnded = Active;
        Reset();
        return activeHoverEnded
            ? HoverExitResult.ActiveHoverEnded
            : HoverExitResult.SuppressionCleared;
    }

    public void Reset()
    {
        Active = false;
        SuppressedUntilExit = false;
        _outsideSamples = 0;
    }
}

internal sealed class DragDirectionTracker
{
    private double _pendingDelta;

    public int Direction { get; private set; }

    public int Observe(double deltaX)
    {
        if (!double.IsFinite(deltaX) || Math.Abs(deltaX) < 0.01)
            return Direction;

        var sign = Math.Sign(deltaX);
        if (_pendingDelta != 0 && Math.Sign(_pendingDelta) != sign)
            _pendingDelta = 0;
        _pendingDelta += deltaX;
        if (Math.Abs(_pendingDelta) < PetInteractionPolicy.DragDirectionThreshold)
            return Direction;

        Direction = sign;
        _pendingDelta = 0;
        return Direction;
    }

    public void Reset()
    {
        _pendingDelta = 0;
        Direction = 0;
    }
}

using CutWire.Studio.Application.Abstractions;

namespace CutWire.Studio.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

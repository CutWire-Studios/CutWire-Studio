namespace CutWire.Studio.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

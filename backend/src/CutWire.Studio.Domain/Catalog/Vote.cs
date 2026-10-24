namespace CutWire.Studio.Domain.Catalog;

/// <summary>One user's vote on one item. <see cref="Weight"/> is what the vote contributed to the item's tallies.</summary>
public sealed class Vote
{
    private Vote() { }

    public Vote(Guid itemId, Guid userId, int value, double weight, DateTimeOffset now)
    {
        ItemId = itemId;
        UserId = userId;
        Value = value;
        Weight = weight;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid ItemId { get; private set; }
    public Guid UserId { get; private set; }
    /// <summary>1 for up, -1 for down.</summary>
    public int Value { get; private set; }
    public double Weight { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Change(int value, double weight, DateTimeOffset now)
    {
        Value = value;
        Weight = weight;
        UpdatedAt = now;
    }

    /// <summary>The weight of a new vote: T0 accounts younger than <paramref name="newAccountAge"/> count <paramref name="newAccountWeight"/>.</summary>
    public static double WeightFor(Accounts.TrustLevel trust, DateTimeOffset accountCreatedAt, DateTimeOffset now,
        TimeSpan newAccountAge, double newAccountWeight) =>
        trust == Accounts.TrustLevel.T0 && now - accountCreatedAt < newAccountAge ? newAccountWeight : 1.0;
}

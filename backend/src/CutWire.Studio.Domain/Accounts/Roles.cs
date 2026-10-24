namespace CutWire.Studio.Domain.Accounts;

public static class Roles
{
    public const string Moderator = "moderator";
    public const string Admin = "admin";

    public static readonly string[] All = [Moderator, Admin];
}

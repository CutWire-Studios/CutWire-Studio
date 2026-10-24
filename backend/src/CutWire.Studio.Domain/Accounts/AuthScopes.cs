namespace CutWire.Studio.Domain.Accounts;

public static class AuthScopes
{
    public const string ForgeRead = "forge:read";
    public const string ForgeWrite = "forge:write";
    public const string CommunityPublish = "community:publish";
    public const string Drift = "drift";

    public static readonly string[] Personal = [ForgeRead, ForgeWrite, CommunityPublish];

    public static readonly string[] Api = [ForgeRead, ForgeWrite, CommunityPublish, Drift];
}

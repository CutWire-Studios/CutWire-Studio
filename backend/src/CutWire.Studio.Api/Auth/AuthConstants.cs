namespace CutWire.Studio.Api.Auth;

public static class AuthSchemes
{
    public const string Studio = "Studio";
    public const string Pat = "Pat";
}

public static class AuthPolicies
{
    public const string Session = "session";
    public const string NotBanned = "not-banned";
    public const string ScopePrefix = "scope:";
    public const string RolePrefix = "role:";

    public static string Scope(string scope) => ScopePrefix + scope;
    public static string Role(string role) => RolePrefix + role;
}

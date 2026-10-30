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

    /// <summary>Server-rendered /admin pages: cookie session only, moderators and admins.</summary>
    public const string AdminArea = "admin-area";

    /// <summary>The admin-only subset of the /admin pages.</summary>
    public const string AdminOnly = "admin-only";

    /// <summary>Moderators and admins (admins are not implicitly moderators in Identity).</summary>
    public static readonly string Moderation = Role("moderator,admin");

    public static string Scope(string scope) => ScopePrefix + scope;
    public static string Role(string role) => RolePrefix + role;
}

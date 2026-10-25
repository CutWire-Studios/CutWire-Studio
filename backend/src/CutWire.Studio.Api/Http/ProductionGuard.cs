using Npgsql;

namespace CutWire.Studio.Api.Http;

/// <summary>
/// Refuses to start outside Development when a secret or host setting is missing or still at its
/// dev default. <c>Security:AllowInsecureDefaults=true</c> opts out (tests, local compose).
/// </summary>
public static class ProductionGuard
{
    public const int MinSealKeyLength = 32;

    public static void Validate(IConfiguration config, IHostEnvironment env)
    {
        if (env.IsDevelopment() || config.GetValue("Security:AllowInsecureDefaults", false)) return;

        var problems = new List<string>();

        var sealKey = config["Stock:Media:SealKey"];
        if (string.IsNullOrEmpty(sealKey) || sealKey.Length < MinSealKeyLength)
            problems.Add($"Stock:Media:SealKey must be set to at least {MinSealKeyLength} characters.");

        if (config.GetValue("Auth:OpenIddict:UseEphemeralKeys", false))
            problems.Add("Auth:OpenIddict:UseEphemeralKeys must be false; configure the signing and encryption certificates.");

        var connection = config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connection))
            problems.Add("ConnectionStrings:Default must be set.");
        else
        {
            var password = new NpgsqlConnectionStringBuilder(connection).Password;
            if (string.IsNullOrEmpty(password) || password == "studio")
                problems.Add("ConnectionStrings:Default must carry a non-default database password.");
        }

        var hosts = config["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(hosts) || hosts.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains("*"))
            problems.Add("AllowedHosts must list the public host names instead of '*'.");

        if (problems.Count > 0)
            throw new InvalidOperationException(
                "Insecure production configuration:" + Environment.NewLine + " - " + string.Join(Environment.NewLine + " - ", problems));
    }
}

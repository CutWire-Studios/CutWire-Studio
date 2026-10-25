using CutWire.Studio.Api.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace CutWire.Studio.UnitTests;

public class ProductionGuardTests
{
    private static readonly Dictionary<string, string?> Good = new()
    {
        ["Stock:Media:SealKey"] = new string('k', 32),
        ["ConnectionStrings:Default"] = "Host=db;Database=studio;Username=studio;Password=s3cret-value",
        ["AllowedHosts"] = "studio.example.com",
    };

    private static IConfiguration Config(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>(Good);
        foreach (var (key, value) in overrides) values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public void CompleteConfiguration_Passes() =>
        Should.NotThrow(() => ProductionGuard.Validate(Config(), new Env("Production")));

    [Theory]
    [InlineData("Stock:Media:SealKey", null)]
    [InlineData("Stock:Media:SealKey", "short")]
    [InlineData("ConnectionStrings:Default", "Host=db;Database=studio;Username=studio;Password=studio")]
    [InlineData("ConnectionStrings:Default", "Host=db;Database=studio;Username=studio")]
    [InlineData("AllowedHosts", "*")]
    [InlineData("AllowedHosts", "a.example.com;*")]
    [InlineData("Auth:OpenIddict:UseEphemeralKeys", "true")]
    public void WeakSetting_FailsStartup(string key, string? value) =>
        Should.Throw<InvalidOperationException>(() => ProductionGuard.Validate(Config((key, value)), new Env("Production")));

    [Fact]
    public void Development_AndExplicitOptOut_SkipTheChecks()
    {
        var weak = Config(("Stock:Media:SealKey", null), ("AllowedHosts", "*"));
        Should.NotThrow(() => ProductionGuard.Validate(weak, new Env("Development")));
        Should.NotThrow(() => ProductionGuard.Validate(Config(("Stock:Media:SealKey", null), ("Security:AllowInsecureDefaults", "true")), new Env("Production")));
    }
}

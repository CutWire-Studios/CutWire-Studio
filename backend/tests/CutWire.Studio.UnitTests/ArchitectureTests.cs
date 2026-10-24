using NetArchTest.Rules;
using Shouldly;

namespace CutWire.Studio.UnitTests;

public class ArchitectureTests
{
    private const string Domain = "CutWire.Studio.Domain";
    private const string Application = "CutWire.Studio.Application";
    private const string Infrastructure = "CutWire.Studio.Infrastructure";
    private const string Api = "CutWire.Studio.Api";

    private static readonly System.Reflection.Assembly DomainAssembly = typeof(Domain.Common.Entity<>).Assembly;
    private static readonly System.Reflection.Assembly ApplicationAssembly = typeof(Application.DependencyInjection).Assembly;
    private static readonly System.Reflection.Assembly InfrastructureAssembly = typeof(Infrastructure.DependencyInjection).Assembly;

    [Fact]
    public void Domain_DependsOnNothingInternalOrFramework() =>
        AssertNoDependency(DomainAssembly, Application, Infrastructure, Api, "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore");

    [Fact]
    public void Application_DoesNotDependOnInfrastructureOrApi() =>
        AssertNoDependency(ApplicationAssembly, Infrastructure, Api, "Npgsql", "Microsoft.AspNetCore");

    [Fact]
    public void Infrastructure_DoesNotDependOnApi() =>
        AssertNoDependency(InfrastructureAssembly, Api);

    [Fact]
    public void ProjectReferences_FollowLayering()
    {
        Names(DomainAssembly).ShouldNotContain(n => n.StartsWith("CutWire.Studio"));
        Names(ApplicationAssembly).Where(n => n.StartsWith("CutWire.Studio")).ShouldBe([Domain]);
        Names(InfrastructureAssembly).Where(n => n.StartsWith("CutWire.Studio")).ShouldBe([Application, Domain], ignoreOrder: true);
    }

    private static IEnumerable<string> Names(System.Reflection.Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!);

    private static void AssertNoDependency(System.Reflection.Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }
}

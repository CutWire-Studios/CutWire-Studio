using System.Text.Json.Nodes;
using CutWire.Studio.Application.Marketplace;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Marketplace;
using Shouldly;

namespace CutWire.Studio.UnitTests;

public class MarketplaceLayoutTests
{
    private static string Page(string sections, string id = "p1", string extra = "") =>
        $$"""[{"id":"{{id}}","title":"Page"{{extra}},"sections":[{{sections}}]}]""";

    private static string[] Codes(string json)
    {
        var result = LayoutValidator.Validate(json);
        return result.IsSuccess ? [] : result.Error.Violations!.Select(v => $"{v.Field}:{v.Code}").ToArray();
    }

    private static void Rejects(string json, string field, string code) => Codes(json).ShouldContain($"{field}:{code}");

    [Fact]
    public void DefaultLayout_IsValid() => Codes(MarketplaceManifest.DefaultLayoutJson).ShouldBeEmpty();

    [Fact]
    public void Minimal_Hero_IsValid() =>
        Codes(Page("""{"id":"h","type":"hero","title":"Hi","image":"https://x.example/a.png","action":{"type":"open-url","url":"https://x.example"}}""")).ShouldBeEmpty();

    [Fact]
    public void Rejects_NotJson_NotArray_EmptyArray_TooManyPages()
    {
        Rejects("{", "pages", "invalid_json");
        Rejects("{}", "pages", "type");
        Rejects("[]", "pages", "count");
        var pages = string.Join(',', Enumerable.Range(0, 13).Select(i => $$"""{"id":"p{{i}}","title":"t","sections":[{"id":"n","type":"downloads"}]}"""));
        Rejects($"[{pages}]", "pages", "count");
    }

    [Fact]
    public void Rejects_DuplicateKeysInJson() => Rejects("""[{"id":"a","id":"b"}]""", "pages", "invalid_json");

    [Fact]
    public void Rejects_BadPageFields()
    {
        Rejects("""[{"title":"t","sections":[{"id":"n","type":"downloads"}]}]""", "pages[0].id", "required");
        Rejects(Page("""{"id":"n","type":"downloads"}""", id: "Bad Id"), "pages[0].id", "pattern");
        Rejects(Page("""{"id":"n","type":"downloads"}""", id: new string('a', 41)), "pages[0].id", "pattern");
        Rejects(Page("""{"id":"n","type":"downloads"}""", extra: ",\"nope\":1"), "pages[0].nope", "unknown_key");
        Rejects(Page("""{"id":"n","type":"downloads"}""", extra: ",\"titles\":{\"de\":5}"), "pages[0].titles.de", "type");
        Rejects(Page("""{"id":"n","type":"downloads"}""", extra: ",\"titles\":{\"not a lang\":\"x\"}"), "pages[0].titles.not a lang", "pattern");
        Rejects(Page("""{"id":"n","type":"downloads"}""", extra: $",\"title2\":\"x\""), "pages[0].title2", "unknown_key");
        Rejects("""[{"id":"a","title":"t","sections":[]}]""", "pages[0].sections", "count");
        Rejects("""[{"id":"a","title":"t"}]""", "pages[0].sections", "required");
    }

    [Fact]
    public void Accepts_Titles() =>
        Codes(Page("""{"id":"n","type":"downloads"}""", extra: ",\"titles\":{\"de\":\"Seite\",\"pt-BR\":\"Pagina\"},\"icon\":\"star\"")).ShouldBeEmpty();

    [Fact]
    public void Rejects_DuplicatePageIds()
    {
        const string page = """{"id":"a","title":"t","sections":[{"id":"n","type":"downloads"}]}""";
        Rejects($"[{page},{page}]", "pages[1].id", "duplicate");
    }

    [Fact]
    public void Rejects_DuplicateSectionIds_PerPageOnly()
    {
        Rejects(Page("""{"id":"n","type":"downloads"},{"id":"n","type":"downloads"}"""), "pages[0].sections[1].id", "duplicate");
        const string other = """{"id":"b","title":"t","sections":[{"id":"n","type":"downloads"}]}""";
        Codes($$"""[{"id":"a","title":"t","sections":[{"id":"n","type":"downloads"}]},{{other}}]""").ShouldBeEmpty();
    }

    [Fact]
    public void Rejects_TooManySections()
    {
        var sections = string.Join(',', Enumerable.Range(0, 31).Select(i => $$"""{"id":"s{{i}}","type":"downloads"}"""));
        Rejects(Page(sections), "pages[0].sections", "count");
    }

    [Fact]
    public void Rejects_UnknownSectionType_AndKeys()
    {
        Rejects(Page("""{"id":"n","type":"carousel"}"""), "pages[0].sections[0].type", "enum");
        Rejects(Page("""{"id":"n","type":"downloads","extra":1}"""), "pages[0].sections[0].extra", "unknown_key");
        Rejects(Page("""{"id":"n","type":"hero","title":"t","source":{"id":"sfx"}}"""), "pages[0].sections[0].source", "unknown_key");
    }

    [Theory]
    [InlineData("shelf", ",\"title\":\"t\"")]
    [InlineData("grid", "")]
    [InlineData("chips", ",\"target\":\"g\"")]
    [InlineData("stock-browser", "")]
    [InlineData("sfx-browser", "")]
    public void SourceBoundSections_RequireSource(string type, string extra)
    {
        const string grid = """{"id":"g","type":"grid","source":{"id":"sfx"}},""";
        Rejects(Page(grid + $$$"""{"id":"n","type":"{{{type}}}"{{{extra}}}}"""), "pages[0].sections[1].source", "required");
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("stock.")]
    [InlineData("stock.Video")]
    [InlineData("community")]
    [InlineData("sfx.extra")]
    public void Rejects_BadSourceId(string id) =>
        Rejects(Page($$$"""{"id":"n","type":"grid","source":{"id":"{{{id}}}"}}"""), "pages[0].sections[0].source.id", "pattern");

    [Theory]
    [InlineData("drift-assets")]
    [InlineData("sfx")]
    [InlineData("stock.video")]
    [InlineData("community.audio-effect")]
    public void Accepts_SourceIds(string id) =>
        Codes(Page($$$"""{"id":"n","type":"grid","source":{"id":"{{{id}}}"}}""")).ShouldBeEmpty();

    [Fact]
    public void Source_BindingKeys()
    {
        Codes(Page("""{"id":"n","type":"grid","source":{"id":"stock.video","category":"c","q":"x","sort":"top","period":"week","provider":"pexels","filters":{"orientation":"landscape"}}}""")).ShouldBeEmpty();
        Rejects(Page("""{"id":"n","type":"grid","source":{"id":"sfx","limit":3}}"""), "pages[0].sections[0].source.limit", "unknown_key");
        Rejects(Page("""{"id":"n","type":"grid","source":{"id":"sfx","filters":{"a":1}}}"""), "pages[0].sections[0].source.filters.a", "type");
        Rejects(Page("""{"id":"n","type":"grid","source":{"id":"sfx","filters":[]}}"""), "pages[0].sections[0].source.filters", "type");
    }

    [Fact]
    public void BrowserSections_NeedMatchingSourceKind()
    {
        Rejects(Page("""{"id":"n","type":"stock-browser","source":{"id":"sfx"}}"""), "pages[0].sections[0].source.id", "source_kind");
        Rejects(Page("""{"id":"n","type":"sfx-browser","source":{"id":"stock.video"}}"""), "pages[0].sections[0].source.id", "source_kind");
    }

    [Fact]
    public void Chips_TargetMustBeAGridOnTheSamePage()
    {
        Rejects(Page("""{"id":"c","type":"chips","source":{"id":"sfx"},"target":"g"}"""), "pages[0].sections[0].target", "unknown_target");
        Rejects(Page("""{"id":"c","type":"chips","source":{"id":"sfx"},"target":"s"},{"id":"s","type":"shelf","title":"t","source":{"id":"sfx"}}"""), "pages[0].sections[0].target", "unknown_target");
        Rejects(Page("""{"id":"c","type":"chips","source":{"id":"sfx"}}"""), "pages[0].sections[0].target", "required");
        Codes(Page("""{"id":"c","type":"chips","source":{"id":"sfx"},"target":"g"},{"id":"g","type":"grid","source":{"id":"sfx"}}""")).ShouldBeEmpty();
    }

    [Fact]
    public void Hero_Rules()
    {
        Rejects(Page("""{"id":"h","type":"hero"}"""), "pages[0].sections[0].title", "required");
        Rejects(Page("""{"id":"h","type":"hero","title":"t","image":"http://x.example/a.png"}"""), "pages[0].sections[0].image", "invalid_url");
        Rejects(Page("""{"id":"h","type":"hero","title":"t","image":"not a url"}"""), "pages[0].sections[0].image", "invalid_url");
        Rejects(Page("""{"id":"h","type":"hero","title":"t","subtitle":"%s"}""".Replace("%s", new string('x', 201))), "pages[0].sections[0].subtitle", "too_long");
    }

    [Fact]
    public void Notice_Rules()
    {
        Codes(Page($$"""{"id":"n","type":"notice","text":"{{new string('x', 500)}}","tone":"warning"}""")).ShouldBeEmpty();
        Rejects(Page($$"""{"id":"n","type":"notice","text":"{{new string('x', 501)}}"}"""), "pages[0].sections[0].text", "too_long");
        Rejects(Page("""{"id":"n","type":"notice","text":"t","tone":"error"}"""), "pages[0].sections[0].tone", "enum");
        Rejects(Page("""{"id":"n","type":"notice"}"""), "pages[0].sections[0].text", "required");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("49")]
    [InlineData("1.5")]
    [InlineData("\"12\"")]
    public void Shelf_LimitMustBeWholeNumberUpTo48(string limit) =>
        Rejects(Page($$$"""{"id":"s","type":"shelf","title":"t","limit":{{{limit}}},"source":{"id":"sfx"}}"""), "pages[0].sections[0].limit", "range");

    [Theory]
    [InlineData("1")]
    [InlineData("48")]
    public void Shelf_LimitBounds(string limit) =>
        Codes(Page($$$"""{"id":"s","type":"shelf","title":"t","limit":{{{limit}}},"source":{"id":"sfx"}}""")).ShouldBeEmpty();

    [Fact]
    public void Shelf_SeeAll_MustNamePageInLayout()
    {
        Rejects(Page("""{"id":"s","type":"shelf","title":"t","source":{"id":"sfx"},"seeAll":{"page":"missing"}}"""), "pages[0].sections[0].seeAll.page", "unknown_page");
        Codes(Page("""{"id":"s","type":"shelf","title":"t","source":{"id":"sfx"},"seeAll":{"page":"p1"}}""")).ShouldBeEmpty();
    }

    [Fact]
    public void Grid_Rules()
    {
        Rejects(Page("""{"id":"g","type":"grid","source":{"id":"sfx"},"searchable":"yes"}"""), "pages[0].sections[0].searchable", "type");
        Codes(Page("""{"id":"g","type":"grid","title":"t","searchable":true,"source":{"id":"sfx"}}""")).ShouldBeEmpty();
    }

    [Fact]
    public void Actions()
    {
        Codes(Page("""{"id":"h","type":"hero","title":"t","action":{"type":"open-page","page":"p1"}}""")).ShouldBeEmpty();
        Codes(Page("""{"id":"h","type":"hero","title":"t","action":{"type":"open-item","source":"stock.video","id":"abc"}}""")).ShouldBeEmpty();
        Rejects(Page("""{"id":"h","type":"hero","title":"t","action":{"type":"open-page","page":"zzz"}}"""), "pages[0].sections[0].action.page", "unknown_page");
        Rejects(Page("""{"id":"h","type":"hero","title":"t","action":{"type":"open-url","url":"http://x.example"}}"""), "pages[0].sections[0].action.url", "invalid_url");
        Rejects(Page("""{"id":"h","type":"hero","title":"t","action":{"type":"open-url","url":"https://x.example","x":1}}"""), "pages[0].sections[0].action.x", "unknown_key");
        Rejects(Page("""{"id":"h","type":"hero","title":"t","action":{"type":"open-item","source":"bad","id":"a"}}"""), "pages[0].sections[0].action.source", "pattern");
        Rejects(Page("""{"id":"h","type":"hero","title":"t","action":{"type":"run"}}"""), "pages[0].sections[0].action.type", "enum");
        Rejects(Page("""{"id":"h","type":"hero","title":"t","action":"go"}"""), "pages[0].sections[0].action", "type");
    }

    [Fact]
    public void Validate_ReturnsViolationsAsValidationError()
    {
        var result = LayoutValidator.Validate("[]");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LayoutValidator.InvalidCode);
        result.Error.Violations.ShouldNotBeNull();
    }

    private static SemVersion V(string v) => SemVersion.TryParse(v, out var s) ? s : throw new ArgumentException(v);

    [Fact]
    public void Layout_Matches_DistributionList()
    {
        var all = new MarketplaceLayout();
        var some = new MarketplaceLayout { Distributions = ["windows"] };

        all.Matches("android-play", V("1.0.0")).ShouldBeTrue();
        some.Matches("windows", V("1.0.0")).ShouldBeTrue();
        some.Matches("android-play", V("1.0.0")).ShouldBeFalse();
    }

    [Fact]
    public void Layout_Matches_VersionRange_MinInclusiveMaxExclusive()
    {
        var layout = new MarketplaceLayout { MinAppVersion = "1.2.0", MaxAppVersion = "2.0.0" };

        layout.Matches("windows", V("1.1.9")).ShouldBeFalse();
        layout.Matches("windows", V("1.2.0")).ShouldBeTrue();
        layout.Matches("windows", V("1.9.9")).ShouldBeTrue();
        layout.Matches("windows", V("2.0.0")).ShouldBeFalse();
    }

    [Fact]
    public void Matches_NullVersion_IgnoresTheRange()
    {
        new MarketplaceLayout { MinAppVersion = "9.0.0" }.Matches("web", null).ShouldBeTrue();
        new FeatureFlag { MaxAppVersion = "0.1.0" }.Matches("web", null).ShouldBeTrue();
        new FeatureFlag { Distributions = ["windows"] }.Matches("web", null).ShouldBeFalse();
    }

    [Fact]
    public void DisabledLayout_NeverMatches() => new MarketplaceLayout { Enabled = false }.Matches("windows", V("1.0.0")).ShouldBeFalse();

    [Fact]
    public void Flag_Matches_Range() =>
        new FeatureFlag { MinAppVersion = "1.0.0", MaxAppVersion = "1.5.0" }.Matches("appimage", V("1.5.0")).ShouldBeFalse();

    [Fact]
    public void Specificity_RanksDistributionAboveVersionBounds()
    {
        Targeting.Specificity(["windows"], null, null).ShouldBeGreaterThan(Targeting.Specificity([], "1.0.0", "2.0.0"));
        Targeting.Specificity([], "1.0.0", null).ShouldBeGreaterThan(Targeting.Specificity([], null, null));
    }

    [Fact]
    public void DefaultLayout_UsesOnlyKnownSourcesAndParses() =>
        ((JsonArray)JsonNode.Parse(MarketplaceManifest.DefaultLayoutJson)!).Count.ShouldBe(9);
}

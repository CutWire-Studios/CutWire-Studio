using System.Text.Json;
using System.Text.Json.Serialization;
using CutWire.Studio.Application.Quality;
using CutWire.Studio.Domain.Catalog;
using Shouldly;

namespace CutWire.Studio.UnitTests;

public class ContentRulesFixtureTests
{
    private static readonly ContentRulesOptions Rules = new();

    private static ContentGatePipeline Pipeline()
    {
        var words = new BloomWordList();
        return new ContentGatePipeline(
        [
            new TitleGate(Rules, words), new TextGate(Rules), new EnglishGate(Rules, words), new ProfanityGate(), new TaxonomyGate(Rules, Kinds.Registry),
        ]);
    }

    private sealed record Case(string Name, string Kind, string Title, string Summary, string Description, string[] Tags, string Category, string License);

    private static Case Valid(string name) => new(name, "effect", "Dreamy Glow", Drafts.Summary, Drafts.Description, ["glow", "dream"], "dreamy", "CC0-1.0");

    private static List<Case> Cases()
    {
        var v = Valid("valid");
        var longTitle = new string('a', 30) + " " + new string('b', 40);
        return
        [
            v,
            v with { Name = "title_shouting", Title = "BAD!!!!" },
            v with { Name = "title_short", Title = "Glow" },
            v with { Name = "title_single_dictionary_word", Title = "Dreamy" },
            v with { Name = "title_single_gibberish", Title = "Zzqxvk" },
            v with { Name = "title_long", Title = longTitle },
            v with { Name = "title_caps", Title = "NEON GLOW" },
            v with { Name = "title_url", Title = "Visit www.example.com now" },
            v with { Name = "title_unicode", Title = "Café glow" },
            v with { Name = "title_spaces", Title = " Glow  effect " },
            v with { Name = "title_repeat", Title = "Glowwwwww effect" },
            v with { Name = "title_punct", Title = "Glow effect!!!" },
            v with { Name = "summary_short", Summary = "tiny" },
            v with { Name = "summary_link", Summary = "Adds a glow, see https://example.com for more." },
            v with { Name = "summary_unicode", Summary = "Adds a soft dreamy glow ✨ around highlights." },
            v with { Name = "description_short", Description = "Too short." },
            v with { Name = "description_links", Description = Drafts.Description + " See a.com and b.com and example.org too." },
            v with { Name = "description_blank_lines", Description = Drafts.Description + "\n\n\n\n\nMore text here." },
            v with { Name = "description_not_english", Description = "Este efecto agrega un brillo suave alrededor de las partes mas brillantes de tu clip. Usa el control para cambiar la fuerza del resplandor en el video." },
            v with { Name = "profanity_leet", Summary = "Adds a fuck1ng dreamy glow around the highlights." },
            v with { Name = "profanity_tags_title", Title = "Shit glow effect" },
            v with { Name = "category_missing", Category = "" },
            v with { Name = "category_invalid", Category = "nope" },
            v with { Name = "tags_none", Tags = [] },
            v with { Name = "tags_many", Tags = ["glow", "dream", "film", "neon", "pixel", "retro"] },
            v with { Name = "tags_unknown_and_duplicate", Tags = ["glow", "glow", "nonsense"] },
            v with { Name = "license_invalid", License = "WTFPL" },
            v with { Name = "license_missing", License = "" },
            v with { Name = "kind_unknown", Kind = "mystery" },
            v with { Name = "kind_disabled", Kind = "language-pack" },
            v with { Name = "everything_empty", Title = "", Summary = "", Description = "", Tags = [], Category = "", License = "" },
            v with { Name = "crlf_description", Description = Drafts.Description.Replace(". ", ".\r\n") },
        ];
    }

    [Fact]
    public async Task WritesQualityRulesAndGateCases()
    {
        var pipeline = Pipeline();
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };
        var rules = new QualityRulesProducer(Rules, Kinds.Registry, new BloomWordList()).Produce();

        var cases = new List<object>();
        string[]? validExpected = null;
        foreach (var c in Cases())
        {
            if (!AssetKindId.TryParse(c.Kind, out var kind)) throw new InvalidOperationException(c.Name);
            var draft = new SubmissionDraft(kind, Drafts.Owner, c.Title, c.Summary, c.Description, c.Tags, c.Category, c.License, [], null);
            var violations = await pipeline.CheckMetadataAsync(draft, TestContext.Current.CancellationToken);
            var expected = violations.Select(x => $"{x.Field}:{x.Code}").Order(StringComparer.Ordinal).ToArray();
            if (c.Name == "valid") validExpected = expected;
            cases.Add(new
            {
                name = c.Name, kind = c.Kind, title = c.Title, summary = c.Summary, description = c.Description, tags = c.Tags, category = c.Category,
                license = c.License,
                expected,
            });
        }

        var dir = Path.Combine(RepoPaths.ContentRules(), "test-fixtures");
        RepoPaths.WriteIfChanged(Path.Combine(dir, "quality-rules.json"), JsonSerializer.Serialize(rules, web) + "\n");
        RepoPaths.WriteIfChanged(Path.Combine(dir, "gate-cases.json"), JsonSerializer.Serialize(cases, web) + "\n");

        validExpected.ShouldBeEmpty();
    }
}

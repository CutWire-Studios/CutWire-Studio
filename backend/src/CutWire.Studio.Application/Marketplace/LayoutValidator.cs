using System.Text.Json;
using System.Text.Json.Nodes;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Application.Marketplace;

/// <summary>Strict validation of a layout's <c>pages</c> array. Unknown keys are rejected so a typo in the admin editor cannot silently do nothing.</summary>
public static class LayoutValidator
{
    public const string InvalidCode = "marketplace.invalid_layout";

    public static Result<JsonArray> Validate(string? pagesJson)
    {
        var violations = new List<FieldViolation>();
        JsonNode? root = null;
        try
        {
            root = JsonNode.Parse(pagesJson ?? "");
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            violations.Add(new("pages", "invalid_json", "The layout is not valid JSON."));
        }

        if (violations.Count == 0)
        {
            if (root is not JsonArray pages) violations.Add(new("pages", "type", "The layout must be an array of pages."));
            else
            {
                try
                {
                    Pages(pages, violations);
                    if (violations.Count == 0) return pages;
                }
                catch (ArgumentException)
                {
                    violations.Add(new("pages", "invalid_json", "An object repeats a key."));
                }
            }
        }

        return Error.Validation(InvalidCode, "The layout is not valid.", violations);
    }

    private static void Pages(JsonArray pages, List<FieldViolation> v)
    {
        if (pages.Count is < 1 or > LayoutDocument.MaxPages)
            v.Add(new("pages", "count", $"A layout needs 1 to {LayoutDocument.MaxPages} pages.", new Dictionary<string, object> { ["max"] = LayoutDocument.MaxPages }));

        var ids = pages.OfType<JsonObject>().Select(p => p["id"] is JsonValue id && id.TryGetValue<string>(out var s) ? s : null).Where(s => s is not null).ToHashSet();
        var seen = new HashSet<string>();
        for (var i = 0; i < pages.Count; i++)
        {
            var path = $"pages[{i}]";
            if (pages[i] is not JsonObject page)
            {
                v.Add(new(path, "type", "A page must be an object."));
                continue;
            }

            Page(page, path, ids!, seen, v);
        }
    }

    private static void Page(JsonObject page, string path, HashSet<string> pageIds, HashSet<string> seen, List<FieldViolation> v)
    {
        Keys(page, path, ["id", "title", "titles", "icon", "sections"], v);
        var id = Id(page, "id", path, v);
        if (id is not null && LayoutDocument.ReservedPageIds.Contains(id))
            v.Add(new($"{path}.id", "reserved", $"Page id \"{id}\" is reserved for a fixed client route."));
        if (id is not null && !seen.Add(id)) v.Add(new($"{path}.id", "duplicate", $"Page id \"{id}\" is used twice."));
        Str(page, "title", path, true, LayoutDocument.MaxString, v);
        Str(page, "icon", path, false, LayoutDocument.MaxString, v);

        if (page.TryGetPropertyValue("titles", out var titles))
        {
            if (titles is not JsonObject map) v.Add(new($"{path}.titles", "type", "titles must map language codes to strings."));
            else
                foreach (var (lang, _) in map)
                {
                    if (!LayoutDocument.IsLanguage(lang)) v.Add(new($"{path}.titles.{lang}", "pattern", "Not a language code."));
                    Str(map, lang, $"{path}.titles", true, LayoutDocument.MaxString, v);
                }
        }

        if (page["sections"] is not JsonArray sections)
        {
            v.Add(new($"{path}.sections", page.ContainsKey("sections") ? "type" : "required", "sections must be an array."));
            return;
        }

        if (sections.Count is < 1 or > LayoutDocument.MaxSections)
            v.Add(new($"{path}.sections", "count", $"A page needs 1 to {LayoutDocument.MaxSections} sections.", new Dictionary<string, object> { ["max"] = LayoutDocument.MaxSections }));

        var grids = sections.OfType<JsonObject>()
            .Where(s => s["type"] is JsonValue t && t.TryGetValue<string>(out var name) && name == LayoutDocument.Grid)
            .Select(s => s["id"] is JsonValue sid && sid.TryGetValue<string>(out var value) ? value : null)
            .Where(s => s is not null).ToHashSet();
        var sectionIds = new HashSet<string>();
        for (var i = 0; i < sections.Count; i++)
        {
            var sectionPath = $"{path}.sections[{i}]";
            if (sections[i] is not JsonObject section)
            {
                v.Add(new(sectionPath, "type", "A section must be an object."));
                continue;
            }

            Section(section, sectionPath, sectionIds, grids!, pageIds, v);
        }
    }

    private static void Section(JsonObject s, string path, HashSet<string> sectionIds, HashSet<string> grids, HashSet<string> pageIds, List<FieldViolation> v)
    {
        var id = Id(s, "id", path, v);
        if (id is not null && !sectionIds.Add(id)) v.Add(new($"{path}.id", "duplicate", $"Section id \"{id}\" is used twice on this page."));

        var type = Str(s, "type", path, true, LayoutDocument.MaxString, v);
        if (type is null) return;
        if (!LayoutDocument.SectionTypes.Contains(type))
        {
            v.Add(new($"{path}.type", "enum", $"Unknown section type \"{type}\".", new Dictionary<string, object> { ["allowed"] = LayoutDocument.SectionTypes }));
            return;
        }

        var sourceBound = LayoutDocument.SourceBound.Contains(type);
        string[] own = type switch
        {
            LayoutDocument.Hero => ["title", "subtitle", "image", "action"],
            LayoutDocument.Notice => ["text", "tone"],
            LayoutDocument.Shelf => ["title", "limit", "seeAll"],
            LayoutDocument.Grid => ["title", "searchable"],
            LayoutDocument.Chips => ["target"],
            _ => [],
        };
        Keys(s, path, ["id", "type", ..own, ..(sourceBound ? new[] { "source" } : [])], v);

        if (sourceBound) Source(s, type, path, v);

        switch (type)
        {
            case LayoutDocument.Hero:
                Str(s, "title", path, true, LayoutDocument.MaxString, v);
                Str(s, "subtitle", path, false, LayoutDocument.MaxString, v);
                Url(s, "image", path, false, v);
                if (s.TryGetPropertyValue("action", out var action)) Action(action, $"{path}.action", pageIds, v);
                break;
            case LayoutDocument.Notice:
                Str(s, "text", path, true, LayoutDocument.MaxText, v);
                var tone = Str(s, "tone", path, false, LayoutDocument.MaxString, v);
                if (tone is not null && !LayoutDocument.Tones.Contains(tone)) v.Add(new($"{path}.tone", "enum", "tone must be info or warning."));
                break;
            case LayoutDocument.Shelf:
                Str(s, "title", path, true, LayoutDocument.MaxString, v);
                if (s.TryGetPropertyValue("limit", out var limit)
                    && !(limit is JsonValue lv && lv.TryGetValue<int>(out var n) && n is >= 1 and <= LayoutDocument.MaxLimit))
                    v.Add(new($"{path}.limit", "range", $"limit must be a whole number from 1 to {LayoutDocument.MaxLimit}."));
                if (s.TryGetPropertyValue("seeAll", out var seeAll))
                {
                    if (seeAll is not JsonObject see) v.Add(new($"{path}.seeAll", "type", "seeAll must be an object."));
                    else
                    {
                        Keys(see, $"{path}.seeAll", ["page"], v);
                        PageRef(see, "page", $"{path}.seeAll", pageIds, v);
                    }
                }

                break;
            case LayoutDocument.Grid:
                Str(s, "title", path, false, LayoutDocument.MaxString, v);
                Bool(s, "searchable", path, v);
                break;
            case LayoutDocument.Chips:
                var target = Str(s, "target", path, true, LayoutDocument.MaxString, v);
                if (target is not null && !grids.Contains(target))
                    v.Add(new($"{path}.target", "unknown_target", $"No grid section \"{target}\" on this page."));
                break;
        }
    }

    private static void Source(JsonObject s, string type, string path, List<FieldViolation> v)
    {
        var at = $"{path}.source";
        if (!s.TryGetPropertyValue("source", out var node))
        {
            v.Add(new(at, "required", $"A {type} section needs a source."));
            return;
        }

        if (node is not JsonObject source)
        {
            v.Add(new(at, "type", "source must be an object."));
            return;
        }

        Keys(source, at, LayoutDocument.SourceKeys, v);
        var id = Str(source, "id", at, true, LayoutDocument.MaxString, v);
        if (id is not null)
        {
            if (!LayoutDocument.IsSourceId(id)) v.Add(new($"{at}.id", "pattern", $"\"{id}\" is not a source id."));
            else if (type == LayoutDocument.StockBrowser && !id.StartsWith("stock.", StringComparison.Ordinal))
                v.Add(new($"{at}.id", "source_kind", "A stock-browser needs a stock.<type> source."));
            else if (type == LayoutDocument.SfxBrowser && id != "sfx")
                v.Add(new($"{at}.id", "source_kind", "An sfx-browser needs the sfx source."));
        }

        foreach (var key in LayoutDocument.SourceKeys.Where(k => k is not ("id" or "filters")))
            Str(source, key, at, false, LayoutDocument.MaxString, v);

        if (source.TryGetPropertyValue("filters", out var filters))
        {
            if (filters is not JsonObject map) v.Add(new($"{at}.filters", "type", "filters must map names to strings."));
            else
                foreach (var (name, _) in map)
                    Str(map, name, $"{at}.filters", true, LayoutDocument.MaxString, v);
        }
    }

    private static void Action(JsonNode? node, string path, HashSet<string> pageIds, List<FieldViolation> v)
    {
        if (node is not JsonObject action)
        {
            v.Add(new(path, "type", "action must be an object."));
            return;
        }

        var type = Str(action, "type", path, true, LayoutDocument.MaxString, v);
        switch (type)
        {
            case null:
                return;
            case "open-page":
                Keys(action, path, ["type", "page"], v);
                PageRef(action, "page", path, pageIds, v);
                break;
            case "open-item":
                Keys(action, path, ["type", "source", "id"], v);
                var source = Str(action, "source", path, true, LayoutDocument.MaxString, v);
                if (source is not null && !LayoutDocument.IsSourceId(source)) v.Add(new($"{path}.source", "pattern", $"\"{source}\" is not a source id."));
                Str(action, "id", path, true, LayoutDocument.MaxString, v);
                break;
            case "open-url":
                Keys(action, path, ["type", "url"], v);
                Url(action, "url", path, true, v);
                break;
            default:
                v.Add(new($"{path}.type", "enum", $"Unknown action type \"{type}\".", new Dictionary<string, object> { ["allowed"] = LayoutDocument.ActionTypes }));
                break;
        }
    }

    private static void PageRef(JsonObject o, string key, string path, HashSet<string> pageIds, List<FieldViolation> v)
    {
        var page = Str(o, key, path, true, LayoutDocument.MaxString, v);
        if (page is not null && !pageIds.Contains(page)) v.Add(new($"{path}.{key}", "unknown_page", $"No page \"{page}\" in this layout."));
    }

    private static void Keys(JsonObject o, string path, string[] allowed, List<FieldViolation> v)
    {
        foreach (var (key, _) in o)
            if (!allowed.Contains(key)) v.Add(new($"{path}.{key}", "unknown_key", $"Unknown key \"{key}\"."));
    }

    private static string? Id(JsonObject o, string key, string path, List<FieldViolation> v)
    {
        var value = Str(o, key, path, true, LayoutDocument.MaxString, v);
        if (value is null) return null;
        if (LayoutDocument.IsId(value)) return value;
        v.Add(new($"{path}.{key}", "pattern", "Ids use lowercase letters, digits and dashes, 1 to 40 characters."));
        return null;
    }

    private static string? Str(JsonObject o, string key, string path, bool required, int max, List<FieldViolation> v)
    {
        if (!o.TryGetPropertyValue(key, out var node) || node is null)
        {
            if (required) v.Add(new($"{path}.{key}", "required", $"{key} is required."));
            return null;
        }

        if (node is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            v.Add(new($"{path}.{key}", "type", $"{key} must be a string."));
            return null;
        }

        if (text.Length > max)
        {
            v.Add(new($"{path}.{key}", "too_long", $"{key} is limited to {max} characters.", new Dictionary<string, object> { ["max"] = max }));
            return null;
        }

        return text;
    }

    private static void Url(JsonObject o, string key, string path, bool required, List<FieldViolation> v)
    {
        var value = Str(o, key, path, required, LayoutDocument.MaxUrl, v);
        if (value is not null && !(Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
            v.Add(new($"{path}.{key}", "invalid_url", $"{key} must be an https URL."));
    }

    private static void Bool(JsonObject o, string key, string path, List<FieldViolation> v)
    {
        if (o.TryGetPropertyValue(key, out var node) && !(node is JsonValue value && value.TryGetValue<bool>(out _)))
            v.Add(new($"{path}.{key}", "type", $"{key} must be true or false."));
    }
}

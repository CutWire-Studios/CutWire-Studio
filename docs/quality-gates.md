# Quality gates

Every submission runs through `ContentGatePipeline` (`Application/Quality`). All gates run and every violation is returned. Blocking violations become one `Error.Validation("content_rejected", ...)` with field errors; `Flag` violations do not block and are returned in `GateReport.Flags` for moderators. Limits live in `ContentRulesOptions` (config section `ContentRules`); `QualityRulesProducer` serialises them for `/api/v1/meta/quality-rules`.

Violation: `Field`, `Code` (stable snake_case), `Message`, `Params`, `Severity` (`Block`, `Flag`).

| Gate | Field | Code | Rule (default) |
| --- | --- | --- | --- |
| Title | `title` | `required`, `non_ascii`, `too_short`, `too_long` | printable ASCII, 6-60 chars |
| | | `bad_whitespace` | no leading/trailing/double spaces |
| | | `needs_words` | 2+ words, or one dictionary word of 4+ letters |
| | | `all_caps` | not all caps when 4+ letters |
| | | `contains_url` | no URL or domain |
| | | `repeated_chars`, `repeated_punctuation` | no run of 4 identical chars (any case) or 3 identical punctuation |
| Text | `summary`, `description` | `required`, `non_ascii` (params `index`, `char`), `too_short`, `too_long` | printable ASCII plus `\n`; summary 20-140, description 80-4000 |
| | `summary` | `links_not_allowed` | no links |
| | `description` | `too_many_links` | at most 2 links |
| | `description` | `too_many_blank_lines` | at most 2 consecutive blank lines |
| English | `description` | `not_english` | summary + description: at least 75% of tokens in the word list; skipped under 8 tokens. Ignored: tokens with digits, tech terms, the item's tag words, title words repeated in the description. Suffixes s/es/ed/ing/ly/er/est are stripped before lookup |
| Profanity | `title`, `summary`, `description`, `tags` | `profanity` | whole-word denylist after leet normalisation (`0 1 3 4 5 @ $`) |
| Taxonomy | `kind` | `unknown_kind` | kind exists and is enabled |
| | `category` | `required`, `invalid_category` | one of the kind's categories |
| | `tags` | `too_few_tags`, `too_many_tags`, `unknown_tag`, `duplicate_tag` | 1-5 tags from the kind's vocabulary |
| | `license` | `required`, `invalid_license` | CC0-1.0, CC-BY-4.0, CC-BY-SA-4.0, MIT, Apache-2.0, GPL-3.0-or-later |
| Media | `media.<role>` | `media_missing`, `media_role_not_allowed`, `media_duplicate_role` | roles from the kind's media specs |
| | | `media_too_large`, `media_undecodable`, `media_not_webp` | decoded by content (the declared mime is ignored); must be WebP within the byte cap |
| | | `media_wrong_dimensions` | exact width x height |
| | | `media_blank`, `media_transparent` | luma standard deviation below 6, or fully transparent |
| Duplicate | `package` | `duplicate_package` | exact package sha256 exists |
| | `title` | `duplicate_title` | owner already has the same normalised title |
| | `media.thumbnail` | `similar_thumbnail` (Flag) | dHash Hamming distance <= 6 to an existing thumbnail |
| Upload quota | `account` | `upload_limit` | uploads per day: T0 3, T1 10, T2 30, T3+ unlimited |

Package validation (`IAssetKind.ValidateAsync`) is separate, see `asset-kinds.md`. Its errors are `Error.Validation` with a `package` field.

## Data sources

- `Application/Quality/Resources/english-words.txt.gz`: en_US hunspell dictionary expanded with `unmunch`, lowercase ASCII letters only (about 88k words).
- `tech-terms.txt` (allowlist) and `profanity.txt` (denylist) next to it.
- `IDuplicateLookup` and `IUploadQuota` are implemented against the database later; `AddContentPipeline` does not register them.

## Adding a gate

Implement `IContentGate`, register it in `AddContentPipeline`, add its limits to `ContentRulesOptions` and `QualityRulesDto`, and test it with positive and negative cases.

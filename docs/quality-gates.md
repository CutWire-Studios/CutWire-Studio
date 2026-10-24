# Quality gates

## Error shape

Gate failures are `422` problem+json with `code: "content_rejected"` and two views of the same failures:

```json
{
  "status": 422,
  "code": "content_rejected",
  "detail": "The submission does not meet the quality rules.",
  "errors": { "title": ["Title must be at least 6 characters."] },
  "violations": [
    { "field": "title", "code": "too_short", "message": "Title must be at least 6 characters.", "params": { "min": 6 } }
  ]
}
```

`errors` (field to messages) is the shape every validation problem already had, so existing clients keep working. `violations` is the stable, machine-readable list (`field`, `code` from the tables below, `message`, optional `params`); clients should branch on it. Package errors (`400`) carry a single `package` violation whose code is the package error code (see `asset-kinds.md`); non-gate validation problems may omit `violations`.

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

- `backend/tools/BloomGen/data/english-words.txt.gz`: en_US and en_GB hunspell dictionaries expanded with `unmunch`, lowercase ASCII letters only, merged and sorted (279,198 words). It is only the generator input and is not embedded. The generator prunes words that suffix normalisation already reaches from a shorter kept word (191,193 words remain, 229 KB filter) and self-checks that every original word still passes lookup-with-normalisation.
- `Application/Quality/Resources/english.bloom`: the embedded Bloom filter built from it (below). Regenerate with `dotnet run --project backend/tools/BloomGen`; a unit test fails when the embedded file differs from a fresh build.
- `tech-terms.txt` (allowlist) and `profanity.txt` (denylist) next to it. Both are served in `/api/v1/meta/quality-rules` (`text.techTerms`, `text.profanity`) so the client does not hard-code them, together with the link regex (`text.linkPattern`, case-insensitive) and the title run limits.
- `IDuplicateLookup` and `IUploadQuota` are implemented in `Infrastructure/Catalog`. Package, thumbnail and title duplicates only consider `published` and `hidden` items, so two drafts cannot block each other; the item being checked is excluded. The thumbnail check is `bit_count((dhash # $1)::bit(64)) <= 6` (a sequential scan, fine for thousands of items). The quota counts items the owner published in the last 24 hours, including ones since removed.

## English word list (Bloom filter)

Membership in the word list is probabilistic: no false negatives, about 1% false positives (measured 0.98% on 200,000 random non-words). Current build: 279,198 words, m = 2,678,336 bits, k = 7, 334,816 bytes.

File `english.bloom` (little endian):

| offset | size | field |
| --- | --- | --- |
| 0 | 8 | magic `CWBLOOM1` |
| 8 | 1 | format version (1) |
| 9 | 1 | k, hash count (7) |
| 10 | 2 | reserved (0) |
| 12 | 4 | m, bit count (multiple of 8) |
| 16 | 4 | seed (`0x00C0FFEE`) |
| 20 | 4 | word count |
| 24 | m/8 | bit array; bit i is `bytes[i >> 3] & (1 << (i & 7))` |

Algorithm, over the UTF-8 bytes of a lowercase word:

1. `h1 = FNV-1a-32(bytes)` with offset basis `0x811C9DC5 xor seed`, prime `0x01000193`.
2. `h2 = Murmur3-x86-32(bytes, seed) | 1`.
3. For i in 0..k-1: `position_i = (h1 + i * h2) mod m`, computed without 32-bit wrap (use 64-bit or doubles).
4. The word is present when all k bits are set.

`m` is the smallest multiple of 8 with `(1 - e^(-k*n/m))^k <= 0.01` for k = 7. The generator sorts and de-duplicates words, so output is deterministic.

Served at `/api/v1/meta/english-<first 8 hex of sha256>.bloom` (`application/octet-stream`, `Cache-Control: public, max-age=31536000, immutable`, `ETag` = full sha256). `quality-rules.englishWordList.url` carries the current URL.

Suffix normalisation (`EnglishGate.Candidates`, mirrored in `web/packages/content-rules`): try the word, then `-ies`/`-ied` to `-y` (length > 3), then for each suffix in `s es ed ing ly er est` (word length > suffix length + 2): the stem, and for `ed ing er est` also `stem + "e"` and, when the stem ends in a doubled letter (stem length > 2), the stem without one of them. The word is known when any candidate is in the filter.

`@cutwire/content-rules` mirrors the title, text, English, profanity and taxonomy gates from the quality-rules JSON. Its tests read the real `english.bloom` and fixtures written by backend unit tests (`web/packages/content-rules/test-fixtures`: hash vectors, suffix candidates, and `gate-cases.json` with the backend's violation codes per input); both sides must agree.

## Upload flow

`POST /items` (text gates, 422 with every violation) then `PUT /items/{id}/package` and `PUT /items/{id}/media/{role}` (kind validation and media gate at upload time), then `POST /items/{id}/publish` runs the full pipeline: any `Block` violation is a 422; `Flag` violations are stored in `item_flags` for the moderation queue and the item still publishes. Published items take `POST /items/{id}/versions` (semver must increase; `version_not_increased`, `duplicate_package`) and `PATCH /items/{id}` re-runs the metadata gates and the title duplicate check. Additional codes: `package_missing` (field `package`), `media_role_not_allowed` for an unknown role.

## Adding a gate

Implement `IContentGate`, register it in `AddContentPipeline`, add its limits to `ContentRulesOptions` and `QualityRulesDto`, and test it with positive and negative cases.

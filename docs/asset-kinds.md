# Asset kinds

An asset kind is an `IAssetKind` (`Application/Catalog`) registered in DI. Its data (display name, extensions, caps, categories, tag vocabulary, media specs, enabled flag) comes from the embedded `Infrastructure/Catalog/asset-kinds.json`; its code only validates the package.

| Kind | Input | Package cap (packed / unpacked) | Media |
| --- | --- | --- | --- |
| `effect` | `.driftfx` | 2 MB / 8 MB | thumbnail 512x288, 40 KB |
| `transition` | `.driftfx` | 2 MB / 8 MB | preview-strip 1536x128, 80 KB; thumbnail 512x288, 40 KB |
| `audio-effect` | `.driftfx` | 2 MB / 8 MB | thumbnail 512x288, 40 KB |
| `theme` | `.json` or `.zip` | 2 MB per entry, 8 MB total, 64 entries | screenshot 1280x720, 120 000 B; thumbnail 512x288, 40 KB |
| `look` | `.cube` | 4 MB | thumbnail 512x288, 40 KB |
| `effect-template` | `template.json` | 64 KB | thumbnail 512x288, 40 KB |
| `language-pack` | disabled | n/a | n/a |

All media are `image/webp` only and required. Categories come from Drift's bundled `category` values plus Forge's lists.

## Package error codes

Package errors are `Error.Validation` with the field `package`; oversized uploads are `package_too_large` (`TooLarge`).

`.driftfx` (`DriftfxReader`, `DriftfxKind`): `driftfx_too_short`, `driftfx_bad_magic`, `driftfx_unsupported_version`, `driftfx_header_damaged`, `driftfx_size_mismatch`, `driftfx_digest_mismatch`, `unpacked_too_large`, `payload_invalid`, `manifest_invalid`, `manifest_not_canonical`, `unsafe_path`, `file_table_invalid`, `file_digest_mismatch`, `wrong_kind`, `unsafe_layout`, `unexpected_file`, `missing_file`, `shader_version`, `param_invalid`, `param_type_not_allowed`.

Checks: magic `DRIFTFX\0`, format version 1, canonical sorted-key manifest, contiguous file table inside the payload, per-file sha256, trailing sha256, zstd decompressed into a buffer of the declared size (never larger than `MaxUnpackedBytes`), `provides[0].kind` matches the kind, a single `<root>/<id>/` folder, no `..`/absolute/backslash paths, referenced shaders and textures present, only `.json/.frag/.png/.jpg/.webp` (GPU) or `.json` plus `ir/*.wav` (audio, no code).

Theme: `theme_schema_invalid` (JSON Schema), `theme_name_required`, `theme_name_invalid` (reserved `dark light system graphite frost`), `theme_schema_version`, `theme_invalid`, `theme_too_large`, `qml_not_allowed`, `zip_unsafe_path`, `zip_symlink`, `zip_unexpected_entry`, `zip_too_many_entries`, `zip_entry_too_large`, `zip_too_large`, `zip_invalid`, `missing_file`.

Look: `lut_not_text`, `lut_size_invalid` (2-65), `lut_size_missing`, `lut_1d_unsupported`, `lut_unknown_keyword`, `lut_domain_invalid`, `lut_row_invalid`, `lut_row_count`.

Effect template: `template_invalid` (id, displayName, category, `sync` in beat/bar/onset/clip, layers or tracks, each `effectId`, optional `pulse` with `param`, `rest`, `peak`, `decayMs`).

Disabled kinds return `kind_disabled`.

## Adding a kind

1. Add an entry to `asset-kinds.json` (key is the kind id) and a `static readonly AssetKindId` in `Domain/Catalog/AssetKindId.cs` if it is well known.
2. Add a sealed class deriving from `AssetKindBase` (or `GpuDriftfxKind` / `DriftfxKind` for `.driftfx` packages) and implement `Validate(bytes, sha256)` returning a `PackageInfo`.
3. Register it with `services.AddSingleton<IAssetKind, YourKind>()` in `AddContentPipeline`.
4. Add fixtures and tests under `tests/CutWire.Studio.UnitTests`.

To ship the kind later without exposing it, set `"enabled": false`.

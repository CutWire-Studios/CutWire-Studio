## What and why

## Checklist

- [ ] Tests added or updated; `dotnet test` and `pnpm -r typecheck build test` pass
- [ ] No secrets, tokens or real credentials in code, config, fixtures or logs
- [ ] New input (request, upload, provider/yt-dlp output, URL) is validated, size-bounded and owner-scoped
- [ ] New outbound fetch goes through the SSRF-safe client; new subprocess uses `ArgumentList`
- [ ] Auth, CSP or header changes are described, and `docs/security.md` is updated if the model changed
- [ ] New dependency is justified, pinned via the lockfile, and has no open high/critical advisory

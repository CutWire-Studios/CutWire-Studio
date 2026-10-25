# NIST SSDF (SP 800-218 v1.1) mapping

Self-assessment for a solo/small-team open-source project. Status: **Met**, **Partial**, **Not met**, **N/A**. "Owner action" means a GitHub or infrastructure setting that cannot be stored in the repository. Practices that only make sense for larger organisations are marked Partial or N/A with the reason rather than padded with process. Detail lives in [security.md](security.md) and [../SECURITY.md](../SECURITY.md).

Workflows: `build` = `.github/workflows/build.yml`, `security` = `security.yml`, `codeql` = `codeql.yml`.

## PO: Prepare the Organization

| ID | Status | Evidence | Remaining gap / owner action |
| --- | --- | --- | --- |
| PO.1.1 Document security requirements for development infrastructure | Partial | security.md "Recommended GitHub settings", "Supply chain" | Owner applies the settings; not machine-enforced |
| PO.1.2 Document security requirements for software | Met | security.md "Security requirements", threat model, headers/CSP policy | |
| PO.1.3 Communicate requirements to third parties | N/A | No contracted suppliers; third-party code is consumed as dependencies (PW.4) | Revisit if contractors join |
| PO.2.1 Define roles and responsibilities | Partial | `.github/CODEOWNERS`, SECURITY.md (single maintainer triages) | Add a second maintainer to remove the single point of failure |
| PO.2.2 Provide role-based security training | Not met | | Maintainer self-study; not tracked. Disproportionate to record |
| PO.2.3 Obtain management commitment | N/A | Maintainer is the decision maker | |
| PO.3.1 Specify toolchain security requirements | Met | security.md "Supply chain" and "Release checklist" | |
| PO.3.2 Implement supporting toolchains | Met | `build`, `security`, `codeql`, Dependabot, SHA-pinned actions, least-privilege `permissions`, `persist-credentials: false`, concurrency, no `pull_request_target` | |
| PO.3.3 Configure toolchain to generate audit trail | Partial | GitHub Actions logs, PR history, build/provenance attestations | Enable branch protection so merges are reviewable; consider the organisation audit log |
| PO.4.1 Define criteria for checking security | Met | CI gates: analyzers as errors, high/critical advisory failures, Trivy HIGH/CRITICAL, gitleaks, CodeQL; release checklist | |
| PO.4.2 Gather and review information to support criteria | Met | CI results, SARIF in code scanning, CSP reports at `/api/v1/csp-report` | Owner reviews code scanning alerts |
| PO.5.1 Separate and protect environments | Partial | Dev uses `deploy/compose.dev.yml` and `Security:AllowInsecureDefaults`; production refuses dev defaults (`ProductionGuard`); CI has no production secrets except `ANSIBLE_DISPATCH_TOKEN` in one job | Deployment hardening lives in the private `cutwire-ansible` repo (not assessed) |
| PO.5.2 Secure and harden development endpoints | Not met | | Developer machines are outside the repo; owner enables 2FA, disk encryption, signed commits if wanted |

## PS: Protect the Software

| ID | Status | Evidence | Remaining gap / owner action |
| --- | --- | --- | --- |
| PS.1.1 Store code under least-privilege access control | Partial | CODEOWNERS; workflows use read-only token by default | Owner: branch protection/ruleset on `main`, 2FA for the organisation, minimal collaborator permissions |
| PS.2.1 Help consumers verify release integrity | Met | `build` image job: signed build-provenance attestation, BuildKit SBOM and provenance, digest pushed to GHCR; verify with `gh attestation verify` | No keyless cosign signature (attestation covers it) |
| PS.3.1 Archive and protect each release | Partial | Every build is an immutable `sha-<commit>` image in GHCR with provenance; git history | No tagged releases or release notes; no archive outside GHCR/GitHub. Owner: restrict package deletion |
| PS.3.2 Collect, maintain and share provenance data (SBOM) | Met | BuildKit SBOM attached to the image (`docker buildx imagetools inspect --format '{{ json .SBOM }}'`); lock files committed | |

## PW: Produce Well-Secured Software

| ID | Status | Evidence | Remaining gap / owner action |
| --- | --- | --- | --- |
| PW.1.1 Use risk modelling to inform design | Met | security.md threat model summary and per-component table | |
| PW.1.2 Track and maintain security requirements | Met | security.md "Security requirements" and findings table | |
| PW.1.3 Use secure design for reusable/third-party components | Partial | Architecture layering with architecture tests (`ArchitectureTests`), SSRF-safe client, sealed media URLs | Not formally reviewed per component |
| PW.2.1 Review the design | Partial | Security review and CSP pass (security.md); PR template security checklist | Solo review; no independent design review |
| PW.4.1 Acquire well-secured components | Met | Central package management, lock files, Dependabot, dependency review, advisory audits | |
| PW.4.2 Create well-secured in-house components for reuse | Met | Shared SSRF client, `ResultExtensions.SafeLocalPath`, security headers middleware, content gates, each with tests | |
| PW.4.4 Verify acquired components | Met | Docker digests, SHA-256 verification of yt-dlp and bgutil downloads, SHA-pinned actions, frozen/locked restores | Hashes for the two downloads are updated by hand with the version |
| PW.5.1 Follow secure coding practices | Met | Findings table, "reviewed and dismissed" list, `AnalysisModeSecurity=All` with warnings as errors (0 warnings in `src`), nullable enabled, `ArgumentList`, regex timeouts | |
| PW.6.1 Use compiler/build tools that support security | Met | .NET 10 SDK pinned (`global.json`), `Nullable`, `TreatWarningsAsErrors`, .NET analyzers (`Directory.Build.props`); TypeScript strict via `tsconfig.base.json`; oxlint | No ESLint security plugin (oxlint covers the lint stage; plugin was judged noisy) |
| PW.6.2 Configure to improve security | Met | Release single-file publish, non-root container, tini, `check:dist` blocks inline script and `eval` in bundles | |
| PW.7.1 Determine code review and analysis | Met | PR template, CODEOWNERS, CodeQL, analyzers, release checklist | |
| PW.7.2 Perform code review and analysis | Partial | CodeQL (C#, JS/TS) per PR and weekly; analyzers each build | No second human reviewer; owner enables required status checks |
| PW.8.1 Determine testing | Met | Unit, integration (Testcontainers Postgres), web tests, security regression tests per finding | |
| PW.8.2 Perform testing | Met | `build` runs web typecheck, lint, test, build and backend tests; `security` runs audits and secret scan; Trivy on the image | No DAST/fuzzing; CSP in-browser behaviour not exercised in CI |
| PW.9.1 Define secure default configuration | Met | `ProductionGuard` fails fast on missing seal key, ephemeral OpenIddict keys, dev DB password, wildcard `AllowedHosts`; secure cookies, headers and rate limits on by default | |
| PW.9.2 Implement default settings | Met | Same; documented in security.md "Production configuration"; forwarded-headers proxies configurable | Owner sets the production values in the deployment |

## RV: Respond to Vulnerabilities

| ID | Status | Evidence | Remaining gap / owner action |
| --- | --- | --- | --- |
| RV.1.1 Gather information on potential vulnerabilities | Met | SECURITY.md reporting channels, Dependabot alerts, `security` weekly audit, CodeQL | Owner enables private vulnerability reporting and Dependabot alerts |
| RV.1.2 Review, analyse and test software for vulnerabilities | Met | CodeQL, advisory audits, Trivy, security review in security.md | |
| RV.1.3 Have a policy for disclosure and remediation | Met | SECURITY.md (timelines, supported versions, coordinated disclosure) | |
| RV.2.1 Analyse each vulnerability | Met | SECURITY.md triage step; severity convention in security.md | |
| RV.2.2 Plan and implement risk responses | Met | Fix with regression test, deploy via CI, findings table tracks open items | Open items remain: security.md findings 14 and 15 |
| RV.3.1 Analyse root causes | Met | SECURITY.md step 4 | Process only; no incidents yet to exercise it |
| RV.3.2 Analyse root causes over time | Partial | Findings table is the history | No periodic review; revisit the table at each major change |
| RV.3.3 Review the SDLC for similar vulnerabilities | Partial | SECURITY.md step 4 (search for the same pattern, add a check or checklist line) | |
| RV.3.4 Review and improve the SDLC | Partial | This mapping and the release checklist | Re-assess yearly or after an incident |

## Summary

| Group | Met | Partial | Not met | N/A |
| --- | --- | --- | --- | --- |
| PO (13) | 5 | 4 | 2 | 2 |
| PS (4) | 2 | 2 | 0 | 0 |
| PW (16) | 13 | 3 | 0 | 0 |
| RV (9) | 6 | 3 | 0 | 0 |
| Total (42) | 26 | 12 | 2 | 2 |

## Owner actions (not possible from the repository)

1. Enable GitHub private vulnerability reporting (Settings, Security).
2. Enable Dependabot alerts and security updates, secret scanning and push protection.
3. Add branch protection or a ruleset on `main` with the required checks listed in security.md.
4. Enforce 2FA for the organisation; scope `ANSIBLE_DISPATCH_TOKEN` to dispatch on `cutwire-ansible` only, with an expiry.
5. Set read-only default workflow permissions and restrict package access in GHCR.
6. In the deployment set `Stock__Media__SealKey`, OpenIddict certificate paths, a non-default DB password, `AllowedHosts`, and `ForwardedHeaders__KnownProxies`/`KnownNetworks` for the real proxy or tunnel.
7. Private repository only: dependency review and code scanning need GitHub Advanced Security; if that is not available, remove those jobs.

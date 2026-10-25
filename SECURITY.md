# Security policy

## Supported versions

CutWire Studio is deployed continuously from `main`. Only the current `main` branch and the image published from it (`ghcr.io/cutwire-studios/cutwire-studio:latest`, tags `sha-<commit>`) receive security fixes. There are no maintained release branches.

## Reporting a vulnerability

Do not open a public issue.

1. Preferred: GitHub private vulnerability reporting, via the repository's **Security** tab, **Report a vulnerability**.
2. Fallback: email suhasdissa@gmail.com with "CutWire security" in the subject.

Include affected component and URL or commit, reproduction steps or a proof of concept, and the impact you see. Do not access other users' data, degrade the service, or run automated scanners at volume against the production site; test against a local instance (`deploy/compose.dev.yml`).

## What to expect

| Step | Target |
| --- | --- |
| Acknowledgement | within 3 business days |
| Triage (valid or not, severity, owner) | within 7 days |
| Fix or mitigation for high/critical | within 30 days |
| Fix for medium/low | next regular release, usually within 90 days |
| Disclosure | after the fix is deployed, coordinated with the reporter, at most 90 days after the report |

Reporters are credited in the advisory unless they ask not to be. This is a volunteer project: there is no bug bounty.

## How reports are handled

1. **Triage (RV.1/RV.2).** Reproduce, rate severity (impact in this deployment, as in `docs/security.md`), and open a private GitHub Security Advisory with a temporary private fork if code changes are needed.
2. **Fix.** Add a regression test, fix on a private branch, merge to `main`. CI runs CodeQL, dependency, secret and image scans; the deploy dispatch ships the fix.
3. **Disclose.** Publish the advisory (request a CVE through GitHub when the issue affects others), and add the finding to the table in `docs/security.md`.
4. **Root cause (RV.3).** For every medium-or-higher issue, record in the advisory or `docs/security.md` why it was possible (missing requirement, missed review, no test) and the systemic fix: a new analyzer or CI check, a test pattern, or a checklist line in `.github/pull_request_template.md`. Search the codebase for the same pattern before closing.

Dependency advisories are normally handled by Dependabot pull requests and the weekly `security` workflow; they follow the same timelines by severity.

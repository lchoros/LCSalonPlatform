# Salon Platform — AI agent bootstrap kit

Drop these files into an empty GitHub repo, then follow the steps.

## What's here
| Path | Purpose |
| --- | --- |
| `CLAUDE.md` | Rules every agent reads first: stack, architecture, definition of done, autonomy tiers, hard limits |
| `.claude/agents/*.md` | Role subagents: product-analyst, architect, builder, tester, ux-frontend, reviewer, security, release-manager |
| `.claude/settings.json` | Local permissions: agents can't merge, push to main, touch CI/CLAUDE.md, or read secrets |
| `.github/workflows/ci.yml` | Format, build, unit/arch/integration/E2E tests, tier check, CodeQL, gitleaks, staging deploy |
| `.github/workflows/agent-build.yml` | Issue labelled `agent:ready` → Orchestrator plans and opens PRs |
| `.github/workflows/agent-review.yml` | 4 independent reviewers → commit statuses; automatic fix rounds (max 3) |
| `.github/workflows/merge-gate.yml` | Auto-merges Tier 0/1 when the right checks pass; Tier 2 waits for you |
| `.github/workflows/agent-digest.yml` | Evening digest issue |
| `scripts/tier-check.sh` | Path rules that force the minimum tier; 600-line PR limit |
| `KICKOFF_PROMPT.md` | Paste into Claude Code to start M0 |
| `docs/PLAN.md`, `docs/adr/`, `docs/glossary.md` | Shared memory for agents |

Also add the system design doc as `docs/system-design.md` (export the Doc as Markdown).

## Phase 1: local only
No Docker, no hosting, no real payments or SMS. Install the .NET 10 SDK, PostgreSQL 17 (create `salon_dev` and `salon_test`)
and Mailpit (single exe). Payments go through a Manual provider that only records them. GitHub is used for PRs and CI only.

## Run locally
1. Install the .NET 10 SDK (`global.json` pins 10.0.4xx), PostgreSQL 17 and [Mailpit](https://mailpit.axllent.org/) (single exe).
2. Create the databases once:
   ```sql
   CREATE DATABASE salon_dev;
   CREATE DATABASE salon_test;
   ```
   `appsettings.Development.json` expects `postgres/postgres` on `localhost:5432`. Override with user secrets if yours differ:
   `dotnet user-secrets set "ConnectionStrings:Default" "Host=...;Database=salon_dev;Username=...;Password=..." -p src/Presentation/Salon.Web`.
   Tests read `ConnectionStrings__Test` (env var) and fall back to `salon_test` with the same defaults.
3. Download the Tailwind 4 standalone CLI into `tools/` (see `tools/README.md`) and build the stylesheet:
   `./tools/tailwindcss -i src/Presentation/Salon.Web/Styles/app.css -o src/Presentation/Salon.Web/wwwroot/css/app.css --minify`
4. Start Mailpit (`mailpit`, inbox at http://localhost:8025, SMTP on 1025) and the app: `dotnet watch --project src/Presentation/Salon.Web`.
5. Tests: `dotnet test tests/Salon.Tests.Unit`, `...Architecture`, `...Integration`, then
   `pwsh tests/Salon.Tests.E2E/bin/Debug/net10.0/playwright.ps1 install chromium` once and `dotnet test tests/Salon.Tests.E2E`
   (screenshots land in `tests/Salon.Tests.E2E/screenshots/`).
6. Migrations: `dotnet ef migrations add <Name> -p src/Libraries/Salon.Data -s src/Presentation/Salon.Web` (the `dotnet-ef` tool is in `.config/dotnet-tools.json`; run `dotnet tool restore` first).

## Solution layout
See CLAUDE.md §Architecture. `Salon.slnx` holds `src/Libraries` (Core, Data, Services), `src/Presentation` (Web.Framework, Web),
`src/Plugins` (Payments.Manual, Sms.Log) and `tests/` (Unit, Integration, Architecture, E2E). `Directory.Build.props` turns on
nullable, warnings-as-errors and the recommended analyzers; `Directory.Packages.props` pins every package version centrally.

## Setup (about 30 minutes)
1. Create the repo; push this kit to `main`.
2. Secrets: `ANTHROPIC_API_KEY`; `MERGE_BOT_TOKEN` (fine-grained PAT or a GitHub App with contents + pull requests write).
   Use a token rather than GITHUB_TOKEN for merges so the push to `main` triggers CI (and, from M3, the deploy).
3. Repo variable: `AGENTS_ENABLED=true` (set to `false` any time to stop all agents).
4. Settings → General: enable "Allow auto-merge" and "Automatically delete head branches".
   Settings → Actions: allow GitHub Actions to create pull requests.
5. Branch protection on `main`: require PRs, require status checks `build-test`, `tier-check`, `security-scan`,
   `agent/merge-gate`; require linear history; block force pushes. Keep yourself as admin.
6. Labels: `agent:ready`, `agent:stop`, `needs-owner`, `tier:0`, `tier:1`, `tier:2`, `digest`.
7. Install the Claude GitHub App on the repo (needed by `anthropics/claude-code-action`), and check the action's
   README for the current input names before the first run, since they can change between versions.
8. Locally: `claude` in the repo root, paste `KICKOFF_PROMPT.md`. Or open an issue "M0 walking skeleton" and label it `agent:ready`.

## Your daily loop
- Evening: read the digest issue. Approve or close anything labelled `needs-owner`.
- Comment on any ADR you disagree with; agents treat "owner may veto" decisions as reversible.
- Feed salon feedback into an issue and label it for the product-analyst, or just write it in `docs/product/`.

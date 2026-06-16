# Dependency updates — follow-up

Audited 2026-06-12. All four frontends (foundation, saas, community, marketing) and both backends
(saas, foundation) checked. Updates grouped by effort.

## Tier 1 — safe, do now

Mechanical patch/minor bumps, no breaking changes. Apply across all repos in one pass.

**Frontend (foundation, saas, community)**
- All `@radix-ui/*` — patch bumps
- `react` / `react-dom` 19.2.6 → 19.2.7
- `@tanstack/react-query` 5.100.x → 5.101.0
- `@tanstack/react-virtual` 3.13.x → 3.14.2 ← **priority**: scrolling/overscan improvements relevant to utilization grid
- `react-router-dom` 7.16.0 → 7.17.0
- `xstate` 5.30–31 → 5.32.0
- `zustand` 5.0.12–13 → 5.0.14
- `date-fns` 4.1.0–4.3.0 → 4.4.0
- `vite` 8.0.14 → 8.0.16
- `vitest` + `@vitest/coverage-v8` 4.1.7 → 4.1.8
- `tailwind-merge` 3.5.0 → 3.6.0 (saas, community)
- `typescript-eslint` 8.59.4 → 8.61.0
- `@types/node` 25.6.0 → 25.9.3
- `@types/react` 19.2.15 → 19.2.17
- `happy-dom` 20.9.0 → 20.10.2

**`lucide-react` version drift — fix in the same pass**
SaaS is at 1.8.0, Community 1.11.0, Foundation 1.16.0; all should be 1.17.0. Icons added
in later minors silently break in the lagging repos.

**Backend (saas + foundation)**
- `Microsoft.*` extensions 10.0.7 → 10.0.9
- `MailKit` 4.16.0 → 4.17.0
- ~~`FluentAssertions` 8.9.0 → 8.10.0~~ **DONE 2026-06-15: replaced with `AwesomeAssertions` 9.4.0** (OSS fork). FluentAssertions v8 is commercial-paid; Orkyo dependencies must never use commercial licenses. Namespace renamed `FluentAssertions` → `AwesomeAssertions` across saas + foundation + community test projects.
- `StackExchange.Redis` 2.12.14 → 2.13.17
- `Testcontainers.PostgreSql` 4.11.0 → 4.12.0
- `CsvHelper` 33.0.1 → 33.1.0
- `Microsoft.NET.Test.Sdk` 18.5.1 → 18.6.0

## Tier 2 — major version, low-medium risk

Run lint / smoke-test after each.

| Package | Jump | Notes |
|---|---|---|
| `eslint-plugin-react-hooks` | 5 → 7 | New rules may surface warnings |
| `eslint` | 9 → 10 | Already on flat config; minimal migration |
| `Swashbuckle.AspNetCore` | 9 → 10 | OpenAPI UI config changed; verify `/swagger` after deploy |
| `Serilog.Sinks.Grafana.Loki` | 8 → 9 | Spot-check Grafana Loki ingestion after deploy |
| `coverlet.collector` | 6 → 10 | Dev-only; no prod impact |

## Tier 3 — major version, needs migration work

### `react-day-picker` v9 → v10
`selected`/`onSelect`/`mode` API restructured. Audit all date picker usages across all three
frontends before bumping.

### `FluentValidation` v11 → v12
Currently pinned to `11.*`. v12 has breaking changes in `IRuleBuilder` extension chains and
validator inheritance. Audit all validators.

### `Npgsql` v9 → v10
**Hold until PostgreSQL is upgraded to 17.** Npgsql 10 targets PG17 and drops several v9 APIs;
the stack runs PG16.

## Tier 4 — standalone migration project

### Tailwind CSS v3 → v4 (saas, community)
Full rewrite: `tailwind.config.js` moves to CSS `@theme`, `@apply` semantics changed, new
content/purge model. Meaningful gains (smaller output, faster builds). Plan as a dedicated
half-day task per app — do not bundle with Tier 1.

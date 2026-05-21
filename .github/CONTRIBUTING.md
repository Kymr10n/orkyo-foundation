# Contributing to orkyo-foundation

`orkyo-foundation` is the shared domain layer consumed by `orkyo-saas` and `orkyo-community`. Changes here ripple to both products, so we hold it to a higher standard than product-specific code.

## Before You Start

**Check the placement rule first.**
New code belongs here only if it has an analogue in *both* SaaS and Community. Multi-tenancy or a `tenantId` parameter alone is not a reason to keep code in SaaS — the `OrgContext` abstraction lets the same code work in both editions. When in doubt, open an issue to discuss before writing code.

## Development Setup

```bash
./setup.sh        # one-time: install tools, git hooks
dotnet build      # verify everything compiles
dotnet test backend/tests/Orkyo.Foundation.Tests.csproj
```

## Making Changes

### Public API

The public API surface must stay backward-compatible within a major version. If you are adding to the public API:

- Additive changes (new types, new optional parameters): bump the **minor** version.
- Breaking changes (removed or renamed symbols, changed signatures): bump the **major** version and open coordinated downstream PRs in `orkyo-saas` and `orkyo-community` in the same batch.

Run the downstream test script before opening a PR for any public API change:

```bash
./scripts/test-downstream.sh
```

### Migrations

- Every new migration file under `backend/migrations-foundation/sql/` **must** carry a `-- @migration-class:` header (`expand`, `contract`, `data`, or `none`).
- Applied migrations are **immutable**. Never edit a file that has been merged to `main`. If a migration has a bug, write a follow-up migration.

### Frontend

The npm package (`@kymr10n/foundation`) is published from `frontend/`. See [`frontend/ARCHITECTURE.md`](../frontend/ARCHITECTURE.md) for the rendering split and routing conventions.

## Pull Request Checklist

Use the PR template — it covers the key checks. The short version:

- [ ] Placement check: belongs in foundation (analogue in both products).
- [ ] No runtime wiring added (`Program.cs`, DI registrations, middleware).
- [ ] Public API change documented and version bump included if needed.
- [ ] Tests added or updated. `dotnet test` passes locally.
- [ ] `dotnet format` passes (enforced by the pre-push hook).
- [ ] Migration header present if a migration was added.

## Running Tests

```bash
# Foundation tests only
dotnet test backend/tests/Orkyo.Foundation.Tests.csproj

# Full downstream suite (Foundation + Community + SaaS)
./scripts/test-downstream.sh
```

## Questions

Open an issue or start a discussion. For architectural questions about where code belongs, include a brief description of what the code does in both SaaS and Community contexts.

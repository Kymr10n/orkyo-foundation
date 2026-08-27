# Conventions

When the same problem is solved two ways in this repo, this file says which way wins.

Nothing here is worth a sweep of its own. Fix a divergence when you are already editing
the file for another reason — a pull request that only renames variables costs review
attention and buys nothing. The exceptions are the items already swept, marked **done**;
those should stay swept.

This file exists because a 2026-08 review found the same problem solved differently in
sibling files often enough that new code had no way to tell which sibling to copy.

## Data access

**Opening a connection.** Primary constructor, parameter named `connectionFactory`, local
named `conn`:

```csharp
public class ThingRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
{
    await using var conn = connectionFactory.CreateOrgConnection(orgContext);
```

Both `db` and `conn` are in use (~200 sites, roughly evenly split), as are explicit
`_`-prefixed fields alongside primary constructors. `conn` and the primary constructor win.

**Upserts** use `EXCLUDED`, not the parameter:

```sql
ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value
```

`value = @value` reads as if it might be a different value than the one being inserted.
**Done** in the settings repositories, where both spellings lived in one file.

**Inserting a row that a concurrent request might also insert** — users by email, above
all — uses `ON CONFLICT ... DO NOTHING` with `RETURNING id`, then re-reads on the empty
result. `UserProvisioningService.CreateAsync` is the reference. A bare INSERT here means
the loser of a race gets an exception instead of the winner's row.

**Paging** goes through `NpgsqlQueryExtensions.QueryPagedAsync` and returns
`PagedResult<T>`. Two repositories hand-roll the count/page/read loop and return a tuple;
they are the exception, not a second convention.

**Binding a nullable parameter** uses `AddNullable`, not `x.HasValue ? x.Value : DBNull.Value`.

**Transactions** name the variable `tx`. Rely on `await using` disposal for rollback;
call `RollbackAsync` explicitly only where the catch does something else as well.

## Errors

**"No such resource"** throws `NotFoundException`. Not `KeyNotFoundException`, not a
string-matched `InvalidOperationException`. **Done** — and the global handler no longer
maps `KeyNotFoundException` at all, so a stray dictionary miss surfaces as the 500 it is
rather than a 404 that hides it.

**Building an error response** goes through `ProblemResults.Problem` (usually via
`ErrorResponses.*`). Every response carries `code` and the `application/problem+json`
content type; the frontend switches on `code`. **Done** — `AuthProblemDetails` and the
reporting-token 402's hand-rolled `{error, message}` body were the last two exceptions.

**Wire error codes** come from `ApiErrorCodes` (lowercase snake). `ErrorCodes` (SCREAMING
snake, plus one PascalCase outlier) predates it and is still referenced; prefer
`ApiErrorCodes` for anything new.

**Exception mapping is global.** `AppExceptionHandler` owns exception→HTTP. An endpoint
that catches `ArgumentException` to return a 400 is duplicating it.

## Endpoints

Reach for the helper before writing the shape by hand — all in `EndpointHelpers`:

| Situation | Helper |
|---|---|
| value or 404 | `OkOrNotFound` |
| deleted/updated, or 404 | `NoContentOrNotFound` |
| validation failed | `ValidationFailed` |
| validate then handle | `ExecuteAsync` |

**The audit actor** is `principal.UserIdOrNull`, not `UserId == Guid.Empty ? null : UserId`.

**Bare `Results.NotFound()`** (empty body) is only for deliberately hiding whether
something exists — the anonymous calendar-feed routes. Everywhere else uses
`ErrorResponses.NotFound` so the body carries a `code`.

## Naming

**Reads are `Get`.** `Fetch`, `Load` and `Find` all appear; one class uses `Fetch` and
`Load` for the same kind of operation. Async methods end in `Async`.

**`I`-prefixed means an interface.** `ICalendarWriter` is a static class and should be
renamed when it is next touched.

**Hooks live in `hooks/`** (frontend). Two currently live beside their components.

## Validation

**Length limits come from `DomainLimits`.** Roughly fifty validator sites still use bare
numbers, some of them the same value as a constant that already exists. Adopt on touch;
add the constant if it is missing.

**Shared patterns live in `ValidationPatterns` / `ResourceTypeKeyRules`.** Anchor with
`\A` and `\z`, never `^` and `$`: in .NET `$` also matches before a trailing newline, so
`"#ffffff\n"` passes a `$`-anchored hex check. **Done** for the hex-colour and identifier
patterns.

**Create/update validator pairs** share their rules through a common base — see
`SiteRequestValidator`. Criterion and several others still restate the rules in both.

**Status values come from constants**, not SQL literals: `MembershipStatusConstants`,
`UserStatusConstants`, `RoleConstants`, `AssignmentStatuses`. About twenty SQL strings
still hardcode `'active'` and `'admin'`, nine hardcode `'keycloak'`, and conflict severity
is written as bare `"error"`/`"warning"` beside a `Kind` that correctly uses constants.

## Configuration

**No fallback values — fail early.** Required config goes through
`ConfigurationExtensions.GetRequired*` (or `DeploymentConfig`'s `Require`), which throws
at startup; optional config through `GetOptionalString`/`IsSet`. There is deliberately no
`GetValueOrDefault(key, fallback)` helper: `configuration[key] ?? fallback` substitutes
only for *null*, and the deploy pipeline writes `KEY=` for every unset key, so an empty
string sails past `??` and silently replaces the intended value — the BFF cookie-name
bug. Defaults live in env templates, never in compiled code.
`ConventionContractTests.NoSourceFile_FallsBackOnARawConfigRead` enforces this.

## Frontend dialogs

**Viewer gating has no mechanical guard.** Editing controls inside a dialog are gated on
`canEdit` (or the equivalent permission flag); two dialogs shipped without it in 2026-08.
eslint cannot express "a `DialogFooter` whose actions lack a `canEdit` gate", so there is
no lint rule — the protection is that the raw dialog-shell import ban pushes new dialogs
through `FormDialog`/`ScaffoldDialog`, which embed the gate. When you write a dialog that
bypasses those shells, checking the gate is on you.

## Types

**A patch field that can be cleared uses `Optional<T>`.** Plain `T?` plus `SetIfNotNull`
cannot express "set this to null" — the field silently cannot be cleared, which is the
bug `Optional<T>` exists to prevent. It currently has exactly one consumer
(`Resource.HomeSiteId`) while several other clearable ids use the plain form.

**Interfaces earn their existence.** A single implementation that nothing mocks and no
composition layer swaps is ceremony. Note the counter-example before deleting one:
`IAdminAuditService` has a single implementation here and is injected by orkyo-saas, so
it is a real seam. Deleting a public interface from this repo is a breaking change for
both consuming products.

## Layering

**Services do not write SQL.** Eight of them do, some with a dozen raw `NpgsqlCommand`s;
those are repositories wearing service names. Move the query into a repository when you
next touch it.

## Foundation is a package

orkyo-saas and orkyo-community consume this repo as NuGet and npm packages. Anything
`public` is API: an export with no callers *here* may still be load-bearing there, and
deleting it breaks the consumer at its next pin bump rather than in this repo's CI. Grep
all three trees before removing a public symbol. The `downstream-compile` job in
`template-sync-check.yml` builds both consumers' backends against the current foundation
checkout weekly, so a backend break surfaces the following Monday at the latest; the npm
side has no equivalent (consumers resolve the published package), so frontend exports
still rely on the grep.

# Input & domain validation

Two validation mechanisms coexist in the backend **by design**. They are not redundant — they
validate different things at different layers. Use this guide to decide which one a new rule belongs
in, so the split stays intentional rather than ad hoc.

## 1. Request-shape validation — FluentValidation

**What:** structural/format rules on an incoming request DTO — required fields, lengths, ranges,
enum membership, mutually-exclusive fields, regex. Rules that can be decided from the request alone,
without touching the database.

**Where:** an `AbstractValidator<TRequest>` in
[`backend/core/Validators/`](../backend/core/Validators/) (e.g. `CreateRequestRequestValidator`,
`CreateSiteRequestValidator`). All validators in that assembly are auto-registered
(`AddValidatorsFromAssemblyContaining<…>` in `FoundationServiceExtensions`).

**How it runs:** at the transport boundary, via the endpoint helper —

```csharp
group.MapPost("/", async (CreateCriterionRequest request, IValidator<CreateCriterionRequest> validator, …) =>
    await EndpointHelpers.ExecuteAsync(request, validator, async () => { … }));
```

`EndpointHelpers.ExecuteAsync(request, validator, handler)` runs the validator first and returns
`Results.ValidationProblem(...)` (RFC-9457 problem details) on failure, so the handler only sees a
shape-valid request. Endpoints do **not** call `validator.Validate()` themselves.

## 2. Cross-entity / domain validation — service validators

**What:** business rules that depend on other data or current state — availability/overbooking,
capability matching, conflicting assignments, tenant-setting interdependencies. These need DB reads
and often must return *more* than pass/fail (a list of blockers vs. soft warnings the UI renders
differently).

**Where:** a dedicated validator service in
[`backend/core/Services/`](../backend/core/Services/) behind an interface (e.g.
`IResourceAssignmentValidator`, `TenantSettingsValidator`) returning a domain `ValidationResult`
(`Blockers` + `Warnings`), **not** an `AbstractValidator<T>`.

**How it runs:** invoked *inside* the owning service (e.g. `ResourceAssignmentService` calls
`IResourceAssignmentValidator.ValidateAsync`), which decides whether a blocker becomes a hard failure
or a soft-block. It is part of the business operation, not a transport-boundary filter.

## Choosing

- Can the rule be decided from the request body alone? → **FluentValidation** (mechanism 1).
- Does it need the database, other entities, or a richer blocker/warning result? → **service
  validator** (mechanism 2).
- Don't put cross-entity rules in a FluentValidator (it would need data access at the boundary), and
  don't re-check pure shape inside a service (the boundary already guaranteed it).

Avoid a third style: ad-hoc `if (...) return ErrorResponses.BadRequest(...)` scattered in handlers.
A one-line guard on a raw query-string parameter (e.g. a missing `?token=`) is fine; anything richer
belongs in one of the two mechanisms above. Error responses, when you do return them directly, go
through [`ErrorResponses`](../backend/src/Helpers/ErrorResponses.cs) for a consistent shape.

# Copilot Instructions

These instructions are mandatory for this repository.

## Product Architecture

- `orkyo-foundation` is the shared feature core for Orkyo products.
- It should contain shared domain building blocks, reusable technical components, cross-product contracts, and reusable feature behavior used by both `orkyo-saas` and `orkyo-community`.
- `orkyo-saas` and `orkyo-community` are composition layers; do not push model-specific composition concerns down into foundation.

## What Belongs Here

- Shared feature logic used by more than one product composition.
- Shared contracts, result models, reason taxonomies, domain policies, validators, and reusable services/hooks.
- Reusable technical components that are not tied to one app shell or hosting model.

## What Must Stay Out

- Multi-tenant-specific composition and SaaS-only operational flows.
- Standalone packaging, bootstrap, or community-specific operational glue.
- Deployment automation and infrastructure wiring.
- Product-shell-specific pages, routes, and adapters unless they are truly generic.

## Engineering Guidance

- Foundation should be rich enough to prevent SaaS/Community duplication.
- Prefer extracting shared behavior here rather than duplicating it across composition repos.
- Keep dependencies environment-agnostic and avoid hidden coupling to infra or hosted-only runtime assumptions.
- Local development must not depend on private package feeds unless explicitly requested for release workflows.

## Documentation Language

Applies to `docs/` and `frontend/docs/`. **`requirements/` is out of scope** (historical spec
packs). Never apply these rules to marketing copy or brand writing.

When you write technical text (documentation, READMEs, runbooks, procedures, error messages,
release notes, reports), obey these rules from ASD-STE100 Simplified Technical English:

CLASSIFY FIRST. Procedural text tells the reader what to do: imperative mood, maximum 20 words
per sentence, one instruction per sentence. Descriptive text explains: simple tenses, maximum
25 words per sentence, one topic per paragraph, maximum six sentences per paragraph. Never mix
the two in one passage. Everything in `orkyo-foundation/docs/` is descriptive.

VERBS. Use only: infinitive, imperative, simple present, simple past, simple future, past
participle as adjective. No present perfect ("has completed" → "completed"). No "-ing" verb
forms ("making it easy" → new sentence). Active voice; passive only in descriptions when the
agent is unknown. Approved modals: can, will, must. Banned: should, would, may, might, could.
For "should": write "must" if required, delete if optional.

SENTENCES. Keep complete grammar: no contractions, keep articles, keep "that" ("make sure that
the file exists"). Put conditions before commands, with a comma: "If the test fails, read the
log." No semicolons — write two sentences. Use a vertical list for more than two items.

WORDS. One word, one meaning, for the whole document: pick one of check/verify/confirm and
keep it. Noun chains of maximum three words; break longer ones with prepositions ("the timeout
value for the connection pool"). Delete words that carry no fact: simply, seamlessly, robust,
powerful, comprehensive, leverage, "in order to", "it is worth noting". Replace: utilize →
use, prior to → before, in the event that → if, e.g. → for example. American spelling.

WARNINGS. Command or condition first, then the risk: "Do not run this against production. The
command deletes rows."

NEVER TOUCH. Code blocks, identifiers, CLI commands, file paths, quoted error messages,
product names, UI labels. Each counts as one word toward sentence limits.

SELF-CHECK before returning: scan for contractions, "has been", "should", ", making",
semicolons. Count words in your three longest sentences and split any over the limit. Collapse
synonym rotation.

The Orkyo term list — which word to use for each concept — is in
`orkyo-documentation/docs/LANGUAGE-STANDARD.md`. Read it before renaming anything.

## Authorization

- Roles: **Viewer** (read core content, no Settings or Administration area), **Editor** (read + write Settings and all general content, no Administration area), **Admin** (everything). See [`docs/authorization.md`](../docs/authorization.md) before touching endpoints.
- Every tenant endpoint group declares one convention at its `MapGroup` (`RequireMemberReadEditorWrite` / `RequireMemberReadAdminWrite` / `RequireAdminArea`); non-mutating POSTs use `.AllowMemberWrite()`. A conformance test fails CI on any ungated write.
- Frontend: gate write affordances with `useCanEdit()`; edit-dialog Save buttons are disabled when `!canEdit`. Route segments use `RequireEditor` (for `/settings`) and `RequireTenantAdmin` (for `/tenant-admin`); corresponding nav links are hidden via `useCanEdit()` / `useIsTenantAdmin()` in `SidebarNav`.

## Dialog & mutation feedback

- Don't hand-roll `toast.*` / `invalidateQueries` in a dialog mutation. Declare `meta: { successMessage, errorMessage?, invalidates }` on `useMutation`; the central `MutationCache` (`query-client.ts`) fires the toast + cache invalidation. Keep inline `ErrorAlert` for in-context errors. Full-CRUD entities use `createCrudHooks` (`entityLabel`). See [`docs/dialog-feedback.md`](../docs/dialog-feedback.md).

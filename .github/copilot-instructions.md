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

---
name: Feature request / placement proposal
about: Propose new shared behaviour or suggest moving existing code into foundation
title: "feat: "
labels: enhancement
assignees: ''
---

## Placement check

> Code belongs in `orkyo-foundation` only if it has an analogue in **both** `orkyo-saas` and `orkyo-community`.

- [ ] This behaviour is needed in both products.
- [ ] Multi-tenancy is not the only reason it is currently outside foundation.

## What you want to add or move

<!-- Describe the feature or the existing code you want to extract into foundation. -->

## Why it belongs here

<!-- Explain the SaaS use case and the Community use case. -->

## Proposed API sketch (optional)

```csharp
// Interface, method signatures, or types
```

## Alternatives considered

<!-- Any alternative approaches or reasons not to extract this. -->

## Downstream impact

- [ ] No public-API change
- [ ] Additive change (minor version bump)
- [ ] Breaking change (major version bump + coordinated downstream PRs)

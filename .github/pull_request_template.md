## Summary
<!-- 1–3 bullets describing what changed and why. Focus on the why; the diff shows the what. -->

## Type of change
- [ ] Backend (Orkyo.Foundation, Orkyo.Shared)
- [ ] Migration framework (abstractions / runtime / migrations-foundation)
- [ ] Frontend (@kymr10n/foundation)
- [ ] Keycloak image / theme
- [ ] CI/CD / packaging
- [ ] Documentation only

## Downstream impact
<!-- This package is consumed by orkyo-saas and orkyo-community. Any public API change ripples to both. -->
- [ ] No public-API change
- [ ] Public-API change — backward compatible (additive)
- [ ] Public-API change — breaking (requires major version bump + coordinated downstream PRs)

## Placement check
- [ ] Confirms the change belongs in foundation (has analogue in both SaaS and Community)
- [ ] Not applicable

## Test plan
- [ ] Unit tests added / updated
- [ ] Integration tests (Testcontainers) pass locally
- [ ] Verified consuming repo build still passes (saas / community)

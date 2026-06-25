# Optional PR Template Addition

Add this section to the repository PR template after the Claude audit has been completed.

```md
## DRY / KISS / Tenant Safety Check

- [ ] This PR does not introduce a second way to solve an existing problem.
- [ ] This PR reuses existing UI and backend patterns where appropriate.
- [ ] Any new abstraction is justified by repeated current use, not speculative future use.
- [ ] Tenant isolation has been considered and validated.
- [ ] Loading, empty, error, and permission states follow existing product patterns.
- [ ] Dependencies were not added unless necessary.
- [ ] Dead or replaced code was removed.

### Complexity Impact

Describe briefly whether this PR increases, reduces, or preserves complexity.

### Tenant Isolation Validation

Describe how tenant isolation was checked, or why this PR does not affect it.
```

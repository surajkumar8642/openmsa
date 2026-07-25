# ADR-0002: Stateless Request Authorization

## Decision

Do not issue long-lived authorization grants.

## Consequences

- Every API call revalidates token and context.
- Authorization decisions cannot be inferred from stale session state.

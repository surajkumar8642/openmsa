# ADR-0001: Managed-Space Architecture in .NET

## Decision

Use a gateway-first modular architecture with explicit core packages and immutable identifiers.

## Consequences

- Easier boundary control and testability.
- Simpler evolution to alternative adapters (e.g., PostgreSQL index, S3 storage).

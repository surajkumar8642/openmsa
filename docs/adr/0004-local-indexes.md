# ADR-0004: Local Indexes

## Decision

Store indexed resource lookup fields in an internal index and avoid full-folder scans.

## Consequences

- Predictable query performance.
- Explicit indexing contracts for supported search fields.

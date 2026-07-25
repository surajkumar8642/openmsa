# ADR-0005: Write-Only Inbox

## Decision

Inbox must support deposits without allowing depositor enumeration or reads.

## Consequences

- Depositor workflows can push documents without being able to infer prior state.
- Owners keep stronger control over inbound visibility.

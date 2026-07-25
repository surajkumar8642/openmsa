# ADR-0003: Asymmetric JWT Signing

## Decision

Use RSA-based asymmetric signing for JWTs (RS256).

## Consequences

- Only public keys are deployed to gateways.
- Private signing key is local-only and never published.

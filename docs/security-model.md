# Security Model

- **Stateless authorization**: no long-lived authorization sessions.
- **JWT security**: asymmetric RS256 signing and key verification by gateway.
- **No folder enumeration**: all operations go through sectioned gateways.
- **Generic responses**: avoid revealing discovery details.
- **Hardened IDs and hashes**:
  - Opaque IDs from random URL-safe bytes
  - HMAC keyed hash for mobile-like identifiers

Failure modes should be logged as structured audit events without including sensitive tokens or credentials.

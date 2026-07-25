# Threat Model (Condensed)

## Threats

- Token tampering / replay
- Policy bypass
- Index probing and enumeration
- Path traversal / unsafe object ids
- Overly broad policy that leaks data

## Controls

- signature/claim validation on every request
- deny-by-default policy semantics
- claim comparison limited to approved envelope fields
- object id allowlist pattern for storage operations
- generic denied responses

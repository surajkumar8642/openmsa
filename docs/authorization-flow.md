# Authorization Flow

1. Request arrives with Bearer token.
2. Gateway validates JWT signature, issuer, audience, expiry and claims.
3. Gateway resolves `spaceRef` → internal `spaceId`.
4. Gateway checks operation against section capabilities.
5. Gateway loads policy and evaluates against subject and resource claims.
6. Gateway executes index lookup and fetches storage object if allowed.
7. Audit event is recorded.

All failures can be reduced into generic `not found`/`forbidden` responses.

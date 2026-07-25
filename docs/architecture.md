# OpenMSA Architecture

OpenMSA separates concerns into:

- **Identity Service** – issues signed access tokens and publishes public keys.
- **Space Resolver** – maps public aliases / IDs to opaque space IDs.
- **Gateway** – the only path for reads/writes.
- **Policy Engine** – evaluates local and global policy for each request.
- **Index Adapter** – fast lookup by section + claim fields.
- **Storage Adapter** – stores envelope objects; gateways never expose raw storage paths.

The gateway validates each request independently and can return a generic not-found style result when authorization fails.

### Principle: Global contract first

Global contracts define:

- fixed sections and allowed operations,
- required metadata,
- mandatory policy restrictions.

Owners may define local policy and manifest details only inside allowed bounds.

# Core Concepts

- **Managed Space**: A security boundary with fixed logical sections and owned policies.
- **Space Manifest**: Space metadata and allowed operations.
- **Space Resolver**: Resolves public references to opaque space IDs.
- **Identity Claim**: Signed token fields such as `sub`, `mobile_verified`, `mobile_hash`.
- **Resource Envelope**: Signed/trusted wrapper around resource payload metadata.
- **Write-Only Inbox**: accepts deposits but does not support listing/read by depositors.

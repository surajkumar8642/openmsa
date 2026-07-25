# Write-Only Inbox

Supported rules:

- deposits are accepted from authenticated users with deposit capability,
- depositors can store references but cannot enumerate existing inbox items,
- depositors cannot read previously deposited resources,
- owners and local authorized identities can read inbox resources.

The inbox behavior prevents metadata mining from write-only external flows.

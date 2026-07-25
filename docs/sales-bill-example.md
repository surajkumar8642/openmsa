# Sales-Bill Example

Example:

1. Supplier creates a resource in `salesBills`.
2. Gateway stores a canonical envelope and index entries for receiver references.
3. Supplier deposits a lightweight inbox reference into receiver space inbox.
4. Receiver authenticates and queries `receiverMobileHash`.
5. Gateway returns summaries only; full envelopes are returned by a second authorized read request.

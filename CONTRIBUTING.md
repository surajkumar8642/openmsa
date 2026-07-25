# Contributing to OpenMSA

Thanks for your interest in improving OpenMSA.

## Scope

This repository contains a .NET 9 proof-of-concept for a governed decentralized managed-space architecture. We value small, reviewable changes.

## Development

```bash
dotnet restore
dotnet build
dotnet test
dotnet format
```

## Branch and PR expectations

- Keep PRs focused and small.
- Add/adjust tests for changed behavior.
- Include explicit security or compatibility rationale for policy/auth changes.
- Do not submit secrets, credentials, private keys, or raw tokens.

## Reporting issues

- Use templates under `.github/ISSUE_TEMPLATE`.
- Include reproducible steps and expected behavior.
- Include endpoint/body examples when reporting API issues.

## Code style

- Use explicit types where helpful.
- Avoid broad exceptions.
- Keep methods small and testable.
- Use existing abstractions in `src/OpenMSA.*` and avoid direct platform-specific assumptions.

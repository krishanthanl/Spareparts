# Phase 0 execution record

Plan: Plan/Phase0-Implementation-plan.md. Approved by user.

- Ruling: reuse the approved current checkout on phase0-foundation; no additional worktree needed for the new empty repository.
- Ruling: SDK/cache files live under ignored .local because global template/cache writes are restricted.
- Ruling: no fresh-review subagent because the approved plan explicitly requests no delegation; perform a whole-change self-review.
- Docker is not on PATH. PostgreSQL integration tests will remain explicitly skipped until an engine or disposable database is available; no production database will be contacted.
- Visual foundation: navy #18354a, steel #536b7a, blue #225c9a, cloud #f4f7fa, white #ffffff. Segoe UI/system typography; left-aligned layouts with generous working space. Admin uses MUI navigation; public uses a restrained automotive wordmark and simple responsive information layout. No fake business figures, catalogue, login or checkout.

## Backend evidence
- Test-first: foundation tests initially failed because required domain/time/persistence types were absent; API tests failed before API configuration types existed.
- 18 backend unit/API tests passed with the local test host allowed to communicate. Restricted execution cannot connect to its test host; this is an environment limitation, not a successful test run.
- PostgreSQL integration project compiles; one real-database test is skipped without explicit Docker/disposable-database configuration.
- Ruling: pin EF Core/Relational 10.0.15 explicitly to avoid mismatched transitive patches from the provider and design package.
- Ruling: use npm 11.6.2 locally for installation after npm 10.9.0 crashed in peer resolution; pin compatible tooling rather than change globally installed Node/npm.

# KICKOFF — StripKit

The canonical kickoff prompt lives in **[`docs/KICKOFF.md`](docs/KICKOFF.md)** — open
that and paste the prompt into a fresh session.

Fast orientation for a new session:

1. Read `CLAUDE.md`, then `docs/SOURCE_MAP.md` and `docs/ARCHITECTURE.md`.
2. Read `docs/HANDOFF.md` for the current state and the next task.
3. Establish the baseline: `dotnet build StripKit.sln -c Debug` (expect 0/0) and
   `dotnet test` (expect 49/49).

> This file is intentionally a pointer so there is a single source of truth for the
> kickoff prompt (`docs/KICKOFF.md`) and the two cannot drift apart.

# Contributing

Thanks for taking the time to improve signalr-scale-lab.

## Prerequisites

- .NET SDK 10.0.400 or later (`global.json` rolls forward to the latest feature band)
- Docker (integration tests start Redis through Testcontainers; `docker compose up` runs the demo and the benchmarks)

## Workflow

1. Open an issue first for anything larger than a small fix, so we can agree on the approach.
2. Branch from `main` and keep each pull request to one concern.
3. Before pushing:

   ```bash
   dotnet build                 # must be warning-free: warnings are errors
   dotnet test                  # unit, property, golden and integration tests (Docker running)
   dotnet format --verify-no-changes
   docker compose up --build    # the demo must end with "allAsExpected":true
   ```

## Commit messages

[Conventional Commits](https://www.conventionalcommits.org/) are required. They keep the history readable, make the
hand-written CHANGELOG easy to assemble at release time, and show which version bump a change needs:

- `feat:` new capability (minor bump)
- `fix:` bug fix (patch bump)
- `feat!:` or a `BREAKING CHANGE:` footer (major bump)
- `docs:`, `test:`, `build:`, `ci:`, `refactor:`, `perf:`, `chore:` for everything else

## Changing the charts or the report table

The approved outputs for a fixed synthetic data set live in `tests/ScaleLab.UnitTests/Golden/`. Regenerate them with
`UPDATE_GOLDEN=1 dotnet test --project tests/ScaleLab.UnitTests` and review the diff like code. Then regenerate the
published report from the committed measurements:

```bash
dotnet run -c Release --project src/ScaleLab.LoadGen -- report --env "$(cat benchmark-results/environment.txt)"
```

CI regenerates the report the same way and fails if `docs/images` or `benchmark-results/summary.md` differ from what
is committed.

## Changing a measurement

Re-run the matrix (`bench/run-matrix.sh`, about 25 minutes), commit the new run files in `benchmark-results/runs/`,
regenerate the report, and update `docs/benchmarks.md` and the README with the new numbers and the host load at the
time. Never edit a number by hand. If a change makes a scenario worse, say so in the pull request.

## Dependencies

Versions are pinned in `Directory.Packages.props` and updated by hand in reviewed commits. CI fails on any package
with a known vulnerability (`dotnet list package --vulnerable --include-transitive`); fix it with a pinned upgrade,
including transitive pins where needed.

## Architecture decisions

A change to a decision recorded in `docs/adr/` needs a new ADR that supersedes the old one. Do not edit the old ADR in
place. Use the existing files as the format (MADR: context, options considered, decision, consequences).

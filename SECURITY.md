# Security policy

## Supported versions

Only the latest release receives fixes.

## Reporting a vulnerability

Please **do not open a public issue**. Use GitHub's private vulnerability reporting instead:
**Security -> Report a vulnerability** on this repository. You can expect an acknowledgement within 5 working days. If
the report is confirmed, a fix or mitigation plan follows within 30 days.

Please include the affected version or commit, steps to reproduce and the impact you observed.

## Security-relevant design

See [docs/threat-model.md](docs/threat-model.md) for assets, threats and mitigations. In short:

| Area | Default |
|---|---|
| Broadcast and stats API | Requires `X-Lab-Key` (constant-time comparison) when a key is configured; compose always configures one |
| Payloads | Broadcast payload capped at 4,096 bytes; invalid sizes and unknown hubs are rejected |
| Redis backplane | Only on the compose network, channels prefixed; the backplane is not authenticated in the demo (known gap) |
| Affinity cookie | HashCookie (destination hash, no address), HttpOnly, SameSite=Lax |
| Containers | Chiseled .NET images, non-root user, no shell; compose binds published ports to 127.0.0.1; memory limits per container |
| Dependencies | Pinned versions, CI fails on known-vulnerable packages, CodeQL |
| Secrets | None in the repository. `.env.example` holds a clearly marked development-only key |
| Known gaps | Hubs accept anonymous connections; Redis has no password or TLS in the demo. Both are fine for a local lab and wrong for production |

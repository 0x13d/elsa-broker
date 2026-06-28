# ElsaBroker — agile process

> **Start a session here:** [`KICKOFF.md`](KICKOFF.md) — the start-here ritual (points to the canonical portfolio ritual).

Lightweight agile for an AI-coding team. The **project-manager** agent owns this folder. It exists so
work is **visible, versioned, prioritized, and durable across sessions**.

## The shape of it

| Concept | Maps to | Lives in |
|---------|---------|----------|
| **Sprint** | one Claude session (the ~5h usage window) | `sprints/<version>.md` (the kanban) |
| **Version** | a shippable increment / request (SemVer) | named everywhere as `vMAJOR.MINOR.PATCH` |
| **Release** | what actually shipped in a version | `release/<version>.md` → feeds `CHANGELOG.md` |
| **Backlog** | groomed, deferred work | `backlog/README.md` |

## Semantic versioning

Every shippable request gets a version. The PM proposes the bump; the **dotnet-architect confirms MAJOR**.

- **MAJOR** — breaks a public contract: `ISubmitRequest`/`RequestStatus`, the message shape, the
  broker↔Elsa dispatch/callback contract, a published package's public API, or the `workflows/`
  convention.
- **MINOR** — backwards-compatible feature (new request type support, new module, new option).
- **PATCH** — bug fix / docs / internal change, no surface change.

Pre-1.0 caveat: we're at `0.x`. Anything is technically allowed at MINOR, but we follow the intent —
**MINOR for features, PATCH for fixes** — so the contract hardens cleanly at `1.0.0`.

## The kanban

One file per version under `sprints/`. Columns, in order: **Backlog → To Do → In Progress → Review →
Done** (plus **Blocked** when needed). Card format:

```
- [ ] `EB-007` (M · dotnet-architect) Short title — one-line note  ← In Progress
```

`EB-###` is a stable task id (monotonic). Owner is a team agent. Size is **S / M / L**. Each team member
updates their own cards; the PM keeps the board coherent and checkpoints after every move.

## Sprint lifecycle

1. **Open.** PM creates `sprints/<version>.md` from `_template.md`, **rolls incomplete tasks forward**
   from the previous sprint, grooms the backlog, and **proposes** the work items + version bump for
   approval.
2. **Run.** Move cards across the board; keep WIP low. Defer scope-creep prompts to the backlog with a
   one-line rationale. Checkpoint the file after each state change.
3. **Ship & close.** When Done is accepted: write `release/<version>.md`, update `CHANGELOG.md` from it,
   mark the sprint closed, **roll incomplete tasks to the next sprint**, and record velocity.
4. **Post-mortem.** The PM runs a brief retro with the team (stored in the `### Post-mortem` section of
   the sprint file). Three questions: *What went well?*, *What could improve?*, *Process changes to
   adopt.* Each agent that owned a card contributes a bullet. Process changes are applied immediately
   (update this README, templates, or agent instructions).
5. **Next sprint.** The PM proposes the next sprint — informed by the post-mortem's process changes,
   the velocity trend, and the now-unblocked backlog. Steps 3–5 are a single automatic sequence:
   **close → post-mortem → propose next sprint.**

## Velocity & session limits

A sprint is time-boxed by the session, not by a fixed scope. **The usage limit cannot be detected
programmatically** — there is no signal to poll for tokens remaining or the 5h reset. We therefore rely
on **durable state**: the kanban is checkpointed after every task, so a mid-sprint cutoff is fully
recoverable by the next session. Track completed S/M/L per sprint in the release report to size the next
one. If the harness ever surfaces a context/usage warning, the PM checkpoints, writes a handoff note at
the bottom of the sprint file, and stops cleanly.

## Definition of Done

A card is Done when: code builds (`dotnet build`, 0 warnings) and tests pass; docs/ADRs updated if
behavior or decisions changed; the relevant `CHANGELOG.md` entry is drafted; and the owner has noted the
outcome on the card. Shipping a version additionally requires a `release/<version>.md` and a closed
kanban.

## Files

- `sprints/_template.md` — kanban template.
- `release/_template.md` — release-report template.
- `backlog/README.md` — the prioritized backlog.

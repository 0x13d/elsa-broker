# KICKOFF — elsa-broker (`EB-`)

**Start here.** Follow the canonical portfolio ritual —
[`software-team/agile/KICKOFF.md`](../../../../../.claude/reference/software-team/agile/KICKOFF.md) (reconcile
with `make safe-check` → orient on board + [`backlog/README.md`](backlog/README.md) → open a sprint → build &
verify each card → checkpoint). Live state lives in [`sprints/`](sprints/), the backlog, and
[`SAFe_Report.md`](../../../../../SAFe_Report.md) — not here.

**This team:** a .NET 9 mTLS durable-queueing front-end for Elsa 3 workflows. Task prefix **`EB-`**.
Toolchain in `~/.dotnet`; DocFX site → `dist/elsa-broker`. **Verify a card:** `dotnet build` · `dotnet test`
· `scripts/trust-report.sh` · DocFX renders. Use `-beta` pre-release tags in-progress; reserve a clean
`1.0.0` for the public NuGet launch. Publishing (NuGet / GitHub Pages) is blocked on EPIC-010 secrets.

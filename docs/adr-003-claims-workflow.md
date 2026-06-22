# ADR-003 — Claims adjudication workflow: in-process vs Durable Functions

**Status:** Accepted · **Context:** Phase 3 execution on an Azure *learner* sandbox
**Related:** ADR-001 (AI/ML substitutions), ADR-002 (compute & messaging)

---

## Context

The LLD models claims adjudication (`Filed → Triaged → Fraud-scored → Approved/Referred → Paid`)
as an **Azure Durable Functions orchestration** (LLD A.1.3.2) — a durable, replayable saga decoupled
from the synchronous FNOL path.

## Decision

Ship **both** drivers behind one seam (`IAdjudicationTrigger`), selected by config; run
**in-process** in the sandbox/demo and keep **Durable Functions** as the production-faithful option.

```
OutboxDispatcher (after ClaimFiled) → IAdjudicationTrigger.TriggerAsync(claimId)
   Claims:Adjudication:Mode = InProcess        → InProcessAdjudicationTrigger        (runs AdjudicationService in the claims container)
   Claims:Adjudication:Mode = DurableFunctions → DurableFunctionsAdjudicationTrigger (HTTP → Function App orchestration)
                              + Claims:Adjudication:FunctionsBaseUrl
```
The **saga logic is identical** either way — both invoke the same `AdjudicationService` steps; only the
*host* differs. (Code: `Claims.Infrastructure/DependencyInjection.cs`; orchestration in `Claims.Functions/AdjudicationFunctions.cs`.)

## Why in-process for the sandbox / demo
- **Fewer moving parts.** No separate Function App + storage account to provision, secure, and keep
  in sync with the services.
- **No cold-start lag.** Consumption-plan Functions cold-start ~5–10s; the first claim on stage would
  look slow. The claims container is kept warm (min-replicas=1), so adjudication is instant.
- **Same behaviour to demonstrate.** The state machine, fraud call, payment call, and status
  transitions are exactly the same — the demo shows the full workflow regardless of host.

## Why Durable Functions in production
- **Durable orchestration state + replay** — survives restarts; the orchestration history is queryable.
- **Built-in retries / timers / fan-out** for the activity steps.
- **Decoupling + independent scale** off the claims API hot path.

## How to switch (no code change)
1. `infra/deploy-functions.ps1` — provisions the Function App + storage and zip-deploys `Claims.Functions`.
2. Set on the claims app: `Claims__Adjudication__Mode=DurableFunctions` and
   `Claims__Adjudication__FunctionsBaseUrl=https://<fn-host>`.

The orchestrator calls back into the services through the **public gateway** (the services use
internal ingress), so its `Claims/Fraud/Payments` URLs are the gateway URL.

## Talking point
Same pattern as ADR-001/002: a stable interface (`IAdjudicationTrigger`) with a constraint-appropriate
default and a documented, config-only path to the architecture's target. The design is Durable-Functions
native; the sandbox runs the in-process equivalent for reliability and zero standing cost.

See also: `infra/RECREATE-ALL.md` (Phase 7), `infra/CLOUD-SHELL.md`.

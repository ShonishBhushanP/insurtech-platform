# ADR-002 — Compute (Container Apps vs AKS) & messaging (Service Bus, not Dapr)

**Status:** Accepted · **Context:** Phase 3 execution on an Azure *learner* sandbox
**Related:** ADR-001 (AI/ML substitutions)

---

## 1. Compute — Azure Container Apps instead of AKS

The deployment diagram names **AKS**. We ran **Azure Container Apps** as a faithful stand-in.

### Why
1. **RBAC we don't have.** The account is **Contributor**, which by design lacks
   **`Microsoft.Authorization/roleAssignments/write`** (granting access is reserved for Owner / User
   Access Administrator). AKS needs role assignments for nearly everything useful:
   - ACR image pull → **AcrPull** on the registry (`--attach-acr`)
   - Workload Identity (pods → SQL / Service Bus / Key Vault) → federated credential + data-plane roles
   - Custom VNet / Azure CNI → **Network Contributor** on the subnet
   - Add-ons (Container Insights, Key Vault CSI, App Gateway ingress) → various roles

   A bare cluster might provision, but **every integration this platform needs requires role
   assignments we can't create** — so we'd fall back to key/admin-cred workarounds anyway, plus pay
   for the cluster.
2. **Idle cost.** AKS runs node-pool VMs **24/7** (standing charge even when idle). Container Apps
   **scales to zero** and is consumption-billed — ~₹0 between demos. Pairs with our budget posture.
3. **Operational overhead + provisioning time.** AKS = managing nodes, upgrades, ingress, CNI;
   ~10–15 min cluster spin-up that's fragile against the **~15-min sandbox token expiry**. Container
   Apps is fully managed and deploys fast.

### Why this is faithful
Container Apps **is** managed Kubernetes — AKS + KEDA (autoscale) + Envoy (ingress) + optional Dapr,
under the hood. We keep the architecture's containerized-microservices model (independent services,
revisions, autoscaling, a gateway in front) and shed the cluster-ops/standing-cost. Designing the
apps **RBAC-free** (ACR **admin credentials** for pull; **connection strings/keys** instead of
Managed Identity) is what let them run under Contributor-only.

### When AKS *is* the right call (production)
Large scale; node-level control; a service mesh; custom operators/CRDs; multi-tenant isolation;
GPU/specialised node pools. The swap is a redeploy of the same containers — the app code is identical.

---

## 2. Messaging — Service Bus (via connection string), not Dapr

The LLD specifies **Azure Service Bus + the transactional outbox** pattern. We did **not** adopt
Dapr (even though Container Apps offers it) — Dapr is not in the architecture, and adding it would be
a *deviation*, not a move toward fidelity. It's also an abstraction *on top of* a broker, so it
doesn't remove the need for one.

### What's implemented
- A transport-agnostic `IEventBus` seam with two implementations: `InMemoryEventBus` (default) and
  `ServiceBusEventBus` (real Azure Service Bus), selected by config.
- The **transactional outbox**: domain events are persisted in the same DB transaction as the
  aggregate; a background `OutboxDispatcher` tails undispatched rows and **publishes them to the bus**
  (best-effort — a publish failure never blocks the adjudication saga), then stamps `DispatchedUtc`.

### The RBAC-free twist
`ServiceBusEventBus` normally authenticates with **Managed Identity** — which needs the role
assignment we can't create (§1). So the bus registration now also accepts a **SAS connection
string** (`Azure:ServiceBus:ConnectionString`): creating a namespace and reading its connection
string only needs **Contributor**, so **real Service Bus runs on the sandbox without any RBAC**.

Selection order: `ConnectionString` (sandbox) → `FullyQualifiedNamespace` + Managed Identity
(production-secure) → `InMemoryEventBus` (offline default).

### Tier note
Topics (pub/sub, as the architecture intends) require **Standard** (small hourly base cost — delete
the namespace after the demo). A near-zero-cost alternative is **Basic + a queue** of the same name
(point-to-point; no code change since the sender API is identical), at the loss of pub/sub fan-out.

See also: ADR-001, `docs/ai-pipelines.md`.

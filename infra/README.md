# InsurTech — Azure Infrastructure (Bicep)

Provisions the platform from the Azure deployment diagram and deploys the services to it. The
app code is **config-switchable**: with no Azure config it runs fully local (InMemory / in-process);
when these resources' endpoints are injected as env vars, the Azure adapters activate
automatically — no code change.

## What gets deployed

| Diagram element | This IaC provisions | File |
|---|---|---|
| **Edge** — Front Door + WAF | Azure Front Door Standard + WAF policy *(optional)* | `edge.bicep` |
| **API Plane** — API Management | YARP gateway Container App (public ingress) — APIM stand-in¹ | `apps.bicep` |
| **Compute** — AKS (Microservices) | Azure **Container Apps** (managed, serverless) — AKS stand-in¹ | `apps.bicep` |
| **Compute** — Durable Functions | in-process adjudication saga in the Claims app¹ | (app code) |
| **Data Tier** — SQL MI | Azure **SQL Database** (Policy/Claims/Fraud) — MI stand-in¹ | `platform.bicep` |
| **Data Tier** — Cosmos DB | Cosmos (serverless): docs-metadata + read-model containers | `platform.bicep` |
| **Data Tier** — Blob | Storage account: docs-staging / immutable / archive | `platform.bicep` |
| **Data Tier** — Redis | Azure Cache for Redis (Basic) | `platform.bicep` |
| **Shared Platform** — Service Bus | Service Bus namespace + `claims-events` / `fraud-events` topics | `platform.bicep` |
| **Shared Platform** — Key Vault | Key Vault (RBAC) | `platform.bicep` |
| **Shared Platform** — Monitor | Log Analytics + Application Insights | `platform.bicep` |
| **Shared Platform** — Sentinel | enable on the Log Analytics workspace² | (portal step) |
| Identity / RBAC | one user-assigned identity + role assignments (KV, SB, Blob, Cosmos, ACR) | `platform.bicep` |

¹ **Pragmatic substitution** to keep the deploy fast and inexpensive while preserving the
architecture. The diagram's production targets (AKS, SQL MI, APIM Premium, Durable Functions) are
drop-in: the services are containers, the data adapters are standard SQL/Cosmos/Blob clients, and
the saga is already isolated behind an interface.
² Microsoft Sentinel is enabled on the workspace via `az sentinel` / portal (no Bicep resource needed).

## Prerequisites

- Azure CLI (`az login`) with **Contributor + User Access Administrator** on the target subscription
  (the deploy creates role assignments).
- Bicep (bundled with recent Azure CLI).

## Deploy

```powershell
# 1. set your SQL Entra admin in platform.bicepparam:
az ad signed-in-user show --query id -o tsv              # -> sqlAadAdminObjectId
az ad signed-in-user show --query userPrincipalName -o tsv  # -> sqlAadAdminLogin

# 2. one command does platform → image build/push → apps:
./deploy.ps1 -ResourceGroup rg-insurtech -Location centralindia
# prints the public Gateway URL when done.
```

### Post-deploy: grant the app identity SQL access (AAD)
Azure SQL can't be granted DB roles from Bicep. Connect to **each** database as the Entra admin
(e.g. via `sqlcmd -G` or the portal query editor) and run:

```sql
CREATE USER [insurtech-id] FROM EXTERNAL PROVIDER;   -- the managed identity name
ALTER ROLE db_datareader ADD MEMBER [insurtech-id];
ALTER ROLE db_datawriter ADD MEMBER [insurtech-id];
ALTER ROLE db_ddladmin   ADD MEMBER [insurtech-id];  -- EnsureCreated needs DDL on first run
```

### Optional edge (Front Door + WAF)
```powershell
$fqdn = az containerapp show -g rg-insurtech -n insurtech-gateway --query properties.configuration.ingress.fqdn -o tsv
az deployment group create -g rg-insurtech -f edge.bicep -p gatewayFqdn=$fqdn
```

### Point the frontend at Azure
Set `frontend/.env` → `VITE_API_BASE=https://<gateway-or-frontdoor-host>` and rebuild.

## How the app activates Azure (config switches)

Each adapter turns on when its setting is present (injected as env vars by `apps.bicep`):

| Setting (env var form) | Activates |
|---|---|
| `ConnectionStrings__PolicyDb` / `ClaimsDb` / `FraudDb` | EF Core → Azure SQL (else InMemory) |
| `Azure__ServiceBus__FullyQualifiedNamespace` | `IEventBus` → Service Bus (else in-process) |
| `Azure__Redis__ConnectionString` | `IIdempotencyStore` → Redis (else in-memory) |
| `Azure__Storage__BlobServiceUri` | Document upload URLs → Blob user-delegation SAS (else loopback) |
| `Azure__Cosmos__Endpoint` | Document metadata → Cosmos (else in-memory) |
| `Azure__KeyVault__Uri` | Key Vault configuration source |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Azure Monitor / OpenTelemetry traces+metrics+logs |
| `Fraud__Aml__Endpoint` (+ `Key`) | Fraud scoring → Azure ML endpoint (else local heuristic) |
| `AZURE_CLIENT_ID` | binds `DefaultAzureCredential` to the user-assigned identity |

All Azure access is via **Managed Identity** (`DefaultAzureCredential`) — no secrets in config.

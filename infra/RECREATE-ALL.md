# Recreate the whole InsurTech deployment from scratch

Use this after a resource cleanup wiped the Azure resources. Run in **Azure Cloud Shell**
(switch shell per the notes — the `deploy-backend.ps1` step is PowerShell; the `az` blocks work in
either). Everything is parameterised at the top.

> Order matters: SQL → SWA (to get its hostname) → backend → frontend content → linked backend →
> (optional) AI/messaging. The Container Apps environment gets a **new random domain** on recreate;
> all commands fetch the gateway FQDN dynamically, and the UI talks same-origin via the linked
> backend, so the changed domain doesn't matter.

```bash
RG=rg-azuser7069_mml.local-yyRMB
LOC=centralindia
PREFIX=insurtech
SWA=insurtech-ui-swa
REPO=ShonishBhushanP/insurtech-platform
SQL_PWD='<choose-a-strong-password>'        # 12+ chars: upper/lower/digit/symbol
ANTHROPIC_KEY='<sk-ant-... or leave blank>'  # for Claude OCR + fraud analysis (optional)
```

---

## Phase 0 — Check what survived

```bash
az group show -n $RG -o table 2>/dev/null || echo ">>> RESOURCE GROUP IS GONE <<<"
az resource list -g $RG -o table
```
- If the **resource group still exists** (most likely — cleanup usually deletes contents), continue.
- If the **RG is gone**, try `az group create -n $RG -l $LOC`. If that's denied (learner subs are
  often scoped to a pre-created RG), you'll need the sandbox re-provisioned — stop here and tell me.

---

## Phase 1 — Azure SQL  *(az; either shell)*

```bash
cd ~/insurtech-platform 2>/dev/null || (git clone https://github.com/$REPO.git && cd insurtech-platform)
git pull

az deployment group create -g $RG -f infra/sandbox-sql.bicep \
  -p sqlAdminPassword="$SQL_PWD" namePrefix=$PREFIX

# Capture the connection string for the backend
SQL_FQDN=$(az sql server list -g $RG --query "[0].fullyQualifiedDomainName" -o tsv)
SQL_CS="Server=tcp:${SQL_FQDN},1433;Database=insurtech;User ID=insurtechadmin;Password=${SQL_PWD};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
echo "$SQL_CS"
```

---

## Phase 2 — Static Web App (create early to get its hostname)  *(az; either shell)*

```bash
az staticwebapp create -n $SWA -g $RG -l eastasia --sku Standard
SWA_HOST=$(az staticwebapp show -n $SWA -g $RG --query defaultHostname -o tsv)
echo "SWA = https://$SWA_HOST"
```
*(Static Web Apps regions are limited to: `centralus, eastus2, westus2, westeurope, eastasia`.
`eastasia` is closest to a `centralindia` backend; the linked backend works cross-region.)*

---

## Phase 3 — Backend → Container Apps  *(PowerShell Cloud Shell)*

Switch Cloud Shell to **PowerShell**, then:
```powershell
cd ~/insurtech-platform/infra
./deploy-backend.ps1 -ResourceGroup rg-azuser7069_mml.local-yyRMB `
  -SqlConnectionString "<paste the $SQL_CS value from Phase 1>" `
  -FrontendOrigin "https://<paste $SWA_HOST>"
```
This recreates the ACR, builds all 10 images server-side (~15–25 min, resumable), and deploys the
Container Apps environment + Log Analytics + 9 services + gateway. It prints the **gateway URL** at
the end.

---

## Phase 4 — Deploy the frontend content  *(Bash)*

```bash
# Give the GitHub Action the new SWA deployment token, then trigger a build
TOKEN=$(az staticwebapp secrets list -n $SWA -g $RG --query properties.apiKey -o tsv)
gh secret set AZURE_STATIC_WEB_APPS_API_TOKEN -b "$TOKEN" -R $REPO   # or set it in GitHub UI
gh workflow run "azure-static-web-apps.yml" -R $REPO                 # or push any commit
```
The workflow builds with `VITE_API_BASE=/api` (same-origin). Wait for it to go green.

---

## Phase 5 — Same-origin proxy (Option 3) + gateway hardening  *(Bash)*

```bash
# Link the gateway as the SWA backend
GW=$(az containerapp show -g $RG -n ${PREFIX}-gateway --query id -o tsv)
az staticwebapp backends link -n $SWA -g $RG --backend-resource-id "$GW" --backend-region $LOC

# Linking auto-enables EasyAuth on the gateway — turn it off (app does its own auth)
az containerapp auth update -g $RG -n ${PREFIX}-gateway --enabled false

# Keep the gateway + core services warm (avoid cold-start timeouts in the demo)
for s in gateway policy claims fraud payments documents; do
  az containerapp update -g $RG -n ${PREFIX}-$s --min-replicas 1 -o none
done

# Verify the chain
G=$(az containerapp show -g $RG -n ${PREFIX}-gateway --query "properties.configuration.ingress.fqdn" -o tsv)
echo "gateway /v1 : $(curl -s -o /dev/null -w '%{http_code}' "https://$G/v1/policies?userId=usr_8b2")"
echo "gateway /api: $(curl -s -o /dev/null -w '%{http_code}' "https://$G/api/v1/policies?userId=usr_8b2")"
echo "SWA /api    : $(curl -s -o /dev/null -w '%{http_code}' "https://$SWA_HOST/api/v1/policies?userId=usr_8b2")"
```
All three should be **200**.

---

## Phase 6 — Optional: the Azure-native AI/messaging extras

### 6a. Claude OCR + fraud analysis (needs `$ANTHROPIC_KEY`)
```bash
for app in ${PREFIX}-documents ${PREFIX}-fraud; do
  az containerapp secret set -g $RG -n $app --secrets anthropic-key="$ANTHROPIC_KEY"
  az containerapp update -g $RG -n $app \
    --set-env-vars Llm__Provider=Claude Llm__Anthropic__ApiKey=secretref:anthropic-key -o none
done
```

### 6b. Azure Document Intelligence (F0) — overrides Claude for OCR
```bash
az cognitiveservices account create -n ${PREFIX}-docintel -g $RG -l $LOC --kind FormRecognizer --sku F0 --yes
EP=$(az cognitiveservices account show -n ${PREFIX}-docintel -g $RG --query properties.endpoint -o tsv)
KEY=$(az cognitiveservices account keys list -n ${PREFIX}-docintel -g $RG --query key1 -o tsv)
az containerapp secret set -g $RG -n ${PREFIX}-documents --secrets docintel-key="$KEY"
az containerapp update -g $RG -n ${PREFIX}-documents \
  --set-env-vars Azure__DocIntel__Endpoint="$EP" Azure__DocIntel__Key=secretref:docintel-key -o none
```

### 6c. Real Service Bus (outbox transport) — Standard tier (delete after demo)
```bash
SB=insurtechsb$RANDOM
az servicebus namespace create -g $RG -n $SB -l $LOC --sku Standard
az servicebus topic create -g $RG --namespace-name $SB -n claims-events
az servicebus topic subscription create -g $RG --namespace-name $SB --topic-name claims-events -n all
CS=$(az servicebus namespace authorization-rule keys list -g $RG --namespace-name $SB -n RootManageSharedAccessKey --query primaryConnectionString -o tsv)
az containerapp secret set -g $RG -n ${PREFIX}-claims --secrets servicebus-cs="$CS"
az containerapp update -g $RG -n ${PREFIX}-claims \
  --set-env-vars Azure__ServiceBus__ConnectionString=secretref:servicebus-cs Azure__ServiceBus__Topic=claims-events -o none
```

### 6d. App Insights (workspace-based, no RBAC)
```bash
az extension add -n application-insights 2>/dev/null || true
LAW=$(az monitor log-analytics workspace show -g $RG -n ${PREFIX}-law --query id -o tsv)
az monitor app-insights component create -g $RG -a ${PREFIX}-appins -l $LOC --workspace "$LAW" --application-type web
AI_CS=$(az monitor app-insights component show -g $RG -a ${PREFIX}-appins --query connectionString -o tsv)
for app in gateway policy claims fraud documents payments partner underwriting notification audit; do
  az containerapp update -g $RG -n ${PREFIX}-$app --set-env-vars APPLICATIONINSIGHTS_CONNECTION_STRING="$AI_CS" -o none
done
```

---

## Teardown after the demo (avoid standing cost)
```bash
az servicebus namespace delete -g $RG -n $SB          # Standard SB base cost
az cognitiveservices account delete -n ${PREFIX}-docintel -g $RG
# Container Apps scale to zero on their own; or set --min-replicas 0 to drop them fully.
```

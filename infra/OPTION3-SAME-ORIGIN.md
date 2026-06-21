# Option 3 — Same-origin API proxy (bypass the corporate-proxy block)

## Why
The corporate proxy (Zscaler) **blocks the browser's calls to `*.azurecontainerapps.io`** (the API
domain) but **allows `*.azurestaticapps.net`** (the SWA loads fine; the API hangs). Proven: the app
works on a phone hotspot, fails on the corporate network.

Fix: route the API **through the SWA's own origin** using an Azure Static Web Apps **linked backend**,
so the browser only ever talks to `red-water-0a9d64f00.7.azurestaticapps.net` and never the
Container Apps domain. The SWA reverse-proxies `/api/*` to the gateway; the gateway strips the
`/api` prefix and routes to the domain services.

## Code already changed (pushed to main)
- **Gateway** (`InsurTech.Gateway/Program.cs`): middleware strips a leading `/api` segment before routing.
- **Frontend** (SWA workflow): `VITE_API_BASE="/api"` → same-origin requests.
- **`staticwebapp.config.json`**: `/api/*` excluded from `navigationFallback` (so it reaches the backend, not `index.html`).

The frontend redeploys automatically via the GitHub Action on push. The two backend changes
(gateway + documents OCR) need the images rebuilt, then the backend linked — run the steps below
**in Azure Cloud Shell** (bypasses Zscaler + token expiry).

## Run in Azure Cloud Shell (Bash)

```bash
RG=rg-azuser7069_mml.local-yyRMB
ACR=insurtechacra7ad58ee
SWA=insurtech-ui-swa      # confirm: az staticwebapp list -o table

# 0. Latest code
git clone https://github.com/ShonishBhushanP/insurtech-platform.git 2>/dev/null || true
cd insurtech-platform && git pull

# 1. Rebuild ONLY gateway + documents images (server-side; ~3-5 min)
az acr build -r $ACR -t insurtech-gateway:latest \
  --build-arg PROJECT=src/Gateway/InsurTech.Gateway/InsurTech.Gateway.csproj \
  --build-arg DLL=InsurTech.Gateway.dll \
  -f backend/Dockerfile backend

az acr build -r $ACR -t insurtech-documents:latest \
  --build-arg PROJECT=src/Services/Documents/DocMgmt.Api/DocMgmt.Api.csproj \
  --build-arg DLL=DocMgmt.Api.dll \
  -f backend/Dockerfile backend

# 2. Roll the two apps onto the new images (new revision forces a fresh pull of :latest)
SFX=r$(date +%H%M%S)
az containerapp update -g $RG -n insurtech-gateway   --image $ACR.azurecr.io/insurtech-gateway:latest   --revision-suffix $SFX
az containerapp update -g $RG -n insurtech-documents --image $ACR.azurecr.io/insurtech-documents:latest --revision-suffix ${SFX}d

# 3. Link the gateway as the SWA's backend (the same-origin /api proxy)
GW=$(az containerapp show -g $RG -n insurtech-gateway --query id -o tsv)
az staticwebapp backends link -n $SWA -g $RG --backend-resource-id "$GW" --backend-region centralindia

# 4. Verify the link
az staticwebapp backends show -n $SWA -g $RG -o table
```

## Verify it works
1. Wait for the GitHub Action ("Deploy Frontend…") to go green (rebuilds with `VITE_API_BASE=/api`).
2. On the **corporate network**, open `https://red-water-0a9d64f00.7.azurestaticapps.net/`, log in,
   open **Claims** / **Policies**. They should load (browser now calls `…azurestaticapps.net/api/v1/…`).
3. DevTools → Network: the `…/api/v1/claims` request should be **200** (not pending), served from the
   `azurestaticapps.net` origin.

## Rollback
- Unlink: `az staticwebapp backends unlink -n $SWA -g $RG`
- Revert the UI to direct cross-origin: set `VITE_API_BASE` back to the gateway URL in
  `.github/workflows/azure-static-web-apps.yml` and push.

## Notes / gotchas
- Linked backends require the SWA **Standard** plan (we're on Standard).
- Cross-region is fine — `--backend-region centralindia` (the gateway's region) while the SWA
  content is elsewhere.
- If the link command reports the backend is already linked, unlink first (above), then re-link.
- Keep `insurtech-gateway` at **min replicas = 1** so the `/api` proxy is always warm.

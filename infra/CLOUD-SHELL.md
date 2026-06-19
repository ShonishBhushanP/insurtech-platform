# Deploying from Azure Cloud Shell

On a locked-down corporate machine, two things block deploys from a local terminal:

1. **Corporate proxy (Zscaler) blocks Azure data-plane uploads** — e.g. `az acr build` uploads
   your source to Azure's build service and Zscaler returns a block page (`<!--Cognizant Prod
   Cloud-->`), so the build fails. (Management-plane calls like creating resources still work.)
2. **Short-lived sandbox tokens** — a learner/sandbox Temporary Access Pass expires every ~15 min,
   so long operations (10 image builds ≈ 20 min) die part-way.

**Azure Cloud Shell solves both:** it runs *inside Azure*, so uploads never traverse the proxy,
and its session stays authenticated. Use it for any data-plane-heavy deploy (ACR builds, Function
zip-deploy, blob uploads).

## Steps

1. Open **https://shell.azure.com** → choose **PowerShell**. (First run creates a small storage
   account for Cloud Shell — accept it.)

2. Confirm the subscription:
   ```powershell
   az account show --query name -o tsv
   ```

3. Clone the repo (prompts for a GitHub PAT if the repo is private):
   ```powershell
   git clone https://github.com/ShonishBhushanP/insurtech-platform.git
   cd insurtech-platform/infra
   ```

4. **Backend → Container Apps** (9 microservices + gateway; builds 10 images server-side):
   ```powershell
   ./deploy-backend.ps1 -ResourceGroup rg-azuser7069_mml.local-yyRMB `
     -SqlConnectionString "Server=tcp:<server>.database.windows.net,1433;Database=insurtech;User ID=insurtechadmin;Password=<pwd>;Encrypt=True;" `
     -FrontendOrigin "https://<your-static-site>"
   ```
   Prints the **public gateway URL** when done. The build step is **resumable** — re-run and it
   skips images already in ACR.

5. **Durable Functions** (Claims adjudication orchestrator), after the services are public:
   ```powershell
   ./deploy-functions.ps1 -ResourceGroup rg-azuser7069_mml.local-yyRMB `
     -ClaimsUrl https://<claims-or-gateway> -FraudUrl https://<fraud> -PaymentsUrl https://<payments>
   ```

6. **Repoint the frontend:** set `VITE_API_BASE` in `.github/workflows/azure-static-web-apps.yml`
   to the gateway URL and push → the Static Web App redeploys and the public UI uses the Azure backend.

## Notes
- The local scripts (`deploy-frontend.ps1`, `sandbox-sql.bicep`) also run in Cloud Shell unchanged;
  the corporate-proxy `REQUESTS_CA_BUNDLE` handling is skipped automatically when not on Windows.
- Cloud Shell has `az`, `git`, `dotnet`, and PowerShell pre-installed.
- The SQL firewall rule `AllowAllForDemo` lets the Container Apps reach Azure SQL; tighten or remove
  it after the demo.

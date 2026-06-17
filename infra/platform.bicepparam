using './platform.bicep'

// Resource naming prefix (lowercase, 3-10 chars).
param namePrefix = 'insurtech'

// REQUIRED — the Entra identity that becomes the SQL administrator.
// Get yours with:  az ad signed-in-user show --query id -o tsv   /   --query userPrincipalName -o tsv
param sqlAadAdminObjectId = '00000000-0000-0000-0000-000000000000'
param sqlAadAdminLogin = 'you@yourtenant.onmicrosoft.com'

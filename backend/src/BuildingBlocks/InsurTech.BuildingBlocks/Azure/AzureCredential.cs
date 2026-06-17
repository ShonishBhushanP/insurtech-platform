using Azure.Core;
using Azure.Identity;

namespace InsurTech.BuildingBlocks.Azure;

/// <summary>
/// Single shared <see cref="TokenCredential"/> for all Azure SDK clients. Uses
/// <see cref="DefaultAzureCredential"/> so the same code authenticates via Managed Identity /
/// Workload Identity in AKS (LLD IR-06 — no secrets in config) and via developer credentials
/// (az login / Visual Studio) when running locally against Azure.
/// </summary>
public static class AzureCredential
{
    public static TokenCredential Instance { get; } = new DefaultAzureCredential(
        new DefaultAzureCredentialOptions { ExcludeInteractiveBrowserCredential = true });
}

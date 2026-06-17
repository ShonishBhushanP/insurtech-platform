// ─────────────────────────────────────────────────────────────────────────────
// InsurTech — Edge tier (deployment diagram: Azure Front Door + WAF).
// OPTIONAL. Deploy after apps.bicep, passing the gateway's public FQDN. Fronts the gateway
// Container App with Azure Front Door Standard + a WAF policy (rate-limit custom rule).
// Managed OWASP rule sets require the Premium SKU — switch `sku` to Premium_AzureFrontDoor
// and add managedRules for production (LLD IR-03).
// ─────────────────────────────────────────────────────────────────────────────

param namePrefix string = 'insurtech'
@description('Public FQDN of the gateway container app (apps.bicep output gatewayFqdn).')
param gatewayFqdn string
param sku string = 'Standard_AzureFrontDoor'

var tags = { platform: 'InsurTech', phase: 'Phase3', managedBy: 'bicep' }

resource waf 'Microsoft.Network/FrontDoorWebApplicationFirewallPolicies@2022-05-01' = {
  name: '${namePrefix}waf'
  location: 'global'
  tags: tags
  sku: { name: sku }
  properties: {
    policySettings: { enabledState: 'Enabled', mode: 'Prevention' }
    customRules: {
      rules: [ {
        name: 'RateLimitPerIp'
        priority: 1
        ruleType: 'RateLimitRule'
        rateLimitDurationInMinutes: 1
        rateLimitThreshold: 1000
        matchConditions: [ {
          matchVariable: 'RequestUri'
          operator: 'Contains'
          matchValue: [ '/' ]
        } ]
        action: 'Block'
      } ]
    }
  }
}

resource profile 'Microsoft.Cdn/profiles@2023-05-01' = {
  name: '${namePrefix}-afd'
  location: 'global'
  tags: tags
  sku: { name: sku }
}

resource endpoint 'Microsoft.Cdn/profiles/afdEndpoints@2023-05-01' = {
  parent: profile
  name: '${namePrefix}-edge'
  location: 'global'
  properties: { enabledState: 'Enabled' }
}

resource originGroup 'Microsoft.Cdn/profiles/originGroups@2023-05-01' = {
  parent: profile
  name: 'gateway-origin-group'
  properties: {
    loadBalancingSettings: { sampleSize: 4, successfulSamplesRequired: 3 }
    healthProbeSettings: {
      probePath: '/health'
      probeRequestType: 'GET'
      probeProtocol: 'Https'
      probeIntervalInSeconds: 60
    }
  }
}

resource origin 'Microsoft.Cdn/profiles/originGroups/origins@2023-05-01' = {
  parent: originGroup
  name: 'gateway-origin'
  properties: {
    hostName: gatewayFqdn
    originHostHeader: gatewayFqdn
    httpPort: 80
    httpsPort: 443
    priority: 1
    weight: 1000
    enabledState: 'Enabled'
  }
}

resource route 'Microsoft.Cdn/profiles/afdEndpoints/routes@2023-05-01' = {
  parent: endpoint
  name: 'default-route'
  dependsOn: [ origin ]
  properties: {
    originGroup: { id: originGroup.id }
    supportedProtocols: [ 'Https' ]
    patternsToMatch: [ '/*' ]
    forwardingProtocol: 'HttpsOnly'
    httpsRedirect: 'Enabled'
    linkToDefaultDomain: 'Enabled'
  }
}

resource securityPolicy 'Microsoft.Cdn/profiles/securityPolicies@2023-05-01' = {
  parent: profile
  name: 'waf-association'
  properties: {
    parameters: {
      type: 'WebApplicationFirewall'
      wafPolicy: { id: waf.id }
      associations: [ {
        domains: [ { id: endpoint.id } ]
        patternsToMatch: [ '/*' ]
      } ]
    }
  }
}

output frontDoorEndpointHostName string = endpoint.properties.hostName

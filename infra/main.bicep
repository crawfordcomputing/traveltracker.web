// Minimal infra for Travel Tracker.
// Deploys a Linux App Service on a free/basic plan. The app targets SQL Server
// only; Azure SQL is provisioned when deploySql=true, otherwise you supply
// ConnectionStrings__Default yourself (app setting or Key Vault reference).
//
// Deploy:
//   az group create -n travel-tracker-rg -l eastus
//   az deployment group create -g travel-tracker-rg -f infra/main.bicep \
//     -p appName=<globally-unique-name>

@description('Globally unique name for the App Service (also used as the default hostname).')
param appName string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('App Service plan SKU. B1 is the cheapest always-on tier; F1 is free.')
param sku string = 'B1'

@description('Set true to also provision Azure SQL and point the app at it.')
param deploySql bool = false

@description('SQL admin login (only used when deploySql=true).')
param sqlAdminLogin string = 'ttadmin'

@description('SQL admin password (only used when deploySql=true).')
@secure()
param sqlAdminPassword string = ''

@description('Azure Storage connection string for receipt blobs (Storage:Blob:ConnectionString). Receipts are Blob-only; the app cannot serve receipts without this.')
@secure()
param storageConnectionString string = ''

@description('Blob container name for receipts.')
param storageContainer string = 'receipts'

var planName = '${appName}-plan'
var sqlServerName = '${appName}-sql'
var sqlDbName = 'traveltracker'

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  sku: {
    name: sku
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = if (deploySql) {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2023-05-01-preview' = if (deploySql) {
  parent: sqlServer
  name: sqlDbName
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}

resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = if (deploySql) {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

var sqlConnectionString = deploySql
  ? 'Server=tcp:${sqlServerName}${environment().suffixes.sqlServerHostname},1433;Database=${sqlDbName};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=true;TrustServerCertificate=false;'
  : ''

resource web 'Microsoft.Web/sites@2023-12-01' = {
  name: appName
  location: location
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: sku != 'F1'
      ftpsState: 'Disabled'
      appSettings: [
        {
          // App is SQL Server only. With deploySql=true this points at the provisioned
          // Azure SQL DB; otherwise set it here (or via a Key Vault reference) before the
          // app can start. Startup fails fast if it is empty.
          name: 'ConnectionStrings__Default'
          value: deploySql ? sqlConnectionString : ''
        }
        {
          // Receipt storage is Azure Blob only (local disk was removed: App Service
          // wipes wwwroot on deploy). Supply the connection string here or swap in a
          // Key Vault reference; the Expenses pages fail until it is set.
          name: 'Storage__Blob__ConnectionString'
          value: storageConnectionString
        }
        {
          name: 'Storage__Blob__Container'
          value: storageContainer
        }
      ]
    }
  }
}

output webAppUrl string = 'https://${web.properties.defaultHostName}'

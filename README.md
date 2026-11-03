# DelegationStation

## Table of Contents
- [Overview](#overview)
- [Usage](#usage)
- [Dependencies](#dependencies)
	- [CosmosDB](#cosmosdb)
	- [Azure AD Configuration](#azure-ad-configuration)
	  - [Groups and Administrative Units](#groups-and-administrative-units)
	  - [Managed Identity Setup](#managed-identity-setup)
	  - [App Registration (Web Application)](#app-registration-web-application)
	    - [Authentication Blade](#authentication-blade)
	    - [Token Configuration](#token-configuration)
	    - [Enterprise Application Setup](#enterprise-application-setup)
	    - [API Permissions](#api-permissions)
	    - [Certificates & Secrets](#certificates--secrets)
	  - [App Registration (UpdateDevices Function App)](#app-registration-updatedevices-function-app)
	    - [Graph Permissions](#graph-permissions-1)
	    - [Certificates & Secrets](#certificate-based-authentication-1)
	    - [Grant Roles or Group Ownership](#grant-roles-or-group-ownership)
      - [App Registration (CorporateIdentifierSync Function App)](#app-registration-corporateidentifier-function-app)
	    - [Graph Permissions](#graph-permissions-2)
	    - [Certificates & Secrets](#certificate-based-authentication-2)
- [Application Configurations](#application-configurations)
  - [Web Application Configuration](#web-application-configuration)
    - [Environment Variables](#environment-variables)
	- [Certificate Configuration](#certificate-configuration)
  - [Update Devices Configuration](#update-devices-configuration)
    - [Environment Variables](#environment-variables-1)
    - [Certificate Configuration](#certificate-configuration-1)
  - [Corporate Identifier Sync Configuration](#corporate-identifier-sync-configuration)
    - [Environment Variables](#environment-variables-2)
    - [Certificate Configuration](#certificate-configuration-2)




## Overview

This repository contains 4 applications:  
* Web Application 
* CorporateIdentifierSync function app
  * AddNewDevices function: adds newly enrolled devices to the tenant's Corporate Identifiers
  * ConfirmSync function: confirms that devices remain synced as Corporate Identifiers
  * DeviceDeletion function: removes devices from Corporate Identifiers and DelegationStation
  * CorpIDAudit function: periodically reports Corporate Identifier usage and warns when usage nears the configured maximum
  * ReconcileSyncState function: syncs or unsyncs devices when a tag's "enable sync" setting is changed
* UpdateDevices function app
  * UpdateDevices function: applies configuration changes for devices in Delegation Station following enrollment
  * StragglerHandler function: re-attempts updates for devices that enrolled but could not be processed due to delays in InTune updating hardware information
  * Cleanup function:  Cleans up old DB entries related to the hand-off between the UpdateDevices and StragglerHandler functions 
* (WIP) InTuneEnrollment function app

The repository also includes a supporting WebJob used during deployment:
* **SeedCorpIDCounter** (triggered WebJob): one-time job that seeds an initial `CorpIDCounter` document required by CorporateIdentifierSync. See [`SeedCorpIDCounter/README.md`](SeedCorpIDCounter/README.md).

These applications can be deployed into Azure App Services (Windows or Linux) or Container Instances via Azure CLI, Visual Studio, or GitHub Actions.

Software is currently built on .NET8 and function apps are using isolated worker model.

**Upgrading an existing deployment?** See [`SPECIAL_INSTRUCTIONS.md`](SPECIAL_INSTRUCTIONS.md) for the 2.4 non-managed-identity upgrade steps, [`SPECIAL_INSTRUCTIONS_MANAGED_IDENTITY.md`](SPECIAL_INSTRUCTIONS_MANAGED_IDENTITY.md) to transition an existing deployment to managed identity, and the [`SpecialInstructions`](SpecialInstructions) folder for archived release-specific instructions.

## Dependencies

### CosmosDB

The software utilizes a CosmosDB to maintain application data.  
* Database Name:  DelegationStationData 
* Container
    - Name: DeviceData  
    - PartitionKey: /PartitionKey

 *Note:  You can use a different  DB and Container name, but you will need to update related configuration values for each of the apps.*

 ### Azure AD Configuration

 #### Groups and Administrative Units
* Create a Security Group that will contain all the Adminstrative Users. (The GUID of this group will be used in the configuration of the webapp.)
* (Optional) Create an Administrative Unit that will contain all the groups that the application will be able to assign devices to.  This supports the most secure configuration.  Alternatives are mentioned below.
 

#### Managed Identity Setup

The Web Application, UpdateDevices Function App, and CorporateIdentifierSync Function App use their system-assigned managed identities to access Microsoft Graph and Cosmos DB when deployed to Azure. Create and deploy the three Azure resources, and enable a **system-assigned identity** on each, before assigning permissions. The scripts below read the resource names and identity principal IDs from Azure; they do not create the resources or enable their identities.

##### Configure Microsoft Graph permissions

The [`scripts/configureGraphMSIAuth.sh`](scripts/configureGraphMSIAuth.sh) script grants these Microsoft Graph application roles to the deployed identities:

| Identity | Microsoft Graph application roles |
| --- | --- |
| Web Application | `AdministrativeUnit.Read.All`, `DeviceManagementManagedDevices.Read.All`, `Group.Read.All` |
| UpdateDevices Function App | `AdministrativeUnit.ReadWrite.All`, `Device.ReadWrite.All`, `DeviceManagementManagedDevices.PrivilegedOperations.All`, `GroupMember.ReadWrite.All`, `DeviceManagementManagedDevices.Read.All` |
| CorporateIdentifierSync Function App | `DeviceManagementManagedDevices.ReadWrite.All`, `DeviceManagementServiceConfig.ReadWrite.All` |

The UpdateDevices assignment includes `GroupMember.ReadWrite.All`, a broad tenant-level permission. This is intentional for the development environment where this script is used. Production uses a separate Administrative Unit (AU) configuration; confirm its exact setup before documenting or applying it in production.

Before running the script, edit the resource group and resource names near its top. Sign in with Azure CLI (`az login`), set the matching Azure cloud (`az cloud set`), and select the correct subscription (`az account set`). The signed-in operator must be permitted to grant Microsoft Graph application-role assignments. The script uses `https://graph.microsoft.us` and is configured for Azure US Government; for another cloud, replace every occurrence with that cloud's Microsoft Graph endpoint. Run it from Bash with Azure CLI and `jq` installed. It assigns application roles directly to the existing managed identity service principals; it does not create app registrations. The script is not idempotent, so review current assignments before running it again.

##### Configure Cosmos DB access

The [`scripts/enableCosmosMI.sh.template`](scripts/enableCosmosMI.sh.template) script disables Cosmos DB key-based authentication, then assigns the built-in Cosmos DB Data Contributor role at the **account scope** to the Web Application and both function identities. It also assigns that role to the Azure CLI signed-in user.

Copy the template to a working script, replace the resource group, Cosmos DB account, and app resource names, then run it from Bash with Azure CLI and `jq` installed. Sign in and select the intended subscription first. The resources and system-assigned identities must already exist, and the operator needs permission to change Cosmos DB account settings and create data-plane role assignments.

This script grants broad data access across the Cosmos DB account and disables local/key authentication. Once key authentication is disabled, connection-string authentication stops working; configure `COSMOS_ENDPOINT` for the application and ensure each app uses its managed identity. Review the account scope and existing role assignments before running: the script is not idempotent and can create duplicate role assignments if run repeatedly.

To re-enable Cosmos DB key-based authentication, edit the resource group and account name in [`scripts/disableCosmosMI.sh.template`](scripts/disableCosmosMI.sh.template) and run it with Azure CLI. This only re-enables local authentication; it does not remove the role assignments.

##### Local development

Managed identities are used by the deployed applications in Azure. For Microsoft Graph, the applications use the configured app-registration certificate or client secret when the host environment is Development or `GRAPH_USE_APP_CREDENTIALS` is `true`, both locally and in Azure; see the application configuration sections below. When the environment is not Development and the flag is unset or `false`, Graph authentication uses managed identity. Keep production apps in Production and use `GRAPH_USE_APP_CREDENTIALS=true` to retain app-registration authentication without changing production features. This switch does not change Cosmos DB authentication or interactive user sign-in. For local Cosmos DB development, use `COSMOS_CONNECTION_STRING` unless the development environment has a managed identity endpoint available. The Cosmos setup script's role assignment for the signed-in user grants Cosmos DB data-plane access but does not itself provide managed identity credentials to a local process.

#### App Registration (Web Application)

From your AzureAD tenant in the Azure portal, add a new app registration.  
**Name**:  DelegationStation-webapp</br>
**Supported account types**:  Accounts in this organizational directory only</br>

##### Authentication Blade

Click on **+ Add a platform** and select **Web**</br>

Set the following settings and don't forget to hit **Save**

###### Redirect URI
Add a Redirect URI value to return to the application URL.
Example: https://delegationstation.azurewebsites.net/signin-oidc or https://delegationstation.azurewebsites.net/.auth/login/aad/callback

*Note:  The URL path (/signin-oidc) must match the CallbackPath setting in the Application.*

###### Implicit Grant and hybrid flows
Select **ID tokens** for the implicit grant and hybrid flows.

###### Supported account types
Select **Accounts in this organizational directory only**

##### Token Configuration
This application utilizes Entra ID groups for accessing the application through claims. Each group that is assigned a role must be configured to be sent as a claim from the enterprise application.
Add the following claims to the App registration:

1. Navigate to the Token configuration blade.
1. Click on **+ Add groups claim**
1. Select **Security groups** OR **Groups assigned to the application (recommended for large enterprise companies to avoid exceeding the limit on the number of groups a token can emit)**
1. For each token type (ID, Access, SAML) select:
   - Group ID
   - Emit groups as role claims

##### Enterprise Application Setup

The Enterprise Application configuration will allow you to restrict designated users and groups to the application.

- From the AD tenant screen in the Azure Portal, select **Enterprise Applications**
- Click on the App Registration you just created.
- Click on **Users and groups**
- Add any groups or users who need access to the application.

##### API Permissions

This app registration is still used for interactive user sign-in. The delegated `User.Read` permission below is for user login and is separate from the app's managed identity.

###### For User Login

Ensure the following Permission is listed under Microsoft.Graph
- User.Read  (Delegated)

###### Graph Permissions

When using managed identity, assign the Graph application roles to the Web Application identity as described in [Managed Identity Setup](#managed-identity-setup), rather than to this app registration. Add the following application permissions to this app registration only when the Web Application cannot use managed identity:

Click on **+ Add a permission** 
Select **Microsoft Graph** 
Select **Application permissions**

Add the following permissions:
- Group.Read.All                  (Allows for listing of AD Groups)
- AdministrativeUnit.Read.All     (Allows for listing of AD Administrative Units)

Once done, ensure that you have been Granted Admin Consent for these permissions.

##### Certificates & Secrets

These credentials are only needed when the Web Application cannot use managed identity for Microsoft Graph authentication.

###### Managed Identity (Federated Credential)

- Click on **Certificates & secrets**
- Click on **Federated credentials**
- Click on **+ Add credential**
- Select scenario **Managed Identity**
- Click on **Select a managed identity**
- Choose the subscription
- Under **Managed identity**, select **App Service**, then choose the identity of the correct DelegationStation web app
- Click **Select**
- Under **Credential details**, enter a name, for example `DEV-DSWebApp-MSI`
- Click **Add**

###### Certificate-based Authentication

This assumes you already have a certificate you will be using for authentication.  
You will need a .CER/.CRT/.PEM file with the public key for the App Registration.
But you will also need a PFX with the private key (and a set pass phrase) to configure in the Application

- Click on **Certificates** 
- Click on **Upload Certificate**
- Select the file to upload from your local machine.
- If you do not enter a description, it will use the Subject which you will need to use in the Application Configuration settings.

###### Secret-based Authentication

- Click on **Client secrets**
- Click on **+ New client secret**
- Store the value in a secure location (key vault is recommended).  This will be used in the configuration of the application.  



#### App Registration (UpdateDevices Function App)

**Note:** This app registration is only needed when the function cannot use managed identity. When using managed identity, assign the required Microsoft Graph roles to the function identity as described in [Managed Identity Setup](#managed-identity-setup).

From your AzureAD tenant in the Azure portal, add a new app registration.  
**Name**:  DelegationStation-updatedevices</br>
**Supported account types**:  Accounts in this organizational directory only</br>

##### Graph Permissions

Click on **+ Add a permission** 
Select **Microsoft Graph** 
Select **Application permissions**

Add the following permissions:
- DeviceManagementManagedDevices.PrivilegedOperations.All (Required to read devices from InTune and to rename devices)
- Device.ReadWrite.All                    (Allows for updating device attributes)
- AdministrativeUnit.ReadWrite.All        (Allows for updating AUs)

Reduced permissions for monitoring without changes (will log changes, but not have permissions to apply them):
- DeviceManagementManagedDevices.Read.All
- Device.Read.All

##### Certificates & Secrets

###### Certificate-based Authentication

This assumes you already have a certificate you will be using for authentication.  
You will need a .CER/.CRT/.PEM file with the public key for the App Registration.
But you will also need a PFX with the private key (and a set pass phrase) to configure in the Application

- Click on **Certificates** 
- Click on **Upload Certificate**
- Select the file to upload from your local machine.
- If you do not enter a description, it will use the Subject which you will need to use in the Application Configuration settings.

###### Secret-based Authentication

- Click on **Client secrets**
- Click on **+ New client secret**
- Store the value in a secure location (key vault is recommended).  This will be used in the configuration of the application. 

##### Grant Roles or Group Ownership

In order to limit the function app to only be able to update only relevant Security Groups, use one of the following two methods:
- Option 1:  Make the App Registration the owner of each of the groups the function app should have permissions to edit  
- Option 2:  Assign the App Registration a custom role on an AU containing all and only the relevant groups with the following permissions:
   - microsoft.directory/groups/members/read
   - microsoft.directory/groups.security/members/update

Any groups added to the application under Tags will have to have the service principals permissions added in order to work as expected.

Alternatively, you could add GroupMember.ReadWrite.All to the Graph Permissions granted to the App Registrations, but it is not recommended due to the broad access.

Do not make these changes if using reduced permissions to monitor changes but not apply them.

#### App Registration (CorporateIdentifier Function App)

**Note:** This app registration is only needed when the function cannot use managed identity. When using managed identity, assign the required Microsoft Graph roles to the function identity as described in [Managed Identity Setup](#managed-identity-setup).

From your AzureAD tenant in the Azure portal, add a new app registration.  
**Name**:  DelegationStation-corpidsync</br>
**Supported account types**:  Accounts in this organizational directory only</br>

##### Graph Permissions

Click on **+ Add a permission** 
Select **Microsoft Graph** 
Select **Application permissions**

Add the following permissions:
- DeviceManagementManagedDevices.ReadWrite.All (Allows for reading and deleting devices from InTune)
- DeviceManagementServiceConfig.ReadWrite.All (Allows for reading, creating/deleting corporate identifiers in InTune) 

Reduced permissions for monitoring without changes (will log changes, but not have permissions to apply them):
- DeviceManagementManagedDevices.Read.All
- DeviceManagementServiceConfig.Read.All 

##### Certificates & Secrets

###### Certificate-based Authentication

This assumes you already have a certificate you will be using for authentication.  
You will need a .CER/.CRT/.PEM file with the public key for the App Registration.
But you will also need a PFX with the private key (and a set passphrase) to configure in the Application

- Click on **Certificates** 
- Click on **Upload Certificate**
- Select the file to upload from your local machine.
- If you do not enter a description, it will use the Subject which you will need to use in the Application Configuration settings.

###### Secret-based Authentication

- Click on **Client secrets**
- Click on **+ New client secret**
- Store the value in a secure location (key vault is recommended).  This will be used in the configuration of the application. 
## Application Configurations

### Web Application Configuration

#### Environment Variables
Use the following environment variables to configure the application. These can be set in the `appsettings.json` file or in the Azure App Service configuration.

Note that for nested settings, in the portal, the nesting is done via two underscore characters.  For example,
AzureAd__TenantId or AzureAD__ClientCertificates__CertificateDistinguishedName.

"AzureAd": {<br/>
&emsp;"Instance": "",<br/>
&emsp;"Domain": "",<br/>
&emsp;"TenantId": "",<br/>
&emsp;"ClientId": "",<br/>
&emsp;"CallbackPath": "",<br/>
&emsp;"ClientCertificates": {<br/>
&emsp;&emsp;&emsp;"CertificateDistinguishedName": ""<br/>
&emsp;&emsp;}<br/>
}<br/>

<b>"Instance": ""</b></br>
The AuzreAD authentication endpoint.  For example, "https://login.microsoftonline.com/"

<b>"Domain": ""</b></br>
The domain of your AzureAD tenant.  For example, "delegationstation.microsoftonline.com"

<b>"TenantId": ""</b><br/>
Can be found in the Azure Portal under Azure Active Directory -> Properties -> Directory ID
Can also be found in the Azure Portal under the App Registration -> Overview -> Directory ID

<b>"ClientId" : ""</b><br/>
Can be found in the Azure Portal under the App Registration -> Overview -> Application (client) ID

<b>"CallbackPath": ""</b><br/>
The path returned to after a successful user login.  Must match what is set in the App Registration Authentication settings for the web app.
A typical setting would be "/signin-oidc"

<b>"CertificateDistinguishedName" : ""</b><br/>
The subject name of the certificate to be used for client certificate authentication. 

"AzureApp": {<br/>
&emsp;"ClientSecret": ""<br/>
}<br/>

<b>"ClientSecret": ""</b><br/>
(Optional) Required if not using certificate-based authentication.
Can be found in the Azure Portal under the App Registration -> Certificates & secrets -> Client secrets

<b>"COSMOS_CONNECTION_STRING": ""</b><br/>
Can be found in the Azure Portal under the Cosmos DB -> Keys -> Primary Connection String

<b>"COSMOS_ENDPOINT": ""</b><br/>
Use this setting when using managed-identity based authentication to CosmosDB.  This is the URL of the CosmosDB instance.

<b>"COSMOS_DATABASE_NAME" : ""</b><br/>
(Optional) The name of the Cosmos DB database. Default is "DelegationStationData"

<b>"COSMOS_CONTAINER_NAME" : ""</b><br/>
(Optional) The name of the Cosmos DB container. Default is "DeviceData"

<b>"MAX_CORPIDS_ALLOWED": "10000"</b><br/>
(Optional) The maximum number of Corporate Identifiers used by the System Status page to display utilization and available capacity. Default is 10000. Set this to the same value configured on the CorporateIdentifierSync Function App.

<b>"APPLICATIONINSIGHTS_CONNECTION_STRING": ""</b><br/>
The connection string for the Application Insights resource used by the Web App's telemetry. This setting already existed before 2.4.

<b>"DefaultAdminGroupObjectId" : ""</b><br/>
AzureAD group that contains Administrative users of the application, who can perform additional delegations within the application.
Can be found in the Azure Portal under Azure Active Directory -> Groups -> Group -> Properties -> Object ID

<b>"AzureEnvironment" : ""</b><br/>
Can be set to "AzurePublicCloud", "AzureUSDoD", or "AzureUSGovernment" depending on the environment you are using.

<b>"GraphEndpoint": ""</b><br/>
The URL of the graph instance you will be connecting to. 
For example, "https://graph.microsoft.com/"

<b>"AllowedHosts": "*"</b><br/>

<b>"DefaultActionDisable": "false"</b><br/>
(Optional) Can be set to "true" to disable the device if not found in the database. If set to "false" the device will be allowed to connect if not found in the database.

<b>"GRAPH_USE_APP_CREDENTIALS": "true"</b><br/>
(Optional) Set to `true` to authenticate to Graph using the configured app-registration certificate or client secret instead of managed identity, both locally and in Azure. Development also uses app-registration authentication, even when the flag is unset or `false`. Visual Studio's existing launch profiles set `ASPNETCORE_ENVIRONMENT=Development`, so no additional variable is required there. Managed identity is used only when the environment is not Development and the flag is unset or `false`. Keep production web apps in Production and use this flag instead of changing their environment.

#### Certificate Configuration

This is only needed if you are connecting to Graph using certificates.

##### Local

- In Windows Explorer, go to the location of the PFX with the private key.
- Right-click on the file and choose 'Install PFX'
- Choose **Current User**
- Confirm the correct PFX file is listed and click **Next**
- Enter password for the private key and click **Next**
- Click on **Place all certificates in the following store** and click on **Browse...**
- Choose **Personal**
- Review the final screen and choose **Finish**

##### Azure

- In the Azure Portal, go the Function App resource
- Click on **Settings** -> **Certificates**
- Click on **Bring your own certificates (.pfx)**
- Click on **+ Add certificate**
- Choose **Upload certificate (.pfx)** 
- Enter the file location and password and click on **Validate**
- Click on **Add**

*Note:  In order to keep the certificates separate the WebApp and Function App need to be deployed in different RGs.  Certs are shared across webspaces.  More info here:  https://learn.microsoft.com/en-us/azure/app-service/app-service-plan-manage#move-an-app-to-another-app-service-plan

### Update Devices Configuration

#### Environment Variables
Use the `local.settings.json.template` file to help setup your local configuration file.  
When deploying to Azure, these will go in your Configuration Settings or Environment Variables section, which is separate in some versions of the portal.  

<b>"AzureEnvironment" : ""</b><br/>
"AzurePublicCloud", "AzureUSDoD", or "AzureUSGovernment" depending on the Azure environment you are using.

<b>"COSMOS_CONNECTION_STRING": ""</b><br/>
Can be found in the Azure Portal under the Cosmos DB -> Keys -> Primary Connection String

<b>"COSMOS_ENDPOINT": ""</b><br/>
Use this setting when using managed-identity based authentication to CosmosDB.  This is the URL of the CosmosDB instance.

<b>"COSMOS_DATABASE_NAME" : ""</b><br/>
(Optional) The name of the Cosmos DB database. Default is "DelegationStationData"

<b>"COSMOS_CONTAINER_NAME" : ""</b><br/>
(Optional) The name of the Cosmos DB container. Default is "DeviceData"

<b>"TriggerTime": "0 */15 * * * *"</b><br/>
Cron expression for the frequency of the primary UpdateDevices function. The recommendation is every 15 minutes.

<b>"SHTriggerTime": "0 0 */4 * * *"</b><br/>"
Cron expression for the frequency of the StragglerHandler function.  The recommendation is every 4 hours.

<b>"CleanupTriggerTime": "0 0 */12 * * *"</b><br/>
Cron expression for the frequency of the Cleanup function.  The recommendation is to run this twice a day.

<b>"AzureAd:TenantId" : ""</b><br/>
Can be found in the Azure Portal under Azure Active Directory -> Properties -> Directory ID
Can also be found in the Azure Portal under the App Registration -> Overview -> Directory ID

<b>"AzureAd:ClientId" : ""</b><br/>
ClientID of the App Registration you created for this function app.
Can be found in the Azure Portal under the App Registration -> Overview -> Application (client) ID

<b>"AzureApp:ClientSecret" : ""</b><br/>
ClientSecret of the App Registration you created for this function app.  
(Optional) Can be found in the Azure Portal under the App Registration -> Certificates & secrets -> Client secrets
Required if you are not going to use Certificate-based authentication.

<b>"CertificateDistinguishedName" : ""</b><br/>
(Optional) If using a certificate in place of a client secret, this the Subject of that certificate.  
Can be found in the Azure Portal under the App Registration -> Certificates & secrets -> Client certificates
Will look under CurrentUser\my store for the certificate with the distinguished name.

<b>"GraphEndpoint": ""</b><br/>
The URL of the graph instance you will be connecting to. 
For example, "https://graph.microsoft.com/"

<b>"DefaultActionDisable": "false"</b><br/>
(Optional) Can be set to "true" to disable the device if not found in the database. If set to "false" the device will be allowed to connect if not found in the database.

<b>"MaxUpdateDeviceAttempts": "5"</b><br/>
(Optional, set with caution)  This setting is used by the StragglerHandler and does not determine the number of attempts for the UpdateDevices function.
This setting is so the StragglerHandler does not start processing a device before UpdateDevices has completed it's attempts.
The default is 5, which is the correct value for using the default UpdateDevices frequency of every 15 minutes.

<b>"MaxStragglerAttempts": "5"</b><br/>
(Optional)  The number of times the Straggler Handler will re-attempt to process a device if it encounters errors during processing.  Defaults to 5.

<b>"GRAPH_USE_APP_CREDENTIALS": "true"</b><br/>
(Optional) Set to `true` to authenticate to Graph using the configured app-registration certificate or client secret instead of managed identity, both locally and in Azure (under `Values` in `local.settings.json` for local functions). Development also uses app-registration authentication, even when the flag is unset or `false`. Managed identity is used for both v1.0 and beta Graph clients only when the host environment is not Development and the flag is unset or `false`. Keep production function apps in Production and use this flag instead of changing their environment.

### Certificate Configuration

This is only needed if you are connecting to Graph using certificates.

##### Local

- In Windows Explorer, go to the location of the PFX with the private key.
- Right-click on the file and choose 'Install PFX'
- Choose **Current User**
- Confirm the correct PFX file is listed and click **Next**
- Enter password for the private key and click **Next**
- Click on **Place all certificates in the following store** and click on **Browse...**
- Choose **Personal**
- Review the final screen and choose **Finish**

##### Azure

- In the Azure Portal, go the Function App resource
- Click on **Settings** -> **Certificates**
- Click on **Bring your own certificates (.pfx)**
- Click on **+ Add certificate**
- Choose **Upload certificate (.pfx)** 
- Enter the file location and password and click on **Validate**
- Click on **Add**

*Note:  In order to keep the certificates separate the WebApp and Function App need to be deployed in different RGs.  Certs are shared across webspaces.  More info here:  https://learn.microsoft.com/en-us/azure/app-service/app-service-plan-manage#move-an-app-to-another-app-service-plan

### Corporate Identifier Sync Configuration

#### Environment Variables
Use the `local.settings.json.template` file to help setup your local configuration file.  
When deploying to Azure, these will go in your Configuration Settings or Environment Variables section, which is separate in some versions of the portal.  

<b>"AzureEnvironment" : ""</b><br/>
"AzurePublicCloud", "AzureUSDoD", or "AzureUSGovernment" depending on the Azure environment you are using.

<b>"COSMOS_CONNECTION_STRING": ""</b><br/>
Can be found in the Azure Portal under the Cosmos DB -> Keys -> Primary Connection String

<b>"COSMOS_ENDPOINT": ""</b><br/>
Use this setting when using managed-identity based authentication to CosmosDB.  This is the URL of the CosmosDB instance.

<b>"COSMOS_DATABASE_NAME" : ""</b><br/>
(Optional) The name of the Cosmos DB database. Default is "DelegationStationData"

<b>"COSMOS_CONTAINER_NAME" : ""</b><br/>
(Optional) The name of the Cosmos DB container. Default is "DeviceData"


The following settings must be set to cron expressions that control the frequency of each function:

<b>"AddDevicesTriggerTime": "0 */5 * * * *"</b><br/>
Cron for the AddDevices function.

<b>"DeleteDevicesTriggerTime": "0 2-59/5 * * * *"</b><br/>
Cron for the DeleteDevices function.

<b>"ConfirmSyncTriggerTime": "0 0 */4 * * *"</b><br/>
Cron for the ConfirmSync function.

<b>"CorpIDAuditTriggerTime": "0 0 */6 * * *"</b><br/>
Cron for the CorpIDAudit function.

<b>"ReconcileSyncStateTriggerTime": "0 0 */12 * * *"</b><br/>
Cron for the ReconcileSyncState function, which syncs or unsyncs devices when a tag's "enable sync" setting changes.

<b>"EnableCorpIDSync": "true"</b><br/>
Set to true to enable the Corporate Identifier Sync function.
If set to false, Delete function will only attempt to delete devices from DelegationStation.  The Add/ConfirmSync functions will exit upon running, but we recommend disabling the function from the Azure Portal.

<b>"AzureAd:TenantId" : ""</b><br/>
Can be found in the Azure Portal under Azure Active Directory -> Properties -> Directory ID
Can also be found in the Azure Portal under the App Registration -> Overview -> Directory ID

<b>"AzureAd:ClientId" : ""</b><br/>
ClientID of the App Registration you created for this function app.
Can be found in the Azure Portal under the App Registration -> Overview -> Application (client) ID

<b>"AzureApp:ClientSecret" : ""</b><br/>
ClientSecret of the App Registration you created for this function app.  
(Optional) Can be found in the Azure Portal under the App Registration -> Certificates & secrets -> Client secrets
Required if you are not going to use Certificate-based authentication.

<b>"CertificateDistinguishedName" : ""</b><br/>
(Optional) If using a certificate in place of a client secret, this the Subject of that certificate.  
Can be found in the Azure Portal under the App Registration -> Certificates & secrets -> Client certificates
Will look under CurrentUser\my store for the certificate with the distinguished name.

<b>"GraphEndpoint": ""</b><br/>
The URL of the graph instance you will be connecting to. 
For example, "https://graph.microsoft.com/"

<b>"SyncIntervalHours":  "24"</b><br/>
Amount of time to pass before checking that a device is still synced.  For example, "24" will pull all devices with a sync time older than Now-24 hours.

<b>"AddDeviceBatchSize": "5000"</b><br/>
(Optional) Amount of devices the AddDevices function will attempt to add in a single batch.  Default is 5000, which is estimated to run 15 minutes, but can be set lower if you are having issues with the function timing out.

<b>"ReconcileSyncBatchSize": "1000"</b><br/>
(Optional) Number of devices the **ReconcileSyncState** function processes per batch. Default is 1000.

<b>"MAX_CORPIDS_ALLOWED": "10000"</b><br/>
(Optional) The maximum number of Corporate Identifiers allowed in the tenant. The Add/ConfirmSync/Audit/Reconcile functions use this as a cap to avoid exceeding tenant limits. Default is 10000.

<b>"MAX_CORPID_RETRIES": "10"</b><br/>
(Optional) Number of times the AddDevices function will retry adding a device to Corporate Identifiers before marking it as Failed. Default is 10.

<b>"CORPID_WARNING_THRESHOLD_PERCENT": "90"</b><br/>
(Optional) Usage percentage (of `MAX_CORPIDS_ALLOWED`) at which the CorpIDAudit function logs a warning. Valid range is 1-100; values outside the range are clamped. Default is 90.

<b>"GRAPH_USE_APP_CREDENTIALS": "true"</b><br/>
(Optional) Set to `true` to authenticate to Graph using the configured app-registration certificate or client secret instead of managed identity, both locally and in Azure (under `Values` in `local.settings.json` for local functions). Development also uses app-registration authentication, even when the flag is unset or `false`. Managed identity is used for both v1.0 and beta Graph clients only when the host environment is not Development and the flag is unset or `false`. Keep production function apps in Production and use this flag instead of changing their environment.

#### Certificate Configuration

This is only needed if you are connecting to Graph using certificates.

##### Local

- In Windows Explorer, go to the location of the PFX with the private key.
- Right-click on the file and choose 'Install PFX'
- Choose **Current User**
- Confirm the correct PFX file is listed and click **Next**
- Enter password for the private key and click **Next**
- Click on **Place all certificates in the following store** and click on **Browse...**
- Choose **Personal**
- Review the final screen and choose **Finish**

##### Azure

- In the Azure Portal, go the Function App resource
- Click on **Settings** -> **Certificates**
- Click on **Bring your own certificates (.pfx)**
- Click on **+ Add certificate**
- Choose **Upload certificate (.pfx)** 
- Enter the file location and password and click on **Validate**
- Click on **Add**

*Note:  In order to keep the certificates separate the WebApp and Function App need to be deployed in different RGs.  Certs are shared across webspaces.  More info here:  https://learn.microsoft.com/en-us/azure/app-service/app-service-plan-manage#move-an-app-to-another-app-service-plan

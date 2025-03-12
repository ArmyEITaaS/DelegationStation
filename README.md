# DelegationStation

## Table of Contents
- [Overview](#overview)
- [Dependencies](#dependencies)
	- [CosmosDB](#cosmosdb)
	- [App Registration (Web Application)](#app-registration-web-application)
	  - [Authentication Blade](#authentication-blade)
	  - [API Permissions](#api-permissions)
	  - [Certificates & Secrets](#certificates--secrets)
	  - [Token Configuration](#token-configuration)
	  - [Enterprise Application Setup](#enterprise-application-setup)
	- [App Registration (UpdateDevices Function App)](#app-registration-updatedevices-function-app)
	  - [Graph Permissions](#graph-permissions-1)
	  - [Certificates & Secrets](#certificate-based-authentication-1)
	- [Azure AD Configuration](#azure-ad-configuration)
- [Web Application Configuration](#web-application-configuration)
    - [Environment Variables](#environment-variables)
	- [Certificate Configuration](#certificate-configuration)
- [Update Devices Configuration](#update-devices-configuration)
  - [EnvironmentVariables](#environment-variables-1)
  - [Certificate Configuration](#certificate-configuration-1)




## Overview

This repository contains 3 applications:  
* Web Application 
* UpdateDevices function app
  * UpdateDevices function: applies configuration changes for devices in Delegation Station following enrollment
  * StragglerHandler function: re-attempts updates for devices that enrolled but could not be processed due to delays in InTune updating hardware information
  * Cleanup function:  Cleans up old DB entries related to the hand-off between the UpdateDevices and StragglerHandler functions 
* (WIP) InTuneEnrollment function app

These applications can be deployed into Azure App Services (Windows or Linux) or Container Instances via Azure CLI, Visual Studio, or GitHub Actions.

Software is currently built on .NET6 and function apps are using isolated worker model.

## Dependencies

### CosmosDB

The software utilizes a CosmosDB to maintain application data.  
* Database Name:  DelegationStationData 
* Container
  * Name: DeviceData  
  * PartitionKey: /PartitionKey

 *Note:  You can use a different  DB and Container name, but you will need to update related configuration values for each of the apps.*

### App Registration (Web Application)

From your AzureAD tenant in the Azure portal, add a new app registration.  
**Name**:  DelegationStation</br>
**Supported account types**:  Accounts in this organizational directory only</br>

#### Authentication Blade

Click on **+ Add a platform** and select **Web**</br>

Set the following settings and don't forget to hit **Save**

##### Redirect URI
Add a Redirect URI value to return to the application URL.
Example: https://delegationstation.azurewebsites.net/signin-oidc or https://delegationstation.azurewebsites.net/.auth/login/aad/callback

*Note:  The URL path (/signin-oidc) must match the CallbackPath setting in the Application.*

##### Implicit Grant and hybrid flows
Select **ID tokens** for the implicit grant and hybrid flows.

##### Supported account types
Select **Accounts in this organizational directory only**

#### API Permissions

##### For User Login

Ensure the following Permission is listed under Microsoft.Graph
- User.Read  (Delegated)

##### Graph Permissions

Click on **+ Add a permission** 
Select **Microsoft Graph** 
Select **Application permissions**

Add the following permissions:
- Device.ReadWrite.All   (Allows for updating of device attributes)
- Directory.Read.All     (Allows for listing of AD Groups)
- DeviceManagementManagedDevices.ReadAll (Allows for reading and updating devices)

Once done, ensure that you have been Granted Admin Consent for these permissions.

#### Certificates & Secrets

##### Certificate-based Authentication

This assumes you already have a certificate you will be using for authentication.  
You will need a .CER/.CRT/.PEM file with the public key for the App Registration.
But you will also need a PFX with the private key (and a set pass phrase) to configure in the Application

- Click on **Certificates** 
- Click on **Upload Certificate**
- Select the file to upload from your local machine.
- If you do not enter a description, it will use the Subject which you will need to use in the Application Configuration settings.

##### Secret-based Authentication

- Click on **Client secrets**
- Click on **+ New client secret**
- Store the value in a secure location (key vault is recommended).  This will be used in the configuration of the application.  

#### Token Configuration
This application utilizes Entra ID groups for accessing the application through claims. Each group that is assigned a role must be configured to be sent as a claim from the enterprise application.
Add the following claims to the App registration:

1. Navigate to the Token configuration blade.
1. Click on **+ Add groups claim**
1. Select **Security groups** OR **Groups assigned to the application (recommended for large enterprise companies to avoid exceeding the limit on the number of groups a token can emit)**					
1. For each token type (ID, Access, SAML) select:
   - Group ID
   - Emit groups as role claims

#### Enterprise Application Setup

The Enterprise Application configuration will allow you to restrict designated users and groups to the application.

- From the AD tenant screen in the Azure Portal, select **Enterprise Applications**
- Click on the App Registration you just created.
- Click on **Users and groups**
- Add any groups or users who need access to the application.



### App Registration (UpdateDevices Function App)

From your AzureAD tenant in the Azure portal, add a new app registration.  
**Name**:  DelegationStationFunction</br>
**Supported account types**:  Accounts in this organizational directory only</br>

#### Graph Permissions

Click on **+ Add a permission** 
Select **Microsoft Graph** 
Select **Application permissions**

Add the following permissions:
- Device.ReadWrite.All   (Allows for updating of device attributes)
- Directory.Read.All     (Allows for listing of AD Groups)
- DeviceManagementManagedDevices.ReadAll (Allows for reading and updating devices)
- AdministrativeUnit.ReadWrite.All (Allows for updating AUs)

Reduced permissions for monitoring without changes (will log changes, but not have permissions to apply them):
- Device.Read.All
- DeviceManagementManagedDevices.Read.All

#### Certificates & Secrets

##### Certificate-based Authentication

This assumes you already have a certificate you will be using for authentication.  
You will need a .CER/.CRT/.PEM file with the public key for the App Registration.
But you will also need a PFX with the private key (and a set pass phrase) to configure in the Application

- Click on **Certificates** 
- Click on **Upload Certificate**
- Select the file to upload from your local machine.
- If you do not enter a description, it will use the Subject which you will need to use in the Application Configuration settings.

##### Secret-based Authentication

- Click on **Client secrets**
- Click on **+ New client secret**
- Store the value in a secure location (key vault is recommended).  This will be used in the configuration of the application. 

### Azure AD Configuration

In order to limit the function app to only be able to update only relevant Security Groups, use one of the following two methods:
- Option 1:  Make the App Registration the owner of each of the groups the function app should have permissions to edit  
- Option 2:  Assign the App Registration a custom role on an AU containing all and only the relevant groups with the following permissions:
   - microsoft.directory/groups/members/read
   - microsoft.directory/groups.security/members/update

Any groups added to the application under Tags will have to have the service principals permissions added in order to work as expected.

Alternatively, you could add GroupMember.ReadWrite.All to the Graph Permissions granted to the App Registrations, but it is not recommended due to the broad access.

Do not make these changes if using reduced permissions to monitor changes but not apply them.


## Web Application Configuration

### Environment Variables
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

<b>"COSMOS_DATABASE_NAME" : ""</b><br/>
(Optional) The name of the Cosmos DB database. Default is "DelegationStationData"

<b>"COSMOS_CONTAINER_NAME" : ""</b><br/>
(Optional) The name of the Cosmos DB container. Default is "DeviceData"

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

### Certificate Configuration

This is only needed if you are connecting to Graph using certificates.

#### Local

- In Windows Explorer, go to the location of the PFX with the private key.
- Right-click on the file and choose 'Install PFX'
- Choose **Current User**
- Confirm the correct PFX file is listed and click **Next**
- Enter password for the private key and click **Next**
- Click on **Place all certificates in the following store** and click on **Browse...**
- Choose **Personal**
- Review the final screen and choose **Finish**

#### Azure

- In the Azure Portal, go the Function App resource
- Click on **Settings** -> **Certificates**
- Click on **Bring your own certificates (.pfx)**
- Click on **+ Add certificate**
- Choose **Upload certificate (.pfx)** 
- Enter the file location and password and click on **Validate**
- Click on **Add**

*Note:  In order to keep the certificates separate the WebApp and Function App need to be deployed in different RGs.  Certs are shared across webspaces.  More info here:  https://learn.microsoft.com/en-us/azure/app-service/app-service-plan-manage#move-an-app-to-another-app-service-plan

## Update Devices Configuration

### Environment Variables
Use the `local.settings.json.template` file to help setup your local configuration file.  
When deploying to Azure, these will go in your Configuration Settings or Environment Variables section, which is separate in some versions of the portal.  

<b>"AzureEnvironment" : ""</b><br/>
"AzurePublicCloud", "AzureUSDoD", or "AzureUSGovernment" depending on the Azure environment you are using.

<b>"COSMOS_CONNECTION_STRING": ""</b><br/>
Can be found in the Azure Portal under the Cosmos DB -> Keys -> Primary Connection String

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

### Certificate Configuration

This is only needed if you are connecting to Graph using certificates.

#### Local

- In Windows Explorer, go to the location of the PFX with the private key.
- Right-click on the file and choose 'Install PFX'
- Choose **Current User**
- Confirm the correct PFX file is listed and click **Next**
- Enter password for the private key and click **Next**
- Click on **Place all certificates in the following store** and click on **Browse...**
- Choose **Personal**
- Review the final screen and choose **Finish**

#### Azure

- In the Azure Portal, go the Function App resource
- Click on **Settings** -> **Certificates**
- Click on **Bring your own certificates (.pfx)**
- Click on **+ Add certificate**
- Choose **Upload certificate (.pfx)** 
- Enter the file location and password and click on **Validate**
- Click on **Add**

*Note:  In order to keep the certificates separate the WebApp and Function App need to be deployed in different RGs.  Certs are shared across webspaces.  More info here:  https://learn.microsoft.com/en-us/azure/app-service/app-service-plan-manage#move-an-app-to-another-app-service-plan

# Special Instructions: Transitioning Microsoft Graph to Managed Identity (v2.4)

In 2.4, Delegation Station defaults to use Managed Identity authentication to Graph.  The v2.4 special instructions cover the steps to use the exiting methods.  These instructions are specific to the migration to Managed Identity only.  The staging section can be run prior to upgrading to v2.4, but you must be on v2.4 prior to switching over.

For a new deployment, follow the standard setup in [`README.md`](README.md).

FIX:  Keep the Web Application app registration: it is still used for interactive user sign-in. The app-registration Graph application permissions and credentials are only needed if an application cannot use managed identity.



## Staging

### Managed Identity Setup
- Verify that a **system-assigned managed identity** exists on the Web App, UpdateDevices Function App, and CorporateIdentifierSync Function App.
- In the WebApp's App Registration, create a Federated Credential to use the managed identity:
    - Click on **Certificates & secrets**
    - Click on **Federated credentials**
    - Click on **+ Add credential**
    - Select scenario **Managed Identity**
    - Click on **Select a managed identity**
        - Choose the subscription
        - Under **Managed identity**, select **App Service**
        - Choose the identity of the correct DelegationStation web app
        - Click **Select**
    - Under **Credential details**, enter a name, for example `DEV-DSWebApp-MSI`
    - Click **Add**
### Grant Permissions to Managed Identities

All the Application Permissions granted to the App Registrations will need to be granted to the Managed Identities.

| Managed Identity | Graph Application Permissions |
| --- | --- |
| Update Devices function | AdministrativeUnit.ReadWrite.All<br>Device.ReadWrite.All<br>DeviceManagementManagedDevices.Read.All<br>DeviceManagementManagedDevices.PrivilegedOperations.All<br>[**Development Only**] GroupMember.ReadWrite.All |
| CorpID Sync function | DeviceManagementManagedDevices.ReadWrite.All<br>DeviceManagementServiceConfig.ReadWrite.All |
| DS Web Application | AdministrativeUnit.Read.All<br>DeviceManagementManagedDevices.Read.All<br>Group.Read.All |


**[Development Only]**  Edit and run `scripts/configureGraphMSIAuth.sh` in CloudShell to apply the changes. *(Note:  The script currently assumes all resources are in the same resource groups and assigns `GroupMember.ReadWrite.All` to the UpdateDevices identity, which is a broader permission than we recommend for Production.)*  

**TBD for official production instructions:** Confirm the production Microsoft Graph permission configuration involving the Administrative Unit (AU), including how it applies to the managed identities. Do not assume the development script's `GroupMember.ReadWrite.All` assignment is the production configuration.
### Verify Permissions
To see the permissions within the Azure Portal:


- Go to **Entra** -> **Enterprise Applications** 
- Click on the Application type filter and choose Managed Identities
- For each resource:
    - Click on the Managed Identity with the same name
    - Under Security choose Permissions
    - Verify all the permissions for that application are listed.


### No additional changes until approved transition window

Keep the existing app-registration Graph permissions and credentials until managed identity authentication has been tested. Do not remove them before the corresponding managed identity access is configured.

## Switch-Over

### [Downtime Required] CorpIDSync 

#### Disable Functions

- In the function, under **Settings**, go to **Environment Variables**
    - Search for 'Trigger'
    - Click on **Show Values**
    - Make a note if the frequency of the function doesn't match the following as you will need to adjust some of the steps accordingingly:
        - AddDevicesTriggerTime - every 5 minutes
        - DeleteDevicesTriggerTime - every 5 minutes
        - ConfirmSync - every 4 hours
        - ReconcileSync - every 12 hours
    - Click on Advanced Edit
        - Add the following entries:  
```json
        {  "name": "AzureWebJobs.AddNewDevices.Disabled", "value": "1", "slotSetting": false  },  
        {  "name": "AzureWebJobs.ConfirmSync.Disabled", "value": "1", "slotSetting": false  },  
        {  "name": "AzureWebJobs.CorpIDAudit.Disabled", "value": "1", "slotSetting": false  },  
        {  "name": "AzureWebJobs.DeviceDeletion.Disabled", "value": "1", "slotSetting": false  },  
        {  "name": "AzureWebJobs.ReconcileSyncState.Disabled", "value": "1", "slotSetting": false  },
```

- Click on **Add**
- Search for 'Disabled' to verify you see them but DO NOT click Apply yet.
- In a new tab, on the function page under **Development Tools**, go to **Console**
    - Check the current time (which is in UTC):  `date /T`
    - `cd ../../LogFiles/Application/Functions/Host`
    -  \`ls -ltrh \*.log\`
    - You are interested in the latest log, which will be at the bottom. Replaced <logfile> in the steps below with that file name.  These commands will return the last start/stop of each function.
    - `grep Execut <logfile> | grep Reconcile`
        - This function shouldn't have any work to do, but to be safe make sure it ran and completed within the last 12 hours.
    - `grep Execut <logfile> | grep Confirm`
        - Make sure this function has run and completed within the last 4 hours.  If it's about to trigger again, let it run and the check again.
    - `grep Execut <logfile> | grep -e Add -e Delet`
        - This will be tricky since they run frequently but the goal is to see if you can find a window when neither is running.
- On the AppSettings screen, click on **Apply** to disable all CorpIDSync functions. 
- **Do not proceed** until you have confirmed no function is actively running.

#### Switch to Managed Identity Auth
- In the App Settings for the function:
    - Remove **AZURE\_FUNCTIONS\_ENVIRONMENT**
    - Click on **Apply**
    - Return to **Overview** screen and click on **CorpIDAudit** function
    - Click on **Test/Run** and then click on **Run**
- Validate that in the logs you see it using Managed Identity for Graph:
    ```
    2026-10-28T01:59:47.060 [Information] Using Managed Identity to authenticate to Graph service
    ```
- And that there are no errors reading from Graph:
    ```
    2026-10-28T01:59:47.342 [Information] [INFO] (CorpIDAudit.Run) Retrieving Corporate Identifier counts for audit...
    2026-10-28T01:59:47.743 [Information] [INFO] (GraphBetaService.GetCorporateDeviceIdentifierCountAsync) Fetching corporate device identifier count from Graph.
    2026-10-28T01:59:49.782 [Information] [INFO] (GraphBetaService.GetCorporateDeviceIdentifierCountAsync) Corporate device identifier count: 1
    ```



- Before re-enabling, kick off AddNewDevices and DeleteDevices to confirm they run without errors.  You may need to do this a couple times to make sure you pick up hosts to process.

#### Adjust CorpID Counter object

- If needed, readjust the CorpID counter based on the CorpID outputs before re-enabling the functions.

#### Re-Enable Functions
- In the function, return to **Environment Variables** under **Settings**.
- Search for 'Disabled'
- Delete all of the settings we created to disable the functions

### UpdateDevices
- From the function Overview screen, disable each of the functions.
- In the App Settings for the function:
    - Remove **AZURE\_FUNCTIONS\_ENVIRONMENT**
    - Click on **Apply**
    - Return to **Overview** screen and click on **UpdateDevices** function
    - Click on **Test/Run** and then click on **Run**
- Validate that in the logs you see it using Managed Identity for Graph:
  ```
  2026-10-28T01:59:47.060 [Information] Using Managed Identity to authenticate to Graph service
  ```
- And there are no errors read/writing via Graph.  You may need to run it a few times in order to see it processing a new device.
- Re-enable all of the functions

### DelegationStation WebApp
- In the App Settings for the webapp:
    - Remove **ASPNETCORE\_ENVIRONMENT** 
    - Click on **Apply**
- Have a user verify that they can do the following:
    - Log in successfully.
    - Under tag edit page retrieve Groups and AUs for assignment.

### Monitoring

Regularly monitor functions over the next few days and weeks for any errors.  
We will leave settings related to the old configuration in place until we are satisified there is no need to switch back.

## Rollback

If needed, you can switch back to the previous method of authentication by re-adding the following environment variables:

- For each of the functions:
    - AZURE\_FUNCTIONS\_ENVIRONMENT = Development
- For the webapp:
    - ASPNETCORE\_ENVIRONMENT = Development
## Cleanup

### [Downtime Required] CorpIDSync

- Remove the following App Settings:
    - AzureAd:ClientCertificates:CertificateDistinguishedName
    - AzureAd:ClientId
    - AzureAd:TenantId
- Click on Certificates:
    - Delete certificate
- You can validate they are gone with the following commands in the console:
    - env | grep AzureAd
    - ` certutil -store -user My`

### UpdateDevices

- Remove the following App Settings:
    - AzureAd:ClientCertificates:CertificateDistinguishedName
    - AzureAd:ClientId
    - AzureAd:TenantId
- Click on Certificates:
    - Delete certificate
- You can validate they are gone with the following commands in the console:
    - env | grep AzureAd
    - ` certutil -store -user My`

### WebApp

- Remove the following App Settings:
    - AzureAd\_\_ClientCertificates\_\_CertificateDistinguishedNameAzureAd:ClientId

- Click on Certificates:
    - Delete certificate
- You can validate they are gone with the following commands in the console:
    - env | grep AzureAd
    - ` certutil -store -user My`

### KeyVault

If the App Registration certs were added to KeyVault they can be deleted.

### App Registrations

- For the webapp App Registration
    - Under Certificates & Secrets, the certificate can be removed
    - Under API Permissions, remove all Application Graph permissions, leaving the Delegated User.Read and any openid/profile permissions

- The App Registrations for the function apps can both be deleted.


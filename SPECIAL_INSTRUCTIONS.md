# Special Instructions

Update instructions from 2.3 to 2.4. This release includes both infrastructure and software changes.


This software update includes implementing support for Graph Authenticatin via Managed Identity.  However, these instructions only include the configuration required to maintain current authentication until a separate transition activity can occur. 

For managed identity migration steps, see
[`SPECIAL_INSTRUCTIONS_MANAGED_IDENTITY.md`](SPECIAL_INSTRUCTIONS_MANAGED_IDENTITY.md).

## Staging

### DelegationStation WebApp
Under **Settings** --> **Environment Variables**, add the following:
- GRAPH_USE_APP_CREDENTIALS = true
- MAX\_CORPIDS\_ALLOWED = <current value set in CorpIDSync function>

This will disable Graph Managed Identity Auth until we can transition, using the existing app-registration certificate or client secret instead. Keep `ASPNETCORE_ENVIRONMENT` set to `Production` (or unset, which defaults to Production) so production features remain enabled. If it was previously set to `Development` only for Graph authentication, remove that override after adding `GRAPH_USE_APP_CREDENTIALS`.

### UpdateDevices
Under **Settings** --> **Environment Variables**, add the following:
- GRAPH_USE_APP_CREDENTIALS = true

This will disable Graph Managed Identity Auth until we can transition. Keep `AZURE_FUNCTIONS_ENVIRONMENT` set to `Production` (or unset). If it was previously set to `Development` only for Graph authentication, remove that override after adding `GRAPH_USE_APP_CREDENTIALS`.

### CorpIDSync
Requires a down-time so this update cannot be staged.

## Deployment

### [Downtime Required] Disable CorpIDSync Functions

- In the function, under **Settings**, go to **Environment Variables**
    - Search for 'Trigger'
    - Click on **Show Values**
    - Make a note if the frequency of the function doesn't match the following as you will need to adjust some of the steps accordingly:
        - AddDevicesTriggerTime - every 5 minutes
        - DeleteDevicesTriggerTime - every 5 minutes
        - ConfirmSyncTriggerTime - every 4 hours
        - ReconcileSyncStateTriggerTime - every 12 hours
        - CorpIDAuditTriggerTime - every 4 hours
    - Click on Advanced Edit
        - Add the following entries to the end: 
```json
        {  "name": "AzureWebJobs.AddNewDevices.Disabled", "value": "1", "slotSetting": false  },  
        {  "name": "AzureWebJobs.ConfirmSync.Disabled", "value": "1", "slotSetting": false  },  
        {  "name": "AzureWebJobs.CorpIDAudit.Disabled", "value": "1", "slotSetting": false  },  
        {  "name": "AzureWebJobs.DeviceDeletion.Disabled", "value": "1", "slotSetting": false  },  
        {  "name": "AzureWebJobs.ReconcileSyncState.Disabled", "value": "1", "slotSetting": false  }
```

- Click on **Add**
- Search for 'Disabled' to verify you see them but DO NOT click Apply yet.
- In a new tab, on the function page under **Development Tools**, go to **Console**
    - Check the current time (which is in UTC):  `date /T`
    - `cd ../../LogFiles/Application/Functions/Host`
    -  `ls -ltrh \*.log`
    - You are interested in the latest log, which will be at the bottom. Replace <logfile> in the steps below with that file name.  These commands will return the last start/stop of each function.
    - `grep Execut <logfile> | grep Reconcile`
        - This function shouldn't have any work to do, but to be safe make sure it ran and completed within the last 12 hours.
    - `grep Execut <logfile> | grep Confirm`
        - Make sure this function has run and completed within the last 4 hours.  If it's about to trigger again, let it run and the check again.
    - `grep Execut <logfile> | grep -e Add -e Delet`
        - This will be tricky since they run frequently but the goal is to see if you can find a window when neither is running.
- On the AppSettings screen, click on **Apply** to disable all CorpIDSync functions. 
- **Do not proceed** until you have confirmed no function is actively running.

### Update CorpID Setting
Under **Settings** --> **Environment Variables**, add the following:
- GRAPH_USE_APP_CREDENTIALS = true

This keeps the existing app-registration certificate or client secret authentication for Graph. Keep `AZURE_FUNCTIONS_ENVIRONMENT` set to `Production` (or unset), and remove any `ASPNETCORE_ENVIRONMENT` or `AZURE_FUNCTIONS_ENVIRONMENT` Development override added only for Graph authentication.

### Deploy Updated Infrastructure
Update the infrastructure configuration to the 2.4 baseline which will include resource modifications required to go from .NET8 to .NET10.

### Software
Deploy v2.4 software to both function apps and the webapp.

### Configure Banner Notification

- Use notepad to create a text file called maintenance-banner.txt
    - Enter the following text and save in your Downloads file:
    ```text
    red
    ATTN:  Delegation Station update is in progress.  There will be a temporary delay of CorpID sync services during the update, but post-enrollment processing will still be active.      
    ```

- Bring up an Explorer window and open up the Downloads directory. 
- In the DS webapp in the Azure Portal, Go to Advanced Tools.
    - Click on Debug console in the menu
    - Go into site/wwwroot/wwwroot
- From the explorer window, drag maintenance-banner.txt into the window
- Users should see the updated banner shortly
### Verify Deployment

#### Delegation Station WebApp
For initial checkout, user will need to validate the following:

- They are able to login
- They can complete a device search without performance impacts
- They can search for AUs and Groups related to settings on the Tag Edit screen
- Can access new pages and they display okay
- 
#### UpdateDevices

Manually kick off the UpdateDevices function and confirm:

- It is able to successfully query graph for enrolled devices.
- Scan through logs to verify no other errors requiring immediate attention are seen.

Manually kick off the other two functions and confirm there are no unexpected errors in their logs as well.


#### CorpIDsync

Manually kick off the CorpIDAudit function and confirm:

- It is able to successfully query graph for the CorpID count.
- Scan through logs to verify no other errors requiring immediate attention are seen.

Kick off the other functions one at a time to verify they function.  Make sure they run to completion.
### Modify CorpID counter and related settings

If you've observed some drift with the CorpID counter you can make adjustments while the software is disabled.

- If we have a reserved count of > 0, it should be safe to reset it to 0.
- If Devices in Synced State > DS CorpIDCount, we should set the CorpIDCount == Synced State.
- If Corporate Identifiers in Intune > DS CorpIDCount, we should set CorpIDCount == # in Intune.

If needed, now is also when you can raise the internal limit on CorpIDs:

- MAX\_CORPIDS\_ALLOWED = <new number>

You will also need to change the value of the setting you just added to the WebApp

- MAX\_CORPIDS\_ALLOWED = <new number>

### [End Downtime] Re-enable CorpIDSync functions

- In the function, under **Settings**, go to **Environment Variables**
- Search for 'Disabled'.
- Delete each of the settings we added earlier to disable the functions.
- Click 'Apply'
- Verify AddNewDevices and DeviceDeletion kick off on their own within the next five minutes.  (Verify the other functions later as they have a longer window.)


## Rollback
**Note**:  If the CorpID function was re-enabled, you must repeat the steps to try to shut it down without interrupting current processing.

To revert to the previous version, the infrastructure changes for .NET10 will need to be backed out.
Once that is done, v2.3 of DelegationStation can be redeployed.

The other changes can safely remain.

**Note**:  If the CorpID function was re-enabled, you must repeat the steps to try to shut it down without interrupting current processing.

## Post-Deployment Work

### Configure Banner Notification

- In notepad edit maintenance-banner.txt
- Enter the following text and save in your Downloads file:

  ```text
  green
  ATTN:  Delegation Station update is complete.  CorpID services are back online.
  ```


- Bring up an Explorer window and open up the Downloads directory.
- In the DS webapp in the Azure Portal, Go to Advanced Tools.
    - Click on Debug console in the menu
    - Go into site/wwwroot/wwwroot
- From the explorer window, drag maintenance-banner.txt into the window
- Users should see the updated banner shortly

### Manually clean up device entries with non-space whitespace or hidden characters

Review existing device entries whose Make, Model, or Serial Number contains whitespace other than a regular space (`" "`) or hidden characters.

- In the Cosmos DB account, open **Data Explorer** and select the device container.
- Run a query to identify matching device entries:
    ```sql
    SELECT d.id, d.Make, d.Model, d.SerialNumber
    FROM d
    WHERE d.Type = "Device"
    AND (
      REGEXMATCH(d.Make, "[^-a-zA-Z0-9\_.,&() ]")
      OR REGEXMATCH(d.Model, "[^-a-zA-Z0-9\_.,&()+ ]")
      OR REGEXMATCH(d.SerialNumber, "[^-a-zA-Z0-9\_. ]")
    )
    ```
- Review each match before deleting it; do not delete entries based solely on the query results.
- Use **Data Explorer** to manually delete the confirmed unwanted entries.
- Run the query again to confirm the intended entries were removed.

## Cleanup

Remove maintenance-banner.txt day after completion.

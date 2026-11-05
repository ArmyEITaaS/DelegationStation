#! /bin/bash

###
###  ENV-specific settings
###
###  Set these to the resource and resource group names you are using in your deployment
###

GRAPH_APP_ID="00000003-0000-0000-c000-000000000000" # Microsoft Graph's App ID

rg="mpeckgcch2026-rg"

updateDS_name="updatedevices-msi"
corpIDsync_name="corpidsync-msi"
webapp_name="delegationstation-msi"


# Required for running in Git Bash 
# Without it "/" will get converted to C:/ style paths
export MSYS_NO_PATHCONV=1

###
### Get Role IDs
###

### SETUP

# Get the object ID using the appId
GRAPH_SP_OID=$(az rest --method GET \
  --url "https://graph.microsoft.us/v1.0/servicePrincipals(appId='${GRAPH_APP_ID}')" \
  --query "id" -o tsv)

echo "Graph Object ID: $GRAPH_SP_OID"
echo ""

### WebApp
AUReadID=`az rest --method GET --url "https://graph.microsoft.us/v1.0/servicePrincipals/${GRAPH_SP_OID}/appRoles" --query "value[?value=='AdministrativeUnit.Read.All'].id" -o tsv`
echo "AU Read Role ID: $AUReadID"

ManagedDevicesReadID=`az rest --method GET --url "https://graph.microsoft.us/v1.0/servicePrincipals/${GRAPH_SP_OID}/appRoles" --query "value[?value=='DeviceManagementManagedDevices.Read.All'].id" -o tsv`
echo "Managed Devices Read Role ID: $ManagedDevicesReadID"

GroupReadID=`az rest --method GET --url "https://graph.microsoft.us/v1.0/servicePrincipals/${GRAPH_SP_OID}/appRoles" --query "value[?value=='Group.Read.All'].id" -o tsv`
echo "Group Read Role ID: $GroupReadID"

# UpdateDevices

AUReadWriteID=`az rest --method GET --url "https://graph.microsoft.us/v1.0/servicePrincipals/${GRAPH_SP_OID}/appRoles" --query "value[?value=='AdministrativeUnit.ReadWrite.All'].id" -o tsv`
echo "AU ReadWrite Role ID: $AUReadWriteID"

DeviceReadWriteID=`az rest --method GET --url "https://graph.microsoft.us/v1.0/servicePrincipals/${GRAPH_SP_OID}/appRoles" --query "value[?value=='Device.ReadWrite.All'].id" -o tsv`
echo "Device ReadWrite Role ID: $DeviceReadWriteID"

ManagedDevicesPrivOpID=`az rest --method GET --url "https://graph.microsoft.us/v1.0/servicePrincipals/${GRAPH_SP_OID}/appRoles" --query "value[?value=='DeviceManagementManagedDevices.PrivilegedOperations.All'].id" -o tsv`
echo "Managed Device Privileged Operation Role ID: $ManagedDevicesPrivOpID"

# DeviceManagementManagedDevices.Read.All retrieved above
echo "Managed Devices Read Role ID: $ManagedDevicesReadID"


GroupMemberReadWriteID=`az rest --method GET --url "https://graph.microsoft.us/v1.0/servicePrincipals/${GRAPH_SP_OID}/appRoles" --query "value[?value=='GroupMember.ReadWrite.All'].id" -o tsv`
echo "Group Member ReadWrite Role ID: $GroupMemberReadWriteID"

# CorpID
ManagedDevicesReadWriteID=`az rest --method GET --url "https://graph.microsoft.us/v1.0/servicePrincipals/${GRAPH_SP_OID}/appRoles" --query "value[?value=='DeviceManagementManagedDevices.ReadWrite.All'].id" -o tsv`
echo "Managed Devices ReadWrite Role ID: $ManagedDevicesReadWriteID"

ManagedDevicesConfigReadWriteID=`az rest --method GET --url "https://graph.microsoft.us/v1.0/servicePrincipals/${GRAPH_SP_OID}/appRoles" --query "value[?value=='DeviceManagementServiceConfig.ReadWrite.All'].id" -o tsv`
echo "Managed Devices Config ReadWrite Role ID: $ManagedDevicesConfigReadWriteID"
echo ""


###
### Get Managed IDs
### 

echo "Getting managed ids..."
echo ""

updateFunction_id=`az functionapp show --resource-group $rg --name $updateDS_name --query "{identity:identity}" | jq '.identity.principalId' | tr -d '"'`
echo "Update Devices Function Managed Identity ID: $updateFunction_id"

corpIDSync_id=`az functionapp show --resource-group $rg --name $corpIDsync_name --query "{identity:identity}" | jq '.identity.principalId' | tr -d '"'`
echo "Corporate Identity Sync Function Managed Identity ID: $corpIDSync_id"

webapp_id=`az webapp show --resource-group $rg --name $webapp_name --query "{identity:identity}" | jq '.identity.principalId' | tr -d '"'`
echo "Delegation Station WebApp Managed Identity ID: $webapp_id"
echo ""



### 
### Make Assigningments
###

echo "Assigning permission for Delegation Station webapp"

az rest --method POST --url "https://graph.microsoft.us/v1.0/servicePrincipals/${GRAPH_SP_OID}/appRoleAssignedTo" --headers "Content-Type=application/json" \
	--body "{
            \"principalId\": \"${webapp_id}\",
            \"resourceId\": \"${GRAPH_SP_OID}\",
            \"appRoleId\": \"${AUReadID}\"
        }"

az rest --method POST --url "https://graph.microsoft.us/v1.0/servicePrincipals/${GRAPH_SP_OID}/appRoleAssignedTo" --headers "Content-Type=application/json" \
	--body "{
            \"principalId\": \"${webapp_id}\",
            \"resourceId\": \"${GRAPH_SP_OID}\",
            \"appRoleId\": \"${ManagedDevicesReadID}\"
        }"

az rest --method POST --url "https://graph.microsoft.us/v1.0/servicePrincipals/${GRAPH_SP_OID}/appRoleAssignedTo" --headers "Content-Type=application/json" \
	--body "{
            \"principalId\": \"${webapp_id}\",
            \"resourceId\": \"${GRAPH_SP_OID}\",
            \"appRoleId\": \"${GroupReadID}\"
        }"
echo "Done"
echo ""


echo "Assigning permission for UpdateDevices function"
az rest --method POST --url "https://graph.microsoft.us/v1.0/servicePrincipals/${GRAPH_SP_OID}/appRoleAssignedTo" --headers "Content-Type=application/json" \
	--body "{
            \"principalId\": \"${updateFunction_id}\",
            \"resourceId\": \"${GRAPH_SP_OID}\",
            \"appRoleId\": \"${AUReadWriteID}\"
        }"

az rest --method POST --url "https://graph.microsoft.us/v1.0/servicePrincipals/${GRAPH_SP_OID}/appRoleAssignedTo" --headers "Content-Type=application/json" \
	--body "{
            \"principalId\": \"${updateFunction_id}\",
            \"resourceId\": \"${GRAPH_SP_OID}\",
            \"appRoleId\": \"${DeviceReadWriteID}\"
        }"

az rest --method POST --url "https://graph.microsoft.us/v1.0/servicePrincipals/${GRAPH_SP_OID}/appRoleAssignedTo" --headers "Content-Type=application/json" \
	--body "{
            \"principalId\": \"${updateFunction_id}\",
            \"resourceId\": \"${GRAPH_SP_OID}\",
            \"appRoleId\": \"${ManagedDevicesPrivOpID}\"
        }"

az rest --method POST --url "https://graph.microsoft.us/v1.0/servicePrincipals/${GRAPH_SP_OID}/appRoleAssignedTo" --headers "Content-Type=application/json" \
	--body "{
            \"principalId\": \"${updateFunction_id}\",
            \"resourceId\": \"${GRAPH_SP_OID}\",
            \"appRoleId\": \"${GroupMemberReadWriteID}\"
        }"

az rest --method POST --url "https://graph.microsoft.us/v1.0/servicePrincipals/${GRAPH_SP_OID}/appRoleAssignedTo" --headers "Content-Type=application/json" \
	--body "{
            \"principalId\": \"${updateFunction_id}\",
            \"resourceId\": \"${GRAPH_SP_OID}\",
            \"appRoleId\": \"${ManagedDevicesReadID}\"
        }"
echo "Done"
echo ""


echo "Assigning permission for CorpIDSync function"
az rest --method POST --url "https://graph.microsoft.us/v1.0/servicePrincipals/${GRAPH_SP_OID}/appRoleAssignedTo" --headers "Content-Type=application/json" \
	--body "{
            \"principalId\": \"${corpIDSync_id}\",
            \"resourceId\": \"${GRAPH_SP_OID}\",
            \"appRoleId\": \"${ManagedDevicesReadWriteID}\"
        }"

az rest --method POST --url "https://graph.microsoft.us/v1.0/servicePrincipals/${GRAPH_SP_OID}/appRoleAssignedTo" --headers "Content-Type=application/json" \
	--body "{
            \"principalId\": \"${corpIDSync_id}\",
            \"resourceId\": \"${GRAPH_SP_OID}\",
            \"appRoleId\": \"${ManagedDevicesConfigReadWriteID}\"
        }"
echo "Done"
echo ""

exit



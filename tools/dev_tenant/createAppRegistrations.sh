#!/bin/bash

# This script has been tested being run from Git Bash
# It assumes you are already logged into the Azure tenant where you want to store your App Registrations
# 
# This only generates the app registration
# The upload of the .CER file for each registration must be done manually as there is no command-line option to do that
# Permissions must be added manually as well (but could be scripted in the future - commented out code is from a previous atempt to do so.
# Additionally any authentication/token configuration for WebApp must still be done manually (although believe this could be automated in the future)

software_array=("WebApp" "UpdateDevices" "CorpIDSync")
environment_array=("AIRS" "local")

for e in "${environment_array[@]}"; do
  for sw in "${software_array[@]}"; do
    name="DS-$sw-$e"

    # Create App Reg w/o permissions
    echo "Creating App Reg: $name"
    appID=`az ad app create --display-name $name --query appId -o tsv`
    #echo $appID

    # These steps are done when you generate a AppReg from the portal
    # These make your app visible on the Enterprise App screen - unclear if not having them impacts other functionality
    if [ "$sw" = "WebApp" ]; then
	    echo "setting $name to Enterprise App"
      az ad sp create --id $appID 
      az ad sp update --id $appID --set 'tags=["WindowsAzureActiveDirectoryIntegratedApp"]'
    fi

  done
done

###
### Reference code from attempt to modify permissions
###

#
# Get ID for MS Graph resource
#
# graphID=`az ad sp list -o json --all | jq -r '.[] | select(.appDisplayName=="Microsoft Graph") | .appId'`
#echo "Graph ID: $graphID"

#
#Get ID for relevent permissions
#

 # DS Web UI
# AdministrativeUnit.Read.All
# auReadId=`az ad sp show --id 00000003-0000-0000-c000-000000000000 -o json | jq -r '.appRoles.[] | select(.value=="AdministrativeUnit.Read.All") | .id'`
#echo "AdministrativeUnit.Read.All == $auReadId"

# Group.Read.All
# groupReadId=`az ad sp show --id 00000003-0000-0000-c000-000000000000 -o json | jq -r '.appRoles.[] | select(.value=="Group.Read.All") | .id'`
#echo "Group.Read.All == $groupReadId"

# User.Read (Delegated)
# userReadId=`az ad sp show --id 00000003-0000-0000-c000-000000000000 -o json | jq -r '.appRoles.[] | select(.value=="User.Read.All") | .id'`
#echo "User.Read.All == $userReadId"


# UpdateDevices
# AdministrativeUnit.ReadWrite.All
# auReadWriteId=`az ad sp show --id 00000003-0000-0000-c000-000000000000 -o json | jq -r '.appRoles.[] | select(.value=="AdministrativeUnit.ReadWrite.All") | .id'`
#echo "AdministrativeUnit.ReadWrite.All == $auReadWriteId"

# Device.ReadWrite.All
# deviceReadWriteId=`az ad sp show --id 00000003-0000-0000-c000-000000000000 -o json | jq -r '.appRoles.[] | select(.value=="Device.ReadWrite.All") | .id'`
#echo "Device.ReadWrite.All == $deviceReadWriteId"

# DeviceManagementManagedDevices.PrivilegedOperations.All
# managedDevicePrivilegedOpsId=`az ad sp show --id 00000003-0000-0000-c000-000000000000 -o json | jq -r '.appRoles.[] | select(.value=="DeviceManagementManagedDevices.PrivilegedOperations.All") | .id'`
#echo "DeviceManagementManagedDevices.PrivilegedOperations.All == $managedDevicePrivilegedOpsId"

# DeviceManagementDevices.Read.All
# managedDeviceReadId=`az ad sp show --id 00000003-0000-0000-c000-000000000000 -o json | jq -r '.appRoles.[] | select(.value=="DeviceManagementManagedDevices.Read.All") | .id'`
#echo "DeviceManagementManagedDevices.Read.All == $managedDeviceReadId"

# GroupMember.ReadWrite.All
# groupReadWriteId=`az ad sp show --id 00000003-0000-0000-c000-000000000000 -o json | jq -r '.appRoles.[] | select(.value=="Group.ReadWrite.All") | .id'`
#echo "Group.ReadWrite.All == $groupReadWriteId"

# CorpIDSync
# DeviceManagementMangedDEvices.ReadWRite.All
# managedDeviceReadWriteId=`az ad sp show --id 00000003-0000-0000-c000-000000000000 -o json | jq -r '.appRoles.[] | select(.value=="DeviceManagementManagedDevices.ReadWrite.All") | .id'`
#echo "DeviceManagementManagedDevices.ReadWrite.All == $managedDeviceReadWriteId"

# DevicemanagementServiceConfig.ReadWriteAll
# serviceConfigReadWriteId=`az ad sp show --id 00000003-0000-0000-c000-000000000000 -o json | jq -r '.appRoles.[] | select(.value=="DeviceManagementServiceConfig.ReadWrite.All") | .id'`
#echo "DeviceManagementServiceConfig.ReadWrite.All == $serviceConfigReadWriteId"

#
# Create Update Devices App Registration
#


# resources='[{ "resourceAppId": '$graphId', "resourceAccess": [{"id": '$auReadWriteId',"type": "Scope"},{"id": '$deviceReadWriteId',"type": "Scope"},{"id": '$managedDeviceReadId',"type": "Scope"},{"id": '$groupReadWriteId',"type": "Scope"},{"id": '$managedDevicePrivilegedOpsId', "type": "Scope"}]}]'
# echo $resources

# echo $resources | jq

#az ad app create --display-name testapp --required-resource-accesses $resources --debug


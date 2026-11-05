#! /bin/bash

# This script is written to run in GitBash
# where you are logged into the Azure tenant you want to create these objects
#
# NOTE:  Be aware.  It will create duplicate AUs/groups if run multiple times since those are unique per ID.

# Get tenant domain name
az account get-access-token -o none
domain=`az rest --method get --url 'https://graph.microsoft.us/v1.0/domains?$select=id' | jq -r '.value.[] | .id'`
echo "Adding tenant resources to: $domain"
echo ""


# Create Delegation Station Admin Group and return ID (for configuration)
echo "Generating DS Admin Group..."
adminGroupID=`az ad group create --display-name "DS Admins" --mail-nickname "DSAdmins" | jq -r '.id'`
echo "DS Admin Group ID:  $adminGroupID"
echo ""

# Create account to use for host logins
echo "Create account for host login..."
echo "Enter initial password: "
read temppass
echo ""
az ad user create --display-name "DS Host Login" --user-principal-name "DSHostLogin@$domain" --password $temppass -o none

# Create test users/groups and membership relationships
# cat user_list | while read line
# do
  # username=$line
  # displayName=$username
  # shortName=`echo $username | sed 's/ //g' | tr '[:upper:]' '[:lower:]'`
  # principalName="$shortName@$domain"
  # echo "Creating user: " $displayName $principalName
  # az ad user create --display-name "$displayName" --user-principal-name $principalName --password "1qaz2wsx!QAZ@WSX" -o none
  # userID=`az ad user show --id $principalName | jq -r '.id'`
  # echo "UserID: $userID"
  # echo ""


  # echo "Creating group: $username ($shortName)"
  # az ad group create --display-name "$username" --mail-nickname $shortName -o none
  # echo ""
# 
  # echo "Adding user $username to group $username"
  # az ad group member add --group "$username" --member-id $userID
  # echo ""

# done
# 
# echo "-----------"

# Create DS test device groups
for i in $(seq 1 3);
do
  deviceGroup="DS Device Group $i"
  shortName=`echo $deviceGroup | sed 's/ //g' | tr '[:upper:]' '[:lower:]'`
  echo "Creating group: " $deviceGroup
  az ad group create --display-name "$deviceGroup" --mail-nickname "$shortName"
done

# create DS test AUs
for i in $(seq 1 3);
do
  echo "Creating AU: DS AU $i"
  az rest --method post --url 'https://graph.microsoft.us/v1.0/directory/administrativeUnits' --body "{'displayName': 'DS AU $i', 'description': 'Delegation Station AU $i'}"
done




exit





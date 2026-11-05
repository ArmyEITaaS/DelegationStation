#!/bin/bash

#
#  This script is intended to be run locally in GitBash
#  where you are logged into your Azure subscription where you deploy resources
#  
#  It will generate self-signed certs to use with App Registrations for DS software
#
#  1.  Edit to the KeyVault you would like to store the certificates in
#  2.  (Optional) Edit the environment array for the environment names you are creating App Reg's for.  
#      By default it assumes you will want a set for you Local dev and for you Azure deployment.
#

# Upload this 
VAULTNAME="<VAULTNAME>"

create_cert()
{
  local name="$1"
  local ex="$2"


  openssl genrsa 2048 > $DIRNAME/$name.key
  openssl req -key $DIRNAME/$name.key -new -subj "/C=US/ST=TX/O=DelegationStation/CN=$name" -out $DIRNAME/$name.csr
  openssl x509 -signkey $DIRNAME/$name.key -in $DIRNAME/$name.csr -req -days 365 -out $DIRNAME/$name.crt
  winpty openssl pkcs12 -inkey $DIRNAME/$name.key -in $DIRNAME/$name.crt -export -passout "pass:$expass" -out $DIRNAME/$name.pfx

}

upload_cert_to_KV()
{
  local name="$1"
  local ex="$2"

  echo "Uploading $name cert to KeyVault $VAULTNAME"
  az keyvault certificate import --file $DIRNAME/$name.pfx --name $name --vault-name $VAULTNAME --password $ex


}

DIRNAME="certificates"
mkdir -p $DIRNAME

software_array=("WebApp" "UpdateDevices" "CorpIDSync")
environment_array=("local" "AIRS")

echo "Please enter cert export password:"
read expass
echo ""

for e in "${environment_array[@]}"; do
  echo "Generating certs for environment: $e"

  echo "Do you want to upload these certs to KeyVault (y|n)"
  read kvUpload
  echo ""

  for sw in "${software_array[@]}"; do
    echo "  Generate cert for: $sw"

    name="DS-$sw-$e"
    

    create_cert $name $expass

    if [ "$kvUpload" = "y" ]; then
      upload_cert_to_KV $name $expass
    fi


    echo ""

  done

  echo ""
done





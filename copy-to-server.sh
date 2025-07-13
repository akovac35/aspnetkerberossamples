#! /bin/bash

APP_HOST_SCP_USER=dev   # SCP user for the application host
APP_HOST=172.17.197.54 # Application host IP address
APP_HOST_PATH=/opt/app  # Path on the application host where files will be copied

rm -r ./bin/Release/net8.0/linux-x64/publish                        # Clean previous publish output
dotnet publish --sc -r linux-x64 -c Release                         # Publish the application for Linux x64 runtime
cp ./linux-server.keytab ./bin/Release/net8.0/linux-x64/publish     # Copy the Kerberos keytab file to the publish directory
cp ./linux-server.pfx ./bin/Release/net8.0/linux-x64/publish        # Copy the server certificate file to the publish directory
# Copy the published files to the application host using SCP
scp ./bin/Release/net8.0/linux-x64/publish/*  $APP_HOST_SCP_USER@$APP_HOST:$APP_HOST_PATH

echo "Files copied to $APP_HOST_SCP_USER@$APP_HOST:$APP_HOST_PATH"
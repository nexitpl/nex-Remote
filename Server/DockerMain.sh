#!/bin/bash

echo "Entered main script."

ServerDir=/var/www/nexRemote
nexRemoteData=/nexRemote-data

AppSettingsVolume=/nexRemote-data/appsettings.json
AppSettingsWww=/var/www/nexRemote/appsettings.json

if [ ! -f "$AppSettingsVolume" ]; then
	echo "Copying appsettings.json to volume."
	cp "$AppSettingsWww" "$AppSettingsVolume"
fi

if [ -f "$AppSettingsWww" ]; then
	rm "$AppSettingsWww"
fi

ln -s "$AppSettingsVolume" "$AppSettingsWww"

echo "Starting nexRemote server."
exec /usr/bin/dotnet /var/www/nexRemote/nexRemote_Server.dll
#!/bin/bash
echo "Thanks for trying nexRemote!"
echo

Args=( "$@" )
ArgLength=${#Args[@]}

for (( i=0; i<${ArgLength}; i+=2 ));
do
    if [ "${Args[$i]}" = "--host" ]; then
        HostName="${Args[$i+1]}"
    elif [ "${Args[$i]}" = "--approot" ]; then
        AppRoot="${Args[$i+1]}"
    fi
done

if [ -z "$AppRoot" ]; then
    read -p "Enter path where the nexRemote server files should be installed (typically /var/www/nexRemote): " AppRoot
    if [ -z "$AppRoot" ]; then
        AppRoot="/var/www/nexRemote"
    fi
fi

if [ -z "$HostName" ]; then
    read -p "Enter server host (e.g. remote.nex-it.pl): " HostName
fi

chmod +x "$AppRoot/nexRemote_Server"

echo "Using $AppRoot as the nexRemote website's content directory."

apt-get -y install curl
apt-get -y install software-properties-common
apt-get -y install gnupg

UbuntuVersion=$(lsb_release -r -s)
UbuntuVersionInt=$(("${UbuntuVersion/./}"))

# Install .NET Core Runtime.
if [ $UbuntuVersionInt -ge 2204 ]; then
    apt-get install -y aspnetcore-runtime-6.0
else
    wget -q https://packages.microsoft.com/config/ubuntu/$UbuntuVersion/packages-microsoft-prod.deb
    dpkg -i packages-microsoft-prod.deb
    add-apt-repository universe
    apt-get update
    apt-get -y install apt-transport-https
    apt-get -y install aspnetcore-runtime-6.0
    rm packages-microsoft-prod.deb
fi




# Install Caddy
apt install -y debian-keyring debian-archive-keyring apt-transport-https
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' | tee /etc/apt/sources.list.d/caddy-stable.list
apt update
apt install caddy


# Configure Caddy
caddyConfig="
$HostName {
    reverse_proxy 127.0.0.1:5000
}
"

echo "$caddyConfig" > /etc/caddy/Caddyfile


# Create nexRemote service.

serviceConfig="[Unit]
Description=nexRemote Server

[Service]
WorkingDirectory=$AppRoot
ExecStart=/usr/bin/dotnet $AppRoot/nexRemote_Server.dll
Restart=always
# Restart service after 10 seconds if the dotnet service crashes:
RestartSec=10
SyslogIdentifier=nexremote
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target"

echo "$serviceConfig" > /etc/systemd/system/nexRemote.service


# Enable service.
systemctl enable nexRemote.service
# Start service.
systemctl restart nexRemote.service


# Restart caddy
systemctl restart caddy
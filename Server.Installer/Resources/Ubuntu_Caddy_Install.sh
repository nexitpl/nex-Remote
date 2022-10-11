#!/bin/bash
echo "nex-Remote Serwer by nex-IT Jakub Potoczny"
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
    read -p "Podaj �cie�k�, w kt�rej powinny zosta� zainstalowane pliki serwera nex-Remote (zazwyczaj /var/www/nex-Remote): " AppRoot
    if [ -z "$AppRoot" ]; then
        AppRoot="/var/www/nex-Remote/"
    fi
fi

if [ -z "$HostName" ]; then
    read -p "Wpisz nazw� hosta serwera (np. remote.nex-it.pl): " HostName
fi

chmod +x "$AppRoot/nex-Remote_Server"

echo "Using $AppRoot jako katalog zawarto�ci witryny nex-Remote."

UbuntuVersion=$(lsb_release -r -s)

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
# HTTP->HTTPS redirects
http://remote.nex-it.pl {
  redir https://{host}{uri}
}

https://remote.nex-it.pl {
    # Custom SSL Conf
    tls /etc/ssl/certs/remote.nex-it.pl.crt /etc/ssl/private/remote.nex-it.pl.key
}

"

echo "$caddyConfig" > /etc/caddy/Caddyfile


# Create nex-Remote service.

serviceConfig="[Unit]
Description=nex-Remote

[Service]
WorkingDirectory=$AppRoot
ExecStart=/usr/bin/dotnet $AppRoot/nex-Remote_Server.dll
Restart=always
# Restart service after 10 seconds if the dotnet service crashes:
RestartSec=10
SyslogIdentifier=nex-Remote
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target"

echo "$serviceConfig" > /etc/systemd/system/nex-Remote.service


# Enable service.
systemctl enable nex-Remote.service
# Start service.
systemctl restart nex-Remote.service


# Restart caddy
systemctl restart caddy
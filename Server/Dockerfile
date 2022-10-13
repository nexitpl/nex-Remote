FROM ubuntu:jammy

EXPOSE 5000

ENV ASPNETCORE_ENVIRONMENT="Production"
ENV ASPNETCORE_URLS="http://*:5000"

RUN \
  apt-get -y update && \
  apt-get -y install \
  apt-utils \
  wget \
  apt-transport-https \
  unzip \
  acl \
  libssl1.0

RUN \
  apt-get -y install aspnetcore-runtime-6.0


RUN \
  mkdir -p /var/www/nexRemote && \
  mkdir /config && \
  wget -q https://github.com/nexitpl/nexRemote/releases/latest/download/nexRemote_Server_Linux-x64.zip && \
  unzip -o nexRemote_Server_Linux-x64.zip -d /var/www/nexRemote && \
  rm nexRemote_Server_Linux-x64.zip

RUN \
  mkdir -p /nexRemote-data && \
  sed -i 's/DataSource=nexRemote.db/DataSource=\/nexRemote-data\/nexRemote.db/' /var/www/nexRemote/appsettings.json

VOLUME "/nexRemote-data"

WORKDIR /var/www/nexRemote

COPY DockerMain.sh /

RUN chmod 755 /DockerMain.sh

ENTRYPOINT ["/DockerMain.sh"]
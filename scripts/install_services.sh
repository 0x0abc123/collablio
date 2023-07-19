#!/bin/bash
PATH=$PATH:/sbin:/usr/sbin
SCRIPT_DIR=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
COLLABLIO_USER=collablio

re='^uid=0(root)'
if [ ! "$(id | grep $re)" ] ; then
  echo 'This installer script must be run as root, exiting...'
  exit 1
fi

for var in "$@"
do
    if [ "$var" == "--dgraph" ] ; then OPT_DGRAPH=1 ; continue ; fi
    if [ "$var" == "--deb10" ] ; then OPT_DEBIAN=10 ; continue ; fi
    if [ "$var" == "--deb11" ] ; then OPT_DEBIAN=11 ; continue ; fi
    if [ "$var" == "--deb12" ] ; then OPT_DEBIAN=12 ; continue ; fi
done

echo "Installing Collablio...."
echo

if [ ! "$(which dotnet)" ]
then
    echo dotnet is not installed, installing...
    if [ ! -z "$OPT_DEBIAN" ]
    then
        PKG_URL="https://packages.microsoft.com/config/debian/${OPT_DEBIAN}/packages-microsoft-prod.deb"
        wget  "$PKG_URL" -O /tmp/packages-microsoft-prod.deb
	dpkg -i /tmp/packages-microsoft-prod.deb
	apt-get update &&  apt-get install -y apt-transport-https && apt-get update && apt-get install -y dotnet-sdk-3.1

    else
        echo "This installer script only supports Debian 10-12, exiting..."
        exit 1
    fi
else
    echo "dotnet found, skipping installation..."
fi


# $SCRIPT_DIR/daemon_collablio.sh
cat << __EOF_CD > $SCRIPT_DIR/daemon_collablio.sh
#!/bin/bash
cd $SCRIPT_DIR/..
while true
do
dotnet run --configuration Release
done
__EOF_CD

adduser "$COLLABLIO_USER" --system
chown -R "$COLLABLIO_USER" $SCRIPT_DIR/..
chmod a+x  $SCRIPT_DIR/daemon_collablio.sh

# /etc/systemd/system/collablio_daemon.service
cat << __EOF_DS > /etc/systemd/system/collablio_daemon.service
[Unit]
Description=Collablio Daemon
After=network.target

[Service]
Type=simple
User=$COLLABLIO_USER
ExecStart=$SCRIPT_DIR/daemon_collablio.sh
Restart=on-failure

[Install]
WantedBy=default.target
__EOF_DS

systemctl daemon-reload
systemctl enable collablio_daemon.service
systemctl start collablio_daemon.service

###########################################################
# If selected, install Dgraph standalone Docker service
###########################################################

if [ -z "$OPT_DGRAPH" ]
then
	echo "You have chosen not to install Dgraph, finished installation."
	exit 0
fi

if [ ! "$(which docker)" ]
then
    echo Docker is not installed, installing...
	apt-get update &&  apt-get install -y docker.io
else
    echo "Docker found, skipping installation..."
fi


# /etc/systemd/system/dgraph_daemon.service
cat << __EOF_DSD > /etc/systemd/system/dgraph_daemon.service
[Unit]
Description=Dgraph Daemon
After=docker.service
Requires=docker.service

[Service]
Type=simple
ExecStart=/usr/bin/docker run --rm -p 127.0.0.1:8080:8080 -p 127.0.0.1:9080:9080 -p 127.0.0.1:8000:8000 -v /dgraph:/dgraph dgraph/standalone:v20.11.3
Restart=on-failure

[Install]
WantedBy=default.target
__EOF_DSD


systemctl daemon-reload
systemctl enable dgraph_daemon.service
systemctl start dgraph_daemon.service

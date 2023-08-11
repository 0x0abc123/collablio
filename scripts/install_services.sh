#!/bin/bash
PATH=$PATH:/sbin:/usr/sbin
SCRIPT_DIR=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
COLLABLIO_USER=collablio
COLLABLIO_HOME=/var/collablio

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
	apt-get update &&  apt-get install -y apt-transport-https && apt-get update && apt-get install -y dotnet-sdk-6.0

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

#adduser "$COLLABLIO_USER" --system
groupadd --system "$COLLABLIO_USER"
useradd --system -d $COLLABLIO_HOME -s /bin/false -g "$COLLABLIO_USER" "$COLLABLIO_USER"
chown -R "$COLLABLIO_USER":"$COLLABLIO_USER" $SCRIPT_DIR/..
chmod a+x  $SCRIPT_DIR/daemon_collablio.sh
mkdir -p $COLLABLIO_HOME
chown -R "$COLLABLIO_USER":"$COLLABLIO_USER" $COLLABLIO_HOME

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

FINISHMSG="Installation of Collablio is complete,\n"\
"Run:\n"\
"setup.sh (to create Dgraph schema)\n"\
"adduser.sh (to create a Collablio user in Dgraph)\n"


if [ -z "$OPT_DGRAPH" ]
then
	echo "You have chosen not to install Dgraph, finished installation."
	echo -e $FINISHMSG
	exit 0
fi

export DGRAPHVERSION=v21.03.2
echo "downloading and installing dgraph $DGRAPHVERSION ..."

curl https://get.dgraph.io -sSf > dgraph.sh
sed -i 's/--lru_mb 2048//g' dgraph.sh
sed -i 's/grep -Fx/grep -F/g' dgraph.sh
bash dgraph.sh -y -s -v=$DGRAPHVERSION

echo -e $FINISHMSG

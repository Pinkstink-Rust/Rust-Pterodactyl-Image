#!/bin/bash
cd /home/container

# Make internal Docker IP address available to processes.
export INTERNAL_IP=`ip route get 1 | awk '{print $NF;exit}'`

if [ ! -f ./steam/steamcmd.sh ]; then
    echo "Downloading SteamCMD"
    curl -sSL -o ./steamcmd.tar.gz http://media.steampowered.com/installer/steamcmd_linux.tar.gz
    mkdir -p ./steam
    tar -xzf steamcmd.tar.gz -C ./steam
    rm ./steamcmd.tar.gz
fi

if [ ! -z "${BRANCH}" ];
then
    echo "Updating ${BRANCH} Branch"
    # Update Rust Server
    ./steam/steamcmd.sh +login anonymous +force_install_dir /home/container +app_update 258550 -beta ${BRANCH} +quit
    # Validate Rust Server
    ./steam/steamcmd.sh +login anonymous +force_install_dir /home/container +app_update 258550 -beta ${BRANCH} validate +quit
else
    echo "Updating Main Branch"
    # Update Rust Server
    ./steam/steamcmd.sh +login anonymous +force_install_dir /home/container +app_update 258550 +quit
    # Validate Rust Server
    ./steam/steamcmd.sh +login anonymous +force_install_dir /home/container +app_update 258550 validate +quit
fi

# Replace Startup Variables
MODIFIED_STARTUP=`eval echo $(echo ${STARTUP} | sed -e 's/{{/${/g' -e 's/}}/}/g' -e 's/\"/\\\"/g')`

# OxideMod has been replaced with uMod
if [ ! -z "${OXIDE_URL}" ]; then
    echo "Updating Oxide..."
    curl -sSL "${OXIDE_URL}" >oxide.zip
    unzip -o -q oxide.zip
    rm oxide.zip
    echo "Disabling Oxide Sandbox"
    touch RustDedicated_Data/Managed/oxide.disable-sandbox
fi

# Fix for Rust not starting
export LD_LIBRARY_PATH=$LD_LIBRARY_PATH:$(pwd)

mkdir -p /home/container/tmp
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="/home/container/tmp"

echo "Starting Rust Server"
# Run the Server
/ProcessWrapper $MODIFIED_STARTUP

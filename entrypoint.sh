#!/bin/bash
cd /home/container

# Make internal Docker IP address available to processes.
export INTERNAL_IP=`ip route get 1 | awk '{print $NF;exit}'`

# Update Rust Server
./steam/steamcmd.sh +login anonymous +force_install_dir /home/container +app_update 258550 +quit

# Validate Rust Server
./steam/steamcmd.sh +login anonymous +force_install_dir /home/container +app_update 258550 validate +quit

# Replace Startup Variables
MODIFIED_STARTUP=`echo $(echo ${STARTUP} | sed -e 's/{{/${/g' -e 's/}}/}/g')`
# echo ":/home/container$ ${MODIFIED_STARTUP}"

# OxideMod has been replaced with uMod
if [ -f OXIDE_FLAG ] || [ "${OXIDE}" = 1 ] || [ "${UMOD}" = 1 ]; then
    echo "Updating uMod..."
    curl -sSL "https://github.com/Raid-Simulator/Oxide.Rust-RS_Build/releases/latest/download/Oxide.Rust-linux.zip" > umod.zip
    unzip -o -q umod.zip
    rm umod.zip
    echo "Done updating uMod!"
fi

# Fix for Rust not starting
export LD_LIBRARY_PATH=$LD_LIBRARY_PATH:$(pwd)

mkdir /home/container/tmp
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="/home/container/tmp"

# Run the Server
/Pterodactyl_Rust_Process_Wrapper $MODIFIED_STARTUP

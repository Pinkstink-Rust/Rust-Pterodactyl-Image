FROM ubuntu:18.04

LABEL author="Isaac A." maintainer="isaac@isaacs.site"

RUN apt update \
    && apt upgrade -y \
    && apt install -y lib32gcc1 lib32stdc++6 unzip curl iproute2 libgdiplus \
    && useradd -d /home/container -m container

USER container
ENV  USER=container HOME=/home/container

WORKDIR /home/container

COPY ./entrypoint.sh /entrypoint.sh
COPY ./Pterodactyl_Rust_Process_Wrapper /Pterodactyl_Rust_Process_Wrapper
RUN ["chmod", "+x", "/Pterodactyl_Rust_Process_Wrapper"]

CMD ["/bin/bash", "/entrypoint.sh"]

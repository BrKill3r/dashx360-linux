#!/bin/bash
# Gera o pacote Linux autocontido em ../dist/DashX360 (nao precisa de .NET instalado para rodar).
set -e
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH" DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
./tools/convert_all.sh >/dev/null
rm -rf ../dist/DashX360
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishReadyToRun=true -p:DebugType=none -o ../dist/DashX360
cp ../dist-files/install.sh ../dist-files/uninstall.sh ../dist/ && cp README-LINUX.md ../dist/LEIA-ME.md
echo "Pacote pronto em: $(cd ../dist && pwd)"

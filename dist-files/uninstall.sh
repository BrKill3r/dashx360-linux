#!/bin/bash
# Remove o DashX360 instalado pelo install.sh. Seus dados (perfil, biblioteca, configuracoes) ficam em
# ~/.local/share/DashX360/UserData; passe --purge para apagar tudo.
rm -rf "$HOME/.local/share/DashX360/app" "$HOME/.local/share/applications/dashx360.desktop" \
       "$HOME"/.local/share/icons/hicolor/*/apps/dashx360.png "$HOME/.config/autostart/dashx360.desktop"
[ "$1" = "--purge" ] && rm -rf "$HOME/.local/share/DashX360"
echo "DashX360 removido."

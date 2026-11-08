#!/bin/bash
# Instala o DashX360 para o usuario atual (sem sudo): ~/.local/share/DashX360/app + atalho no menu.
set -e
SRC="$(cd "$(dirname "$0")" && pwd)/DashX360"
DEST="$HOME/.local/share/DashX360/app"
if [ ! -x "$SRC/DashX360" ]; then echo "Pasta DashX360 nao encontrada ao lado deste script."; exit 1; fi

echo "==> Verificando dependencias do sistema..."
missing=()
ldconfig -p | grep -q 'libSDL2-2.0.so.0' || missing+=(libsdl2-2.0-0)
ldconfig -p | grep -q 'libgstreamer-1.0.so.0' || missing+=(gstreamer1.0-tools)
ldconfig -p | grep -q 'libgstapp-1.0.so.0' || missing+=(gstreamer1.0-plugins-base)
command -v gst-inspect-1.0 >/dev/null && ! gst-inspect-1.0 avdec_h264 >/dev/null 2>&1 && ! gst-inspect-1.0 openh264dec >/dev/null 2>&1 && missing+=(gstreamer1.0-libav)
command -v pw-record >/dev/null || command -v parec >/dev/null || missing+=(pipewire-bin)
if [ ${#missing[@]} -gt 0 ]; then
  echo "Pacotes recomendados ausentes: ${missing[*]}"
  echo "Instale com:  sudo apt install ${missing[*]}"
fi
if ! id -nG | grep -qw input; then
  echo "Aviso: seu usuario nao esta no grupo 'input'. O atalho global Win+Shift+Ctrl e o controle no YouTube TV"
  echo "       precisam dele:  sudo usermod -aG input $USER   (depois faca logout/login)"
fi

echo "==> Copiando arquivos para $DEST"
mkdir -p "$DEST"
cp -a "$SRC/." "$DEST/"
chmod +x "$DEST/DashX360"

echo "==> Instalando icone"
for s in 16 24 32 48 64 128 256 512; do
  ICON_DIR="$HOME/.local/share/icons/hicolor/${s}x${s}/apps"
  mkdir -p "$ICON_DIR"
  cp "$DEST/Icons/dashx360-$s.png" "$ICON_DIR/dashx360.png" 2>/dev/null || true
done
gtk-update-icon-cache -f -t "$HOME/.local/share/icons/hicolor" >/dev/null 2>&1 || true

mkdir -p "$HOME/.local/share/applications"
cat > "$HOME/.local/share/applications/dashx360.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=DashX360
Comment=Dashboard Xbox 360 (Metro e Blades)
Exec="$DEST/DashX360"
Path=$DEST
Icon=dashx360
Terminal=false
Categories=Game;
StartupWMClass=DashX360
DESKTOP
update-desktop-database "$HOME/.local/share/applications" >/dev/null 2>&1 || true
echo "==> Pronto! Abra 'DashX360' pelo menu de aplicativos ou rode: $DEST/DashX360"

# DashX360 para Linux (Ubuntu)

Port nativo do DashX360 v1.3.1 (WPF/Windows) para **Avalonia 11 + .NET 10**, rodando nativamente no Ubuntu.
Crédito do projeto original: **zivvoz / DashX360** (https://youtube.com/@zivvoz).

## Instalar (usuário final)

Baixe `DashX360-Linux-x64.tar.gz` na página de Releases do GitHub e extraia. Dentro da pasta `DashX360-Linux`:

```bash
./install.sh          # instala em ~/.local/share/DashX360/app e cria o atalho "DashX360" no menu
./uninstall.sh        # remove (use --purge para apagar também perfil/biblioteca/configurações)
```

O pacote é autocontido (inclui o runtime .NET). Dependências do sistema (já vêm no Ubuntu desktop):

```bash
sudo apt install libsdl2-2.0-0 gstreamer1.0-plugins-base gstreamer1.0-plugins-good gstreamer1.0-libav pipewire-bin
```

Para o atalho global do Guide pelo teclado (Win+Shift+Ctrl) e para controlar o YouTube TV com o controle,
seu usuário precisa estar no grupo `input` (`sudo usermod -aG input $USER`, depois logout/login).

## Compilar a partir do código

```bash
# .NET 10 SDK (sem sudo): https://dot.net/v1/dotnet-install.sh --channel 10.0 --install-dir ~/.dotnet
cd DashX360.Linux
./tools/build-release.sh        # gera ../dist/DashX360
~/.dotnet/dotnet run            # executar em modo debug
```

## O que foi trocado (Windows → Linux)

| Windows (original)                         | Linux (port)                                                         |
|--------------------------------------------|----------------------------------------------------------------------|
| WPF (XAML, Triggers, Storyboards)          | Avalonia 11 + camada de compatibilidade `Compat/` + conversor `tools/wpf2avalonia.py` |
| XInput / SharpDX DirectInput               | SDL2 GameController (Xbox, PlayStation, Switch, genéricos; botão Guide físico funciona) |
| NAudio / WaveOut / MediaPlayer             | Mixer próprio em SDL2 (efeitos) + GStreamer (músicas)                |
| WebBrowser (vídeo de boot)                 | GStreamer `playbin` → `VideoView`                                    |
| WebView2 (YouTube TV)                      | Firefox/Chromium em modo kiosk com user-agent de TV + teclado virtual uinput guiado pelo controle |
| Windows Media Session (GSMTC) / teclas multimídia | MPRIS via D-Bus (`gdbus`) — Spotify e outros players          |
| WASAPI loopback (visualizador)             | Monitor do PipeWire (`pw-record`) / PulseAudio (`parec`)             |
| Hook de teclado global (user32)            | evdev (`/dev/input`), com fallback dentro da janela                  |
| user32 (foco/fechar janelas de jogos)      | EWMH via X11/XWayland + `/proc` (inclui jogos Proton/Wine)           |
| Registro (Run) / caminho da Steam          | `~/.config/autostart/dashx360.desktop` / `~/.steam/steam`, `~/.local/share/Steam`, Flatpak, Snap |
| DPAPI                                      | AES-GCM com chave por usuário (0600)                                 |
| Diálogos Win32/WinForms                    | Portal/GTK via Avalonia StorageProvider, ColorPicker do Avalonia     |
| Segoe UI / Segoe MDL2 Assets               | Selawik (métrica compatível, OFL) / Noto Sans Symbols 2 (OFL)        |
| Lançar `.exe`                              | Executáveis nativos, `.sh`, `.AppImage`, `.desktop`, `steam://`, e `.exe` via Wine |

## Dados do usuário

`~/.local/share/DashX360/UserData` (perfil, biblioteca, configurações) e `~/.local/share/DashX360/Logs`.
Para modo portátil, crie um arquivo vazio `portable` ao lado do executável.
Backups exportados pela versão Windows podem ser importados (caminhos `C:\...\Assets\...` são remapeados).

## Diferenças conhecidas

- Em Wayland não é possível sobrepor o Guide por cima de jogos em tela cheia nativos Wayland (limitação do compositor); com jogos XWayland/Proton funciona como no Windows.
- Os controles padrão (ComboBox, Slider, CheckBox) usam o tema Fluent claro do Avalonia, parecido mas não idêntico ao Aero do WPF.
- Tokens do Spotify/Discord salvos pela versão Windows (DPAPI) não podem ser lidos; basta reconectar.

## Ferramentas de desenvolvimento

- `tools/convert_all.sh` — regenera `Xaml/*.axaml` a partir dos XAML WPF originais em `Xaml.Wpf/`.
- `tools/devrun.sh` — roda headless no Xvfb com automação (`DASHX360_TEST_ACTIONS="1500:NextTab,800:shot-x"`).
- `tools/virtual_pad.py` — cria um controle Xbox 360 virtual (uinput) para testar a entrada de ponta a ponta.
- Variáveis de debug: `DASHX360_DEBUG=1`, `DASHX360_TRIGGER_DEBUG=1`, `DASHX360_INPUT_DEBUG=1`, `DASHX360_FOCUS_DEBUG=1`.

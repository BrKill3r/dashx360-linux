# DashX360 for Linux

Unofficial Linux port of [DashX360](https://github.com/ZivvoZ/dashx360), the Xbox 360 Metro & Blades dashboard recreation made by **ZivvoZ**.

All the design, UI, animations and features are ZivvoZ's work. I only ported it so it runs natively on Linux (Ubuntu) instead of Windows. If you like it, go support the original creator:

- Original project: https://github.com/ZivvoZ/dashx360
- YouTube: https://youtube.com/@zivvoz
- Ko-fi: https://ko-fi.com/zivvoz

Original project by zivvoz / DashX360 - inspired by Microsoft's Xbox 360 Dashboard's interfaces.

![Metro](metropreview.png)
![Blades](bladespreview.png)

## Install

Download the latest release and run the installer. No sudo needed, it installs to your user folder and adds DashX360 to the app menu.

```bash
curl -LO https://github.com/BrKill3r/dashx360-linux/releases/latest/download/DashX360-Linux-x64.tar.gz
tar xzf DashX360-Linux-x64.tar.gz
cd DashX360-Linux
./install.sh
```

Then open **DashX360** from the app menu.

The package already includes the .NET runtime. On a normal Ubuntu desktop everything else is usually already there, but if the boot video or sound doesn't work, install these:

```bash
sudo apt install libsdl2-2.0-0 gstreamer1.0-plugins-base gstreamer1.0-plugins-good gstreamer1.0-libav pipewire-bin
```

To open the Guide with Win+Shift+Ctrl from anywhere (outside the dashboard window) and to use the controller in YouTube TV, your user needs to be in the `input` group:

```bash
sudo usermod -aG input $USER
```

Log out and back in after that.

### Update

Download the new release and run `./install.sh` again. Your profile, library and settings are kept.

### Uninstall

```bash
./uninstall.sh            # removes the app, keeps your data
./uninstall.sh --purge    # removes everything
```

Your data lives in `~/.local/share/DashX360/UserData`.

## Usage

Same as the Windows version:

1. Open DashX360 and connect your controller (Xbox, PlayStation, Switch and most generic controllers work).
2. If you use Steam, turn off **Enable Guide Button Chords for controllers** in Steam's controller settings.
3. Back + Start (or Win + Left Shift + Left Ctrl) opens the Guide. The real Guide/Xbox button also works.

Controls:

- `A` / `Enter`: select
- `B` / `Escape`: back
- `X` / `Y`: extra actions where available
- `RB` / `LB`: switch tabs

Fullscreen and aspect ratio (16:9 or 21:9) are in settings > Display.

## What's different on Linux

- Steam games are found automatically (native, Flatpak and Snap Steam) and launched through Steam, so Proton games work too. You can also add native games, AppImages, `.sh` files, `.desktop` entries, or Windows `.exe` files (run through Wine).
- Music from Spotify and other players shows up through MPRIS.
- YouTube TV opens in Firefox (or Chromium) in kiosk mode and you control it with the controller.
- On Wayland, the Guide can't be drawn on top of a game that runs fullscreen as a native Wayland window. Games running through XWayland/Proton (most of them) are fine.
- Spotify/Discord logins saved by the Windows version can't be read on Linux, just connect again.
- Backups exported from the Windows version can be imported.

Tested on Ubuntu 26.04 LTS (GNOME, Wayland). Other distros with GNOME/KDE should work as long as SDL2 and GStreamer are installed.

## Building from source

You need the .NET 10 SDK:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
```

Then:

```bash
cd DashX360.Linux
~/.dotnet/dotnet run                 # run in debug
./tools/build-release.sh             # self-contained build in ../dist
./tools/package-release.sh           # release tarball in ../release
```

The Linux port lives in `DashX360.Linux/`. The rest of the repo is the original Windows project, kept as it is. The port uses Avalonia instead of WPF, with a small compatibility layer so the original code and XAML could be reused almost unchanged. More technical details are in [DashX360.Linux/README-LINUX.md](DashX360.Linux/README-LINUX.md) (in Portuguese).

The original Windows readme is in [README.windows.md](README.windows.md).

## Credits

- **ZivvoZ** - creator of DashX360 (design, UI, features, everything): https://github.com/ZivvoZ/dashx360
- Linux port: [BrKill3r](https://github.com/BrKill3r)
- Fonts: Selawik (Microsoft, SIL OFL) and Noto Sans Symbols 2 (Google, SIL OFL)

## Legal

This is an unofficial, non-commercial fan project. Xbox, Xbox 360, Xbox LIVE, Microsoft, and related names, logos, and imagery are property of Microsoft. This project is not affiliated with, endorsed by, or sponsored by Microsoft.

DashX360 includes some original Xbox 360 assets (sounds and graphics). These remain the property of their respective rights holders.

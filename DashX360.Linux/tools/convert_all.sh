#!/bin/bash
# Regenerates Xaml/*.axaml from the WPF sources in Xaml.Wpf/.
set -e
cd "$(dirname "$0")/.."
conv() { # src dst [events.cs]
  mkdir -p "$(dirname "$2")"
  echo "== $1"
  if [ -n "$3" ]; then python3 tools/wpf2avalonia.py "$1" "$2" --events "$3"; else python3 tools/wpf2avalonia.py "$1" "$2"; fi
}
mkdir -p Src/Generated
conv Xaml.Wpf/root/App.xaml Xaml/App.axaml
conv Xaml.Wpf/root/MainWindow.xaml Xaml/MainWindow.axaml Src/Generated/MainWindow.Events.g.cs
conv Xaml.Wpf/themes/MetroTheme.xaml Xaml/themes/MetroTheme.axaml
conv Xaml.Wpf/bladethemes/DashX360_CenterBladeBodies.xaml Xaml/bladethemes/DashX360_CenterBladeBodies.axaml
conv Xaml.Wpf/bladethemes/DashX360_AccurateSideBlades.xaml Xaml/bladethemes/DashX360_AccurateSideBlades.axaml
conv Xaml.Wpf/controls/MetroTile.xaml Xaml/controls/MetroTile.axaml Src/Generated/MetroTile.Events.g.cs
conv Xaml.Wpf/views/GuideWindow.xaml Xaml/views/GuideWindow.axaml Src/Generated/GuideWindow.Events.g.cs
conv Xaml.Wpf/views/BladesGuideWindow.xaml Xaml/views/BladesGuideWindow.axaml Src/Generated/BladesGuideWindow.Events.g.cs
for f in Xaml.Wpf/views/tabs/*.xaml; do b=$(basename "$f" .xaml); conv "$f" "Xaml/views/tabs/$b.axaml" "Src/Generated/$b.Events.g.cs"; done

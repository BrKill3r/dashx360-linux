using System;
using System.IO;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

/// <summary>XDG autostart entry (~/.config/autostart/dashx360.desktop) replacing the Windows Run registry key.</summary>
public sealed class LinuxStartupRegistrationService : IStartupRegistrationService
{
	private static string AutostartFile
	{
		get
		{
			string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
			if (string.IsNullOrWhiteSpace(config))
			{
				config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
			}
			return Path.Combine(config, "autostart", "dashx360.desktop");
		}
	}

	public void SetLaunchOnStartup(bool enabled)
	{
		try
		{
			string file = AutostartFile;
			if (!enabled)
			{
				if (File.Exists(file))
				{
					File.Delete(file);
				}
				return;
			}
			string launcher = ResolveLauncherPath();
			Directory.CreateDirectory(Path.GetDirectoryName(file)!);
			string icon = Path.Combine(AppPaths.AppFolder, "Icons", "dashx360-256.png");
			File.WriteAllText(file, string.Join("\n", new[]
			{
				"[Desktop Entry]",
				"Type=Application",
				"Name=DashX360",
				"Comment=Xbox 360 Metro & Blades dashboard",
				"Exec=\"" + launcher + "\"",
				"Path=" + AppPaths.AppFolder,
				"Icon=" + icon,
				"Terminal=false",
				"X-GNOME-Autostart-enabled=true",
				""
			}));
		}
		catch (Exception exception)
		{
			App.LogException(exception, "LinuxStartupRegistrationService.SetLaunchOnStartup");
		}
	}

	private static string ResolveLauncherPath()
	{
		string script = Path.Combine(AppPaths.AppFolder, "dashx360.sh");
		if (File.Exists(script))
		{
			return script;
		}
		return Environment.ProcessPath ?? Path.Combine(AppPaths.AppFolder, "DashX360");
	}
}

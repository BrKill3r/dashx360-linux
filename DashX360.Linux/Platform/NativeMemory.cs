using System;
using System.Runtime.InteropServices;

namespace XboxMetroLauncher.Platform;

/// <summary>Linux equivalent of psapi EmptyWorkingSet: returns freed heap pages to the OS (glibc malloc_trim).</summary>
internal static class NativeMemory
{
	[DllImport("libc", EntryPoint = "malloc_trim")]
	private static extern int MallocTrim(nuint pad);

	public static void TrimWorkingSet()
	{
		try
		{
			MallocTrim(0);
		}
		catch (Exception)
		{
		}
	}
}

using System.Collections.Generic;

namespace XboxMetroLauncher.Services;

public interface IAudioService
{
	IReadOnlyList<string> GetOutputDeviceNames();

	void Play(string soundName);

	void Stop(string soundName);

	void WarmUp(string soundName);

	void TrimCachedResources(bool keepGuideReady);
}

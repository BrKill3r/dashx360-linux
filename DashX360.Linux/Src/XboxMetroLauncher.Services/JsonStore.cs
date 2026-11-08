using System;
using System.IO;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XboxMetroLauncher.Services;

public sealed class JsonStore : IJsonStore
{
	private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

	private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	private readonly string _rootPath;

	public JsonStore(string rootPath)
	{
		_rootPath = rootPath;
		Directory.CreateDirectory(_rootPath);
	}

	public async Task<T?> ReadAsync<T>(string fileName, CancellationToken cancellationToken = default(CancellationToken))
	{
		string path = Path.Combine(_rootPath, fileName);
		if (!File.Exists(path))
		{
			return default(T);
		}
		SemaphoreSlim fileLock = GetFileLock(path);
		await fileLock.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			await using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			fileLock.Release();
		}
	}

	public async Task WriteAsync<T>(string fileName, T value, CancellationToken cancellationToken = default(CancellationToken))
	{
		string path = Path.Combine(_rootPath, fileName);
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		SemaphoreSlim fileLock = GetFileLock(path);
		await fileLock.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			string tempPath = path + ".tmp";
			await using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			File.Copy(tempPath, path, overwrite: true);
			File.Delete(tempPath);
		}
		finally
		{
			fileLock.Release();
		}
	}

	private static SemaphoreSlim GetFileLock(string path)
	{
		return FileLocks.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));
	}
}

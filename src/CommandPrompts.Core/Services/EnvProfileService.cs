using System.Text.Json;

using CommandPrompts.Core.Json;
using CommandPrompts.Core.Models;

namespace CommandPrompts.Core.Services;

public sealed class EnvProfileService
{
    private readonly string profilesPath;
    private List<EnvProfile> profiles = new();
    private readonly Lock locker = new();

    public EnvProfileService(string profilesPath)
    {
        this.profilesPath = profilesPath;
        var dir = Path.GetDirectoryName(profilesPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    public IReadOnlyList<EnvProfile> Profiles
    {
        get { lock (locker) return profiles.AsReadOnly(); }
    }

    public async Task<IReadOnlyList<EnvProfile>> LoadAllAsync(CancellationToken ct = default)
    {
        List<EnvProfile> loaded;
        var dir = Path.GetDirectoryName(profilesPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        if (!File.Exists(profilesPath))
        {
            loaded = new List<EnvProfile>();
        }
        else
        {
            await using var fs = new FileStream(profilesPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, bufferSize: 4096, useAsync: true);
            loaded = await JsonSerializer.DeserializeAsync(fs,
                AppJsonContext.Default.ListEnvProfile, ct).ConfigureAwait(false)
                ?? new List<EnvProfile>();
        }
        lock (locker)
        {
            profiles = loaded;
            return profiles.AsReadOnly();
        }
    }

    public async Task SaveAsync(EnvProfile profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (locker)
        {
            var existing = profiles.FindIndex(p =>
                string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
                profiles[existing] = profile;
            else
                profiles.Add(profile);
        }
        await FlushAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        lock (locker)
        {
            profiles.RemoveAll(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        await FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Merges a named profile with session-level overrides (session wins).
    /// </summary>
    public Dictionary<string, string> GetEffectiveVars(
        string? profileName, IReadOnlyDictionary<string, string> sessionOverrides)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(profileName))
        {
            EnvProfile? profile;
            lock (locker)
            {
                profile = profiles.Find(p =>
                    string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));
            }
            if (profile is not null)
            {
                foreach (var kv in profile.Variables)
                    result[kv.Key] = kv.Value;
            }
        }
        foreach (var kv in sessionOverrides)
            result[kv.Key] = kv.Value;
        return result;
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        List<EnvProfile> snapshot;
        lock (locker)
        {
            snapshot = new List<EnvProfile>(profiles);
        }
        var tempPath = profilesPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(fs, snapshot,
                    AppJsonContext.Default.ListEnvProfile, ct).ConfigureAwait(false);
            }
            File.Move(tempPath, profilesPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }
}

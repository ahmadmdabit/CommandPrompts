using CommandPrompts.Core.Models;
using CommandPrompts.Core.Services;

using NUnit.Framework;

namespace CommandPrompts.App.Avalonia.Tests;

public class EnvProfileTests
{
    private string testDir = string.Empty!;

    [SetUp]
    public void Setup()
    {
        testDir = Path.Combine(Path.GetTempPath(), "CPProfiles" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(testDir))
            Directory.Delete(testDir, true);
    }

    [Test]
    public async Task GetEffectiveVarsSessionOverridesProfile()
    {
        var service = new EnvProfileService(Path.Combine(testDir, "profiles.json"));
        await service.SaveAsync(new EnvProfile
        {
            Name = "Production",
            Variables = new() { ["API_KEY"] = "prod-key", ["DEBUG"] = "false" }
        });

        var sessionOverrides = new Dictionary<string, string> { ["DEBUG"] = "true" };
        var effective = service.GetEffectiveVars("Production", sessionOverrides);

        Assert.That(effective["API_KEY"], Is.EqualTo("prod-key"));
        Assert.That(effective["DEBUG"], Is.EqualTo("true"), "Session override must win");
    }

    [Test]
    public async Task SaveThenLoadRoundTrips()
    {
        var service = new EnvProfileService(Path.Combine(testDir, "profiles.json"));
        await service.SaveAsync(new EnvProfile
        {
            Name = "Staging",
            Variables = new() { ["DB"] = "staging-db" }
        });

        var profiles = await service.LoadAllAsync();
        Assert.That(profiles, Has.Count.EqualTo(1));
        Assert.That(profiles[0].Name, Is.EqualTo("Staging"));
    }

    // R1 regression: switching profiles must replace (not accumulate) the
    // profile-derived vars. GetEffectiveVars is pure — it merges what it's
    // given — so the fix lives in the VM. This test documents the expected
    // behavior at the service layer: re-calling with fresh session vars
    // produces only the new profile's vars.
    [Test]
    public async Task GetEffectiveVarsFreshSessionVarsProducesOnlyNewProfileVars()
    {
        var service = new EnvProfileService(Path.Combine(testDir, "profiles.json"));
        await service.SaveAsync(new EnvProfile
        {
            Name = "ProfileA",
            Variables = new() { ["AONLY"] = "a-val", ["SHARED"] = "from-a" }
        });
        await service.SaveAsync(new EnvProfile
        {
            Name = "ProfileB",
            Variables = new() { ["BONLY"] = "b-val", ["SHARED"] = "from-b" }
        });

        // Switching from A to B with empty session vars should produce only B's vars.
        var emptySession = new Dictionary<string, string>();
        var effective = service.GetEffectiveVars("ProfileB", emptySession);

        Assert.That(effective.ContainsKey("AONLY"), Is.False,
            "Old profile's vars must not leak after switching");
        Assert.That(effective["BONLY"], Is.EqualTo("b-val"));
        Assert.That(effective["SHARED"], Is.EqualTo("from-b"));
    }
}

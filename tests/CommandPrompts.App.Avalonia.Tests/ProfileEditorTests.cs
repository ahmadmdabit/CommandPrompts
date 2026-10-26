using CommandPrompts.App.Avalonia.Services;
using CommandPrompts.App.Avalonia.ViewModels;
using CommandPrompts.Core.Models;
using CommandPrompts.Core.Services;

using NUnit.Framework;

namespace CommandPrompts.App.Avalonia.Tests;

public class ProfileEditorTests
{
    private string testDir = string.Empty!;

    private sealed class ConfirmingDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public void Inform(string title, string message) { }
        public Task ManageProfilesAsync(EnvProfileService profiles) => Task.CompletedTask;
    }

    [SetUp]
    public void Setup()
    {
        testDir = Path.Combine(Path.GetTempPath(), "CPProfileEditor" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(testDir))
            Directory.Delete(testDir, true);
    }

    private EnvProfileService CreateService() =>
        new(Path.Combine(testDir, Guid.NewGuid() + ".json"));

    [Test]
    public async Task SaveNewPersistsToService()
    {
        var service = CreateService();
        var vm = new ProfileEditorViewModel(service, new ConfirmingDialogs());

        vm.EditName = "Dev";
        vm.EditVariablesText = "APIURL=http://localhost:5000" + Environment.NewLine + "DEBUG=true";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.That(vm.ErrorMessage, Is.Empty);
        var profiles = await service.LoadAllAsync();
        Assert.That(profiles, Has.Count.EqualTo(1));
        Assert.That(profiles[0].Variables["APIURL"], Is.EqualTo("http://localhost:5000"));
    }

    [Test]
    public async Task SaveDuplicateNameReturnsError()
    {
        var service = CreateService();
        await service.SaveAsync(new EnvProfile
        {
            Name = "Dev",
            Variables = new() { ["A"] = "1" }
        });
        var vm = new ProfileEditorViewModel(service, new ConfirmingDialogs());
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.EditName = "dev";
        vm.EditVariablesText = "B=2";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.That(vm.ErrorMessage, Is.Not.Empty);
        var profiles = await service.LoadAllAsync();
        Assert.That(profiles, Has.Count.EqualTo(1), "Duplicate must not overwrite");
    }

    [Test]
    public async Task DeleteRemovesFromService()
    {
        var service = CreateService();
        await service.SaveAsync(new EnvProfile
        {
            Name = "Stale",
            Variables = new() { ["X"] = "y" }
        });
        var vm = new ProfileEditorViewModel(service, new ConfirmingDialogs());
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.SelectedProfile = vm.Profiles.FirstOrDefault(p => p.Name == "Stale");
        Assert.That(vm.SelectedProfile, Is.Not.Null);

        await vm.DeleteCommand.ExecuteAsync(null);

        var profiles = await service.LoadAllAsync();
        Assert.That(profiles, Is.Empty);
    }

    [Test]
    public async Task SessionRefreshProfilesDeletedProfileFallsBackToUserVars()
    {
        var service = CreateService();
        await service.SaveAsync(new EnvProfile
        {
            Name = "Gone",
            Variables = new() { ["FROMPROFILE"] = "p" }
        });

        var session = new Session { Name = "Fallback" };
        var executor = new ProcessExecutor();
        using var repository = new SessionRepository();
        var vm = new SessionViewModel(session, executor, repository);
        vm.AttachProfiles(service);

        vm.EnvironmentVariablesText = "MINE=1";
        vm.SelectedProfileName = "Gone";

        await service.DeleteAsync("Gone");
        vm.RefreshProfiles();

        Assert.That(vm.EnvironmentVariablesText, Does.Contain("MINE=1"));
        Assert.That(vm.EnvironmentVariablesText, Does.Not.Contain("FROMPROFILE"));
    }
}

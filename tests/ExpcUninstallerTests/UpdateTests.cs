using ExpcUninstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpcUninstallerTests;

[TestClass]
public sealed class UpdateTests
{
    private static AppEntry App => new("LocalMachine/Registry64/sample", "Sample", "1.2", null, null, null, "Vendor")
    {
        Sources = ["LocalMachine/Registry64/sample"],
        Metadata = new("sample", "Sample", "1.2", "Vendor", "", "", "", "", "", null, "", "")
    };
    private static ManagedPackage Package => new() { Id = "Vendor.Sample", Name = "Sample", Publisher = "Vendor", RemotePublisher = "Vendor", Installed = "1.2", Latest = "1.3",
        ProductCodes = ["sample"], RemoteCodes = ["sample"], Scope = "System", Architecture = "X64", Source = "winget", IdentityConfirmed = true, Applicable = true };
    [TestMethod] public void ExactIdentityHasInstallableRing() => Assert.IsTrue(new PackageManagers([Package]).Evaluate(App, null, false).CanInstall);
    [TestMethod] public void SimilarNameDifferentCodeDoesNotMatch() => Assert.IsFalse(PackageManagers.Matches(App, Package with { ProductCodes = ["sample-other"] }));
    [TestMethod] public void GuidProductCodesNormalizeBracesWithoutMatchingDifferentProducts()
    {
        const string code = "83D5E1BF-1FD9-46EA-9C83-A06263B106A5";
        Assert.IsTrue(PackageManagers.SameProductCode(code, "{" + code.ToLowerInvariant() + "}"));
        Assert.IsFalse(PackageManagers.SameProductCode(code, "83D5E1BF-1FD9-46EA-9C83-A06263B106A6"));
        Assert.IsTrue(PackageManagers.Matches(App with { Metadata = App.Metadata! with { KeyName = "{" + code + "}" } }, Package with { ProductCodes = [code] }));
    }
    [TestMethod] public void PublisherScopeAndVersionCannotBeIgnored()
    {
        foreach (var p in new[] { Package with { Publisher = "Other" }, Package with { Scope = "User" }, Package with { Installed = "1.1" } })
            Assert.IsFalse(PackageManagers.Matches(App, p));
    }
    [TestMethod] public void X64CandidateDoesNotMatchOnlyX86Registration() => Assert.IsFalse(PackageManagers.Matches(App with { Sources = ["LocalMachine/Registry32/sample"] }, Package));
    [TestMethod] public void AmbiguousAndMultiManagerMatchesRequireChoice()
    {
        foreach (var other in new[] { Package with { Id = "Vendor.OtherEdition" }, Package with { Manager = "Chocolatey" } })
        {
            var result = new PackageManagers([Package, other]).Evaluate(App, null, false);
            Assert.AreEqual(VersionState.Ambiguous, result.State); Assert.IsFalse(result.CanInstall);
        }
    }
    [TestMethod] public void ExplicitSourceSelectsExactlyOneManager()
    {
        var result = new PackageManagers([Package, Package with { Manager = "Chocolatey" }]).Evaluate(App,
            new VersionRule { Provider = "winget", WingetId = Package.Id }, true);
        Assert.AreEqual("WinGet", result.Package?.Manager); Assert.IsTrue(result.CanInstall);
    }
    [TestMethod] public void MissingManagerIsUnknownAndDoesNotInventUpdate() => Assert.AreEqual(VersionState.Unknown, new PackageManagers().Evaluate(App, null, false).State);
    [TestMethod] public void ExplicitManagerFailureDoesNotProposeAnotherSource()
    {
        foreach (var failure in new[] { "ManagerUnavailable", "ManagerTimeout", "ManagerFailed" })
        {
            var manager = new PackageManagers(); manager.Availability["Scoop"] = L.T(failure);
            var result = manager.Evaluate(App, new VersionRule { Provider = "scoop", WingetId = "sample", ConfirmManagedIdentity = true }, true);
            Assert.AreEqual(failure, result.Reason); Assert.IsFalse(result.CanInstall);
        }
    }
    [TestMethod] public void OptionalManagerRequiresVersionAndSystemSpecificCompatibilityReview()
    {
        var package = Package with { Manager = "Chocolatey" };
        var rule = new VersionRule { Provider = "chocolatey", WingetId = package.Id, ConfirmManagedIdentity = true };
        var manager = new PackageManagers([package]);
        Assert.IsFalse(manager.Evaluate(App, rule, true).CanInstall);
        var reviewed = rule with { ConfirmedCompatibilityVersion = package.Latest, ConfirmedCompatibilitySystem = PackageManagers.CompatibilitySystem };
        Assert.IsTrue(manager.Evaluate(App, reviewed, true).CanInstall);
        Assert.IsFalse(manager.Evaluate(App, reviewed with { ConfirmedCompatibilitySystem = "another PC version" }, true).CanInstall);
        Assert.IsFalse(new PackageManagers([package with { Latest = "2.0" }]).Evaluate(App, reviewed, true).CanInstall);
    }
    [TestMethod] public void LowerOrUnrecognizedAvailableVersionIsNotCurrent()
    {
        foreach (var latest in new[] { "1.1", "unknown", "1.3-beta" })
        { var value = new PackageManagers([Package with { Latest = latest }]).Evaluate(App, null, false); Assert.AreEqual(latest.Contains("beta") ? VersionState.Manual : VersionState.Unknown, value.State); Assert.IsFalse(value.CanInstall); }
    }
    [TestMethod] public void CurrentManualPinnedErrorAndUnconfirmedNeverGetRing()
    {
        foreach (var p in new[] { Package with { Latest = "1.2" }, Package with { Pinned = true }, Package with { Applicable = false }, Package with { IdentityConfirmed = false }, Package with { Error = "network" } })
            Assert.IsFalse(new PackageManagers([p]).Evaluate(App, null, false).CanInstall);
    }
    [TestMethod] public void DriversAndFirmwareExcluded()
    {
        foreach (var name in new[] { "BIOS Tools", "Firmware update", "Драйверы Рутокен", "Display drivers" })
            Assert.IsFalse(new PackageManagers([Package]).Evaluate(App with { Name = name }, null, false).CanInstall);
    }
    [TestMethod] public void UpgradeArgumentsPreserveExactIdScopeVersionAndProtections()
    {
        var args = PackageManagers.WingetUpgradeArguments(Package);
        CollectionAssert.Contains(args, "--exact"); CollectionAssert.Contains(args, "1.3"); CollectionAssert.Contains(args, "machine");
        foreach (var prohibited in new[] { "--all", "--force", "--include-pinned", "--ignore-security-hash", "--accept-package-agreements", "--accept-source-agreements" }) CollectionAssert.DoesNotContain(args, prohibited);
        Assert.Throws<InvalidOperationException>(() => PackageManagers.WingetUpgradeArguments(Package with { Pinned = true }));
        Assert.Throws<InvalidOperationException>(() => PackageManagers.WingetUpgradeArguments(Package with { Id = "--all" }));
    }
    [TestMethod] public void VerifiedInstalledVersionRequiredEvenForExitZero()
    {
        Assert.AreEqual("UpdateNotVerified", MainWindow.UpdateOutcome(0, false));
        Assert.AreEqual("UpdateVerified", MainWindow.UpdateOutcome(0, true));
        Assert.AreEqual("UpdateVerifiedReboot", MainWindow.UpdateOutcome(3010, true));
        Assert.AreEqual("UpdateNotVerifiedReboot", MainWindow.UpdateOutcome(unchecked((int)0x8A15010A), false));
        Assert.AreEqual("UpdateVerifiedReboot", MainWindow.UpdateOutcome(unchecked((int)0x8A150109), true));
        Assert.AreEqual("UpdateVerifiedWithError", MainWindow.UpdateOutcome(5, true));
    }
    [TestMethod] public void PinParserRecognizesLiveEmptyOutputAndFailsClosedOnUnknownLocaleOrTruncation()
    {
        Assert.AreEqual(0, PackageManagers.ParsePins(new(0, "Закрепления не настроены.\r\n", ""), [Package.Id])?.Count);
        Assert.AreEqual(0, PackageManagers.ParsePins(new(0, "There are no pins configured.\r\n", ""), [Package.Id])?.Count);
        Assert.IsNull(PackageManagers.ParsePins(new(0, "unrecognized output", ""), [Package.Id]));
        Assert.IsNull(PackageManagers.ParsePins(new(0, "Name  Id\n---------\nSample  Vendor.Sam…", ""), [Package.Id]));
        Assert.IsTrue(PackageManagers.ParsePins(new(0, "Name  Id  Version\n---------------------\nПрограмма  Vendor.Sample  Blocking", ""), [Package.Id])!.Contains(Package.Id));
    }
    [TestMethod] public void ChocolateyOnlyReturnsManagedIdsWithStructuredVersions()
    {
        var result = PackageManagers.ParseChocolatey("sample|1.2\nother|3.0\nChocolatey banner", "sample|1.2|1.3|true\n");
        Assert.AreEqual(2, result.Count); Assert.IsTrue(result[0].Pinned); Assert.IsFalse(result[0].IdentityConfirmed);
    }
    [TestMethod] public void ManagerFeedCredentialsAreNotCopiedIntoSourcesOrLogs()
    {
        Assert.IsTrue(PackageManagers.ValidManagerSource("https://community.chocolatey.org/api/v2/"));
        Assert.IsFalse(PackageManagers.ValidManagerSource("https://user:fixture@example.com/feed"));
        Assert.IsFalse(PackageManagers.ValidManagerSource("https://example.com/feed?credential=fixture"));
        Assert.IsFalse(PackageManagers.ValidManagerSource("https://example.com/feed#fixture"));
    }
    [TestMethod] public void OneCFormParsingDoesNotTransmitOrPersistSecrets()
    {
        var fields = ProtectedSources.LoginFields("<form id='loginForm'><input type='hidden' name='execution' value='fixture-token'><input name='_eventId' type='hidden' value='submit'><input name='password' type='password' value='ignored'></form>");
        Assert.AreEqual(2, fields.Count); Assert.IsFalse(fields.ContainsKey("password"));
        Assert.Throws<InvalidOperationException>(() => ProtectedSources.LoginFields("<form id='loginForm'><input name='captchaCode'></form>"));
        Assert.IsTrue(ProtectedSources.AllowedOneC(new Uri("https://login.1c.ru/login")));
        foreach (var url in new[] { "https://update.1s.ru/", "https://login.1c.ru.evil.example/", "http://login.1c.ru/", "https://user:pass@login.1c.ru/" }) Assert.IsFalse(ProtectedSources.AllowedOneC(new Uri(url)));
    }
    [TestMethod] public async Task LockedVaultDoesNotPerformAuthentication()
    {
        using var vault = new SecretVault(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".vault"));
        var result = await ProtectedSources.Check(App, new VersionRule { Provider = "1c", Url = "https://releases.1c.ru/" }, vault, CancellationToken.None);
        Assert.AreEqual(VersionState.Authentication, result.State);
    }
}

using System.Runtime.InteropServices;
using System.Text.Json;
using ExpcUninstaller;
using Microsoft.Management.Deployment;
using WinRT;

// Separate read-only process: the UI owns its timeout and can terminate this probe.
try
{
    var manager = Make<PackageManager>("C53A4F16-787E-42A4-B304-29EFFB4BF597");
    var options = Make<CreateCompositePackageCatalogOptions>("526534B8-7E46-47C8-8416-B1685C327D37");
    options.Catalogs.Add(manager.GetPackageCatalogByName("winget"));
    options.CompositeSearchBehavior = CompositeSearchBehavior.LocalCatalogs;
    var connection = await manager.CreateCompositePackageCatalog(options).ConnectAsync();
    if (connection.Status != ConnectResultStatus.Ok) throw new IOException(connection.Status.ToString());
    var result = await connection.PackageCatalog.FindPackagesAsync(Make<FindPackagesOptions>("572DED96-9C60-4526-8F92-EE7D91D38C1A"));
    if (result.Status != FindPackagesResultStatus.Ok) throw new IOException(result.Status.ToString());
    var packages = new List<ManagedPackage>();
    // WinRT out-of-process collections must be indexed; their IEnumerable projection can QI-fail.
    for (var i = 0; i < result.Matches.Count; i++)
    {
        var package = result.Matches[i].CatalogPackage;
        var installed = package.InstalledVersion;
        if (installed == null || package.Id.StartsWith("ARP\\", StringComparison.OrdinalIgnoreCase)
            || package.Id.StartsWith("MSIX\\", StringComparison.OrdinalIgnoreCase)) continue;
        var value = new ManagedPackage { Id = package.Id, Name = package.Name, Installed = installed.Version,
            Publisher = installed.GetMetadata(PackageVersionMetadataField.PublisherDisplayName),
            ProductCodes = Strings(installed.ProductCodes), Scope = installed.GetMetadata(PackageVersionMetadataField.InstalledScope),
            Location = installed.GetMetadata(PackageVersionMetadataField.InstalledLocation), Source = "winget" };
        try
        {
            var latest = package.DefaultInstallVersion;
            if (latest != null)
            {
                var install = Make<InstallOptions>("1095F097-EB96-453B-B4E6-1613637F3B14");
                install.PackageInstallScope = value.Scope == "User" ? PackageInstallScope.User : PackageInstallScope.System;
                var applicable = latest.GetApplicableInstaller(install);
                var codes = Strings(latest.ProductCodes);
                var publisher = latest.GetCatalogPackageMetadata().Publisher;
                // A remote name/publisher correlation alone does not establish ownership.
                var exact = codes.Intersect(value.ProductCodes, StringComparer.OrdinalIgnoreCase).Any();
                value = value with { Latest = latest.Version, RemoteCodes = codes, Channel = latest.Channel,
                    Architecture = applicable?.Architecture.ToString() ?? "", Applicable = applicable != null,
                    RemotePublisher = publisher,
                    IdentityConfirmed = exact && value.Publisher.Length > 0 && value.Publisher.Trim().Equals(publisher.Trim(), StringComparison.OrdinalIgnoreCase) };
            }
        }
        catch (Exception ex) { value = value with { Error = ex.GetType().Name }; }
        packages.Add(value);
        Console.Error.WriteLine($"{i + 1}/{result.Matches.Count}");
    }
    Console.WriteLine(JsonSerializer.Serialize(new ProbeResult(packages)));
}
catch (Exception ex)
{
    Console.WriteLine(JsonSerializer.Serialize(new ProbeResult([], ex.GetType().Name + ": " + ex.HResult.ToString("X8"))));
    Environment.ExitCode = 1;
}
static string[] Strings(IReadOnlyList<string> values)
{
    var result = new string[values.Count]; for (var i = 0; i < result.Length; i++) result[i] = values[i]; return result;
}
static T Make<T>(string clsid)
{
    var instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid(clsid))!)!;
    var pointer = Marshal.GetIUnknownForObject(instance);
    try { return MarshalInterface<T>.FromAbi(pointer); }
    finally { Marshal.Release(pointer); }
}

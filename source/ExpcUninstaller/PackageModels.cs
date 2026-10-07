namespace ExpcUninstaller;

// The helper protocol contains public package metadata only, never credentials.
internal sealed record ManagedPackage
{
    public string Manager { get; init; } = "WinGet";
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string RemotePublisher { get; init; } = "";
    public string Installed { get; init; } = "";
    public string Latest { get; init; } = "";
    public string Scope { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string Channel { get; init; } = "";
    public string Source { get; init; } = "";
    public string[] ProductCodes { get; init; } = [];
    public string[] RemoteCodes { get; init; } = [];
    public bool Applicable { get; init; }
    public bool IdentityConfirmed { get; init; }
    public bool Pinned { get; init; }
    public string Location { get; init; } = "";
    public string Error { get; init; } = "";
    public string Description => Manager + " / " + Id + " / " + Installed + " → " + Latest + " / " + Scope;
}
internal sealed record ProbeResult(List<ManagedPackage> Packages, string Error = "");

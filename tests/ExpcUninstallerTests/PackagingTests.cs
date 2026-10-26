using ExpcUninstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace ExpcUninstallerTests;
[TestClass]
public sealed class PackagingTests
{
    [TestMethod]
    [DataRow(@"C:\Portable\EXPCdelete.exe", @"C:\Portable")]
    [DataRow(@"C:\Portable Apps\EXPCdelete.exe", @"C:\Portable Apps")]
    public void RenamedExternalExeKeepsDataBesideOriginal(string origin, string expected)
    {
        Assert.AreEqual(expected, PortablePaths.Resolve(origin, @"C:\Cache\payload"));
        Assert.AreEqual(PortablePaths.Resolve(origin, @"C:\Cache\payload"),
            PortablePaths.Resolve(System.IO.Path.Combine(expected, "EXPC-Uninstaller-Compact.exe"), @"C:\Cache\payload"));
    }
}

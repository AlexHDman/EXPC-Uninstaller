using ExpcUninstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpcUninstallerTests;
[TestClass]
public sealed class TargetedFallbackTests
{
    private static AppEntry Product(string directory) => new("fixture","Product","1",null,null,"\""+directory+"\\app.exe\",0","EXPC Fixture Vendor")
    { Sources=["CurrentUser/Registry64/EXPC-Fixture-"+Guid.NewGuid()], Metadata=new("EXPC-Fixture","Product","1","EXPC Fixture Vendor","","\""+directory+"\\uninstall.exe\" /remove","","\""+directory+"\\app.exe\",0","",null,"0","0") };
    [TestMethod] public void MissingLocationUsesCommandAndIconWithoutChangingOriginalMetadata()
    {
        var parent=Directory.CreateTempSubdirectory("EXPC-scoped-");var path=Path.Combine(parent.FullName,"Product");Directory.CreateDirectory(path);
        try {
            var app=Product(path);var paths=BcuTargetedJunkAdapter.CaptureInstallPaths(app,[]);
            CollectionAssert.AreEqual(new[]{path},paths);Assert.AreEqual("",app.Metadata!.Location);
            var result=BcuTargetedJunkAdapter.Scan(app with{ConfirmedInstallPaths=paths},catalogOverride:new([],0));
            Assert.IsTrue(result.CheckedScopes > 0);Assert.IsTrue(result.Items.Any(x=>x.Path==path));
            Assert.IsFalse(result.Warning?.Contains(L.T("CleanupScopeUnknown")) == true);
            var neighbor=Product(path) with{Sources=["CurrentUser/Registry64/Neighbor"]};
            Assert.AreEqual(0,BcuTargetedJunkAdapter.CaptureInstallPaths(app,[neighbor]).Length);
        } finally {Directory.Delete(path);parent.Delete();}
    }
    [TestMethod] public async Task SnapshotRetainsOriginalRegistrationIconMsiCodeAndConfirmedPath()
    {
        var app=Product(@"C:\EXPC-Fixture\Product");var original=app.Metadata!;
        var code="{11111111-2222-3333-4444-555555555555}";
        app=app with{Metadata=original with{KeyName=code,WindowsInstaller="1",Uninstall="msiexec.exe /x "+code}};
        AppEntry? atLaunch=null,atSearch=null;
        await new StandardRemoval().Run(app,new(){Uninstall=(snapshot,_)=>{atLaunch=snapshot;return Task.FromResult(ProductActions.Completion(0,true,true));}},_=>{},()=>Task.CompletedTask,_=>Assert.Fail(),_=>true,search:(snapshot,_)=>{atSearch=snapshot;return Task.CompletedTask;});
        Assert.IsNotNull(atLaunch);Assert.IsNotNull(atSearch);Assert.AreEqual(code,atSearch.Metadata!.KeyName);
        Assert.AreEqual(app.Metadata.Icon,atSearch.Metadata.Icon);Assert.AreEqual("",atSearch.Metadata.Location);
        CollectionAssert.AreEqual(app.Sources,atSearch.Sources);CollectionAssert.AreEqual(new[]{@"C:\EXPC-Fixture\Product"},atSearch.ConfirmedInstallPaths);
    }
    [TestMethod] public void NoVerifiedPathsIsNotAnEmptySuccess()
    {
        var app=Product(@"C:\EXPC-Fixture\Vendor") with{Name="Product"};
        var scan=BcuTargetedJunkAdapter.Scan(app,catalogOverride:new([],0));
        Assert.AreEqual(0,scan.CheckedScopes);Assert.IsNotNull(scan.Warning);
    }
    [TestMethod] public void PartialScanRetainsFoundObjectsAndNoSuccessIndicator()
    {
        Exception? failure=null;var thread=new Thread(()=>{try{
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException,true);
            var parent=Directory.CreateTempSubdirectory("EXPC-partial-");var directory=Path.Combine(parent.FullName,"Product");Directory.CreateDirectory(directory);
            try{
                var app=Product(directory);using var form=new OperationWindow(app,2,()=>Task.CompletedTask,services:new(){Scan=(_,_)=>new([new("folder",directory,"Operation66")],"fixture denied registry scope",1)});
                var task=form.Search();var until=DateTime.UtcNow.AddSeconds(10);while(!task.IsCompleted&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(5);}Assert.IsTrue(task.IsCompleted);task.GetAwaiter().GetResult();
                Assert.AreEqual(OperationStage.Leftovers,form.Stage);Assert.AreEqual(OperationActivity.Error,form.Activity);Assert.Contains("fixture denied registry scope",form.Status);Assert.IsTrue(form.Tree.Nodes.Count>0);Assert.IsFalse(form.Visible);
            }finally{Directory.Delete(directory);parent.Delete();}
        }catch(Exception ex){failure=ex;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)));if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

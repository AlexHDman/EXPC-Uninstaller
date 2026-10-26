using ExpcUninstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace ExpcUninstallerTests;
[TestClass]
public sealed class FieldCandidateTests
{
    [TestMethod]
    [DataRow("1.2.12","1.2.5",1)]
    [DataRow("2.56.0.2","2.55.0.5",1)]
    [DataRow("8.9.8.1","8.9.8.1",0)]
    public void ScreenshotNumericExamplesDoNotDowngrade(string installed,string source,int comparison)
    {
        Assert.AreEqual(comparison, ReleaseVersion.Compare(installed,source));
        var now=DateTimeOffset.UtcNow;
        var result=new VersionResult(VersionState.Update,installed,source,"fixture",now,now,"fixture","CompatibilityUnknown","") {Package=new(){ Manager="WinGet", Installed=installed,Latest=source,IdentityConfirmed=true,Applicable=true }};
        Assert.IsFalse(result.CanInstall);
    }
    [TestMethod] [DataRow("6.00.3087 Beta 6","5.10.2418")] [DataRow("4.0.0-dev","3.0.24")]
    public void PrereleaseDoesNotSwitchToStable(string installed,string source)
    { Assert.IsTrue(ReleaseVersion.DifferentChannel(installed,source)); Assert.IsNull(ReleaseVersion.Compare(installed,source)); }
    [TestMethod] public void PortableDataRootIsOriginalExecutableNotExtraction()
    { Assert.AreEqual(@"C:\Field",PortablePaths.Resolve(@"C:\Field\EXPC-Compact.exe",@"C:\Cache\hash")); Assert.AreEqual(@"C:\Existing",PortablePaths.Resolve(null,@"C:\Existing")); }
    [TestMethod] public async Task SearchWindowRouteIsInvokedBeforeAnyEnumeration()
    {
        var app=new AppEntry("fixture","fixture","1",null,null,null,"fixture");int windows=0,scans=0;
        await new StandardRemoval().Run(app,new(){Uninstall=(_,_)=>Task.FromResult(ProductActions.Completion(0,true,true)), Find=_=>{scans++;return[];}},_=>{},()=>Task.CompletedTask,_=>Assert.Fail("Legacy completed-search route used"),_=>true,search:(_,_)=>{windows++;Assert.AreEqual(0,scans);return Task.CompletedTask;});
        Assert.AreEqual(1,windows);Assert.AreEqual(0,scans);
    }
    private static void Sta(Action action)
    {Exception? error=null;var thread=new Thread(()=>{try{action();}catch(Exception ex){error=ex;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)));if(error!=null)throw error;}
    private static void Pump(Task task){var deadline=DateTime.UtcNow.AddSeconds(10);while(!task.IsCompleted&&DateTime.UtcNow<deadline){Application.DoEvents();Thread.Sleep(10);}Assert.IsTrue(task.IsCompleted);task.GetAwaiter().GetResult();}
    [TestMethod] public void SearchBeginsInExistingFormAndCancelIsNotEmptySuccess() => Sta(()=>
    {
        var app=new AppEntry("fixture","fixture","1",null,null,null,"fixture");
        using var form=new OperationWindow(app,2,()=>Task.CompletedTask,services:new(){FindCancellable=(_,token)=>{token.WaitHandle.WaitOne(3000);token.ThrowIfCancellationRequested();return[];}});
        var task=form.Search();Assert.IsTrue(form.Running);Assert.IsFalse(form.Visible);form.Secondary();Pump(task);
        Assert.AreEqual(L.T("SearchCancelled"),form.Status);Assert.IsFalse(form.Running);
    });
    [TestMethod] public void CleanupCancellationStopsNextObjectAndReportsCompleted() => Sta(()=>
    {
        var app=new AppEntry("fixture","fixture","1",null,null,null,"fixture");var root=new JunkItem("folder",@"C:\fixture","fixture");
        var entries=new List<CleanupEntry>{new(root,@"C:\fixture\a",false,"CleanupFiles"),new(root,@"C:\fixture\b",false,"CleanupFiles")};
        int deleted=0;var first=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var form=new OperationWindow(app,2,()=>Task.CompletedTask,services:new(){DeleteEntry=async(_,_)=>{Interlocked.Increment(ref deleted);await first.Task;}},initial:entries);
        form.Toggle(form.Tree.Nodes[0]);Pump(form.Advance());var task=form.Advance();while(Volatile.Read(ref deleted)==0){Application.DoEvents();Thread.Sleep(5);}form.Secondary();first.SetResult();Pump(task);
        Assert.AreEqual(1,deleted);StringAssert.Contains(form.Status,L.F("PartialOperation",1,1));Assert.IsFalse(form.Visible);
    });
    [TestMethod] public void ManualSearchAfterUnknownRemovalHasNoSuccessCheck() => Sta(()=>
    {
        var app=new AppEntry("fixture","fixture","1",null,null,null,"fixture");
        using var form=new OperationWindow(app,2,()=>Task.CompletedTask,services:new(){Find=_=>[]});
        Pump(form.Search(removalConfirmed:false));
        Assert.AreEqual(OperationActivity.Idle,form.Activity);Assert.IsFalse(form.Visible);
    });
}

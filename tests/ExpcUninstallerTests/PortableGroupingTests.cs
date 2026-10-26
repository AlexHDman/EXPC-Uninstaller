using ExpcUninstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace ExpcUninstallerTests;
[TestClass]
public sealed class PortableGroupingTests
{
    private static AppEntry App(string id,string name,string version="1") => new(id,name,version,null,null,null,"Vendor");
    [TestMethod] public void GroupsPreserveIdentityAndSinglesDoNotGroup()
    {
        var apps=new[]{App("one","Product"),App("two","Product","2"),App("three","Single")};
        var state=new HashSet<string>();var rows=ProgramGroups.Build(apps,"",0,false,state);
        Assert.AreEqual(2,rows.Count);Assert.IsNull(rows[0].App);Assert.AreEqual(2,rows[0].Count);
        state.Add(ProgramGroups.Family(apps[0]));rows=ProgramGroups.Build(apps,"",0,false,state);
        Assert.AreSame(apps[0],rows[1].App);Assert.AreSame(apps[1],rows[2].App);
        Assert.IsFalse(ProgramGroups.UninstallOnDoubleClick(rows[0],false));Assert.IsFalse(ProgramGroups.UninstallOnDoubleClick(rows[0],true));
        Assert.IsTrue(ProgramGroups.UninstallOnDoubleClick(rows[1],false));
    }
    [TestMethod] public void VcFamilySearchSortRefreshKeepExpansion()
    {
        var apps=new[]{App("x86","Microsoft Visual C++ 2015 Redistributable (x86)","14"),App("x64","Microsoft Visual C++ 2022 Redistributable (x64)","15")};
        var state=new HashSet<string>{ProgramGroups.Family(apps[0])};
        var rows=ProgramGroups.Build(apps,"",3,true,state);Assert.AreEqual(3,rows.Count);Assert.AreSame(apps[1],rows[1].App);
        rows=ProgramGroups.Build(apps.ToArray(),"",3,false,state);Assert.AreSame(apps[0],rows[1].App);
        state.Clear();rows=ProgramGroups.Build(apps,"x64",0,false,state);Assert.AreEqual(2,rows.Count);Assert.IsTrue(rows[0].Expanded);Assert.AreSame(apps[1],rows[1].App);Assert.AreEqual(0,state.Count);
        Assert.AreEqual(1,ProgramGroups.Build(apps,"",0,false,state).Count);
    }
    [TestMethod] public void TreeStateImagesSurviveNativeHandleAndRecreation()
    {
        Exception? error=null;var thread=new Thread(()=>{try{
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, true);
            for(int pass=0;pass<20;pass++)
            {
                var root=new JunkItem("folder",@"C:\Fixture\Product","fixture");
                var decoration=new Bitmap(16,16); decoration.Dispose();
                var form=new OperationWindow(App("test","Product"),2,()=>Task.CompletedTask,image:decoration,
                    initial:[new(root,@"C:\Fixture\Product\one",false,"CleanupFiles"),new(root,@"C:\Fixture\Product\two",false,"CleanupFiles")]);
                Exception? primary=null;
                try {
                Assert.AreNotEqual(IntPtr.Zero,form.Tree.Handle);
                form.Toggle(form.Tree.Nodes[0].Nodes[0]);Assert.AreEqual(2,form.Tree.Nodes[0].StateImageIndex);
                form.Tree.RecreateNativeHandle();
                form.BackColor=Color.FromArgb(34+pass,31,26);
                form.RebuildStateImages(pass == 0 ? 144 : 96);
                Assert.AreNotEqual(IntPtr.Zero,form.Tree.StateImageList!.Handle);Assert.AreEqual(2,form.Tree.Nodes[0].StateImageIndex);
                form.Toggle(form.Tree.Nodes[0]);Assert.AreEqual(1,form.Tree.Nodes[0].StateImageIndex);Assert.IsFalse(form.Visible);
                } catch(Exception ex) { primary=ex; }
                try { form.Dispose(); } catch(Exception ex) { primary ??= ex; }
                if(primary!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
            }
        }catch(Exception ex){error=ex;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)));if(error!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}

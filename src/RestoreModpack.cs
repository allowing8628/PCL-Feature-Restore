using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

public static class RestoreModpack {
 sealed class Mapper : ImportMapper {
  readonly Dictionary<string,TypeDef> types;
  readonly Dictionary<string,MethodDef> methods;
  readonly Dictionary<string,FieldDef> fields;
  public Mapper(ModuleDef m) {
   types=m.GetTypes().ToDictionary(t=>t.FullName);
   methods=m.GetTypes().SelectMany(t=>t.Methods).ToDictionary(t=>t.FullName);
   fields=m.GetTypes().SelectMany(t=>t.Fields).ToDictionary(t=>t.FullName);
  }
  public override ITypeDefOrRef Map(ITypeDefOrRef t) {
   if(types.TryGetValue(t.FullName,out var match)) return match;
   if(t.FullName.StartsWith("PCL.")) throw new Exception("Missing local type: "+t.FullName);
   return null;
  }
  public override IMethod Map(MethodDef m) {
   if(methods.TryGetValue(m.FullName,out var match)) return match;
   throw new Exception("Missing local method: "+m.FullName);
  }
  public override IField Map(FieldDef f) {
   if(fields.TryGetValue(f.FullName,out var match)) return match;
   throw new Exception("Missing local field: "+f.FullName);
  }
 }
 static object Operand(object o,Importer imp,MethodDef dest,CilBody body,Dictionary<Instruction,Instruction> map) {
  if(o==null) return null;
  if(o is Instruction i) return map[i];
  if(o is IList<Instruction> a) return a.Select(x=>map[x]).ToArray();
  if(o is Local l) return body.Variables[l.Index];
  if(o is Parameter p) return dest.Parameters[p.Index];
  if(o is ITypeDefOrRef t) return imp.Import(t);
  if(o is IMethod m) return imp.Import(m);
  if(o is IField f) return imp.Import(f);
  if(o is MethodSig s) return imp.Import(s);
  return o;
 }
 static void CloneBody(MethodDef source,MethodDef dest,Importer imp) {
  var body=new CilBody {InitLocals=source.Body.InitLocals,MaxStack=source.Body.MaxStack};
  foreach(var l in source.Body.Variables) body.Variables.Add(new Local(imp.Import(l.Type)));
  var map=source.Body.Instructions.ToDictionary(i=>i,i=>new Instruction(i.OpCode));
  foreach(var i in source.Body.Instructions) {map[i].Operand=Operand(i.Operand,imp,dest,body,map);body.Instructions.Add(map[i]);}
  if(source.Body.ExceptionHandlers.Count!=0) throw new Exception("Unexpected dialog exception handlers");
  dest.Body=body;
 }
 static string Fingerprint(MethodDef m) {
  if(!m.HasBody) return "no body";
  var list=m.Body.Instructions.ToList();
  Func<object,string> operand=o=>o==null?"":o is Instruction i?"@"+list.IndexOf(i):o is IList<Instruction> a?string.Join(",",a.Select(i=>list.IndexOf(i))):o is Local l?"local"+l.Index:o is Parameter p?"arg"+p.Index:o.ToString();
  return string.Join(";",m.Body.Variables.Select(l=>l.Type.FullName))+"|"+string.Join(";",list.Select(i=>i.OpCode.Name+":"+operand(i.Operand)))+"|"+string.Join(";",m.Body.ExceptionHandlers.Select(h=>h.HandlerType+":"+operand(h.TryStart)+":"+operand(h.TryEnd)+":"+operand(h.HandlerStart)+":"+operand(h.HandlerEnd)+":"+operand(h.FilterStart)+":"+h.CatchType));
 }
 public static string RestoreRemaining(string input,string output) {
  using(var module=ModuleDefMD.Load(input)) {
   if(module.Assembly.Version.ToString()!="2.99.0.0") throw new Exception("Unexpected version");
   var before=module.GetTypes().SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName,m=>Fingerprint(m));
   var types=module.GetTypes().ToDictionary(t=>t.FullName);
   var modify=types["PCL.PageDownloadModify"].Methods.Single(m=>m.Name=="LoadInstanceFinished");
   var card=modify.Body.Instructions.Single(i=>i.Operand is IMethod m&&m.Name=="get_CardMinecraft");
   int ci=modify.Body.Instructions.IndexOf(card);
   if(modify.Body.Instructions[ci+1].OpCode!=OpCodes.Ldc_I4_2||!(modify.Body.Instructions[ci+2].Operand is IMethod vis)||vis.Name!="set_Visibility") throw new Exception("Unexpected visibility pattern");
   modify.Body.Instructions[ci+1].OpCode=OpCodes.Ldc_I4_0;

   var type=types["PCL.PageDownloadResourceDetail"];
   var method=type.Methods.Single(m=>m.Name=="InitializeStackResourceVersions");
   var ins=method.Body.Instructions;
   int itemIndex=ins.ToList().FindLastIndex(i=>i.OpCode==OpCodes.Stloc_S&&i.Operand is Local l&&l.Type.FullName=="PCL.ResourceVersion");
   if(itemIndex<0) throw new Exception("Version loop not found");
   int insert=itemIndex+1;
   var fallback=ins[insert];
   int nextIndex=ins.ToList().FindIndex(insert,i=>i.OpCode==OpCodes.Ldloca_S&&i.Operand is Local l&&l.Index==6);
   if(nextIndex<0||ins[insert].OpCode!=OpCodes.Ldarg_1) throw new Exception("Unexpected loop layout");
   var next=ins[nextIndex];
   var getChildren=(IMethod)ins.Skip(insert).First(i=>i.Operand is IMethod m&&m.Name=="get_Children").Operand;
   var toList=(IMethod)ins.Skip(insert).First(i=>i.Operand is IMethod m&&m.Name=="ToListItem").Operand;
   var add=(IMethod)ins.Skip(insert).First(i=>i.Operand is IMethod m&&m.Name=="Add").Operand;
   var itemCtor=types["PCL.MyListItem/ClickEventHandler"].Methods.Single(m=>m.Name==".ctor");
   var iconCtor=types["PCL.MyIconButton/ClickEventHandler"].Methods.Single(m=>m.Name==".ctor");
   var install=type.Methods.Single(m=>m.Name=="Install_Click");
   var save=type.Methods.Single(m=>m.Name=="Save_Click");
   var resourceField=type.Fields.Single(f=>f.Name=="TargetResourceType");
   var current=method.Body.Variables[7];
   var duplicates=method.Body.Variables[1];
   var block=new[]{
    Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldfld,resourceField),
    Instruction.Create(OpCodes.Ldc_I4_2),Instruction.Create(OpCodes.Bne_Un,fallback),
    Instruction.Create(OpCodes.Ldarg_1),Instruction.Create(OpCodes.Callvirt,getChildren),
    Instruction.Create(OpCodes.Ldloc,current),
    Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldftn,install),Instruction.Create(OpCodes.Newobj,itemCtor),
    Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldftn,save),Instruction.Create(OpCodes.Newobj,iconCtor),
    Instruction.Create(OpCodes.Ldloc,duplicates),Instruction.Create(OpCodes.Callvirt,toList),
    Instruction.Create(OpCodes.Callvirt,add),Instruction.Create(OpCodes.Pop),Instruction.Create(OpCodes.Br,next)
   };
   for(int n=0;n<block.Length;n++) ins.Insert(insert+n,block[n]);
   method.Body.SimplifyBranches();method.Body.OptimizeBranches();

   var drop=types["PCL.FormMain/_Closure$__25-0"].Methods.Single(m=>m.Name=="_Lambda$__0");
   var di=drop.Body.Instructions;
   int msg=di.ToList().FindIndex(i=>i.OpCode==OpCodes.Ldstr&&i.Operand.ToString().Contains("不再提供 MC 下载"));
   if(msg<2||di[msg-1].OpCode!=OpCodes.Callvirt||di[msg-2].OpCode!=OpCodes.Ldloc_S) throw new Exception("Unexpected restriction catch");
   var branch=di.Skip(msg).First(i=>i.OpCode==OpCodes.Brtrue||i.OpCode==OpCodes.Brtrue_S);
   var resume=(Instruction)branch.Operand;
   // Jump straight to the original cancellation/error handling with an empty stack.
   di[msg-2].OpCode=OpCodes.Br;di[msg-2].Operand=resume;
   for(int n=msg-1;n<di.IndexOf(resume);n++){di[n].OpCode=OpCodes.Nop;di[n].Operand=null;}
   drop.Body.SimplifyBranches();drop.Body.OptimizeBranches();
   var flag=types["PCL.ModSecret"].Fields.Single(f=>f.Name=="RestrictMcDownload");
   if(!(flag.Constant.Value is bool enabled)||!enabled) throw new Exception("Unexpected feature flag");
   flag.Constant.Value=false;
   var expected=new[]{modify.FullName,method.FullName,drop.FullName};
   var changed=module.GetTypes().SelectMany(t=>t.Methods).Where(m=>before[m.FullName]!=Fingerprint(m)).Select(m=>m.FullName).ToArray();
   if(!expected.OrderBy(x=>x).SequenceEqual(changed.OrderBy(x=>x))) throw new Exception("Unexpected changed methods");
   foreach(var m in module.GetTypes().SelectMany(t=>t.Methods).Where(m=>m.HasBody)) {
    var all=m.Body.Instructions;
    foreach(var i in all){
     if(i.Operand is Instruction b&&!all.Contains(b)) throw new Exception("Invalid branch");
     if(i.Operand is IList<Instruction> bs&&bs.Any(b=>!all.Contains(b))) throw new Exception("Invalid switch");
    }
   }
   var opts=new ModuleWriterOptions(module);opts.MetadataOptions.Flags|=MetadataFlags.PreserveAll;
   module.Write(output,opts);
   using(var check=ModuleDefMD.Load(output)) {
    var after=check.GetTypes().SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName,m=>Fingerprint(m));
    var diff=before.Keys.Where(k=>before[k]!=after[k]);
    if(!expected.OrderBy(x=>x).SequenceEqual(diff.OrderBy(x=>x)))throw new Exception("Unexpected serialized method changes");
    foreach(var r in module.Resources.OfType<EmbeddedResource>()){
     var s=check.Resources.OfType<EmbeddedResource>().Single(x=>x.Name==r.Name);
     if(!r.CreateReader().ToArray().SequenceEqual(s.CreateReader().ToArray()))throw new Exception("Resource changed");
    }
    var originalRefs=module.GetAssemblyRefs().Select(x=>x.FullName).OrderBy(x=>x);
    if(!originalRefs.SequenceEqual(check.GetAssemblyRefs().Select(x=>x.FullName).OrderBy(x=>x)))throw new Exception("Assembly refs changed");
    File.WriteAllLines(Path.ChangeExtension(output,"verification.txt"),expected.Concat(new[]{"PASS: three method changes and feature constant; other methods/resources unchanged; stack and metadata writer validation; target not executed."}));
    return string.Join("\n",expected)+"\nPASS: restored remaining feature gates; target not executed.";
   }
  }
 }
 public static string Run(string input,string donorPath,string output) {
  using(var target=ModuleDefMD.Load(input)) using(var donor=ModuleDefMD.Load(donorPath)) {
   if(target.Assembly.Version.ToString()!="2.99.0.0"||donor.Assembly.Version.ToString()!="2.13.1.1") throw new Exception("Version mismatch");
   var before=target.GetTypes().SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName,m=>Fingerprint(m));
   var tt=target.GetTypes().Single(t=>t.FullName=="PCL.ModModpack");
   var dt=donor.GetTypes().Single(t=>t.FullName=="PCL.ModModpack");
   var imp=new Importer(target,ImporterOptions.TryToUseDefs|ImporterOptions.TryToUseExistingAssemblyRefs,new GenericParamContext(),new Mapper(target));
   var dialog=tt.Methods.Single(m=>m.Name=="ModpackInstall"&&m.Parameters.Count==0);
   var donorDialog=dt.Methods.Single(m=>m.Name=="ModpackInstall"&&m.Parameters.Count==0);
   CloneBody(donorDialog,dialog,imp);
   var install=tt.Methods.Single(m=>m.Name=="ModpackInstall"&&m.Parameters.Count==3);
   var source=dt.Methods.Single(m=>m.Name=="ModpackInstall"&&m.Parameters.Count==3);
   if(!install.Body.Variables.Select(v=>v.Type.FullName).SequenceEqual(source.Body.Variables.Select(v=>v.Type.FullName))) throw new Exception("Locals differ");
   var body=install.Body;
   var old=body.Instructions.ToList();
   var disabled=old.Single(i=>i.OpCode==OpCodes.Ldstr&&((string)i.Operand).Contains("不再提供 MC 下载"));
   int cut=old.IndexOf(disabled);
   var src=source.Body.Instructions.ToList();
   int from=src.FindIndex(i=>i.OpCode==OpCodes.Switch)-1;
   if(from<0||!(src[from].Operand is Local local)||local.Index!=4) throw new Exception("Dispatch pattern mismatch");
   var tail=src.Skip(from).ToList();
   var map=tail.ToDictionary(i=>i,i=>new Instruction(i.OpCode));
   map[tail[0]]=disabled;
   foreach(var i in tail) {map[i].OpCode=i.OpCode;map[i].Operand=Operand(i.Operand,imp,install,body,map);}
   // Keep the current parser, extension checks, archive handling and exception blocks.
   var oldHandlers=body.ExceptionHandlers.ToList();
   var srcHandlers=source.Body.ExceptionHandlers.ToList();
   if(oldHandlers.Count!=srcHandlers.Count) throw new Exception("Exception handler count mismatch");
   for(int n=0;n<oldHandlers.Count;n++) {
    var h=oldHandlers[n];var s=srcHandlers[n];
    Func<Instruction,Instruction,Instruction> fix=(a,b)=>a==null?(b!=null&&map.ContainsKey(b)?map[b]:null):old.IndexOf(a)>=cut?map[b]:a;
    h.TryStart=fix(h.TryStart,s.TryStart);h.TryEnd=fix(h.TryEnd,s.TryEnd);
    h.HandlerStart=fix(h.HandlerStart,s.HandlerStart);h.HandlerEnd=fix(h.HandlerEnd,s.HandlerEnd);
    h.FilterStart=fix(h.FilterStart,s.FilterStart);
   }
   while(body.Instructions.Count>cut) body.Instructions.RemoveAt(body.Instructions.Count-1);
   foreach(var i in tail) body.Instructions.Add(map[i]);
   body.MaxStack=Math.Max(body.MaxStack,source.Body.MaxStack);
   body.SimplifyBranches();body.OptimizeBranches();
   var changed=target.GetTypes().SelectMany(t=>t.Methods).Where(m=>before[m.FullName]!=Fingerprint(m)).Select(m=>m.FullName).ToArray();
   if(changed.Length!=2||!changed.All(n=>n.Contains("PCL.ModModpack::ModpackInstall("))) throw new Exception("Unexpected modified methods");
   foreach(var m in new[]{dialog,install}) {
    var ins=m.Body.Instructions;
    foreach(var i in ins) {
     if(i.Operand is Instruction b&&!ins.Contains(b)) throw new Exception("Invalid branch");
     if(i.Operand is IList<Instruction> bs&&bs.Any(b=>!ins.Contains(b))) throw new Exception("Invalid switch");
    }
   }
   var options=new ModuleWriterOptions(target);
   options.MetadataOptions.Flags|=MetadataFlags.PreserveAll;
   target.Write(output,options); // Includes stack computation and metadata validation; never executes PCL.
   using(var check=ModuleDefMD.Load(output)) {
    var after=check.GetTypes().SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName,m=>Fingerprint(m));
    var differences=before.Keys.Where(k=>before[k]!=after[k]).ToArray();
    if(!differences.OrderBy(x=>x).SequenceEqual(changed.OrderBy(x=>x))) throw new Exception("Unexpected written method changes");
    foreach(var r in target.Resources.OfType<EmbeddedResource>()) {
     var other=check.Resources.OfType<EmbeddedResource>().Single(x=>x.Name==r.Name);
     if(!r.CreateReader().ToArray().SequenceEqual(other.CreateReader().ToArray())) throw new Exception("Resource modified: "+r.Name);
    }
    if(check.GetAssemblyRefs().Any(a=>a.Name==target.Assembly.Name)) throw new Exception("Invalid self assembly reference");
    var methods=check.GetTypes().Single(t=>t.FullName=="PCL.ModModpack").Methods.Where(m=>m.Name=="ModpackInstall").ToArray();
    if(methods.Any(m=>m.Body.Instructions.Any(i=>i.OpCode==OpCodes.Ldstr&&i.Operand.ToString().Contains("不再提供 MC 下载")))) throw new Exception("Blocked importer remains");
    var calls=methods.Single(m=>m.Parameters.Count==3).Body.Instructions.Where(i=>i.OpCode==OpCodes.Call&&i.Operand is IMethod mm&&mm.Name.String.StartsWith("InstallPack")).Select(i=>((IMethod)i.Operand).Name.String).ToArray();
    if(calls.Distinct().Count()!=7) throw new Exception("Missing format handlers");
    File.WriteAllLines(Path.ChangeExtension(output,"verification.txt"),changed.Concat(new[]{"Parser prefix preserved; seven current format handlers restored; all other method bodies and embedded resources unchanged; PCL not executed."}).Concat(calls));
    return string.Join("\n",changed)+"\nPASS: seven format handlers; metadata and stack validation; resources unchanged.";
   }
  }
 }
}

// store54: сравнение двух .rmx — клейма, тело фото (Rows и ChannelRows), строки обратного рассеяния (BSCR),
// κ пар, раздел электрона (все роды строк). StoreCmp a.rmx b.rmx  → код 0 побитно, 1 различаются, 2 ошибка чтения.
using System; using System.Collections.Generic; using System.Globalization; using System.Linq;
using BecquerelMonitor.EfficiencyMaker;
static class P {
  static readonly CultureInfo I = CultureInfo.InvariantCulture;
  static ResponseMatrix L(string p){ MatrixRefusal r; int f; var m=ResponseMatrix.Load(p,out r,out f); if(m==null) throw new Exception(p+": "+r); Console.WriteLine("  {0}: формат {1}", p, f); return m; }
  struct D { public int rows, rowsDiff, bins, binsDiff; public double sumAbs, sumDiff, worstRel; public string worstAt; }
  static void Cmp(ref D d, string key, float[] x, float[] y){
    d.rows++;
    if(x==null||y==null){ if((x==null)!=(y==null)) { d.rowsDiff++; Console.WriteLine("    {0}: строка есть только в одном файле", key);} return; }
    bool rd = x.Length!=y.Length; int n=Math.Min(x.Length,y.Length);
    for(int j=0;j<n;j++){ d.bins++; d.sumAbs+=Math.Abs((double)x[j]);
      if(BitConverter.ToInt32(BitConverter.GetBytes(x[j]),0)!=BitConverter.ToInt32(BitConverter.GetBytes(y[j]),0)){ d.binsDiff++; rd=true; double dd=Math.Abs((double)x[j]-y[j]); d.sumDiff+=dd;
        double rel=dd/Math.Max(1e-30,Math.Abs((double)x[j])); if(rel>d.worstRel){ d.worstRel=rel; d.worstAt=key+" бин "+j.ToString(I); } } }
    if(rd) d.rowsDiff++;
  }
  static void Print(string what, D d){
    Console.WriteLine("  {0}: строк {1}, разных {2}; бинов {3}, разных {4}; Σ|Δ|/Σ|x| {5:E2}; худшая отн. {6:E2} ({7})",
      what, d.rows, d.rowsDiff, d.bins, d.binsDiff, d.sumAbs>0? d.sumDiff/d.sumAbs:0.0, d.worstRel, d.worstAt??"—");
  }
  static int Main(string[] a){
    System.Threading.Thread.CurrentThread.CurrentCulture = I;
    try {
      var A=L(a[0]); var B=L(a[1]); bool same=true;
      Console.WriteLine("  клеймо фото равно: {0}", A.Stamp==B.Stamp); same&=A.Stamp==B.Stamp;
      Console.WriteLine("  тело: {0} | {1}", A.BodyFingerprint, B.BodyFingerprint);
      Console.WriteLine("  узлов: {0} | {1}", A.NodeCount, B.NodeCount); same&=A.NodeCount==B.NodeCount;
      var d=new D(); for(int i=0;i<Math.Min(A.Rows.Length,B.Rows.Length);i++) Cmp(ref d,"row"+i,A.Rows[i],B.Rows[i]); Print("тело Rows",d); same&=d.binsDiff==0&&d.rowsDiff==0;
      if(A.ChannelRows!=null||B.ChannelRows!=null){ var c=new D();
        if(A.ChannelRows==null||B.ChannelRows==null){ Console.WriteLine("  каналы: есть только в одном"); same=false; }
        else for(int k=0;k<Math.Min(A.ChannelRows.Length,B.ChannelRows.Length);k++) for(int i=0;i<Math.Min(A.ChannelRows[k]?.Length??0,B.ChannelRows[k]?.Length??0);i++) Cmp(ref c,"ch"+k+"/"+i,A.ChannelRows[k][i],B.ChannelRows[k][i]);
        Print("каналы",c); same&=c.binsDiff==0&&c.rowsDiff==0; }
      Console.WriteLine("  BSCR: {0} | {1}", A.HasBackscatter, B.HasBackscatter);
      if(A.BackscatterRows!=null&&B.BackscatterRows!=null){ var bs=new D(); int nonEmptyA=0, nonEmptyB=0;
        for(int i=0;i<Math.Min(A.BackscatterRows.Length,B.BackscatterRows.Length);i++){ Cmp(ref bs,"bs"+i,A.BackscatterRows[i],B.BackscatterRows[i]);
          if(A.BackscatterRows[i]!=null&&A.BackscatterRows[i].Any(v=>v!=0f)) nonEmptyA++; if(B.BackscatterRows[i]!=null&&B.BackscatterRows[i].Any(v=>v!=0f)) nonEmptyB++; }
        Print("строки обратного рассеяния",bs); Console.WriteLine("    непустых: {0} | {1} из {2}", nonEmptyA, nonEmptyB, A.BackscatterRows.Length); same&=bs.binsDiff==0&&bs.rowsDiff==0; }
      else same&=A.HasBackscatter==B.HasBackscatter;
      if(A.JointKappa!=null&&B.JointKappa!=null){ int kd=0,kt=0; for(int i=0;i<Math.Min(A.JointKappa.Length,B.JointKappa.Length);i++) for(int j=0;j<Math.Min(A.JointKappa[i].Length,B.JointKappa[i].Length);j++){ kt++; if(BitConverter.DoubleToInt64Bits(A.JointKappa[i][j])!=BitConverter.DoubleToInt64Bits(B.JointKappa[i][j])) kd++; }
        Console.WriteLine("  κ пар: клеток {0}, разных {1}", kt, kd); same&=kd==0; }
      var sa=A.ElectronSection; var sb=B.ElectronSection;
      if(sa!=null&&sb!=null){
        Console.WriteLine("  клеймо раздела равно: {0}", sa.Stamp==sb.Stamp); same&=sa.Stamp==sb.Stamp;
        if(sa.Stamp!=sb.Stamp){ Console.WriteLine("    A: {0}", sa.Stamp); Console.WriteLine("    B: {0}", sb.Stamp); }
        var e=new D();
        Action<string,float[][],float[][]> rows=(k,x,y)=>{ if(x==null&&y==null) return; if(x==null||y==null){ e.rows++; e.rowsDiff++; Console.WriteLine("    род {0} только в одном",k); return; } for(int i=0;i<Math.Max(x.Length,y.Length);i++) Cmp(ref e,k+i,i<x.Length?x[i]:null,i<y.Length?y[i]:null); };
        rows("e-",sa.Electron,sb.Electron); rows("e+",sa.Positron,sb.Positron); rows("l",sa.LineElectron,sb.LineElectron); rows("be",sa.BranchEvent,sb.BranchEvent); rows("bg",sa.BranchGamma,sb.BranchGamma);
        Print("раздел",e); same&=e.binsDiff==0&&e.rowsDiff==0;
      } else { Console.WriteLine("  раздел: {0} | {1}", sa!=null, sb!=null); same&=(sa==null)==(sb==null); }
      Console.WriteLine(same?"ПОБИТНО РАВНЫ":"РАЗЛИЧАЮТСЯ");
      return same?0:1;
    } catch(Exception ex){ Console.WriteLine("ОШИБКА: "+ex.Message); return 2; }
  }
}

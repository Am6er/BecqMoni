# П30 (S66): два ключа --no-anchor / --no-novel в FsaInferProbeF51; CRLF сохраняется.
import io, sys
p='tools/effmaker/probes/FsaInferProbeF51.cs'
raw=io.open(p,'rb').read()
assert raw.count(b'\r\n')==raw.count(b'\n')
s=raw.decode('utf-8').replace('\r\n','\n')
def rep(a,b):
    global s
    assert s.count(a)==1, a[:60]
    s=s.replace(a,b)
rep("""    ///   FsaInferProbeF51 --spectrum=X.xml [--set=Имя] [--efficiency=Имя]
    ///                    [--cut=whole|criterion|only] [--lib-dump]
    ///                    [--modal-control]
    ///
    /// `--cut=` заставляет правило обрыва ряда; БЕЗ ключа зовётся ровно та
    /// перегрузка, которой пользуется приложение, — то есть меряется умолчание,
    /// а не догадка о нём.
""","""    ///   FsaInferProbeF51 --spectrum=X.xml [--set=Имя] [--efficiency=Имя]
    ///                    [--cut=whole|criterion|only] [--lib-dump]
    ///                    [--no-anchor] [--no-novel] [--modal-control]
    ///
    /// `--cut=` заставляет правило обрыва ряда; БЕЗ ключа зовётся ровно та
    /// перегрузка, которой пользуется приложение, — то есть меряется умолчание,
    /// а не догадка о нём. `--no-anchor` / `--no-novel` (`S66`, полоса П30
    /// 12.09.2026) выключают два дополнительных условия приёма `S57` — якорь и
    /// проверку новизны; это те же рычаги, что `--no-infer-anchor` /
    /// `--no-infer-novel` у `CorpusFsaProbe`, только тот на корпусе режим
    /// `--lib=infer` отвергает кодом 12, а экран «Из NucBase» меряется здесь.
    /// Любой из трёх ключей (`--cut=`, `--no-anchor`, `--no-novel`) переводит
    /// вызов на полную перегрузку `Infer`; остальные два довода при этом равны
    /// умолчанию приложения (`DefaultCut`, якоря вкл, новизна вкл).
""")
rep("""            string cutName = null;
            bool libDump = false, modalControl = false;
""","""            string cutName = null;
            bool libDump = false, modalControl = false;
            bool anchors = true, novelty = true;
""")
rep("""                else if (a == "--lib-dump") libDump = true;
                else if (a == "--modal-control") modalControl = true;
""","""                else if (a == "--lib-dump") libDump = true;
                else if (a == "--no-anchor") anchors = false;
                else if (a == "--no-novel") novelty = false;
                else if (a == "--modal-control") modalControl = true;
""")
rep("""            if (cutName == null)
            {
                // ⛔ ТА ЖЕ перегрузка, что у `FsaAnalysisSession.Compute`: меряем
                //    умолчание приложения, а не своё представление о нём.
                spec = FsaCompositionInference.Infer(peaks, rd, out report);
            }
            else
            {
                FsaChainCut cut;
                switch (cutName)
                {
                    case "whole": cut = FsaChainCut.Whole; break;
""","""            if (cutName == null && anchors && novelty)
            {
                // ⛔ ТА ЖЕ перегрузка, что у `FsaAnalysisSession.Compute`: меряем
                //    умолчание приложения, а не своё представление о нём.
                spec = FsaCompositionInference.Infer(peaks, rd, out report);
            }
            else
            {
                FsaChainCut cut;
                switch (cutName)
                {
                    case null: cut = FsaCompositionInference.DefaultCut; break;
                    case "whole": cut = FsaChainCut.Whole; break;
""")
rep("""                spec = FsaCompositionInference.Infer(peaks, rd, FsaCompositionInference.DefaultCoverage,
                                                    true, true, cut, out report);
""","""                spec = FsaCompositionInference.Infer(peaks, rd, FsaCompositionInference.DefaultCoverage,
                                                    anchors, novelty, cut, out report);
""")
rep("""            Console.WriteLine("правило обрыва ряда: {0}{1}", report.Cut,
                              cutName == null ? " (умолчание приложения)" : " (--cut=" + cutName + ")");
""","""            Console.WriteLine("правило обрыва ряда: {0}{1}; якоря {2}, новизна {3}", report.Cut,
                              cutName == null ? " (умолчание приложения)" : " (--cut=" + cutName + ")",
                              anchors ? "вкл" : "ВЫКЛ (--no-anchor)",
                              novelty ? "вкл" : "ВЫКЛ (--no-novel)");
""")
io.open(p,'wb').write(s.replace('\n','\r\n').encode('utf-8'))
print('ok')

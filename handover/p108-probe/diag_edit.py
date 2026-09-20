apply([
("""                report.SetProbeSource(synthetic, a.ActiveResultData);
                var sceneKinds = new HashSet<FsaReportRowKind>();""",
"""                var diagWatch = System.Diagnostics.Stopwatch.StartNew();
                report.SetProbeSource(synthetic, a.ActiveResultData);
                Console.WriteLine("  DIAG после SetProbeSource: IsRunning={0} RunCount={1} Result==scene:{2} ({3} мс)",
                                  synthetic.IsRunning, synthetic.RunCount, ReferenceEquals(synthetic.Result, scene), diagWatch.ElapsedMilliseconds);
                var sceneKinds = new HashSet<FsaReportRowKind>();"""),
("""                    report.RefreshReport();
                    Application.DoEvents();
                    int marks;""",
"""                    report.RefreshReport();
                    Application.DoEvents();
                    Console.WriteLine("  DIAG в окне 320×160: IsRunning={0} RunCount={1} Result==scene:{2} ({3} мс)",
                                      synthetic.IsRunning, synthetic.RunCount, ReferenceEquals(synthetic.Result, scene), diagWatch.ElapsedMilliseconds);
                    int marks;"""),
])

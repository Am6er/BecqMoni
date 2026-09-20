apply([
("""                using (Form host = Host(report, 320, 160))
                {
                    report.RefreshReport();
                    Application.DoEvents();

                    // (П108) Состояние сеанса ПЕРЕД снятием подписей""",
"""                if (Environment.GetEnvironmentVariable("BQ_P108_DIAG_SLEEP") != null)
                {
                    Thread.Sleep(int.Parse(Environment.GetEnvironmentVariable("BQ_P108_DIAG_SLEEP")));
                    Application.DoEvents();
                    Console.WriteLine("  DIAG после задержки: IsRunning={0} RunCount={1} Result==scene:{2}",
                                      synthetic.IsRunning, synthetic.RunCount, ReferenceEquals(synthetic.Result, scene));
                }

                using (Form host = Host(report, 320, 160))
                {
                    report.RefreshReport();
                    Application.DoEvents();

                    // (П108) Состояние сеанса ПЕРЕД снятием подписей"""),
])

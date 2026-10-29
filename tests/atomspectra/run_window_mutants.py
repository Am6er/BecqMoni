"""PR2 review 2, H2 (#SA-3): mutants of the product that only the window checks can catch. Each is built (rest_suite.build_real_mutant) and run through
run_window_checks.run on its scenario; a mutant must turn the checks RED (and the good build must stay green).
Usage: python run_window_mutants.py <srcDir> <workDir>   (srcDir = the BecquerelMonitor/ directory of the tree)"""
import os, sys
sys.stdout.reconfigure(encoding="utf-8")
import rest_suite as rs, run_window_checks as wc
CP, CTLR = "DCControlPanel.cs", "AtomSpectraTcpDeviceController.Rest.cs"
ASK = "if (!activeResultData.MeasurementController.ConfirmAndClearAsking(() => MessageBox.Show(Resources.MSGClearSpectrum, Resources.ConfirmationDialogTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.No))"
MUTANTS = [("v2-panel-ignores-no", "s4a", [(CP, ASK, ASK.replace("DialogResult.No))", "DialogResult.No) && false)"))]),
           ("gui-doc-not-cleared", "s4b", [(CP, "activeDocument.ClearSpectrum();", "")]),
           ("gui-board-not-reset", "s4b", [(CTLR, "bool ok = AtomSpectraRestUi.RunWithWait(delegate { return rest.ClearNow(CommandTimeoutMs); });", "bool ok = true;")]),
           ("gui-stop-asks-clear", "s3c", [("AtomSpectraTcpDeviceController.cs", "stopAsked = r;   // a board reset met", "if (deviceGuid != null && manual) ConfirmClear(r);\n            stopAsked = r;   // a board reset met")])]
src_dir, work = sys.argv[1], sys.argv[2]
for mid, scen, repl in MUTANTS:
    exe = rs.build_real_mutant(os.path.dirname(os.path.abspath(src_dir)), os.path.join(work, mid), repl)
    res = wc.run(exe, [scen])
    print("MUTATION", mid, scen, "-> window checks", "SKIPPED (no stand: the mutant was NOT checked)" if res is None else ("GREEN (not caught)" if res else "RED"), flush=True)

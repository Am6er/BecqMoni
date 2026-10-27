"""PR2 review 2, H2: the window checks of the stand (check_gui.py: s3c delete of a joined spectrum, s4b Clear "Yes", s4a Clear "No") on a build.
Real MainForm on a hidden desktop, REST stub. The stand is outside the repository: its directory comes ONLY from the environment variable PR2_GUI_DIR.
Usage: python run_window_checks.py <exeDir>; also called by run_review_tests.py. run() returns True = PASS, False = FAIL, None = SKIPPED (no PR2_GUI_DIR,
no check_gui.py, REST_NO_WINDOW_CHECKS=1): a skip is NOT a pass. Exit code of a skip is 0 (a maintainer without the stand is not failed), unless PR2_REQUIRE_WINDOWS=1.
Why the stand and not the harness: "No leaves the document as it was" lives in DCControlPanel.ClearMeasurementResult, which needs the MainForm."""
import os, subprocess, sys
sys.stdout.reconfigure(encoding="utf-8")

def skipped(reason):
    print("WINDOW CHECKS: SKIPPED (" + reason + ") - the window checks were NOT run")
    return False if os.environ.get("PR2_REQUIRE_WINDOWS") else None

def run(exe_dir, scenarios=()):
    gui = os.environ.get("PR2_GUI_DIR", "")
    if os.environ.get("REST_NO_WINDOW_CHECKS"):
        return skipped("REST_NO_WINDOW_CHECKS is set")
    if not gui:
        return skipped("PR2_GUI_DIR is not set")
    if not os.path.exists(os.path.join(gui, "check_gui.py")):
        return skipped("no check_gui.py in PR2_GUI_DIR=" + gui)
    p = subprocess.run([sys.executable, os.path.join(gui, "check_gui.py"), "mut"] + list(scenarios), capture_output=True, text=True, encoding="utf-8", errors="replace",
                       env=dict(os.environ, GUI_MUT_DIR=os.path.abspath(exe_dir), PYTHONIOENCODING="utf-8"), timeout=900)
    for line in p.stdout.splitlines():
        print("    " + line)
    print("WINDOW CHECKS: " + ("PASS" if p.returncode == 0 else "FAIL"))
    return p.returncode == 0

if __name__ == "__main__":
    sys.exit(0 if run(sys.argv[1]) is not False else 1)

import os
import re
import sys
import shutil
import socket
import subprocess
import tempfile
import time
import concurrent.futures

def find_msbuild():
    """MSBuild of the newest Visual Studio / Build Tools that has it (vswhere); None when vswhere finds nothing"""
    vswhere = os.path.join(os.environ.get("ProgramFiles(x86)") or r"C:\Program Files (x86)", "Microsoft Visual Studio", "Installer", "vswhere.exe")
    if not os.path.isfile(vswhere):
        return None
    try:
        p = subprocess.run([vswhere, "-latest", "-products", "*", "-requires", "Microsoft.Component.MSBuild", "-find", r"MSBuild\**\Bin\MSBuild.exe"],
                           capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60)
    except (OSError, subprocess.SubprocessError):
        return None
    for line in p.stdout.splitlines():
        path = line.strip()
        if path and os.path.isfile(path):
            return path
    return None


# the environment variables first, then vswhere (any edition: Community, Professional, Build Tools), then the old default
_VS_MSBUILD = None if (os.environ.get("ATOMSPECTRA_MSBUILD") and os.environ.get("ATOMSPECTRA_CSC")) else find_msbuild()
MSBUILD = os.environ.get("ATOMSPECTRA_MSBUILD") or _VS_MSBUILD or r"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
CSC = os.environ.get("ATOMSPECTRA_CSC") or (os.path.join(os.path.dirname(_VS_MSBUILD), "Roslyn", "csc.exe") if _VS_MSBUILD else None) \
    or r"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
D = os.path.dirname(os.path.abspath(__file__))
SRC_FILES = ["AtomSpectraRestPolicy.cs", "AtomSpectraRestClient.cs", "AtomSpectraRestAcquisition.cs", "AtomSpectraRestSession.cs",
             "AtomSpectraRestCalibration.cs", "AtomSpectraTcpLog.cs", "IAtomSpectraSession.cs", "AtomSpectraTcpSession.cs",
             "AtomSpectraTcpAcquisition.cs", "AtomSpectraTcpTransport.cs", "AtomSpectraTcpCalibrationReader.cs",
             "AtomSpectraTcpDeviceConfig.cs", "AtomSpectraTcpDeviceConfig.Rest.cs", "InputDeviceConfig.cs", "Hash/Crc32.cs",
             "AtomSpectraDeviceConsole.cs", "AtomSpectraDevlog.cs", "AtomSpectraRestBootConfig.cs",
             "AtomSpectraRestSession.JoinedAndObserve.cs", "AtomSpectraTcpSession.JoinedStop.cs",
             "AtomSpectraDnsSd.cs", "AtomSpectraDnsSd.Adapters.cs", "AtomSpectraDnsSd.Schedule.cs"]
STUBS = """namespace BecquerelMonitor.Properties { public static class Resources {
  public static string ERRAtomSpectraTcpCalBusy = "busy {0}:{1}"; public static string ERRAtomSpectraTcpConnect = "connect {0}:{1} {2}";
  public static string ERRIncorrectCRC = "crc"; public static string ERREmptyCoefficients = "empty"; public static string ERRReadDataFromPort_Empty = "noanswer";
  public static string ERRAtomSpectraTcpCalGap = "gap"; public static string MSGAtomSpectraRestLinkLost = "REST {0}: polling every {1} s"; public static string ERRAtomSpectraRestUnreachable = "REST {0} ({2}): not started, polling every {1} s"; } }
namespace BecquerelMonitor { public class PolynomialEnergyCalibration { public int PolynomialOrder { get; set; } public double[] Coefficients { get; set; } } }
"""


def resource_stubs(stubs, src_dir, texts):
    """The stub of Properties.Resources for a build from the sources: the members written in `stubs` as they are, plus every
    other Resources.X the sources use, with its English value from Properties/Resources.resx (so a text a source starts to take
    from the resources does not break the source build, and its scenarios see the same words as on the real build)"""
    have = set(re.findall(r"public static string (\w+)\s*=", stubs))
    used = sorted(set(k for t in texts for k in re.findall(r"\bResources\.(\w+)", t)) - have - {"ResourceManager", "Culture"})
    if not used:
        return stubs
    with open(os.path.join(src_dir, "Properties", "Resources.resx"), "r", encoding="utf-8-sig") as fh:
        resx = fh.read()
    members = []
    for k in used:
        m = re.search(r'<data name="%s"[^>]*>\s*<value>(.*?)</value>' % re.escape(k), resx, re.S)
        if m is None:
            raise RuntimeError("resource " + k + " is used by the sources but is not in Resources.resx")
        v = m.group(1).replace("&lt;", "<").replace("&gt;", ">").replace("&quot;", '"').replace("&apos;", "'").replace("&amp;", "&")
        members.append('public static string %s = "%s";' % (k, v.replace("\\", "\\\\").replace('"', '\\"').replace("\r", "\\r").replace("\n", "\\n")))
    marker = "public static class Resources {"
    return stubs.replace(marker, marker + NL + "  " + " ".join(members), 1)


# AppUi (GlobalConfigManager.cs) is not among the sources of a source build; a source that reports through it gets this stub,
# which behaves as the real door does without windows: Report writes one line to stderr, a question throws.
APPUI_STUB = """namespace BecquerelMonitor { public static class AppUi {
  public static bool HasWindows { get { return false; } }
  public static void Report(string text, string caption, System.Windows.Forms.MessageBoxIcon icon) { System.Console.Error.WriteLine("BecqMoni: " + (string.IsNullOrEmpty(caption) ? "" : caption + ": ") + text); }
  public static bool AskYesNo(string text, string caption) { throw new System.InvalidOperationException("BecqMoni: " + caption + ": " + text); }
  public static bool AskYesNo(string text, string caption, System.Windows.Forms.MessageBoxIcon icon, bool defaultNo) { throw new System.InvalidOperationException("BecqMoni: " + caption + ": " + text); }
  public static bool AskYesNo(System.Windows.Forms.IWin32Window owner, string text, string caption, System.Windows.Forms.MessageBoxIcon icon, bool defaultNo) { throw new System.InvalidOperationException("BecqMoni: " + caption + ": " + text); }
  public static string WrapTip(string text) { return text; } } }
"""


def source_stubs(stubs, src_dir, texts):
    """resource_stubs, plus the AppUi stub when the sources use AppUi and do not define it"""
    out = resource_stubs(stubs, src_dir, texts)
    if any(re.search(r"\bAppUi\.", t) for t in texts) and not any("static class AppUi" in t for t in texts):
        out += APPUI_STUB
    return out


NL = chr(10)
SCENARIOS = [
    ("policy", None),
    ("preset", None),
    ("config", None),
    ("parse", None),
    ("join-continue", ["--counting", "1", "--time", "70000", "--total", "400000"]),
    ("restart", ["--counting", "1", "--time", "70000", "--total", "400000"]),
    ("cancel", ["--counting", "1", "--time", "70000", "--total", "400000"]),
    ("observe", ["--counting", "0", "--time", "5000", "--total", "1000"]),
    ("stopped-empty", ["--counting", "0", "--valid", "0"]),
    ("stopped-data", ["--counting", "0", "--time", "5000", "--total", "1000"]),
    ("limit", ["--counting", "0", "--valid", "0"]),
    ("decrease", ["--counting", "1", "--time", "70000", "--total", "400000", "--decrease-at", "7"]),
    ("ext-reset", ["--counting", "1", "--time", "70000", "--total", "400000", "--external-reset-at", "7", "--stop-at", "7"]),  # reset in the Web UI of a stopped analyzer: 404 stays
    ("nodata-404", ["--counting", "0", "--valid", "0"]),
    ("link-loss", ["--counting", "1", "--time", "1000", "--total", "100", "--down-from", "6", "--down-for", "10"]),   # refused: Windows retries a refused connect ~2 s, 10 s gives >= 3 failed polls
    ("clear-fail", ["--counting", "1", "--time", "5000", "--total", "1000", "--reset-fail"]),
    ("ok-false", ["--counting", "0", "--valid", "0", "--cmd-ok-false"]),
    ("close-joined", ["--counting", "1", "--time", "70000", "--total", "400000"]),
    ("deferred-stop", ["--counting", "0", "--valid", "0", "--down-from", "5", "--down-for", "4", "--decrease-at", "7"]),
    ("deferred-stop-ok", ["--counting", "0", "--valid", "0", "--down-from", "5", "--down-for", "4"]),
    ("gzip", ["--counting", "1", "--time", "100", "--total", "50", "--gzip"]),   # the board does not compress kept as a client feature for proxies
    ("queue", ["--counting", "1", "--time", "100", "--total", "50", "--delay-ms", "1500"]),
    ("prefix-auth", ["--counting", "1", "--time", "100", "--total", "50", "--prefix", "/board1", "--require-auth", "Bearer s3cr3t-TOKEN-xyz"]),
    ("stall", ["--counting", "1", "--time", "1000", "--total", "100", "--stop-at", "7"]),
    ("headless", ["--counting", "1", "--time", "70000", "--total", "400000"]),
    ("reopened", ["--counting", "1", "--time", "70000", "--total", "400000"]),
    # fix scenarios (RestHarnessFixes.cs)
    ("overtake", ["--counting", "1", "--time", "70000", "--total", "400000", "--overtake-at", "7"]),
    ("restart-doc", ["--counting", "1", "--time", "70000", "--total", "400000"]),
    ("preset-new", ["--counting", "0", "--valid", "0"]),
    ("deferred-resumed", ["--counting", "0", "--valid", "0", "--sto-late", "--resume-at", "8"]),
    ("clear-stale", ["--counting", "1", "--time", "5000", "--total", "1000", "--reset-keeps-cache"]),
    ("session-key", None),
    ("headless-stopped", ["--counting", "0", "--time", "5000", "--total", "1000"]),
    ("stop-hang", ["--counting", "0", "--valid", "0", "--hang-from", "12", "--hang-for", "20"]),
    ("url-userinfo", None),
    ("time-back", ["--counting", "1", "--time", "70000", "--total", "400000", "--time-back-at", "7"]),
    # (mock matches firmware 1.2.37)
    ("dead-404", ["--counting", "0", "--valid", "0", "--dead", "1"]),
    ("dead-flag", ["--counting", "1", "--time", "100", "--total", "50", "--dead", "1"]),
    ("reset-unsent", ["--counting", "1", "--time", "5000", "--total", "1000", "--reset-unsent"]),
    ("live-nan", ["--counting", "1", "--time", "100", "--total", "50", "--live-nan"]),
    ("link-silent", ["--counting", "1", "--time", "1000", "--total", "100", "--hang-from", "6", "--hang-for", "8"]),
    ("hist-reset", ["--counting", "1", "--time", "70000", "--total", "400000", "--hist-reset-at", "7"]),
    ("deferred-foreign", ["--counting", "0", "--valid", "0", "--down-from", "5", "--down-for", "4", "--stop-at", "7"]),   # stabilization: no resume inside the decision window (a stop-and-resume shorter than cache+tick is not distinguishable, the reason for Unconfirmed)
    # device console (address, Refresh, command line)
    ("console-rest", ["--counting", "0", "--valid", "0"]),
    ("console-rest-notext", ["--counting", "0", "--valid", "0", "--cmd-no-text"]),
    ("console-rest-refused", ["--counting", "0", "--valid", "0", "--cmd-ok-false"]),
    ("console-refresh", ["--counting", "0", "--valid", "0"]),
    ("console-refresh-none", None),
    ("console-address", None),
    ("rest-linklost-text", None),
    ("form-new-empty", None),
    ("form-bad-address", None),
    ("form-mode-texts", None),
    ("form-layout", None),
    ("form-layout-ru", None),
    # the board calibration to the Energy Calibration page and, at Start, to the document and the configuration
    ("form-calib", ["--counting", "0", "--valid", "0"]),
    ("form-calib-unset", ["--counting", "0", "--valid", "0", "--calib-unset"]),
    ("start-calib", None),
    # TCP Start asks "new / continue" instead of sending -sto/-rst/-sta by itself
    # the board's reply compression level (GET/POST /api/boot-config gzip_level)
    ("gzip-read", ["--counting", "0", "--valid", "0", "--boot-gzip", "6"]),
    ("gzip-unsupported", ["--counting", "0", "--valid", "0"]),
    ("gzip-write", ["--counting", "0", "--valid", "0", "--boot-gzip", "0"]),
    ("gzip-okfalse", ["--counting", "0", "--valid", "0", "--boot-gzip", "0", "--boot-ok-false"]),
    ("gzip-ignored", ["--counting", "0", "--valid", "0", "--boot-gzip", "0", "--boot-ignore"]),
    ("form-gzip", ["--counting", "1", "--time", "100", "--total", "50", "--boot-gzip", "1", "--gzip"]),
    ("form-gzip-unsupported", ["--counting", "1", "--time", "100", "--total", "50"]),
    # the -inf answer text in the client log at the Debug level, hidden by default
    ("console-tcp-inf-debug", ["--order", "hs", "--period", "0.5", "--cycles", "8", "--connections", "1", "--inf-text", "AtomSpectra mock, SERIAL 00000001, fw 1.0"]),
    ("console-tcp-start-continue", ["--order", "hs", "--period", "0.5", "--cycles", "12", "--connections", "1"]),
    ("console-tcp-start-new", ["--order", "hs", "--period", "0.5", "--cycles", "12", "--connections", "1"]),
    ("console-tcp-start-idle", ["--order", "hs", "--period", "0.5", "--cycles", "12", "--connections", "1", "--stopped-at", "0"]),
    ("console-tcp-start-stopped", ["--order", "hs", "--period", "0.5", "--cycles", "12", "--connections", "1", "--stopped-at", "500"]),
    ("rest-unreachable-text", None),
    ("console-tcp", ["--order", "hs", "--period", "0.5", "--cycles", "6", "--connections", "1"]),
    # the recorded board responses (fixtures/responses), one case per GET, the last held
    ("f5-channels", ["--cases", "c08_channels_4096.json,c00_baseline.json,c01_epoch_added.json"]),
    ("f5-epoch-boot", ["--cases", "c01_epoch_added.json,c01_epoch_added.json,c01_epoch_added.json,c02_boot_id_changed.json"]),
    ("f5-epoch-dev", ["--cases", "c01_epoch_added.json,c01_epoch_added.json,c01_epoch_added.json,c03_dev_resets_up.json"]),
    ("f5-epoch-spec", ["--cases", "c01_epoch_added.json,c01_epoch_added.json,c01_epoch_added.json,c04_user_resets_up.json"]),
    ("f5-epoch-trip", ["--cases", "c01_epoch_added.json,c01_epoch_added.json,c01_epoch_added.json,c05_total_drop_same_epoch.json"]),
    ("f5-skip", ["--cases", "c01_epoch_added.json,c06_truncated.json,c07_broken.json,c01_epoch_added.json"]),
    ("f5-recover", None),
    ("f5-nodata", ["--cases", "c01_epoch_added.json,c01_epoch_added.json,c01_epoch_added.json," + ",".join(["c10_no_data_404.txt"] * 3) + ",c01_epoch_added.json"]),
    ("f5-calib", None),
    ("f5-centroid", None),
    # timeout = skipped poll; link loss by time (3 configured periods); controller rescale
    ("f5-timeout", ["--cases", "c01_epoch_added.json", "--hang-from", "1", "--hang-for", "4"]),
    ("f5-linklost-time", ["--cases", "c01_epoch_added.json", "--hang-from", "1", "--hang-for", "20"]),
    ("f5-ctl-calib", None),   # real build only (n/a in the source build); its mutation is manual (tools/manual_mutations.py)
    # polling-rule regression tests + BuildArgs; x01 = c02 with all three epoch fields changed
    ("f5-reset-board", ["--cases", "c01_epoch_added.json,c01_epoch_added.json,c01_epoch_added.json,x01_all_epoch_fields_changed.json"]),
    ("f5-reset-own", None),
    ("f5-stale-epoch", None),
    ("f5-linklost-effective", ["--cases", "c01_epoch_added.json", "--hang-from", "0", "--hang-for", "20"]),
    ("f5-nodata-nostall", ["--cases", "c01_epoch_added.json,c01_epoch_added.json,c01_epoch_added.json,c06_truncated.json,c07_broken.json,c10_no_data_404.txt"]),
    ("f5-period-floor", None),
    ("f5-clear-race", ["--cases", "c01_epoch_added.json,c01_epoch_added.json,c01_epoch_added.json,c01_epoch_added.json,c04_user_resets_up.json", "--reset-delay-ms", "3500"]),
    # cases may carry overrides "file|key=int*N" (mock_rest.py load_cases)
    ("f5-window-steps", ["--cases", "c01_epoch_added.json*6," + "c04_user_resets_up.json*3," + "c04_user_resets_up.json|dev_resets=9|time=20|total=2500*9," + "c04_user_resets_up.json|dev_resets=9|boot_id=439041102|time=40|total=4000"]),
    ("f5-window-lost", ["--cases", "c01_epoch_added.json*6,c04_user_resets_up.json", "--reset-delay-ms", "3000"]),
    ("f5-window-notsent", ["--cases", "c01_epoch_added.json*7,c04_user_resets_up.json", "--down-from", "4.5", "--down-for", "18"]),
    ("f5-clear-twice", None),
    ("f5-startover-lost", ["--cases", "c01_epoch_added.json|time=5|total=100|hist_ok=900*2,c06_truncated.json,c01_epoch_added.json|time=12|total=400|spec_resets=1|hist_ok=950"]),
    ("f5-nodata-signal", ["--cases", "c01_epoch_added.json*3,c10_no_data_404.txt*6,c01_epoch_added.json*2,c10_no_data_404.txt*7"]),
    ("f5-stale-after", ["--cases", "c01_epoch_added.json,c06_truncated.json,c07_broken.json"]),
    ("f5-window-swallow", ["--cases", "c01_epoch_added.json*6,c04_user_resets_up.json*2,c04_user_resets_up.json|boot_id=439041555|time=20|total=2500"]),
    ("f5-form-calib", ["--counting", "0", "--valid", "0"]),   # real build only
    # window rule for an own reset
    ("f5-window-repeat-notsent", None),
    ("f5-startover-external", ["--cases", "c01_epoch_added.json|time=5|total=100|hist_ok=900*2,c01_epoch_added.json|time=12|total=400|spec_resets=1|hist_ok=950*10,c01_epoch_added.json|time=40|total=900|spec_resets=1|boot_id=439041555|hist_ok=960"]),
    ("f5-window-lost-v1", ["--cases", "c00_baseline.json*6,c00_baseline.json|time=15|total=1500", "--reset-delay-ms", "3000"]),
    # the "no data" flag, LinkLost by silence, retry of a poll inside its period
    ("f5-nodata-stale", ["--cases", "c01_epoch_added.json*3,c10_no_data_404.txt*6,c01_epoch_added.json,c10_no_data_404.txt*7"]),
    ("f5-linklost-silence", ["--down-from", "0.3", "--down-for", "100"]),   # a board that REFUSES (no answer at all); 503 is an answer now, see f5-linklost-answered
    ("f5-retry-ok", ["--cases", "c06_truncated.json,c01_epoch_added.json,c06_truncated.json,c07_broken.json,c01_epoch_added.json"]),
    ("f5-retry-skip-once", ["--cases", "c06_truncated.json,c07_broken.json,c06_truncated.json,c01_epoch_added.json"]),
    ("f5-retry-period2", ["--cases", "c06_truncated.json,c01_epoch_added.json"]),
    ("f5-retry-budget", ["--cases", "c01_epoch_added.json", "--hang-from", "0", "--hang-for", "60"]),
    ("f5-retry-404", ["--cases", "c10_no_data_404.txt,c01_epoch_added.json"]),
    ("f5-retry-slow", ["--cases", "c01_epoch_added.json", "--hang-from", "0", "--hang-for", "60"]),
    # gzip handling and connection reuse
    ("f5-guard-e5", None),
    ("f5-guard-e6", None),
    ("f5-linklost-answered", ["--down-from", "0", "--down-for", "100", "--down-mode", "503"]),
    ("f5-linklost-answered-data", ["--cases", "c07_broken.json"]),
    ("f5-retry-close-first", ["--cases", "c06_truncated.json,c01_epoch_added.json"]),
    ("f5-retry-close-groups", ["--cases", "c06_truncated.json,c01_epoch_added.json,c06_truncated.json,c01_epoch_added.json,c06_truncated.json,c01_epoch_added.json"]),
    ("f5-gz-request", ["--cases", "c01_epoch_added.json"]),
    ("f5-gz-same", ["--cases", "c01_epoch_added.json,c01_epoch_added.json|@gz=1"]),
    ("f5-gz-cut-trailer", ["--cases", "c01_epoch_added.json|@gz=2,c01_epoch_added.json"]),
    ("f5-gz-cut-mid", ["--cases", "c01_epoch_added.json|@gz=3,c01_epoch_added.json"]),
    ("f5-gz-plain", ["--cases", "c01_epoch_added.json,c01_epoch_added.json"]),
    # 
    ("f5-gz-status", ["--cases", "c01_epoch_added.json|@gz=4"]),   # a wrong CRC: the framework throws, independently of the trailer check
    ("f5-gz-bomb", ["--cases", "c01_epoch_added.json|@gz=5"]),
    ("f5-gz-accept-only", ["--cases", "c01_epoch_added.json"]),
    ("f5-gz-bad-crc", ["--cases", "c01_epoch_added.json|@gz=4,c01_epoch_added.json"]),
    ("f5-fail-closes", ["--down-from", "0", "--down-for", "100", "--down-mode", "503"]),
    ("f5-retry-count-hang", ["--cases", "c01_epoch_added.json", "--hang-from", "0", "--hang-for", "60"]),
    ("f5-body-cut", ["--cases", "c01_epoch_added.json|@cut=1,c01_epoch_added.json|@cut=1,c01_epoch_added.json,c01_epoch_added.json|@cut=2"]),
    ("f5-answer-any-attempt", ["--cases", "c01_epoch_added.json|@hang=20,c06_truncated.json,c01_epoch_added.json|@hang=20"]),
    ("f5-ctl-calib-channels", None),   # real build only (n/a in the source build); its mutation is manual
    # TCP -cal / form button at 4096 and 2048 channels, histogram length before calibration; real build only
    ("f5-linklost-restored", ["--cases", "c07_broken.json*8,c01_epoch_added.json*3"]),
    ("f5-linklost-count-reset", ["--cases", "c07_broken.json*2,c01_epoch_added.json,c07_broken.json*2,c01_epoch_added.json*2"]),
    ("f5-tcp-cal-n", None),
    ("f5-ctl-length-first", None),
    ("f5-form-cal-n", None),
    # link loss by answered failures (mixed series, broken gzip x3, 401/403), the channels of the edited field, the page, TCP without a window, the headless text
    ("f5-linklost-mixed", ["--cases", "c01_epoch_added.json|@status=503,c01_epoch_added.json|@hang=20,c01_epoch_added.json|@status=503,c01_epoch_added.json|@hang=20,c01_epoch_added.json|@status=503"]),
    ("f5-linklost-mixed-poll", ["--cases", "c06_truncated.json,c01_epoch_added.json|@hang=20,c06_truncated.json,c01_epoch_added.json|@hang=20,c06_truncated.json,c01_epoch_added.json|@hang=20"]),
    ("f5-linklost-gzip3", ["--cases", "c01_epoch_added.json|@gz=2,c01_epoch_added.json|@gz=3,c01_epoch_added.json|@gz=4"]),
    ("f5-linklost-401", ["--cases", "c01_epoch_added.json|@status=401"]),
    ("f5-linklost-403", ["--cases", "c01_epoch_added.json|@status=403"]),
    ("f5-linklost-503", ["--cases", "c01_epoch_added.json|@status=503"]),
    ("f5-linklost-access-text", ["--counting", "1", "--time", "1000", "--total", "100", "--fail-from", "3", "--fail-status", "401"]),
    ("f5-form-cfg-n", ["--counting", "0", "--valid", "0"]),
    ("f5-form-test-n", ["--counting", "0", "--valid", "0"]),
    ("f5-ctl-headless-text", ["--counting", "0", "--time", "5000", "--total", "1000"]),
    ("console-tcp-start-headless-counting", ["--order", "hs", "--period", "0.5", "--cycles", "12", "--connections", "1"]),
    ("console-tcp-start-headless-stopped", ["--order", "hs", "--period", "0.5", "--cycles", "12", "--connections", "1", "--stopped-at", "500"]),
    ("console-tcp-start-headless-idle", ["--order", "hs", "--period", "0.5", "--cycles", "12", "--connections", "1", "--stopped-at", "0"]),
    # PR37 fix, UI lane (harness/UiFix.cs): console confirmation, page generations, "Read from device" from the page, the save refusal
    ("ui-console-classify", None),
    ("ui-console-confirm", ["--counting", "0", "--valid", "0"]),
    ("ui-page-generations", ["--counting", "0", "--valid", "0", "--boot-gzip", "1"]),
    ("ui-read-edited", ["--counting", "0", "--valid", "0"]),
    ("ui-save-refusal", ["--counting", "0", "--valid", "0"]),
    # PR37 fix, REST (harness/RestFixPr37.cs): the board's own limit, Continue over another acquisition, observe only behind the document,
    # profile channels, the JSON parser, Disconnect without waiting, a joined Stop, the user texts, no "Expect: 100-continue"
    ("pr37-rest-limit-end", ["--counting", "0", "--valid", "0", "--sta-limit"]),
    ("pr37-rest-limit-stall", ["--counting", "0", "--valid", "0", "--sta-limit", "--stop-at", "8"]),
    ("pr37-rest-continue-other", ["--counting", "1", "--time", "1000", "--total", "37000"]),
    ("pr37-rest-observe-behind", ["--counting", "1", "--time", "1000", "--total", "37000"]),
    ("pr37-rest-channels", None),
    ("pr37-rest-json", None),
    ("pr37-rest-detach-hang", ["--counting", "1", "--time", "100", "--total", "50", "--hang-from", "4", "--hang-for", "30"]),
    ("pr37-rest-stop-joined", ["--counting", "1", "--time", "1000", "--total", "37000"]),
    ("pr37-rest-texts", ["--counting", "0", "--valid", "0", "--cmd-ok-false"]),
    ("pr37-rest-expect", ["--counting", "0", "--valid", "0"]),
    # Replace document spectrum; TCP Start of the same document probes the board
    ("startpkg-replace", ["--counting", "1", "--time", "70000", "--total", "400000"]),
    ("startpkg-replace-guard", ["--counting", "1", "--time", "70000", "--total", "400000"]),
    ("console-tcp-same-behind", ["--order", "hs", "--period", "0.5", "--cycles", "12", "--connections", "1"]),
    ("console-tcp-same-replace", ["--order", "hs", "--period", "0.5", "--cycles", "16", "--connections", "1"]),
    # findings of VERIFY-1, #BM-4/5/13/6, the Start text; console-tcp-night-clear = the #BM-3 diagnosis
    ("night-replace-empty", None),
    ("night-start-text", None),
    ("night-observe-replace", ["--counting", "1", "--time", "70000", "--total", "400000"]),
    ("night-stop-joined-ask", ["--counting", "1", "--time", "70000", "--total", "400000"]),
    ("night-stop-joined-keep", ["--counting", "1", "--time", "70000", "--total", "400000"]),
    ("night-status-401", ["--counting", "1", "--time", "1000", "--total", "100", "--fail-from", "3", "--fail-status", "401"]),
    ("night-answer-stale", None),
    ("night-reset-offer", None),
    ("console-tcp-night-resume", ["--order", "hs", "--period", "0.5", "--cycles", "12", "--connections", "1", "--stopped-at", "500"]),
    ("console-tcp-night-stop-ask", ["--order", "hs", "--period", "0.5", "--cycles", "16", "--connections", "1"]),
    ("console-tcp-night-clear", ["--order", "hs", "--period", "0.5", "--cycles", "16", "--connections", "1", "--stopped-at", "0"]),
    # #BM-10 (RestHarnessBm10.cs, mocks/mock_mdns.py): DNS-SD browse of two boards, choice window logic
    ("bm10-count", ["--set", "two-new"]), ("bm10-chain", ["--set", "two-new"]), ("bm10-txt-sn", ["--set", "two-new"]),
    ("bm10-txt-rest", ["--set", "two-new"]), ("bm10-legacy-serial", ["--set", "legacy"]), ("bm10-legacy-filter", ["--set", "legacy"]),
    ("bm10-mixed", ["--set", "real-146"]), ("bm10-followup", ["--set", "split"]), ("bm10-one-query", ["--set", "two-new"]),
    ("bm10-refresh-two", ["--set", "legacy"]), ("bm10-refresh-one", ["--set", "legacy"]),
    ("bm10-refresh-none", ["--set", "legacy"]), ("bm10-refresh-cancel", ["--set", "legacy"]),
    # findings 1, 3, 4, 6, 7, 8
    ("fix2-stop-auto", None), ("fix2-after-wait-held", None),
    ("bm10-refresh-typed", ["--set", "legacy"]), ("bm10-dot-instance", ["--set", "legacy"]), ("bm10-bad-label", ["--set", "legacy"]),
    ("bm10-flood-cap", ["--set", "flood"]), ("bm10-flood-budget", ["--set", "flood"]),
    ("console-tcp-fix2-silent", ["--order", "hs", "--period", "0.5", "--cycles", "0", "--connections", "1"]),
    ("console-tcp-fix2-silent-new", ["--order", "hs", "--period", "0.5", "--cycles", "0", "--connections", "1"]),
    ("console-tcp-fix2-uncertain", ["--order", "hs", "--period", "6", "--cycles", "1", "--connections", "1"]),
    # findings C, D, E
    ("fix3-name-to-search", None), ("fix3-held-throws", None), ("fix3-offer-stop", None),
    ("fix4-joined-clear-text", None),   # #BM-14/#BM-15
    ("bm10-schedule", ["--set", "two-new", "--delays", "b146=500,b119=5000"]),   # #BM-17: boards answer late, the query is repeated
    ("bm10-adapters", ["--set", "two-new", "--only-from", "127.0.0.3"]),   # #BM-17: one socket per adapter
    ("bm18-chooser", None),   # #BM-18: the board chooser window contains the list (real build only)
    # VERIFY-4 findings 1, 2, 3, 5, 7 (RestHarnessFix9.cs, sets three / difflabel of mocks/mock_mdns.py)
    ("bm10-quiet-three", ["--set", "three", "--delays", "b3=3500"]),
    ("bm10-count-address", ["--set", "difflabel", "--delays", "b119=6500"]),
    ("bm10-bind-fallback", ["--set", "two-new"]),
    ("bm10-log-sent", ["--set", "two-new"]),
    ("bm10-log-packets", ["--set", "split"]),
    # maintainer review of PR 38, board discovery (RestHarnessBm10.cs, RestHarnessFix7.cs; sets of mocks/mock_mdns.py)
    ("bm10-refresh-typed-dead", ["--set", "legacy"]),   # the address in the field does not answer: the browse, the choice
    ("bm10-same-host", ["--set", "samehost"]),   # two boards with one host name: the A of the SRV's own packet
    ("bm10-same-host-split", ["--set", "samehost-split", "--reply-from-owner"]),   # the same, told apart by the source of the SRV packet
    ("bm10-incomplete", ["--set", "orphan"]),   # an instance without an address is counted, one board is not taken silently
    ("bm10-port", ["--set", "norest"]),   # no TXT rest: the port of the form; the port read on comes back for the field
    ("bm10-goodbye", ["--set", "two-new", "--goodbye", "b119=200"]),   # TTL 0 withdraws the board
    ("bm10-long-label", ["--set", "longlabel"]),   # an instance of two labels; a question that cannot be written is left out alone
    ("bm10-no-network", ["--set", "two-new"]),   # nothing sent: the browse ends at once
    ("bm18-choose-stale", None),   # the choice window only for the current search, not over another window (real build only)
    ("fix9-reset-carry", None),
    ("fix9-reset-other-board", None),   # the deferred reset of one board is not given to another (host or port changed)
    # audit 6 (RestHarnessFix10.cs): reentrant Stop under the open question, DNS name chains and length, reset dropped at the switch to REST
    ("console-tcp-fix10-stop-reenter", ["--order", "hs", "--period", "0.5", "--cycles", "16", "--connections", "1"]),
    ("fix10-dns-chain", None), ("fix10-dns-limit", None), ("fix10-dns-good", None), ("fix10-reset-to-rest", None),
    # PR #38, maintainer's REST fixes (RestHarnessFixR.cs): R2 - the guard after Replace starts from the snapshot of the question; R6 - the observe-only hint
    ("fixr2-replace-reset-asked", ["--counting", "1", "--time", "70000", "--total", "400000"]),
    ("fixr2-replace-epoch-asked", ["--cases", "c01_epoch_added.json*2,c02_boot_id_changed.json|time=170000|total=13442130"]),   # the epoch changed, time and counts overtook
    ("fixr2-replace-resume", ["--counting", "0", "--time", "5000", "--total", "1000"]),
    ("fixr2-replace-join-epoch", ["--cases", "c01_epoch_added.json|time=163682,c01_epoch_added.json|time=163690,c01_epoch_added.json|time=163700,c01_epoch_added.json|time=163710*40"]),
    ("fixr6-observe-hint", ["--counting", "1", "--time", "70000", "--total", "400000"]),
]
# scenarios that need the controller or the forms exist in the REAL build only (the source build prints n/a). On the real build each must
# EXECUTE at least this many checks ("checks executed: N" of the harness); n/a or fewer is a failure of the suite, not a pass.
REAL_ONLY_MIN_CHECKS = {"f5-tcp-cal-n": 8, "f5-ctl-length-first": 5, "f5-form-cal-n": 6, "f5-form-cfg-n": 4, "f5-form-test-n": 5, "f5-ctl-headless-text": 3,
                        # PR37 fix, UI lane
                        "ui-console-confirm": 8, "ui-page-generations": 5, "ui-read-edited": 6, "ui-save-refusal": 3,
                        # start package, night package, board discovery
                        "night-start-text": 4, "night-status-401": 5, "night-answer-stale": 4, "night-reset-offer": 6,
                        "fix2-stop-auto": 5, "fix2-after-wait-held": 3, "fix3-held-throws": 6, "fix3-offer-stop": 6, "fix4-joined-clear-text": 9, "bm10-adapters": 7, "bm10-schedule": 4, "bm18-chooser": 23, "bm18-choose-stale": 5,
                        "bm10-quiet-three": 2, "bm10-count-address": 2, "bm10-bind-fallback": 3, "bm10-log-sent": 4, "bm10-log-packets": 3, "fix9-reset-carry": 5, "fix9-reset-other-board": 6,
                        "console-tcp-fix10-stop-reenter": 3, "fix10-dns-chain": 3, "fix10-dns-limit": 2, "fix10-dns-good": 3, "fix10-reset-to-rest": 5,
                        "fixr6-observe-hint": 5}
FIX_SCENARIOS = ["overtake", "restart-doc", "preset-new", "deferred-resumed", "clear-stale", "session-key",
                 "headless-stopped", "stop-hang", "url-userinfo"]
PASSC_SCENARIOS = ["prefix-auth", "parse", "deferred-resumed", "restart", "time-back", "nodata-404", "dead-404", "dead-flag",
                   "reset-unsent", "live-nan", "link-loss", "link-silent", "hist-reset", "deferred-foreign"]

MUTATIONS = [
    ("decrease", "the document is overwritten when the board counters go down (no comparison)",
     [("AtomSpectraRestPolicy.cs", "if (hasLast && (s.Time < lastTime || s.Total < lastTotal || (histFromSample && s.HistOk < lastHistOk)))", "if (false)")], "time-back", False),   # coarse: the whole comparison, reddens time-back and hist-reset; the pointed ones are MP5 (time) and MP6 (hist_ok)
    ("continue-sta", "Start on a counting board + Continue sends -sta instead of joining",
     [("AtomSpectraRestPolicy.cs", "return counting ? AtomSpectraRestStartPlan.Join : AtomSpectraRestStartPlan.Resume;", "return AtomSpectraRestStartPlan.Resume;")], "join-continue", True),
    ("preset", "the document preset 3600 s is kept when joining a board with time >> 3600",
     [("AtomSpectraRestPolicy.cs", "long wanted = Math.Max((long)documentPreset, boardSeconds + add);", "long wanted = documentPreset;")], "preset", True),
    ("close-sto", "closing (Stop) a joined acquisition sends -sto",
     [("AtomSpectraRestSession.cs", "if (!startedByUs)", "if (false)")], "close-joined", True),
    ("deferred-reset", "a failed Clear is queued and sent with the next Start",
     [("AtomSpectraRestSession.cs", "private bool stallReported;", "private bool stallReported; private bool mutResetQueued;"),
      ("AtomSpectraRestSession.cs", "// [clear failed]", "mutResetQueued = true;"),
      ("AtomSpectraRestSession.cs", "// [deferred reset: none] - a failed Clear leaves nothing behind for this Start", "if (mutResetQueued) { client.Reset(commandTimeoutMs); mutResetQueued = false; }")], "clear-fail", True),
    ("404-failure", "HTTP 404 'No spectrum data yet' is counted as a link failure",
     [("AtomSpectraRestAcquisition.cs", "Interlocked.Increment(ref _noData);", "Interlocked.Increment(ref _noData); Interlocked.Increment(ref _failures);")], "nodata-404", True),   # pointed the whole 404 branch off also broke dead-404 and ext-reset
    ("ok-false", "POST 200 {\"ok\":false} is taken as success",
     [("AtomSpectraRestClient.cs", "get { return Ok && !BodySaysRefused(Body); }", "get { return Ok; }")], "ok-false", True),
    ("observe-clear", "Clear in observe-only mode posts a reset",
     [("AtomSpectraRestSession.cs", "AtomSpectraTcpLog.Info(\"Session\", \"Clear: observe only, the board is not reset\");\n                return false;", "AtomSpectraTcpLog.Info(\"Session\", \"Clear: observe only, the board is not reset\");")], "observe", True),
    ("deferred-sto-unguarded", "the deferred -sto is sent although the board was rebooted since our Stop",
     [("AtomSpectraRestSession.cs", "if (!now.HasData || now.Time < stopTime || now.HistOk < stopHist)", "if (false)")], "deferred-stop", True),
    ("no-stall", "a board that stopped by itself is never noticed",
     [("AtomSpectraRestPolicy.cs", "return sawGrowth && lastGrowthMs >= 0 && nowMs - lastGrowthMs >= stallMs;", "return false;")], "stall", True),
    # fix scenarios: every mutation must redden exactly its own scenario (full run)
    ("overtake", "a new acquisition that overtook time/total is written over the document",
     [("AtomSpectraRestPolicy.cs", "if (down >= 0)", "if (false)")], "overtake", True),
    ("restart-doc", "Start over in a document with data",
     [("AtomSpectraRestSession.cs", "if (DocumentSeconds > 0 || DocumentCounts > 0)", "if (false)")], "restart-doc", True),
    ("preset-new", "a new run keeps the document preset 3600 s instead of the board limit",
     [("AtomSpectraRestSession.cs", "if (startedByUs && limitSeconds > 0)", "if (false)")], "preset-new", True),
    ("deferred-resumed", "the deferred -sto hits a board that stopped and was resumed by someone else",
     [("AtomSpectraRestSession.cs", "bool counted = now.Time - first[0] + 2 >= (wall - first[1]) / 1000 && now.Time - stopTime + 3 >= wall / 1000;", "bool counted = true;")], "deferred-foreign", True),
    ("clear-stale", "the cached snapshot from before our reset is taken as data",
     [("AtomSpectraRestPolicy.cs", "if (afterReset && s != null && s.HasData", "if (false && s != null && s.HasData")], "clear-stale", True),
    ("session-key", "the session key carries the DPAPI text (changes on every save)",
     [("AtomSpectraTcpDeviceConfig.Rest.cs", "(RestAuthSecret ?? \"\").GetHashCode()", "RestAuthSecretProtected")], "session-key", True),
    ("headless-stopped", "headless Start on a stopped board with data starts it (-sta) without a question",
     [("AtomSpectraRestPolicy.cs", "return !counting && BoardHasData(board) ? AtomSpectraRestStartPlan.Ask : AtomSpectraRestStartPlan.Join;", "return !counting && BoardHasData(board) ? AtomSpectraRestStartPlan.Resume : AtomSpectraRestStartPlan.Join;")], "headless-stopped", True),
    ("stop-hang", "Stop waits for the poll hanging on the board",
     [("AtomSpectraRestSession.cs", "DetachNoWait();   // the caller (UI thread)", "{ AtomSpectraRestAcquisition hung = acquisition; DetachNoWait(); if (hung != null) hung.Dispose(); }   // the caller (UI thread)")], "stop-hang", True),
    ("url-userinfo", "a URL with user:pass is accepted",
     [("AtomSpectraTcpDeviceConfig.Rest.cs", "&& !string.IsNullOrEmpty(u.Host) && string.IsNullOrEmpty(u.UserInfo);", "&& !string.IsNullOrEmpty(u.Host);")], "url-userinfo", True),
    # tests that were green on broken code, and the mock matched to the board
    ("prefix-lost", "the path prefix of the URL is dropped (requests go to /api/... of the proxy root)",
     [("AtomSpectraRestClient.cs", "_baseUrl = u.GetLeftPart(UriPartial.Path).TrimEnd('/');", "_baseUrl = u.GetLeftPart(UriPartial.Authority);")], "prefix-auth", True),
    ("live-rounded", "live time read wrong (57721.2 -> 57721)",
     [("AtomSpectraRestAcquisition.cs", "                        liveVal = lv;", "                        liveVal = Math.Floor(lv);")], "parse", True),
    ("resumed-sto", "a -sto that reached the board without a clear answer is repeated later",
     [("AtomSpectraRestSession.cs", "if (sr != null && !sr.NotSent && !(sr.Ok && !sr.Accepted))", "if (false)")], "deferred-resumed", True),
    ("restart-stale", "after Start over our own reset is not accepted, the new acquisition is taken for a foreign decrease and never reaches the document",
     [("AtomSpectraRestSession.cs", "guard.ConfirmOwnReset(boardTime, second.HasData ? second.Total : 0);", "guard.Begin(boardTime, second.HasData ? second.Total : 0, true);")], "restart", True),
    ("time-only", "the board time going back is not compared (counts only)",
     [("AtomSpectraRestPolicy.cs", "if (hasLast && (s.Time < lastTime || s.Total < lastTotal ||", "if (hasLast && (s.Total < lastTotal ||")], "time-back", True),
    ("hist-ok", "hist_ok going down (board reboot with autosave) is not compared",
     [("AtomSpectraRestPolicy.cs", "|| (histFromSample && s.HistOk < lastHistOk)))", "))")], "hist-reset", True),
    ("policy-time", "a board whose time grows but hist_ok does not is taken for stopped",
     [("AtomSpectraRestPolicy.cs", "return second.HistOk > first.HistOk || second.Time > first.Time;", "return second.HistOk > first.HistOk;")], "policy", True),
    ("dead-presence", "C mismatch 5: X-Spectrometer-Dead: 0 is taken for a silent analyzer",
     [("AtomSpectraRestClient.cs", "(resp.Headers[\"X-Spectrometer-Dead\"] ?? \"\").Trim() == \"1\"", "resp.Headers[\"X-Spectrometer-Dead\"] != null")], "nodata-404", True),
    ("sent-false", "C mismatch 7: reset answered sent=false is reported as done",
     [("AtomSpectraRestSession.cs", "if (r.Accepted && r.SentFalse)", "if (false)")], "reset-unsent", True),
    ("live-nan", "C mismatch 10: the bare token nan is not parsed (the whole snapshot is lost)",
     [("AtomSpectraRestClient.cs", "if (Sees(\"nan\") ||" + NL + "                    Sees(\"NaN\") ||" + NL + "                    Sees(\"inf\")", "if (Sees(\"inf\")")], "live-nan", True),
    # device console: each mutation must redden its target scenario ("exactly one" is informative)
    ("console-baseline", "console: the board log is read from the start, old lines are shown as the answer",
     [("AtomSpectraDeviceConsole.cs", "                    baseline = next;", "                    baseline = 0;")], "console-rest", False),
    ("console-serial", "Refresh: the serial number of GET /api/device is not read",
     [("AtomSpectraDeviceConsole.cs", "Serial = serial ?? \"?\",", "Serial = \"?\",")], "console-refresh", False),
    ("console-refused", "console: a command the board refused ({\"ok\":false}) is shown as sent",
     [("AtomSpectraDeviceConsole.cs", "if (!r.Accepted)", "if (false)")], "console-rest-refused", False),
    ("address-v6", "address: an IPv6 host is written without brackets",
     [("AtomSpectraDeviceConsole.cs", "string h = host.Contains(\":\") ? \"[\" + host + \"]\" : host;", "string h = host;")], "console-address", False),
    ("poll-clamp", "poll interval is not kept within 1..60",
     [("AtomSpectraDeviceConsole.cs", "return Math.Max(1, Math.Min(60, seconds));", "return seconds;")], "console-address", False),
    ("console-tcp-send", "TCP console: the command is not written to the bridge",
     [("AtomSpectraDeviceConsole.cs", "                acq.SendCommand(command);", "")], "console-tcp", False),
    ("linklost-tcp-text", "REST link loss names the TCP bridge's 60 s instead of the poll period",
     [("AtomSpectraRestSession.cs", "AtomSpectraTcpLog.Mask(client.BaseUrl), periodSeconds);", "AtomSpectraTcpLog.Mask(client.BaseUrl), 60);")], "rest-linklost-text", False),
    # the -inf answer text at the Debug level
    ("inf-not-logged", "the -inf answer text is not written to the log",
     [("AtomSpectraTcpSession.cs", "AtomSpectraTcpLog.Debug(\"TCP\", \"answer to \"", "AtomSpectraTcpLog.Add((AtomSpectraTcpLogLevel)99, \"TCP\", \"answer to \"")], "console-tcp-inf-debug", False),
    ("inf-at-info", "the -inf answer text goes to Info (shown by default; the serial in plain view)",
     [("AtomSpectraTcpSession.cs", "AtomSpectraTcpLog.Debug(\"TCP\", \"answer to \"", "AtomSpectraTcpLog.Info(\"TCP\", \"answer to \"")], "console-tcp-inf-debug", False),
    # gzip_level of the board (AtomSpectraRestBootConfig)
    ("gzip-unsupported-posts", "gzip_level is POSTed to a firmware without the field (release 1.2.37 would answer ok, change nothing)",
     [("AtomSpectraRestBootConfig.cs", "if (before == Unsupported)", "if (false)")], "gzip-unsupported", False),
    ("gzip-no-readback", "{\"ok\":true} counts as success without reading the level back",
     [("AtomSpectraRestBootConfig.cs", "if (after != level)", "if (false)")], "gzip-ignored", False),
    ("gzip-okfalse", "{\"ok\":false} is not recognised (only the HTTP code is looked at)",
     [("AtomSpectraRestBootConfig.cs", "if (!r.Accepted)", "if (false)")], "gzip-okfalse", False),
    ("gzip-any-level", "a level other than 0, 1, 6 is sent to the board",
     [("AtomSpectraRestBootConfig.cs", "if (Array.IndexOf(Levels, level) < 0)\n            {\n                message = \"level \"", "if (false)\n            {\n                message = \"level \"")], "gzip-write", False),
    # TCP Start asks instead of resetting the board
    ("tcp-join-sta", "TCP Continue on a counting board still sends -sta",
     [("AtomSpectraTcpSession.cs", "if (plan != AtomSpectraRestStartPlan.Join) accepted &= Command(\"-sta\", commandTimeoutMs);", "accepted &= Command(\"-sta\", commandTimeoutMs);")], "console-tcp-start-continue", False),
    ("tcp-always-reset", "TCP Start resets the board for every new document again (no question)",
     [("AtomSpectraTcpSession.cs", "AtomSpectraRestBoardSample[] s = ProbeBoard(ProbeMs);   // with or without a window", "return AtomSpectraRestStartPlan.Restart; AtomSpectraRestBoardSample[] s = ProbeBoard(ProbeMs);   // with or without a window")], "console-tcp-start-continue", False),
    ("tcp-joined-stop", "TCP Stop of a joined acquisition sends -sto",
     [("AtomSpectraTcpSession.cs", "if (joinedRunning)", "if (false)")], "console-tcp-start-continue", False),
    ("tcp-counting", "TCP Start does not see that the board counts",
     [("AtomSpectraTcpSession.cs", "s[s.Length - 1].Time > s[0].Time", "false")], "console-tcp-start-continue", False),
    ("tcp-idle-asks", "TCP Start resets an idle board's (empty) spectrum without need",
     [("AtomSpectraTcpSession.cs", "AtomSpectraRestStartPlan plan = AtomSpectraRestPolicy.PlanStart(false, Ask != null, counting, board, DocumentSeconds, DocumentCounts);", "AtomSpectraRestStartPlan plan = AtomSpectraRestStartPlan.Restart;")], "console-tcp-start-idle", False),
    ("unreachable-period", "REST Start on an unreachable board names 60 s instead of the poll period",
     [("AtomSpectraRestSession.cs", "AtomSpectraTcpLog.Mask(client.BaseUrl), periodSeconds, LastErrorText", "AtomSpectraTcpLog.Mask(client.BaseUrl), 60, LastErrorText")], "rest-unreachable-text", False),
    # each rule cut out of the code; full run, the target must be the only red test
    ("channels", "only the 8192 answer is taken (channels=N answer refused)",
     [("AtomSpectraRestAcquisition.cs", "string lengthError = LengthError(bins.Count, declared, requested, out lengthUser);", "string lengthError = bins.Count != Channels ? \"bins count != \" + Channels : null;")], "f5-channels", True),
    ("boot", "boot_id is not compared", [("AtomSpectraRestPolicy.cs", "if (Differs(BootId, before.BootId))", "if (false)")], "f5-epoch-boot", True),
    ("dev", "dev_resets is not compared", [("AtomSpectraRestPolicy.cs", "if (Differs(DevResets, before.DevResets))", "if (false)")], "f5-epoch-dev", True),
    ("spec", "spec_resets is not compared", [("AtomSpectraRestPolicy.cs", "if (Differs(SpecResets, before.SpecResets))", "if (false)")], "f5-epoch-spec", True),
    ("skip", "a skipped poll does not lengthen the period", [("AtomSpectraRestAcquisition.cs", "PeriodAfter(false);", "")], "f5-skip", True),
    ("halve", "5 full polls do not halve the period", [("AtomSpectraRestPolicy.cs", "int next = Math.Max(Math.Max(configuredMs, FloorMs), EffectiveMs / 2);", "int next = EffectiveMs;")], "f5-recover", True),
    ("404", "404 at a known epoch is a Trip", [("AtomSpectraRestPolicy.cs", " && lastEpoch == null)", ")")], "f5-nodata", True),
    ("gzip", "a gzip answer is not decoded", [("AtomSpectraRestClient.cs", "if (encoding != \"gzip\" && encoding != \"deflate\") return wire;", "if (encoding != \"deflate\") return wire;")], "gzip", True),
    ("calib", "calib is applied to N channels without the rescale",
     [("AtomSpectraRestCalibration.cs", "if (channels <= 0 || channels == calibChannels) return (double[])slots.Clone();", "return (double[])slots.Clone();")], "f5-calib", True),
    ("centre", "s = 0 instead of (m-1)/2", [("AtomSpectraRestCalibration.cs", "return (m - 1) / 2.0;", "return 0;")], "f5-calib", True),
    ("timeout", "a poll past its deadline (no response) does not lengthen the period", [("AtomSpectraRestAcquisition.cs", "PeriodAfter(false);", "if (r.Status != 0) PeriodAfter(false);")], "f5-timeout", True),
    ("linklost-eff", "link loss by time counted in the EFFECTIVE period (the adaptive period delays it)", [("AtomSpectraRestAcquisition.cs", "bool silent = silentMs >= Math.Max(3L * Math.Max(_periodMs, periodInForce), (long)LinkLostFloorMs);", "bool silent = silentMs >= Math.Max(3L * _period.EffectiveMs, (long)LinkLostFloorMs);")], "f5-linklost-time", True),   # text follows the adaptive-period rule (the period in force BEFORE this poll's doubling)
    # each polling rule cut out; the target must be red (full run, "exactly one" is informative)
    ("board-reset-stop", "a changed epoch no longer stops polling (the old path: the new set goes into the document)",
     [("AtomSpectraRestPolicy.cs", "return Trip(\"board reset: \" + change);", "AcceptReset(0, 0);")], "f5-reset-board", True),
    ("404-stall", "a 404 moves the board towards a stall again",
     [("AtomSpectraRestSession.cs", "if (u != AtomSpectraRestUpdate.NoData && !stallReported", "if (!stallReported")], "f5-nodata-nostall", True),
    ("epoch-after-stale", "the epoch is checked after Stale again (the new set that caught up is the cached one)",
     [("AtomSpectraRestPolicy.cs", "string change = s != null && s.HasData && s.Epoch != null ?", "string change = s != null && s.HasData && s.Epoch != null && !(afterReset && s.Time >= staleTime && s.Total >= staleTotal) ?")], "f5-stale-epoch", True),
    ("clear-arm-after", "the own-reset allowance is raised only after the reset request returns (the old order)",
     [("AtomSpectraRestSession.cs", "guard.ExpectOwnReset(running != null ? running.NowMs : -1);", ""), ("AtomSpectraRestSession.cs", "guard.ConfirmOwnReset();", "guard.AcceptReset();")], "f5-clear-race", True),
    # each rule of items 1-8 cut out
    ("window-no-term", "item 1: the window never closes after the last change",
     [("AtomSpectraRestPolicy.cs", "windowShiftAt + Math.Max(6000L, 2L * PeriodMs)", "long.MaxValue")], "f5-window-steps", True),
    ("lost-cancels", "item 2: a lost answer withdraws the window like NotSent",
     [("AtomSpectraRestSession.cs", "if (r.NotSent) guard.CancelOwnReset();", "guard.CancelOwnReset();")], "f5-window-lost", True),
    ("notsent-keeps", "item 2 (extra): NotSent does not withdraw the window",
     [("AtomSpectraRestSession.cs", "if (r.NotSent) guard.CancelOwnReset();", "")], "f5-window-notsent", True),
    ("clear-twice", "item 3: a repeated Clear loses the numbers of the board before the first reset",
     [("AtomSpectraRestPolicy.cs", "if (wasOpen && !hasLast) { preTime = Math.Max(preTime, staleTime); preTotal = Math.Max(preTotal, staleTotal); }", "")], "f5-clear-twice", True),
    ("startover-stale", "item 4: Stale by the counters without a reference epoch",
     [("AtomSpectraRestPolicy.cs", " && !(s.Epoch != null && refEpoch == null)", "")], "f5-startover-lost", True),
    ("floor-configured", "item 5: the halving stops at 2 s again",
     [("AtomSpectraRestPolicy.cs", "int next = Math.Max(Math.Max(configuredMs, FloorMs), EffectiveMs / 2);", "int next = Math.Max(FloorMs, EffectiveMs / 2);")], "f5-period-floor", True),
    ("nodata-every-404", "item 6: the no-data line is written at every 404 of the series",
     [("AtomSpectraRestSession.cs", "if (noDataReported || sample.AtMs - noDataSince <= 3 * period) return;", "if (sample.AtMs - noDataSince <= 3 * period) return;")], "f5-nodata-signal", True),
    ("stale-after", "item 7: the freshness mark does not follow the period",
     [("AtomSpectraRestAcquisition.cs", "AtomSpectraTcpLog.StaleAfterSeconds = 2 * _period.EffectiveMs / 1000 + 5;", "")], "f5-stale-after", True),
    ("swallow-line", "the swallowed external reset is not written to the log",
     [("AtomSpectraRestPolicy.cs", "if (windowChanges >= 2 && Note != null) Note(", "if (false && Note != null) Note(")], "f5-window-swallow", True),
    # each rule cut out
    ("repeat-clear-overwrites", "a repeated Clear overwrites the open window",
     [("AtomSpectraRestPolicy.cs", "if (windowPrior) return;   // a repeated Clear leaves the open window as it is until its request is confirmed", "if (windowPrior) { windowT0 = nowMs; windowShiftAt = -1; windowChanges = 0; return; }")], "f5-window-repeat-notsent", True),
    ("startover-no-shift", "the first snapshot with an epoch after Start over is no answer (no shift)",
     [("AtomSpectraRestPolicy.cs", "if (ownReset && s.Epoch != null && refEpoch == null) { windowShiftAt = s.AtMs; if (windowChanges < 1) windowChanges = 1; }", "")], "f5-startover-external", True),
    ("unknown-outcome-trip", "a drop of the counters inside the window of an unknown outcome is a Trip",
     [("AtomSpectraRestPolicy.cs", "if (hasLast && ownReset && openedByExpect && counted) AbsorbChange(", "if (false && hasLast && ownReset && openedByExpect && counted) AbsorbChange(")], "f5-window-lost-v1", True),
    ("stale-ends-series", "a Stale snapshot ends the no-data series",
     [("AtomSpectraRestSession.cs", "if (u == AtomSpectraRestUpdate.Apply) { noDataSince = -1;", "if (true) { noDataSince = -1;")], "f5-nodata-stale", True),
    ("linklost-count", "LinkLost again by 3 failures in a row",
     [("AtomSpectraRestAcquisition.cs", "if ((silent || answeredInRow >= LinkLostAfterFailures) && !_linkLost)", "if ((cf == LinkLostAfterFailures || silent || answeredInRow >= LinkLostAfterFailures) && !_linkLost)")], "f5-linklost-silence", True),
    ("linklost-no-floor", "LinkLost (extra): no 15 s floor",
     [("AtomSpectraRestAcquisition.cs", "Math.Max(3L * Math.Max(_periodMs, periodInForce), (long)LinkLostFloorMs)", "3L * Math.Max(_periodMs, periodInForce)")], "f5-linklost-silence", True),
    ("retry-parse-cut", "a cut-off body is not repeated",
     [("AtomSpectraRestAcquisition.cs", "return r.Ok && error.StartsWith(\"parse exception\");", "return false;")], "f5-retry-ok", True),
    ("retry-count-attempts", "a failure in a row is counted per attempt",
     [("AtomSpectraRestAcquisition.cs", "Interlocked.Increment(ref _retries);", "Interlocked.Increment(ref _retries); Interlocked.Increment(ref _consecutiveFailures);")], "f5-retry-count-hang", True),   # was f5-retry-skip-once, masked by the rule that an answer of any attempt is an answer
    ("retry-no-budget", "no budget, attempts may outlast the period",
     [("AtomSpectraRestAcquisition.cs", " || spent.ElapsedMilliseconds + RetryPauseMs + attemptMs > period", "")], "f5-retry-budget", True),
    ("retry-404", "a 404 is repeated",
     [("AtomSpectraRestAcquisition.cs", "if (r.Status == 404) return false;", "if (r.Status == 404) return true;")], "f5-retry-404", True),
    ("attempt-third", "the attempt is period/3 (the third attempt of a 10 s period does not start)",
     [("AtomSpectraRestAcquisition.cs", "(int)((long)period * AttemptPermille / 1000)", "period / 3")], "f5-retry-slow", True),
    ("attempt-quarter", "the attempt is period/4 (2.5 s at a period of 10 s)",
     [("AtomSpectraRestAcquisition.cs", "(int)((long)period * AttemptPermille / 1000)", "period / 4")], "f5-retry-slow", True),
    ("no-accept-encoding", "gzip: the request does not ask for gzip", [("AtomSpectraRestClient.cs", "if (SendAcceptEncoding)", "if (false)")], "f5-gz-request", True),
    ("no-decode", "gzip: a gzip answer is not decoded by Content-Encoding", [("AtomSpectraRestClient.cs", "Decode(ms.ToArray(), result.ContentEncoding, out problem, out userProblem, out broken)", "ms.ToArray()")], "f5-gz-same", True),
    ("cut-gzip-whole", "gzip: a gzip cut short (the trailer gone) is taken for a whole answer", [("AtomSpectraRestClient.cs", "if (encoding == \"gzip\") CheckGzipTrailer(wire, output.Length);", "")], "f5-gz-cut-trailer", True),
    ("keep-first-connection", "the connection of the failed first attempt stays open", [("AtomSpectraRestAcquisition.cs", "_client.CloseConnections(null);", "")], "f5-retry-close-first", True),
    ("groups-pile-up", "the retry-<Guid> group is not closed after the attempt", [("AtomSpectraRestClient.cs", "if (connectionGroup != null && method == \"GET\") CloseConnections(connectionGroup);", "")], "f5-retry-close-groups", True),
    ("503-silence", "a 503 answer is silence", [("AtomSpectraRestAcquisition.cs", "if (r.Status > 0) MarkAnswered();", "if (r.Status > 0 && r.Status != 503) MarkAnswered();")], "f5-linklost-answered", True),
    ("data-silence", "a 200 whose body does not parse is silence", [("AtomSpectraRestAcquisition.cs", "if (r.Status > 0) MarkAnswered();", "if (r.Status > 0 && r.Status != 200) MarkAnswered();")], "f5-linklost-answered-data", True),
    ("notsent-loses-flag", "the withdrawal of Clear 2 clears the flag of an unknown outcome of Clear 1", [("AtomSpectraRestPolicy.cs", "openedByExpect = priorOpened;", "openedByExpect = false;")], "f5-guard-e5", True),
    ("repeat-overwrites", "a confirmed repeated Clear overwrites windowT0, the shift and the count", [("AtomSpectraRestPolicy.cs", "if (prior && !done) windowKeepAt = pendingT0 < 0 ? -2 : pendingT0;", "if (prior && !done) { windowT0 = pendingT0; windowShiftAt = -1; windowChanges = 0; }")], "f5-guard-e6", True),
    ("no-limit", "the decoded body has no limit", [("AtomSpectraRestClient.cs", "public const int MaxBodyBytes = 1 << 20;", "public const int MaxBodyBytes = int.MaxValue;")], "f5-gz-bomb", True),
    ("status-wiped", "a gzip cut short wipes the status of the answer", [("AtomSpectraRestClient.cs", "{ result.Error = problem; result.BodyBroken = broken; }", "{ result.Error = problem; result.BodyBroken = broken; if (broken) result.Status = 0; }")], "f5-gz-status", True),
    ("last-attempt-open", "the connection of the last failed attempt stays open", [("AtomSpectraRestAcquisition.cs", "{ _client.CloseConnections(\"\"); }", "{ }")], "f5-fail-closes", True),
    ("last-attempt-counts", "only the answer of the last attempt counts", [("AtomSpectraRestAcquisition.cs", "if (r.Status > 0) MarkAnswered();", "if (r.Status > 0 && n == attempts) MarkAnswered();")], "f5-answer-any-attempt", True),
    ("accept-deflate", "deflate is asked for as well", [("AtomSpectraRestClient.cs", "AcceptEncoding] = \"gzip\";", "AcceptEncoding] = \"gzip, deflate\";")], "f5-gz-accept-only", True),
    ("body-status-wiped", "a body that breaks off after the status wipes the status", [("AtomSpectraRestClient.cs", "readProblem = \"body read broke off: \" + ex.Message;", "readProblem = \"body read broke off: \" + ex.Message; result.Status = 0;")], "f5-body-cut", True),
    ("short-body-accepted", "a body shorter than its Content-Length (closed early, no exception) is taken as whole", [("AtomSpectraRestClient.cs", "resp.ContentLength >= 0 && ms.Length < resp.ContentLength", "false")], "f5-body-cut", True),
    ("FXo-own-reset-stops", "extra (not one of rules 1-5): our own reset is judged like a board reset",
     [("AtomSpectraRestPolicy.cs", "if (change != null && !ownReset)", "if (change != null)")], "f5-reset-own", True),
    # answered failures = a lost link, headless: ask and no -sta, length before calibration, no TCP -cal at N != 8192
    ("no-answered-count", "N answered failures in a row do not lose the link",
     [("AtomSpectraRestAcquisition.cs", "if ((silent || answeredInRow >= LinkLostAfterFailures) && !_linkLost)", "if (silent && !_linkLost)")], "f5-linklost-answered", True),
    ("restore-on-failure", "a failing answer restores the link (flapping)",
     [("AtomSpectraRestAcquisition.cs", "if (r.Status > 0) MarkAnswered();", "if (r.Status > 0) MarkGood();")], "f5-linklost-restored", True),
    ("event-repeated", "the loss event is raised again by every further failure",
     [("AtomSpectraRestAcquisition.cs", "if ((silent || answeredInRow >= LinkLostAfterFailures) && !_linkLost)", "if (silent || answeredInRow >= LinkLostAfterFailures)")], "f5-linklost-answered", True),
    ("count-not-reset", "a good poll does not end the run of answered failures",
     [("AtomSpectraRestAcquisition.cs", "            Interlocked.Exchange(ref _answeredFailures, 0);", "            ;")], "f5-linklost-count-reset", True),
    ("refusal-counts", "a refused connection (no answer) counts as an answered failure",
     [("AtomSpectraRestAcquisition.cs", "answeredStatus > 0 ? Interlocked.Increment(ref _answeredFailures)", "true ? Interlocked.Increment(ref _answeredFailures)")], "f5-linklost-silence", True),
    ("headless-ask-null", "Ask without a delegate is not refused (the delegate is called)",
     [("AtomSpectraRestSession.cs", "if (plan == AtomSpectraRestStartPlan.Ask && Ask == null)", "if (false && Ask == null)")], "headless-stopped", True),
    # access refusal, TCP without a window. Controller and form mutations are in REAL_MUTATIONS below.
    ("silence-ends-run", "a silent poll ends the run of answered failures (503 and silence in turn never lose the link)",
     [("AtomSpectraRestAcquisition.cs", ": Volatile.Read(ref _answeredFailures);", ": Interlocked.Exchange(ref _answeredFailures, 0);")], "f5-linklost-mixed", True),
    ("last-attempt-only", "only the LAST attempt of a poll tells whether the board answered",
     [("AtomSpectraRestAcquisition.cs", "if (r.Status > 0) answeredStatus = r.Status;", "answeredStatus = r.Status;")], "f5-linklost-mixed-poll", True),
    ("access-any-status", "any answered failure is a refusal of access", [("AtomSpectraRestAcquisition.cs", "return s == 401 || s == 403;", "return s > 0;")], "f5-linklost-503", True),
    ("access-never", "401/403 are never a refusal of access", [("AtomSpectraRestAcquisition.cs", "return s == 401 || s == 403;", "return false;")], "f5-linklost-401", True),
    ("text-plain", "the lost-link text of a refusal of access is the plain 'does not answer' one",
     [("AtomSpectraRestSession.cs", "if (a != null && a.AccessDenied)", "if (false)")], "f5-linklost-access-text", True),
    ("gzip-status-wiped", "a broken gzip answer wipes the status (the board is taken for silent)",
     [("AtomSpectraRestClient.cs", "{ result.Error = problem; result.BodyBroken = broken; }", "{ result.Error = problem; result.BodyBroken = broken; if (broken) result.Status = 0; }")], "f5-linklost-gzip3", True),
    ("tcp-headless-has-windows", "the TCP Start plans as if there were a window (a counting board is 'asked', so refused)",
     [("AtomSpectraTcpSession.cs", "PlanStart(false, Ask != null, counting, board, DocumentSeconds, DocumentCounts)", "PlanStart(false, true, counting, board, DocumentSeconds, DocumentCounts)")], "console-tcp-start-headless-counting", True),
    ("tcp-headless-sends", "the TCP Start without a window does not refuse a stopped board with data (-sta goes out)",
     [("AtomSpectraTcpSession.cs", "            if (plan == AtomSpectraRestStartPlan.Ask)\n            {\n                // no window to ask in", "            if (false)\n            {\n                // no window to ask in")], "console-tcp-start-headless-stopped", True),
    ("tcp-headless-restart", "the TCP Start without a window restarts the board without a question (-sto -rst -sta), as before",
     [("AtomSpectraTcpSession.cs", "            AtomSpectraRestBoardSample[] s = ProbeBoard(ProbeMs);   // with or without a window", "            if (Ask == null) return AtomSpectraRestStartPlan.Restart;\n            AtomSpectraRestBoardSample[] s = ProbeBoard(ProbeMs);   // with or without a window")], "console-tcp-start-headless-idle", True),
    # PR37 fix, REST: each fix cut out again reddens its own scenario
    ("pr37-limit-stall", "R1: the board's stop at its own limit is declared a stall (the run ends as an accident)",
     [("AtomSpectraRestSession.cs", "guard.Stalled(acq.NowMs, StallMs) && !atLimit)", "guard.Stalled(acq.NowMs, StallMs))")], "pr37-rest-limit-end", False),
    ("pr37-limit-margin", "R1: the preset of our limited run gets the old margin (the document never reaches it)",
     [("AtomSpectraRestSession.cs", "return (int)Math.Min(int.MaxValue / 2, board + limitSeconds);", "return (int)Math.Min(int.MaxValue / 2, board + limitSeconds + 2L * Math.Max(1, periodSeconds) + 5);")], "pr37-rest-limit-end", False),
    ("pr37-continue-channels", "R2: Continue is offered over a board below the document in a channel",
     [("AtomSpectraRestSession.cs", "bool continueAllowed = !AtomSpectraRestPolicy.BoardBehindDocument(second, DocumentSeconds, DocumentCounts) && below < 0;", "bool continueAllowed = !AtomSpectraRestPolicy.BoardBehindDocument(second, DocumentSeconds, DocumentCounts);")], "pr37-rest-continue-other", False),
    ("pr37-guard-unseeded", "R2: the guard does not start from the document's channels",
     [("AtomSpectraRestPolicy.cs", "                lastBins = docBins;", "                lastBins = null;")], "pr37-rest-continue-other", False),
    ("pr37-observe-joins", "R9: observe only joins a board behind the document (and reports a 'reset' at the first snapshot)",
     [("AtomSpectraRestPolicy.cs", "return otherSpectrum || BoardBehindDocument(board, docSeconds, docCounts) ? AtomSpectraRestStartPlan.RefuseObserve : AtomSpectraRestStartPlan.Join;", "return AtomSpectraRestStartPlan.Join;")], "pr37-rest-observe-behind", False),
    ("pr37-channels-any", "R3: any profile channels are taken (16384, 6000)",
     [("AtomSpectraRestAcquisition.cs", "return profileChannels > 0 && profileChannels <= Channels && Channels % profileChannels == 0;", "return profileChannels > 0;")], "pr37-rest-channels", False),
    ("pr37-json-depth", "R9: no depth limit (a deep answer ends the process with StackOverflowException)",
     [("AtomSpectraRestClient.cs", "if (++_depth > MaxDepth) throw", "if (++_depth > int.MaxValue) throw")], "pr37-rest-json", False),
    ("pr37-json-copy", "R4: every token test copies the rest of the text (quadratic parse)",
     [("AtomSpectraRestClient.cs", "return _pos + token.Length <= _text.Length && string.CompareOrdinal(_text, _pos, token, 0, token.Length) == 0;", "return _text.Substring(_pos).StartsWith(token, StringComparison.Ordinal);")], "pr37-rest-json", False),
    ("pr37-nan-time", "R9: NaN in time/lost is cast to int.MinValue",
     [("AtomSpectraRestAcquisition.cs", "if (!Finite(v) || v < 0 || v > int.MaxValue) return false;", "")], "pr37-rest-json", False),
    ("pr37-detach-waits", "R6: Disconnect waits for a poll hanging on the board",
     [("AtomSpectraRestSession.cs", "DetachNoWait();   // never waits: Disconnect runs on the UI thread", "{ AtomSpectraRestAcquisition hung = acquisition; DetachNoWait(); if (hung != null) hung.Dispose(); }")], "pr37-rest-detach-hang", False),
    ("pr37-joined-silent", "R9: Stop of a joined acquisition says nothing",
     [("AtomSpectraRestSession.cs", "if (kept != null) kept(this, EventArgs.Empty);", "")], "pr37-rest-stop-joined", False),
    ("pr37-user-text-english", "R8: the user's text of a refused command is the English log text",
     [("AtomSpectraRestSession.cs", "            lastUserError = RefusalText(r);\n            AtomSpectraTcpLog.Warn(\"Session\", command + \" refused: \" + lastError);", "            lastUserError = lastError;\n            AtomSpectraTcpLog.Warn(\"Session\", command + \" refused: \" + lastError);")], "pr37-rest-texts", False),
    ("pr37-culture", "R7: the epoch change is printed in the culture of the thread",
     [("AtomSpectraRestPolicy.cs", "field + \": \" + Inv(was) + \" -> \" + Inv(now)", "field + \": \" + was + \" -> \" + now")], "pr37-rest-texts", False),
    ("pr37-expect-100", "R9: a POST asks for 100 Continue",
     [("AtomSpectraRestClient.cs", "req.ServicePoint.Expect100Continue = false;", "")], "pr37-rest-expect", False),
    # PR #38, maintainer's REST fixes (RestHarnessFixR.cs)
    ("r2-join-from-zero", "R2: after Replace on a counting board the guard starts from zero, not from the snapshot of the question",
     [("AtomSpectraRestSession.cs", "if (replaced) guard.BeginFrom(second, SeenBins(second), counting);", "if (replaced) guard.Begin(0, 0, counting, null);")], "fixr2-replace-reset-asked", False),
    ("r2-resume-from-zero", "R2: after Replace on a stopped board (-sta) the guard starts from zero",
     [("AtomSpectraRestSession.cs", "if (replaced) guard.BeginFrom(second, SeenBins(second), true);", "if (replaced) guard.Begin(0, 0, true, null);")], "fixr2-replace-epoch-asked", False),
    ("r2-epoch-not-seen", "R2: the epoch of the snapshot the question showed is not the reference of the guard",
     [("AtomSpectraRestPolicy.cs", "                lastEpoch = seen.Epoch;\n", "")], "fixr2-replace-epoch-asked", False),
    ("r6-observe-unmarked", "R6: the observe-only question is not marked (its hint promises -sta to a stopped board)",
     [("AtomSpectraRestSession.JoinedAndObserve.cs", "                ChannelBelow = below,\n                ObserveOnly = true\n", "                ChannelBelow = below\n")], "fixr6-observe-hint", False),
]


# (these mutations belong to the automatic set, each reddens ITS scenario): code that exists in the REAL build only (controller,
# forms). Each: the patched tree is built by MSBuild (build_real_mutant), the harness is built on that exe, the real-only scenarios run on it.
# (id, description, [(file under BecquerelMonitor/, old text, new text)], the scenario that must be red)
CTL = "AtomSpectraTcpDeviceController.cs"
REAL_MUTATIONS = [
    ("calibration-before-length", "the board calibration is applied BEFORE the length checks (the old order)",
     [(CTL, "if (e.Histogram.Length != spectrum.Spectrum.Length && session is AtomSpectraRestSession)", "if (boardCalibrationPending) { boardCalibrationPending = false; AtomSpectraTcpLog.Info(\"Cal\", ApplyBoardCalibration(spectrum, resultData.DeviceConfig, AtomSpectraRestCalibration.ForChannels(((AtomSpectraRestDataReadyArgs)e).Calibration, ((AtomSpectraRestDataReadyArgs)e).CalibChannels, e.Histogram.Length), configSaver)); }\n            if (e.Histogram.Length != spectrum.Spectrum.Length && session is AtomSpectraRestSession)")], "f5-ctl-length-first"),
    ("no-fold-in-controller", "the controller gives the document the 8192-channel polynomial unfolded",
     [(CTL, "AtomSpectraRestCalibration.ForChannels(restSnapshot.Calibration, restSnapshot.CalibChannels, e.Histogram.Length)", "restSnapshot.Calibration")], "f5-ctl-length-first"),
    ("tcp-cal-ungated", "TCP -cal is taken at any N", [("AtomSpectraTcpDeviceController.Calibration.cs", "if (n != AtomSpectraRestSpectrum.Channels)", "if (false)")], "f5-tcp-cal-n"),
    ("tcp-cal-blocked-always", "TCP -cal is never taken, 8192 too", [("AtomSpectraTcpDeviceController.Calibration.cs", "if (n != AtomSpectraRestSpectrum.Channels)", "if (true)")], "f5-tcp-cal-n"),
    ("form-ungated", "the form button takes TCP -cal at any N",
     [("AtomSpectraTcpCalibrationReader.cs", "else if (!rest && n > 0 && n != AtomSpectraRestSpectrum.Channels) coefficients = null;", "")], "f5-form-cal-n"),
    ("form-no-fold", "the form button does not fold the REST polynomial", [("AtomSpectraTcpCalibrationReader.cs", "if (rest && n > 0) coefficients =", "if (false) coefficients =")], "f5-form-cal-n"),
    ("form-arg-removed", "DeviceConfigForm calls the reader without the channels argument",
     [("DeviceConfigForm.cs", "SetActiveDeviceConfigDirty, () => editedChannels);", "SetActiveDeviceConfigDirty);")], "f5-form-cfg-n"),
    ("form-saved-profile", "DeviceConfigForm takes the channels of the SAVED profile, not of the field",
     [("DeviceConfigForm.cs", "int editedChannels = ChannelsBeingEdited(asked);", "int editedChannels = asked.NumberOfChannels;")], "f5-form-cfg-n"),
    ("page-unfolded", "the page of 'Test connection' is given the 8192-channel polynomial where no fold exists",
     [("AtomSpectraTcpDeviceForm.Calibration.cs", "double[] folded = n > 0 ? AtomSpectraRestCalibration.ForChannels(page, 0, n) : page;", "double[] folded = (n > 0 ? AtomSpectraRestCalibration.ForChannels(page, 0, n) : page) ?? page;")], "f5-form-test-n"),
    ("chooser-list-not-added", "the board list is not added to the chooser window (the reported defect)",
     [("AtomSpectraBoardChooser.cs", "form.Controls.Add(list);", "")], "bm18-chooser"),
    ("headless-text", "HeadlessAsk has no text of its own (the generic one)",
     [("AtomSpectraTcpDeviceController.Rest.cs", "return AtomSpectraRestSession.HeadlessAskText;", "return string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraRestStart, rest.LastError ?? outcome.ToString());")], "f5-ctl-headless-text"),
]


def free_port():
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port


def free_port_bm10():   # the mDNS mock needs one number free for UDP 127.0.0.1 and for TCP 127.0.0.2-4
    # the OS picks the number (bind to 0 on the UDP socket, whose excluded ranges differ from TCP's); the same number is then tried on the three
    # TCP addresses. The UDP socket is held meanwhile, so no parallel scenario is given the same number.
    for _ in range(500):
        socks = [socket.socket(socket.AF_INET, socket.SOCK_DGRAM)]
        try:
            socks[0].bind(("127.0.0.1", 0))
            p = socks[0].getsockname()[1]
            for ip in ("127.0.0.2", "127.0.0.3", "127.0.0.4"):
                socks.append(socket.socket(socket.AF_INET, socket.SOCK_STREAM))
                socks[-1].bind((ip, p))
            return p
        except OSError:
            time.sleep(0.01)
        finally:
            for s in socks:
                s.close()
    raise RuntimeError("no free port for the mDNS mock")


# Determinism: the machine is shared with long CPU-bound jobs at normal priority; mock and harness run above
# normal so a second-scale stall of the test clock does not happen (they sleep most of the time, the cost is nil)
PRIO = getattr(subprocess, "ABOVE_NORMAL_PRIORITY_CLASS", 0)


def run(cmd, timeout=180, prio=0):
    try:
        return subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout, creationflags=prio)
    except subprocess.TimeoutExpired:
        return None


HARNESS_FILES = [os.path.join(D, "harness", n) for n in ("RestHarness.cs", "RestHarnessFixes.cs", "RestHarnessPassC.cs", "RestHarnessConsole.cs", "RestHarnessForm.cs", "RestHarnessFormModes.cs", "RestHarnessFormLayout.cs", "RestHarnessCalibration.cs", "RestHarnessTcpStart.cs", "RestHarnessGzip.cs", "RestHarnessInfDebug.cs", "RestHarnessF5.cs", "RestHarnessF5b.cs", "RestHarnessF5c.cs", "RestHarnessF5d.cs", "RestHarnessF5e.cs", "RestHarnessF5f.cs", "RestHarnessF5g.cs", "RestHarnessF5h.cs", "RestHarnessF5i.cs", "RestHarnessF5j.cs", "RestHarnessF5k.cs", "UiFix.cs",
                                                                  "RestHarnessStartPkg.cs", "RestHarnessNight.cs", "RestHarnessBm10.cs", "RestHarnessFix2.cs", "RestHarnessFix3.cs", "RestHarnessFix4.cs",
                                                                  "RestHarnessFix5.cs", "RestHarnessFix6.cs", "RestHarnessFix7.cs", "RestHarnessFix9.cs", "RestHarnessFix9b.cs", "RestHarnessFix10.cs")]
FIXTURE = os.path.join(D, "fixtures", "spectrum_sample.json")   # a spectrum document recorded from a board, read by 'parse'

HARNESS_FILES.append(os.path.join(D, "harness", "RestFixPr37.cs"))   # PR37 fix, REST: the scenarios pr37-rest-*
HARNESS_FILES.append(os.path.join(D, "harness", "RestHarnessFixR.cs"))   # PR #38, maintainer's REST fixes: the scenarios fixr*


def copy_fixture(out_dir):
    shutil.copy2(FIXTURE, os.path.join(out_dir, os.path.basename(FIXTURE)))
    shutil.copytree(os.path.join(D, "fixtures", "responses"), os.path.join(out_dir, "responses"), dirs_exist_ok=True)   # 
    # stabilization: one start of the fresh exe before the parallel runs, so the antivirus scan of a new binary
    # and the first-start cost do not land inside the timed scenarios
    run([os.path.join(out_dir, "RestHarness.exe"), "policy", "0", "-"], timeout=120)


def build_ref(out_dir, exe_dir):
    os.makedirs(out_dir, exist_ok=True)
    exe_copy = os.path.join(out_dir, "BecquerelMonitor.exe")
    shutil.copy2(os.path.join(exe_dir, "BecquerelMonitor.exe"), exe_copy)
    if os.path.exists(os.path.join(exe_dir, "BecquerelMonitor.pdb")):   # file:line in the stack of a red run
        shutil.copy2(os.path.join(exe_dir, "BecquerelMonitor.pdb"), os.path.join(out_dir, "BecquerelMonitor.pdb"))
    for dll in os.listdir(exe_dir):   # the settings form (form-* scenarios) loads the program's own libraries
        if dll.lower().endswith(".dll"):
            shutil.copy2(os.path.join(exe_dir, dll), os.path.join(out_dir, dll))
    if os.path.isdir(os.path.join(exe_dir, "ru")):   # Russian captions for form-layout-ru
        shutil.copytree(os.path.join(exe_dir, "ru"), os.path.join(out_dir, "ru"), dirs_exist_ok=True)
    if os.path.isdir(os.path.join(exe_dir, "config")):   # DeviceConfigForm (f5-form-cfg-n) reads the shipped configuration at construction
        shutil.copytree(os.path.join(exe_dir, "config"), os.path.join(out_dir, "config"), dirs_exist_ok=True)
    out_exe = os.path.join(out_dir, "RestHarness.exe")
    cmd = [CSC, "-nologo", "-langversion:7.3", "-r:" + exe_copy, "-r:System.Xml.dll", "-r:System.Windows.Forms.dll", "-r:System.Drawing.dll", "-out:" + out_exe] + HARNESS_FILES
    p = run(cmd)
    if p is None or p.returncode != 0:
        raise RuntimeError("build_ref failed: " + (p.stdout if p else "") + (p.stderr if p else ""))
    copy_fixture(out_dir)


def build_src(out_dir, src_dir, replacements):
    os.makedirs(out_dir, exist_ok=True)
    applied = set()
    texts = []
    for f in SRC_FILES:
        src_path = os.path.join(src_dir, f)
        with open(src_path, "r", encoding="utf-8-sig", newline=None) as fh:
            text = fh.read()
        for i, (rfile, old, new) in enumerate(replacements):
            if rfile == os.path.basename(f):
                count = text.count(old)
                if count != 1:
                    raise RuntimeError(f"replacement {i} in {rfile} expected 1 occurrence, found {count}")
                text = text.replace(old, new)
                applied.add(i)
        texts.append(text)
        out_path = os.path.join(out_dir, os.path.basename(f))
        with open(out_path, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(text)
    for i in range(len(replacements)):
        if i not in applied:
            raise RuntimeError(f"replacement {i} was not applied")
    with open(os.path.join(out_dir, "Stubs.cs"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write(source_stubs(STUBS, src_dir, texts))
    out_exe = os.path.join(out_dir, "RestHarness.exe")
    cs_files = [os.path.join(out_dir, os.path.basename(f)) for f in SRC_FILES]
    cs_files.append(os.path.join(out_dir, "Stubs.cs"))
    cmd = [CSC, "-nologo", "-langversion:7.3", "-d:TRACE", "-nowarn:0162,0219,0414,0649,0169", "-r:System.Windows.Forms.dll", "-r:System.Drawing.dll", "-r:System.Security.dll", "-r:System.Xml.dll", "-out:" + out_exe] + HARNESS_FILES + cs_files
    p = run(cmd)
    if p is None or p.returncode != 0:
        raise RuntimeError("build_src failed: " + (p.stdout if p else "") + (p.stderr if p else ""))
    copy_fixture(out_dir)


def run_one(build_dir, name, mock_args):
    harness = os.path.join(build_dir, "RestHarness.exe")
    port = free_port_bm10() if name.startswith("bm10-") else free_port()
    log = os.path.join(build_dir, name + "_req.jsonl")
    proc = None
    out = None
    try:
        if mock_args is not None:
            out_path = os.path.join(build_dir, name + "_mock.txt")
            out = open(out_path, "w", encoding="utf-8")
            if name.startswith("bm10-"):   # board discovery: a fake mDNS responder on 127.0.0.1 and /api/device of the boards on 127.0.0.2-4
                cmdline = [sys.executable, "-u", os.path.join(D, "mocks", "mock_mdns.py"), "--port", str(port), "--log", log] + mock_args
            elif name.startswith("console-tcp"):   # the TCP bridge console talks shproto to the bridge mock, not HTTP
                cmdline = [sys.executable, "-u", os.path.join(D, "mocks", "mock_cmd.py"), "--port", str(port), "--log", log] + mock_args
            else:
                cmdline = [sys.executable, "-u", os.path.join(D, "mocks", "mock_rest.py"), "--port", str(port), "--log", log, "--duration", "60"] + mock_args
            proc = subprocess.Popen(cmdline, stdout=out, stderr=subprocess.STDOUT, creationflags=PRIO)
            deadline = time.time() + 10
            started = False
            while time.time() < deadline:
                if proc.poll() is not None:
                    break
                try:
                    with open(out_path, "r", encoding="utf-8") as fh:
                        if "LISTENING" in fh.read():
                            started = True
                            break
                except Exception:
                    pass
                time.sleep(0.05)
            if not started:
                proc.kill()
                proc.wait(timeout=10)
                return (False, "mock did not start")
        args = [harness, name, str(port) if proc else "0", log if proc else "-"]
        p = run(args, timeout=120, prio=PRIO)
        if p is None:
            return (False, "harness timeout")
        text = p.stdout + (("\n[ERR] " + p.stderr) if p.stderr.strip() else "")
        with open(os.path.join(build_dir, name + "_out.txt"), "w", encoding="utf-8") as fh:   # kept for the reader of a red run
            fh.write(text)
        return (p.returncode == 0, text)
    finally:
        if proc:
            proc.kill()
            proc.wait(timeout=10)
        if out:
            out.close()


def run_many(build_dir, names_args, workers=None):   # stabilization: 4 parallel scenarios, not 6 (shared machine); RL_WORKERS=1 runs one by one
    workers = workers or int(os.environ.get("RL_WORKERS", "4"))
    results = {}
    with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as executor:
        futures = {executor.submit(run_one, build_dir, name, args): name for name, args in names_args}
        for future in concurrent.futures.as_completed(futures):
            name = futures[future]
            results[name] = future.result()
    return results


def print_results(tag, results):
    for name, _ in SCENARIOS:
        if name in results:
            ok, text = results[name]
            print(f"{tag} {name}: {'PASS' if ok else 'FAIL'}")
            if not ok:
                for line in text.splitlines():
                    print("    " + line)


def patch_file(path, old, new):
    raw = open(path, "rb").read()
    text = raw.decode("utf-8-sig").replace("\r\n", "\n")
    if text.count(old) != 1:
        raise RuntimeError(f"expected 1 occurrence in {os.path.basename(path)}, found {text.count(old)}: {old[:60]}")
    text = text.replace(old, new)
    if b"\r\n" in raw:
        text = text.replace("\n", "\r\n")
    with open(path, "wb") as fh:
        fh.write((b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b"") + text.encode("utf-8"))


TEST_OUT = "Release_atomspectra_tests"   # own OutputPath and IntermediateOutputPath: never the obj\Release of a ClickOnce publish


def build_real_mutant(tree_root, mdir, replacements):
    """a copy of the source tree (no bin/obj) with the patches, built by MSBuild; returns the exe directory"""
    if os.path.exists(mdir):
        shutil.rmtree(mdir, ignore_errors=True)
    shutil.copytree(tree_root, mdir, ignore=shutil.ignore_patterns("bin", "obj", ".git"))
    for rfile, old, new in replacements:
        patch_file(os.path.join(mdir, "BecquerelMonitor", rfile), old, new)
    # the recipe of CLAUDE.md: MSBuild.exe on BecquerelMonitor.csproj with /p:GenerateManifests=false and its own output and obj directories
    args = [MSBUILD, os.path.join("BecquerelMonitor", "BecquerelMonitor.csproj"), "/restore", "/t:Rebuild", "/p:Configuration=Release", "/p:Platform=AnyCPU", "/p:SignManifests=false", "/p:GenerateManifests=false",
            "/p:OutputPath=bin\\" + TEST_OUT + "\\", "/p:IntermediateOutputPath=obj\\" + TEST_OUT + "\\", "/nologo", "/v:minimal"]
    p = subprocess.run(args, cwd=mdir, capture_output=True, text=True, encoding="utf-8", errors="replace")
    exe_dir = os.path.join(mdir, "BecquerelMonitor", "bin", TEST_OUT)
    if p.returncode != 0 or not os.path.isfile(os.path.join(exe_dir, "BecquerelMonitor.exe")):
        raise RuntimeError("MSBuild failed: " + (p.stdout + p.stderr)[-600:])
    return exe_dir


def real_only_checks(tag, results, required=()):
    """The number of checks each real-build-only scenario executed on the REAL build; n/a, or fewer than the minimum, fails the suite.
    A name in `required` (the --only list) that is absent from the results fails as well"""
    good = True
    for name in required:
        if name not in results:
            good = False
            print(f"{tag} MISSING {name}: requested by --only, no result: FAIL", flush=True)
    for name, need in REAL_ONLY_MIN_CHECKS.items():
        if name not in results:
            continue
        text = results[name][1]
        m = re.search(r"checks executed: (\d+)", text)
        n = int(m.group(1)) if m else 0
        ok = n >= need and "n/a" not in text
        good = good and ok
        print(f"{tag} CHECKS {name}: executed {n} (at least {need} needed){' n/a' if 'n/a' in text else ''}: {'OK' if ok else 'FAIL'}", flush=True)
    return good


def run_real_mutations(src_dir, work_dir):
    """REAL_MUTATIONS on the real build; a mutation is RED when its own scenario fails; the other real-only scenarios are printed (exactly one: informative)"""
    tree_root = os.path.dirname(os.path.abspath(src_dir))
    real_scn = [s for s in SCENARIOS if s[0] in REAL_ONLY_MIN_CHECKS or s[0] in ("f5-ctl-calib", "f5-ctl-calib-channels", "f5-form-calib")]
    red = 0
    for mid, desc, replacements, target in REAL_MUTATIONS:
        mdir = os.path.join(work_dir, "realmut_" + mid)
        try:
            exe_dir = build_real_mutant(tree_root, mdir, replacements)
            bdir = os.path.join(mdir, "harness")
            build_ref(bdir, exe_dir)
        except RuntimeError as e:
            print(f"REAL MUTATION {mid}: BUILD FAIL {e}", flush=True)
            continue
        res = run_many(bdir, real_scn)
        failed = sorted(n for n, (ok, _) in res.items() if not ok)
        red += target in failed
        print(f"REAL MUTATION {mid} ({desc}): {'RED' if target in failed else 'GREEN'}; red tests: {', '.join(failed) if failed else 'none'}; exactly one: {'yes' if failed == [target] else 'NO'}", flush=True)
        shutil.rmtree(mdir, ignore_errors=True)
    print(f"real-build mutations red: {red}/{len(REAL_MUTATIONS)}", flush=True)
    return red == len(REAL_MUTATIONS)


# scenarios whose checks read a text of the resources; they are run once more under each interface language (REST_UI_CULTURE),
# so that a check that holds only for the language of the machine cannot pass here and fail elsewhere
LOCALE_SCENARIOS = ["console-rest-notext", "console-rest-refused", "console-refresh-none", "form-bad-address", "form-calib", "form-calib-unset",
                    "gzip-unsupported", "gzip-ignored", "f5-ctl-headless-text", "pr37-rest-continue-other", "pr37-rest-observe-behind", "pr37-rest-texts",
                    "night-start-text", "fix4-joined-clear-text", "rest-linklost-text", "rest-unreachable-text", "ui-console-confirm", "fixr6-observe-hint"]
LOCALES = ("en-US", "ru-RU")


def run_locales(ref):
    """LOCALE_SCENARIOS on the real build under every language of LOCALES; True when all pass"""
    chosen = [s for s in SCENARIOS if s[0] in LOCALE_SCENARIOS]
    good = True
    for culture in LOCALES:
        os.environ["REST_UI_CULTURE"] = culture
        try:
            res = run_many(ref, chosen)
        finally:
            del os.environ["REST_UI_CULTURE"]
        print_results("REST LOCALE " + culture, res)
        good = good and all(ok for ok, _ in res.values())
    return good


def run_rest_suite(exe_dir, src_dir, work_dir):
    if os.environ.get("REST_NO_SOURCE_MUTATIONS"):   # base scenarios and real-build mutations only (the source mutations take hours)
        MUTATIONS[:] = []
    rest_dir = os.path.join(work_dir, "rest")
    if os.path.exists(rest_dir):
        shutil.rmtree(rest_dir, ignore_errors=True)
    os.makedirs(rest_dir, exist_ok=True)

    ref = os.path.join(rest_dir, "base")
    build_ref(ref, exe_dir)
    base_scn = [] if os.environ.get("REST_SKIP_BASE") else SCENARIOS   # a shard of the mutations (run_mutations_subset.py) does not repeat the base
    res = run_many(ref, base_scn)
    print_results("REST BASE real", res)
    real_ok = sum(1 for ok, _ in res.values() if ok)
    checks_ok = real_only_checks("REST BASE real", res)   # a real-build-only scenario that executed no checks (n/a) is a failure
    locale_ok = True if os.environ.get("REST_SKIP_BASE") else run_locales(ref)   # the text checks in English and in Russian

    src = os.path.join(rest_dir, "src_base")
    build_src(src, src_dir, [])
    res2 = run_many(src, base_scn)
    print_results("REST BASE src", res2)
    src_ok = sum(1 for ok, _ in res2.values() if ok)

    red = 0
    exactly_one = 0
    full_total = 0
    for mid, desc, replacements, target, full in MUTATIONS:
        mdir = os.path.join(rest_dir, "mut_" + mid)
        try:
            build_src(mdir, src_dir, replacements)
        except RuntimeError as e:
            print(f"REST MUTATION {mid}: BUILD FAIL {e}")
            continue
        if full:
            full_total += 1
            res_m = run_many(mdir, SCENARIOS)
            failed = sorted(n for n, (ok, _) in res_m.items() if not ok)
        else:
            res_m = run_many(mdir, [s for s in SCENARIOS if s[0] == target])
            failed = [n for n, (ok, _) in res_m.items() if not ok]
        is_red = target in failed
        red += is_red
        one = (failed == [target])
        if full and one:
            exactly_one += 1
        print(f"REST MUTATION {mid} ({desc}): {'RED' if is_red else 'GREEN'}; red tests: {', '.join(failed) if failed else 'none'}" + (f"; exactly one: {'yes' if one else 'NO'}" if full else ""))
        if full and not one:
            # diagnostics only (the verdict above stands): the off-target red scenario's output and client log
            for n in failed:
                if n != target:
                    for line in res_m[n][1].splitlines():
                        if "-> 200" not in line:
                            print(f"    [{mid}/{n}] {line}")

    n = len(SCENARIOS)
    print(f"rest mock tests (base, real build): {real_ok}/{n}")
    print(f"rest mock tests (base, source build): {src_ok}/{n}")
    print(f"rest mutations red: {red}/{len(MUTATIONS)}")
    print(f"rest key mutations reddening exactly one test: {exactly_one}/{full_total}")
    print("(exactly one is informative: the verdict counts every mutation reddening its own target)")
    real_mut_ok = run_real_mutations(src_dir, rest_dir)   # mutations of the controller and the forms on the real build, in the automatic set
    return real_ok == n and src_ok == n and red == len(MUTATIONS) and checks_ok and real_mut_ok and locale_ok


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    if len(sys.argv) < 3:
        print("usage: python rest_suite.py <exeDir> <srcDir> [workDir] [--only name1,name2]")
        sys.exit(1)
    exe_dir = sys.argv[1]
    src_dir = sys.argv[2]
    work_dir = os.environ.get("REST_WORK") or os.path.join(tempfile.gettempdir(), "atomspectra_tests")   # REST_WORK = a work directory on a local disk
    only = None
    if "--real-mutations" in sys.argv:   # only the real-build mutations (a rerun of that part of the suite)
        sys.exit(0 if run_real_mutations(src_dir, work_dir) else 1)
    # [workDir] and --only together, in either order
    rest_args = sys.argv[3:]
    if "--only" in rest_args:
        i = rest_args.index("--only")
        if i + 1 >= len(rest_args):
            print("usage: python rest_suite.py <exeDir> <srcDir> [workDir] [--only name1,name2]")
            sys.exit(1)
        only = rest_args[i + 1].split(",")
        rest_args = rest_args[:i] + rest_args[i + 2:]
    if rest_args:
        work_dir = rest_args[0]
    if only is not None:
        rest_dir = os.path.join(work_dir, "rest")
        if os.path.exists(rest_dir):
            shutil.rmtree(rest_dir, ignore_errors=True)
        os.makedirs(rest_dir, exist_ok=True)
        ref = os.path.join(rest_dir, "base")
        build_ref(ref, exe_dir)
        scenarios = [s for s in SCENARIOS if s[0] in only]
        res = run_many(ref, scenarios)
        print_results("REST BASE real (only)", res)
        all_ok = all(ok for ok, _ in res.values())
        if os.environ.get("REST_ONLY_SRC"):   # the same scenarios on the source build as well (the red runs before a fix)
            src = os.path.join(rest_dir, "src_base")
            build_src(src, src_dir, [])
            res2 = run_many(src, scenarios)
            print_results("REST BASE src (only)", res2)
            all_ok = all_ok and all(ok for ok, _ in res2.values())
        checks_ok = real_only_checks("REST BASE real (only)", res, only)   # the checks verdict and the requested-but-missing scenarios count in the exit code
        sys.exit(0 if (all_ok and checks_ok) else 1)
    else:
        sys.exit(0 if run_rest_suite(exe_dir, src_dir, work_dir) else 1)

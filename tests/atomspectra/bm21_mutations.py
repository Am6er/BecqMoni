# #BM-21 (RestHarnessBm21.cs): source mutations of the firmware version warning, in the format of rest_suite.MUTATIONS
# (id, description, [(file, old text, new text)], the scenario that must be red, full run). On the good code no bm21 scenario is red.
FW = "AtomSpectraFirmware.cs"
SES = "AtomSpectraRestSession.cs"
READ = "AtomSpectraFirmware.ReadVersion(client, FirmwareReadMs)"
MUTATIONS = [
    ("bm21-string-compare", "the firmware versions are compared as strings (1.10.0 < 1.2.39)",
     [(FW, "if (major != RecMajor)", "if (string.CompareOrdinal(version.Trim(), RecommendedVersion) != 0) return string.CompareOrdinal(version.Trim(), RecommendedVersion) < 0;\n            if (major != RecMajor)")], "bm21-parse", True),
    ("bm21-threshold-38", "the threshold is 1.2.38 instead of 1.2.39",
     [(FW, 'public const string RecommendedVersion = "1.2.39";', 'public const string RecommendedVersion = "1.2.38";')], "bm21-start-low", True),
    ("bm21-hint-on-success", "the firmware is read at every Start, so the hint is there when the Start succeeds",
     [(SES, "lastProbe = second;", "lastProbe = second;\n            FirmwareVersion = " + READ + ";")], "bm21-start-ok-low", True),
    ("bm21-hint-unknown", "the hint is added when the version is unknown (?); a missing version (null) still gives none (IMPL-11: no NullReference of its own)",
     [(FW, "if (below != true)", "if (below == false || version == null)")], "bm21-start-fail-unknown", True),
    ("bm21-block-start", "the Start is refused on a firmware below 1.2.39 instead of a warning",
     [(SES, "lastProbe = second;", "if (AtomSpectraFirmware.IsBelowRecommended(" + READ + ") == true) { Failure = AtomSpectraRestFailure.Unreachable; return AtomSpectraTcpBeginOutcome.ConnectFailed; }\n            lastProbe = second;")], "bm21-start-ok-low", True),
]
MUTATIONS.append(("bm21-long-uncut", "the version is put into the hint without a length limit",
     [(FW, "if (shown.Length > MaxShownVersion)", "if (false)")], "bm21-long", True))

# the controller (real build only, rest_suite.REAL_MUTATIONS): (id, description, [(file, old, new)], the scenario that must be red)
CTL = "AtomSpectraTcpDeviceController.cs"
CTLR = "AtomSpectraTcpDeviceController.Rest.cs"
OPEN = "string ue0; boardFirmware = AtomSpectraFirmware.ReadVersion(config.CreateRestClient(out ue0), AtomSpectraRestSession.FirmwareReadMs);"
REAL = [
    ("bm21-tcp-no-hint", "the TCP refusal does not carry the firmware hint",
     [(CTL, "AppendWarning(refusal, boardFirmware)", "AppendWarning(refusal, null)")], "bm21-ctl-connfail"),
    ("bm21-tcp-read-on-success", "the TCP Start reads the board status also when it succeeds",
     [(CTL, 'info = QueryQuietly(s, "-inf");', 'info = QueryQuietly(s, "-inf"); ' + OPEN)], "bm21-ctl-ok"),
    ("bm21-rest-status-hint", "the status line of a successful REST Start carries the firmware hint",
     [(CTLR, 'AtomSpectraTcpLog.SetStatus("REST", rest.Endpoint, rest.ObserveOnly ? Resources.AtomSpectraRestStatusObserving',
       'string ue1; AtomSpectraTcpLog.SetStatus("REST", rest.Endpoint, AtomSpectraFirmware.AppendWarning(rest.ObserveOnly ? Resources.AtomSpectraRestStatusObserving'),
      (CTLR, ': Resources.AtomSpectraRestStatusMeasuring));', ': Resources.AtomSpectraRestStatusMeasuring), AtomSpectraFirmware.ReadVersion(config.CreateRestClient(out ue1), AtomSpectraRestSession.FirmwareReadMs)));')], "bm21-ctl-rest-ok"),
]

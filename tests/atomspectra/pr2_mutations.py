# PR2 (RestHarnessPr2.cs): real-build mutations of the two scenarios pr2-delete-keep and pr2-clear-after-keep, in the format of
# rest_suite.REAL_MUTATIONS: (id, description, [(file under BecquerelMonitor/, old text, new text)], the scenario that must be red).
# The first is coarse (the tests are alive: several scenarios go red), the other two are point mutations (exactly one scenario red).
CTL = "AtomSpectraTcpDeviceController.cs"
CTLR = "AtomSpectraTcpDeviceController.Rest.cs"
REAL = [
    ("pr2-attach-fails", "coarse: AttachToDevice refuses, nothing is joined",
     [(CTL, "return BeginSession(r, false);", "return false;")], "pr2-delete-keep"),
    ("pr2-stop-asks-clear", "the user's Stop asks the Clear question of the board (a second question after the Stop question)",
     [(CTL, "stopAsked = r;   // a board reset met", "if (deviceGuid != null && manual) ConfirmClear(r);\n            stopAsked = r;   // a board reset met")], "pr2-delete-keep"),
    ("pr2-rest-defers-clear", "REST Clear after a Stop does not reset the board (a deferred reset like TCP)",
     [(CTLR, "bool ok = AtomSpectraRestUi.RunWithWait(delegate { return rest.ClearNow(CommandTimeoutMs); });",
       "bool ok = ReferenceEquals(stopAsked, r) || AtomSpectraRestUi.RunWithWait(delegate { return rest.ClearNow(CommandTimeoutMs); });")], "pr2-clear-after-keep"),
    # PR2 review (IMPL-10): H1 - the question of "Clear" must not replace a TCP session (or drop its deferred reset) before the answer; H3 - the firmware hint of a TCP refusal only from the TCP board's own host
    ("pr2-h1-session-first", "point: the REST question replaces the TCP session before the answer (the session guard is off)",
     [(CTLR, "bool keepSession = (session != null && !(session is AtomSpectraRestSession)) || carriedResetKey != null;", "bool keepSession = false || carriedResetKey != null;")], "console-tcp-pr2-switch-no"),
    ("pr2-h1-reset-first", "point: the REST question drops the deferred TCP reset before the answer (the carried-reset guard is off)",
     [(CTLR, "bool keepSession = (session != null && !(session is AtomSpectraRestSession)) || carriedResetKey != null;", "bool keepSession = (session != null && !(session is AtomSpectraRestSession)) || false;")], "console-tcp-pr2-switch-deferred-no"),
    # PR #39 review: after a switch of the transport to REST "Clear" asks the plain question and then the board's one again (two questions)
    ("a275-switch-two-questions", "point: after the switch to REST the Clear asks the plain question first (the board's question follows after a Yes)",
     [(CTLR, "            AtomSpectraRestSession rest;" + chr(10) + "            if (keepSession)" + chr(10), "            AtomSpectraRestSession rest;" + chr(10) + "            if (keepSession) return DeviceController.AskClearDocument();" + chr(10) + "            if (keepSession)" + chr(10))], "pr2-switch-yes-up"),
    ("pr2-h3-any-rest-host", "point: the firmware version is read from any REST address of the configuration (another board's)",
     [(CTL, "if (config.RestUrlIsTcpHost()) boardFirmware", "if (true) boardFirmware")], "bm21-ctl-other-board"),
]

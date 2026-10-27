# PR2 (RestHarnessPr2.cs, RunPr2Tcp): mutations of the ONE question of "Clear" of a joined TCP acquisition, REAL_MUTATIONS format:
# (id, description, [(file under BecquerelMonitor/, old text, new text)], the scenario that must be red).
CTL = "AtomSpectraTcpDeviceController.cs"
CTLR = "AtomSpectraTcpDeviceController.Rest.cs"
NL = chr(10)
REAL = [
    ("pr2-tcp-confirm-refuses", "coarse: the TCP question always refuses, nothing is ever cleared",
     [(CTLR, "return ConfirmClearTcp(r);   // ONE question", "return false;   // ONE question")], "console-tcp-pr2-clear-joined"),
    ("pr2-tcp-no-clears", "point: after No the TCP Clear still clears the document",
     [(CTL, 'declined, nothing is cleared (document and board)");' + NL + "                return false;",
       'declined, nothing is cleared (document and board)");' + NL + "                return true;")], "console-tcp-pr2-clear-joined-no"),
    ("pr2-tcp-deferred-asks-document", "point: the deferred TCP Clear asks the document question first (two windows again)",
     [(CTL, "if (s == null || !s.JoinedRunning)",
       "if (s == null || !s.JoinedRunning || ((s as AtomSpectraTcpSession) != null && ((AtomSpectraTcpSession)s).ClearDeferred))")],
     "console-tcp-pr2-clear-deferred"),
    ("pr2-tcp-asks-twice", "point: ClearMeasurementResult asks the analyzer question again (two windows again; reddens the joined AND the deferred scenario)",
     [(CTL, "bool askedAlready = ReferenceEquals(tcpClearFor, r);", "bool askedAlready = false;")], "console-tcp-pr2-clear-joined"),
    # PR2 review 2, H1: the TCP question is the old text (-rst, no Start, but nothing about the document): the old check "-rst and no Start" let it pass
    ("pr2-tcp-old-text", "point: the TCP question has the old text (the analyzer only, not the document)",
     [(CTL, "if (!AppUi.AskYesNo(JoinedClearBothText(s),", "if (!AppUi.AskYesNo(JoinedClearText(s),")], "console-tcp-pr2-clear-joined"),
]

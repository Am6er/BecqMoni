# -*- coding: utf-8 -*-
import io, sys
p = sys.argv[1]
s = io.open(p, encoding="utf-8", newline="").read()
nl = "\r\n" if "\r\n" in s else "\n"
def rep(old, new, count=1):
    global s
    old = old.replace("\n", nl); new = new.replace("\n", nl)
    assert s.count(old) == count, (old[:80], s.count(old))
    s = s.replace(old, new)
rep("""    long gEmitEvents = 0;
""", """    long gEmitEvents = 0;

    // EventAction своего потока (ставится в Actions::Build) и слив его карт —
    // зовётся из EndOfRunAction рабочего; тело после определения EventAction.
    class EventActionFwd;
    void FlushThreadTags();
""")
rep("""namespace
{
    // Указатель на EventAction своего потока — чтобы EndOfRunAction рабочего слил его карты.
    G4ThreadLocal EventAction* gThreadEvent = nullptr;
}""", """namespace
{
    // Указатель на EventAction своего потока — чтобы EndOfRunAction рабочего слил его карты.
    G4ThreadLocal EventAction* gThreadEvent = nullptr;

    void FlushThreadTags()
    {
        if (gThreadEvent != nullptr)
        {
            gThreadEvent->Flush();
        }
    }
}""")
rep("""            if (gTagMode && gThreadEvent != nullptr)
            {
                gThreadEvent->Flush();
            }
""", """            if (gTagMode)
            {
                FlushThreadTags();
            }
""")
rep("""    class EventActionFwd;
""", "")
io.open(p, "w", encoding="utf-8", newline="").write(s)
print("ok")

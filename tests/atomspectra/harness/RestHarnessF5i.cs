// a body that breaks off after the status 200.
using System;
using System.Threading;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunF5i(string scenario)
        {
            if (scenario != "f5-body-cut") return RunF5j(scenario);
            F5dWith(F5iBodyCut);
            return true;
        }

        // Case: the board answers 200 and the body breaks off in the middle of the transfer: (a) the connection is closed, (b) silence until the
        // read timeout. The status of the answer stays 200 (BodyBroken, the reason named), the answer counts as received, the poll repeats
        // a broken body inside its period. Cases: 0 and 1 cut and closed, 2 plain, 3 and on (held): cut and silent.
        static void F5iBodyCut()
        {
            var r = Client().Get("/api/spectrum.json?channels=8192", 3000);
            Console.WriteLine("closed: status " + r.Status + ", broken " + r.BodyBroken + ", error " + r.Error + ", not sent " + r.NotSent);
            Check(r.Status == 200 && r.BodyBroken && r.Error != null && !r.NotSent, "a body closed in the middle keeps the status 200 (" + r.Status + ", broken " + r.BodyBroken + ")");
            var a = new AtomSpectraRestAcquisition(Client(), 10, 8192);
            bool p = a.PollOnce();
            Check(p && a.Retries == 1 && a.Failures == 0, "the poll repeats the broken body and gets the document (retries " + a.Retries + ")");
            a.Dispose();
            r = Client().Get("/api/spectrum.json?channels=8192", 2000);
            Console.WriteLine("silent: status " + r.Status + ", broken " + r.BodyBroken + ", error " + r.Error + ", not sent " + r.NotSent);
            Check(r.Status == 200 && r.BodyBroken && r.Error != null && !r.NotSent, "a body that goes silent keeps the status 200 (" + r.Status + ", broken " + r.BodyBroken + ")");
            F5iNotSilence();
        }

        // the board that answers 200 and stalls in the body every time is not silent: no link loss after 15.3 s
        static void F5iNotSilence()
        {
            int lost = 0;
            var b = new AtomSpectraRestAcquisition(Client(), 1, 8192);
            b.LinkLost += (o, e) => lost++;
            b.PollOnce();
            while (b.NowMs < 15300) Thread.Sleep(100);
            b.PollOnce();
            Check(b.Failures == 2, "both polls failed (" + b.Failures + ")");
            Check(lost == 0 && !b.IsLinkLost, "an answer with a body that stalls is not silence: no link loss after 15.3 s");
            b.Dispose();
        }

    }
}

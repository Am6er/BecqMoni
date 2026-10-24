using System;
using System.Reflection;
using System.Runtime.Serialization;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunFix4(string scenario)
        {
            switch (scenario)
            {
                case "fix4-joined-clear-text":
                {
                    Type ctlT = CtlT;
                    if (ctlT == null)
                    {
                        Console.WriteLine("n/a: controller type missing");
                        return true;
                    }

                    Type resT = Type.GetType("BecquerelMonitor.Properties.Resources, BecquerelMonitor");
                    Type sesT = Type.GetType("BecquerelMonitor.AtomSpectraTcpSession, BecquerelMonitor");
                    Type acqT = Type.GetType("BecquerelMonitor.AtomSpectraTcpAcquisition, BecquerelMonitor");
                    Type ifT = Type.GetType("BecquerelMonitor.IAtomSpectraSession, BecquerelMonitor");

                    if (resT == null) { Check(false, "Resources type is missing"); return true; }
                    if (sesT == null) { Check(false, "AtomSpectraTcpSession type is missing"); return true; }
                    if (acqT == null) { Check(false, "AtomSpectraTcpAcquisition type is missing"); return true; }
                    if (ifT == null) { Check(false, "IAtomSpectraSession type is missing"); return true; }

                    MethodInfo text = ctlT.GetMethod("JoinedClearText", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { ifT }, null);
                    if (text == null)
                    {
                        Check(false, "JoinedClearText exists");
                        return true;
                    }
                    Check(true, "JoinedClearText exists");

                    PropertyInfo deferredProp = sesT.GetProperty("ClearDeferred");
                    if (deferredProp == null)
                    {
                        Check(false, "AtomSpectraTcpSession.ClearDeferred exists");
                        return true;
                    }
                    Check(true, "AtomSpectraTcpSession.ClearDeferred exists");

                    string confirm = null;
                    string deferred = null;

                    try
                    {
                        PropertyInfo confirmProp = resT.GetProperty("MSGAtomSpectraTcpClearJoinedConfirm", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        if (confirmProp == null)
                        {
                            Check(false, "MSGAtomSpectraTcpClearJoinedConfirm resource exists");
                            return true;
                        }
                        confirm = (string)confirmProp.GetValue(null, null);
                    }
                    catch (TargetInvocationException tie)
                    {
                        Check(false, "reading MSGAtomSpectraTcpClearJoinedConfirm threw: " + tie.InnerException.Message);
                        return true;
                    }

                    try
                    {
                        PropertyInfo deferredPropRes = resT.GetProperty("MSGAtomSpectraTcpClearJoinedDeferredConfirm", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        if (deferredPropRes == null)
                        {
                            Check(false, "MSGAtomSpectraTcpClearJoinedDeferredConfirm resource exists");
                            return true;
                        }
                        deferred = (string)deferredPropRes.GetValue(null, null);
                    }
                    catch (TargetInvocationException tie)
                    {
                        Check(false, "reading MSGAtomSpectraTcpClearJoinedDeferredConfirm threw: " + tie.InnerException.Message);
                        return true;
                    }

                    if (string.IsNullOrEmpty(confirm))
                    {
                        Check(false, "MSGAtomSpectraTcpClearJoinedConfirm is non-empty");
                        return true;
                    }
                    if (string.IsNullOrEmpty(deferred))
                    {
                        Check(false, "MSGAtomSpectraTcpClearJoinedDeferredConfirm is non-empty");
                        return true;
                    }

                    // CLOSED state
                    object closed = FormatterServices.GetUninitializedObject(sesT);

                    bool closedDeferred;
                    try
                    {
                        closedDeferred = (bool)deferredProp.GetValue(closed, null);
                    }
                    catch (TargetInvocationException tie)
                    {
                        Check(false, "reading ClearDeferred on closed session threw: " + tie.InnerException.Message);
                        return true;
                    }
                    Check(closedDeferred, "closed session (no acquisition): ClearDeferred is true");

                    string tClosed;
                    try
                    {
                        tClosed = (string)text.Invoke(null, new object[] { closed });
                    }
                    catch (TargetInvocationException tie)
                    {
                        Check(false, "JoinedClearText(closed) threw: " + tie.InnerException.Message);
                        return true;
                    }

                    Check(tClosed == deferred, "closed session: the question is the deferred-reset text");
                    Check(tClosed.IndexOf("{0}") < 0, "closed session: the question has no board numbers placeholder");
                    Check(tClosed != confirm, "closed session: not the text that promises an immediate reset");

                    // ACTIVE state
                    object active = FormatterServices.GetUninitializedObject(sesT);

                    FieldInfo fAcq = sesT.GetField("acquisition", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fAcq == null) { Check(false, "field acquisition exists"); return true; }

                    FieldInfo fBoardTime = sesT.GetField("boardTime", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fBoardTime == null) { Check(false, "field boardTime exists"); return true; }

                    FieldInfo fBoardTotal = sesT.GetField("boardTotal", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fBoardTotal == null) { Check(false, "field boardTotal exists"); return true; }

                    object acqObj = FormatterServices.GetUninitializedObject(acqT);
                    FieldInfo fLock = sesT.GetField("guardLock", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fLock == null) { Check(false, "field guardLock exists"); return true; }
                    fLock.SetValue(active, new object());   // no constructor ran: BoardSeconds/BoardCounts lock on it
                    fAcq.SetValue(active, acqObj);
                    fBoardTime.SetValue(active, (long)3700);
                    fBoardTotal.SetValue(active, (long)12345);

                    bool activeDeferred;
                    try
                    {
                        activeDeferred = (bool)deferredProp.GetValue(active, null);
                    }
                    catch (TargetInvocationException tie)
                    {
                        Check(false, "reading ClearDeferred on active session threw: " + tie.InnerException.Message);
                        return true;
                    }
                    Check(!activeDeferred, "active session: ClearDeferred is false");

                    string tActive;
                    try
                    {
                        tActive = (string)text.Invoke(null, new object[] { active });
                    }
                    catch (TargetInvocationException tie)
                    {
                        Check(false, "JoinedClearText(active) threw: " + tie.InnerException.Message);
                        return true;
                    }

                    string expected = string.Format(System.Globalization.CultureInfo.InvariantCulture, confirm, 1, 1, "12345");
                    Check(tActive == expected, "active session: the question is the immediate -rst text with the board numbers");
                    Check(tActive != deferred, "active session: not the deferred text");

                    return true;
                }

                default:
                    Console.WriteLine("n/a: unknown scenario " + scenario);
                    return true;
            }
        }
    }
}

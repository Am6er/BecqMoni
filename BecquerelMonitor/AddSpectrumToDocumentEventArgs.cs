using System;

namespace BecquerelMonitor
{
    // Token: 0x020000F7 RID: 247
    public class AddSpectrumToDocumentEventArgs : EventArgs
    {
        // Token: 0x17000325 RID: 805
        // (get) Token: 0x06000BEF RID: 3055 RVA: 0x0004804C File Offset: 0x0004624C
        // (set) Token: 0x06000BF0 RID: 3056 RVA: 0x00048054 File Offset: 0x00046254
        public string[] Pathnames
        {
            get
            {
                return this.pathnames;
            }
            set
            {
                this.pathnames = value;
            }
        }

        // Token: 0x06000BF1 RID: 3057 RVA: 0x00048060 File Offset: 0x00046260
        public AddSpectrumToDocumentEventArgs(string[] pathnames)
        {
            this.pathnames = pathnames;
        }

        // Token: 0x0400078B RID: 1931
        string[] pathnames;
    }
}

namespace BecquerelMonitor
{
    // Token: 0x0200002B RID: 43
    public class PulseCollection
    {
        // Token: 0x1700010C RID: 268
        // (get) Token: 0x0600023A RID: 570 RVA: 0x000090B4 File Offset: 0x000072B4
        // (set) Token: 0x0600023B RID: 571 RVA: 0x000090BC File Offset: 0x000072BC
        public string Format
        {
            get
            {
                return this.format;
            }
            set
            {
                this.format = value;
            }
        }

        // Token: 0x1700010D RID: 269
        // (get) Token: 0x0600023C RID: 572 RVA: 0x000090C8 File Offset: 0x000072C8
        // (set) Token: 0x0600023D RID: 573 RVA: 0x000090D0 File Offset: 0x000072D0
        public PulseData Pulses
        {
            get
            {
                return this.pulses;
            }
            set
            {
                this.pulses = value;
            }
        }

        // Token: 0x0600023E RID: 574 RVA: 0x000090DC File Offset: 0x000072DC
        public void Add(Pulse pulse)
        {
            this.pulses.Add(pulse);
        }

        // Token: 0x1700010E RID: 270
        public Pulse this[int i]
        {
            get
            {
                return this.pulses[i];
            }
            set
            {
                this.pulses[i] = value;
            }
        }

        // Token: 0x06000242 RID: 578 RVA: 0x0000912C File Offset: 0x0000732C
        public void Clear()
        {
            this.pulses.Clear();
        }

        public PulseCollection Clone()
        {
            PulseCollection pulseCollection = new PulseCollection();
            pulseCollection.Format = this.Format;
            pulseCollection.Pulses = this.Pulses;
            return pulseCollection;
        }

        // Token: 0x040000A5 RID: 165
        PulseData pulses = new PulseData();

        // Token: 0x040000A6 RID: 166
        string format = "Base64 encoded binary";
    }
}

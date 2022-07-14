using CoreAudioApi.Interfaces;
using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi
{
    // Token: 0x020001CF RID: 463
    [StructLayout(LayoutKind.Explicit)]
    public struct PropVariant
    {
        // Token: 0x0600163B RID: 5691 RVA: 0x0006DC00 File Offset: 0x0006BE00
        internal byte[] GetBlob()
        {
            byte[] array = new byte[this.blobVal.Length];
            for (int i = 0; i < this.blobVal.Length; i++)
            {
                array[i] = Marshal.ReadByte((IntPtr)((long)this.blobVal.Data + (long)i));
            }
            return array;
        }

        // Token: 0x170006B6 RID: 1718
        // (get) Token: 0x0600163C RID: 5692 RVA: 0x0006DC5C File Offset: 0x0006BE5C
        public object Value
        {
            get
            {
                VarEnum varEnum = (VarEnum)this.vt;
                VarEnum varEnum2 = varEnum;
                if (varEnum2 <= VarEnum.VT_INT)
                {
                    switch (varEnum2)
                    {
                        case VarEnum.VT_I2:
                            return this.iVal;
                        case VarEnum.VT_I4:
                            return this.lVal;
                        default:
                            switch (varEnum2)
                            {
                                case VarEnum.VT_I1:
                                    return this.bVal;
                                case VarEnum.VT_UI4:
                                    return this.ulVal;
                                case VarEnum.VT_I8:
                                    return this.hVal;
                                case VarEnum.VT_INT:
                                    return this.iVal;
                            }
                            break;
                    }
                }
                else
                {
                    if (varEnum2 == VarEnum.VT_LPWSTR)
                    {
                        return Marshal.PtrToStringUni(this.everything_else);
                    }
                    if (varEnum2 == VarEnum.VT_BLOB)
                    {
                        return this.GetBlob();
                    }
                }
                return "FIXME Type = " + varEnum.ToString();
            }
        }

        // Token: 0x04000CBE RID: 3262
        [FieldOffset(0)]
        short vt;

        // Token: 0x04000CBF RID: 3263
        [FieldOffset(2)]
        short wReserved1;

        // Token: 0x04000CC0 RID: 3264
        [FieldOffset(4)]
        short wReserved2;

        // Token: 0x04000CC1 RID: 3265
        [FieldOffset(6)]
        short wReserved3;

        // Token: 0x04000CC2 RID: 3266
        [FieldOffset(8)]
        sbyte cVal;

        // Token: 0x04000CC3 RID: 3267
        [FieldOffset(8)]
        byte bVal;

        // Token: 0x04000CC4 RID: 3268
        [FieldOffset(8)]
        short iVal;

        // Token: 0x04000CC5 RID: 3269
        [FieldOffset(8)]
        ushort uiVal;

        // Token: 0x04000CC6 RID: 3270
        [FieldOffset(8)]
        int lVal;

        // Token: 0x04000CC7 RID: 3271
        [FieldOffset(8)]
        uint ulVal;

        // Token: 0x04000CC8 RID: 3272
        [FieldOffset(8)]
        long hVal;

        // Token: 0x04000CC9 RID: 3273
        [FieldOffset(8)]
        ulong uhVal;

        // Token: 0x04000CCA RID: 3274
        [FieldOffset(8)]
        float fltVal;

        // Token: 0x04000CCB RID: 3275
        [FieldOffset(8)]
        double dblVal;

        // Token: 0x04000CCC RID: 3276
        [FieldOffset(8)]
        Blob blobVal;

        // Token: 0x04000CCD RID: 3277
        [FieldOffset(8)]
        DateTime date;

        // Token: 0x04000CCE RID: 3278
        [FieldOffset(8)]
        bool boolVal;

        // Token: 0x04000CCF RID: 3279
        [FieldOffset(8)]
        int scode;

        // Token: 0x04000CD0 RID: 3280
        [FieldOffset(8)]
        System.Runtime.InteropServices.ComTypes.FILETIME filetime;

        // Token: 0x04000CD1 RID: 3281
        [FieldOffset(8)]
        IntPtr everything_else;
    }
}

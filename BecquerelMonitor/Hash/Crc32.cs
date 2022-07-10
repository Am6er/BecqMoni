using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace BecquerelMonitor.Hash
{
    public sealed class Crc32 : HashAlgorithm
    {
        public const UInt32 DefaultPolynomial = 0x04C11DB7u;
        public const UInt32 DefaultSeed = 0xffffffffu;

        static UInt32[] defaultTable;

        readonly UInt32 seed;
        readonly UInt32[] table;
        UInt32 hash;
        UInt32 xorOut = 0xffffffff;
        bool reflectIn = true;
        bool reflectOut = true;

        public Crc32()
            : this(DefaultPolynomial, DefaultSeed)
        {
        }

        public Crc32(UInt32 polynomial, UInt32 seed, UInt32 XorOut = 0xFFFFFFFF, bool refIn = true, bool refOut = true)
        {
            if (!BitConverter.IsLittleEndian)
                throw new PlatformNotSupportedException("Not supported on Big Endian processors");

            table = InitializeTable(polynomial, refIn);
            this.seed = hash = seed;

            this.xorOut = XorOut;
            this.reflectIn = refIn;
            this.reflectOut = refOut;
        }

        public override void Initialize()
        {
            hash = seed;
        }

        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            hash = CalculateHash(table, hash, array, ibStart, cbSize, xorOut, reflectIn, reflectOut);
        }

        protected override byte[] HashFinal()
        {
            var hashBuffer = UInt32ToBigEndianBytes(hash ^ xorOut);
            HashValue = hashBuffer;
            return hashBuffer;
        }

        public override int HashSize => 32;

        public static UInt32 Compute(byte[] buffer) => Compute(DefaultSeed, buffer);

        public static UInt32 Compute(UInt32 seed, byte[] buffer) => Compute(DefaultPolynomial, seed, buffer);

        public static UInt32 Compute(UInt32 polynomial, UInt32 seed, byte[] buffer) =>
            ~CalculateHash(InitializeTable(polynomial), seed, buffer, 0, buffer.Length);

        static UInt32[] InitializeTable(UInt32 polynomial, bool refIn = true)
        {
            if (polynomial == DefaultPolynomial && defaultTable != null && refIn)
                return defaultTable;

            uint bit;
            var createTable = new UInt32[256];
            for (var i = 0; i < 256; i++)
            {
                var entry = (UInt32)(refIn ? reflect((uint)i, 8) : (uint)i);

                entry <<= 24;
                for (var j = 0; j < 8; j++)
                {
                    bit = entry & (1u << 31);
                    entry <<= 1;
                    if (bit > 0)
                    {
                        entry ^= polynomial;
                    }
                }

                if (refIn)
                {
                    entry = reflect(entry, 32);
                }
                createTable[i] = entry;
            }

            if (polynomial == DefaultPolynomial && refIn)
                defaultTable = createTable;

            return createTable;
        }


        static UInt32 CalculateHash(UInt32[] table, UInt32 seed, IList<byte> buffer, int start, int size, uint xorOut = 0xffffffff, bool refIn = true, bool refOut = true)
        {
            var hash = seed;
            //if (refIn) hash = reflect(hash, 32); 

            if (refIn)
            {
                for (var i = start; i < start + size; i++)
                    hash = (hash >> 8) ^ table[buffer[i] ^ hash & 0xff];
            }
            else
            {
                for (var i = start; i < start + size; i++)
                    hash = (hash << 8) ^ table[buffer[i] ^ ((hash >> 24) & 0xff)];
            }

            if (refIn ^ refOut)
            {
                hash = reflect(hash, 32);
            }

            return hash;
        }

        static byte[] UInt32ToBigEndianBytes(UInt32 uint32)
        {
            var result = BitConverter.GetBytes(uint32);

            if (BitConverter.IsLittleEndian)
                Array.Reverse(result);

            return result;
        }

        public static UInt32 reflect(UInt32 crc, int bitnum)
        {

            // reflects the lower 'bitnum' bits of 'crc'

            UInt32 i, j = 1, crcout = 0;

            for (i = (UInt32)1 << (bitnum - 1); i > 0; i >>= 1)
            {
                if ((crc & i) > 0)
                    crcout |= j;
                j <<= 1;
            }
            return (crcout);
        }

    }
}
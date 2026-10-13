using System;
using System.IO.Hashing;
using System.Security.Cryptography;
using DiscUtils.Raw;

// Written by Derek Pascarella (ateam)
//
// Rebuilds TOSEC-byte-exact CUE/BIN tracks from a Redump-form track, using the
// TOSEC DAT's expected per-track CRC32 + MD5 + size as the target.
//
// Algorithm tier:
//   1. Verbatim: if the Redump bytes already hash-match the DAT entry, return them.
//   2. Sector-aligned silence pregap (0/75/150/225 sectors of zeros prepended,
//      tail truncated to target size). Hits the standard CD audio pregap case.
//   3. Rolling CRC32 scan over [zero-pad] + [synthesized Mode-1 leading sectors
//      for data tracks] + [Redump bytes] + [synthesized Mode-1 trailing sectors
//      for data tracks] + [zero-pad]. The GF(2)-precomputed rollback table
//      makes each window-shift O(1). Catches sub-sector drive-offset variance
//      (audio) and TOSEC's per-disc Mode-1 leadout conventions around the
//      trailing HD-area data track.

namespace GDMENUCardManager.Core
{
    public static class RedumpReconstructor
    {
        private const int SectorSize = 2352;
        private const int PadSectors = 300;
        private const long PadBytes = (long)SectorSize * PadSectors;
        private const int SynthSectors = 150;

        /// <summary>
        /// Null when no tier produces a hash match. The caller falls back to the strip path.
        /// </summary>
        public static byte[] TryReconstruct(
            byte[] gdiBytes,
            bool gdiIsDataTrack,
            long expectedSize,
            uint expectedCrc,
            byte[] expectedMd5)
        {
            if (gdiBytes == null) throw new ArgumentNullException(nameof(gdiBytes));
            if (expectedMd5 == null || expectedMd5.Length != 16) throw new ArgumentException("expectedMd5 must be 16 bytes.", nameof(expectedMd5));

            // Verbatim.
            if (gdiBytes.LongLength == expectedSize && Crc32.HashToUInt32(gdiBytes) == expectedCrc)
            {
                if (MD5.HashData(gdiBytes).AsSpan().SequenceEqual(expectedMd5))
                    return gdiBytes;
            }

            // Sector-aligned leading-silence pregap candidates.
            foreach (int pregap in new[] { 0, 75, 150, 225 })
            {
                long prepend = (long)pregap * SectorSize;
                if (prepend >= expectedSize) continue;
                long copyFromGdi = expectedSize - prepend;
                if (copyFromGdi > gdiBytes.LongLength) continue;

                var candidate = new byte[expectedSize];
                Array.Copy(gdiBytes, 0, candidate, prepend, copyFromGdi);
                if (Crc32.HashToUInt32(candidate) == expectedCrc &&
                    MD5.HashData(candidate).AsSpan().SequenceEqual(expectedMd5))
                    return candidate;
            }

            // Rolling-CRC scan.
            byte[] leadingSynth = Array.Empty<byte>();
            byte[] trailingSynth = Array.Empty<byte>();
            if (gdiIsDataTrack && gdiBytes.LongLength >= SectorSize)
            {
                int firstTime = DecodeMsfTime(gdiBytes, 12);
                int lastTime = DecodeMsfTime(gdiBytes, gdiBytes.Length - SectorSize + 12);
                leadingSynth = SynthesizeMode1Sectors(firstTime - SynthSectors, SynthSectors);
                trailingSynth = SynthesizeMode1Sectors(lastTime + 1, SynthSectors);
            }

            long bufLen = PadBytes + leadingSynth.LongLength + gdiBytes.LongLength + trailingSynth.LongLength + PadBytes;
            if (bufLen < expectedSize) return null;

            byte[] buf = new byte[bufLen];
            long gdiOffsetInBuf = PadBytes + leadingSynth.LongLength;
            if (leadingSynth.Length > 0) Array.Copy(leadingSynth, 0, buf, PadBytes, leadingSynth.Length);
            Array.Copy(gdiBytes, 0, buf, gdiOffsetInBuf, gdiBytes.Length);
            if (trailingSynth.Length > 0) Array.Copy(trailingSynth, 0, buf, gdiOffsetInBuf + gdiBytes.LongLength, trailingSynth.Length);

            long hit = RollingCrcScan(buf, expectedSize, expectedCrc, expectedMd5);
            if (hit < 0) return null;

            var window = new byte[expectedSize];
            Array.Copy(buf, hit, window, 0, expectedSize);
            return window;
        }

        // Decodes the GD-ROM extended-BCD MSF (standard BCD 0-99, hex-continued
        // 0xA0..0xD9 for 100-139) at sector header bytes [off..off+2], returns the
        // sector's disc-time (= LBA + 150).
        private static int DecodeMsfTime(byte[] data, int headerOffset)
        {
            int m = ((data[headerOffset + 0] >> 4) & 0xF) * 10 + (data[headerOffset + 0] & 0xF);
            int s = ((data[headerOffset + 1] >> 4) & 0xF) * 10 + (data[headerOffset + 1] & 0xF);
            int f = ((data[headerOffset + 2] >> 4) & 0xF) * 10 + (data[headerOffset + 2] & 0xF);
            return m * 60 * 75 + s * 75 + f;
        }

        private static byte EncodeMsfByte(int v) => (byte)(((v / 10) << 4) | (v % 10));

        private static readonly byte[] Mode1Sync = { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 };

        private static byte[] SynthesizeMode1Sectors(int firstSectorTime, int count)
        {
            var buf = new byte[count * SectorSize];
            var sector = new byte[SectorSize];
            for (int i = 0; i < count; i++)
            {
                Array.Clear(sector);
                Array.Copy(Mode1Sync, 0, sector, 0, 12);
                int t = firstSectorTime + i;
                sector[12] = EncodeMsfByte(t / 4500);
                sector[13] = EncodeMsfByte((t % 4500) / 75);
                sector[14] = EncodeMsfByte(t % 75);
                sector[15] = 0x01;
                uint edc = ECM.EDC_Calc(sector, 2064);
                sector[2064] = (byte)(edc & 0xFF);
                sector[2065] = (byte)((edc >> 8) & 0xFF);
                sector[2066] = (byte)((edc >> 16) & 0xFF);
                sector[2067] = (byte)((edc >> 24) & 0xFF);
                ECM.ECC_Populate(sector, 0, sector, 0);
                Array.Copy(sector, 0, buf, i * SectorSize, SectorSize);
            }
            return buf;
        }

        private static long RollingCrcScan(byte[] buf, long targetSize, uint targetCrc, byte[] targetMd5)
        {
            long W = targetSize;
            uint[] outTab = BuildRollbackTable(W);
            uint[] fastTab = BuildFastCrcTable();
            uint[] crcTab = BuildCrcTable();

            uint crc2 = CalcCrc(buf, (int)W, crcTab, 0);
            if (crc2 == targetCrc && MD5.HashData(buf.AsSpan(0, (int)W)).AsSpan().SequenceEqual(targetMd5))
                return 0;

            long limit = buf.LongLength - W;
            for (long i = 0; i < limit; i++)
            {
                crc2 = (crc2 >> 8) ^ fastTab[(byte)crc2 ^ buf[W + i]] ^ outTab[buf[i]];
                if (crc2 == targetCrc && MD5.HashData(buf.AsSpan((int)(i + 1), (int)W)).AsSpan().SequenceEqual(targetMd5))
                    return i + 1;
            }
            return -1;
        }

        private static uint[] BuildRollbackTable(long rollWindowBytes)
        {
            const uint START = 0;
            long add = ~(long)START & 0xFFFFFFFFL;
            add = Multiply(add, Xpow8N(rollWindowBytes));
            add ^= 0xFFFFFFFFL;
            long mul = 0x0000000080000000L ^ Xpow8N(1);
            add = Multiply(add, mul);
            mul = XpowN(8 * rollWindowBytes + 0x20L);
            var table = new uint[256];
            for (uint ii = 0; ii < 256; ii++)
                table[ii] = (uint)(MultiplyUnnormalized(ii, 8, mul) ^ add);
            return table;
        }

        private static uint[] BuildCrcTable()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint r = i;
                for (uint j = 0; j < 8; j++)
                    r = (r >> 1) ^ (0xEDB88320u & ~((r & 1) - 1u));
                t[i] = r;
            }
            return t;
        }

        private static uint[] BuildFastCrcTable()
        {
            var t = new uint[256];
            t[0] = 0;
            uint r = 0xEDB88320u;
            t[128] = r;
            for (uint i = 64; i != 0; i /= 2)
            {
                r = (r >> 1) ^ (0xEDB88320u & ~((r & 1) - 1u));
                t[i] = r;
            }
            for (uint i = 2; i < 256; i *= 2)
                for (uint j = 1; j < i; j++)
                    t[i + j] = t[i] ^ t[j];
            return t;
        }

        private static uint CalcCrc(byte[] buf, int len, uint[] crcTab, int offset)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = offset; i < len + offset; i++)
                crc = crcTab[(crc ^ buf[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private const long One = 0x0000000080000000L;

        private static readonly long[] Normalize = { 0x0000000000000000L, 0x00000000EDB88320L };

        private static readonly long[] XPow2N = {
            0x0000000040000000L,0x0000000020000000L,0x0000000008000000L,0x0000000000800000L,
            0x0000000000008000L,0x00000000EDB88320L,0x00000000B1E6B092L,0x00000000A06A2517L,
            0x00000000ED627DAEL,0x0000000088D14467L,0x00000000D7BBFE6AL,0x00000000EC447F11L,
            0x000000008E7EA170L,0x000000006427800EL,0x000000004D47BAE0L,0x0000000009FE548FL,
            0x0000000083852D0FL,0x0000000030362F1AL,0x000000007B5A9CC3L,0x0000000031FEC169L,
            0x000000009FEC022AL,0x000000006C8DEDC4L,0x0000000015D6874DL,0x000000005FDE7A4EL,
            0x00000000BAD90E37L,0x000000002E4E5EEFL,0x000000004EABA214L,0x00000000A8A472C0L,
            0x00000000429A969EL,0x00000000148D302AL,0x00000000C40BA6D0L,0x00000000C4E22C3CL,
            0x0000000040000000L,0x0000000020000000L,0x0000000008000000L,0x0000000000800000L,
            0x0000000000008000L,0x00000000EDB88320L,0x00000000B1E6B092L,0x00000000A06A2517L,
            0x00000000ED627DAEL,0x0000000088D14467L,0x00000000D7BBFE6AL,0x00000000EC447F11L,
            0x000000008E7EA170L,0x000000006427800EL,0x000000004D47BAE0L,0x0000000009FE548FL,
            0x0000000083852D0FL,0x0000000030362F1AL,0x000000007B5A9CC3L,0x0000000031FEC169L,
            0x000000009FEC022AL,0x000000006C8DEDC4L,0x0000000015D6874DL,0x000000005FDE7A4EL,
            0x00000000BAD90E37L,0x000000002E4E5EEFL,0x000000004EABA214L,0x00000000A8A472C0L,
            0x00000000429A969EL,0x00000000148D302AL,0x00000000C40BA6D0L,0x00000000C4E22C3CL,
        };

        private static long MultiplyUnnormalized(uint unnorm, int degree, long m)
        {
            uint v = unnorm;
            long result = 0;
            while (degree > 0x20)
            {
                degree -= 0x20;
                long value = v & (One | (One - 1));
                result ^= Multiply(value, Multiply(m, XpowN(degree)));
                v >>= 32;
            }
            result ^= Multiply((long)v << (32 - degree), m);
            return result;
        }

        private static long Xpow8N(long n) => XpowN(n << 3);

        private static long XpowN(long n)
        {
            long result = One;
            for (int i = 0; n != 0; i++, n >>= 1)
                if ((n & 1) == 1) result = Multiply(result, XPow2N[i]);
            return result;
        }

        private static long Multiply(long aa, long bb)
        {
            long a = aa, b = bb;
            if ((a ^ (a - 1)) < (b ^ (b - 1))) (a, b) = (b, a);
            if (a == 0) return 0;
            long product = 0;
            for (; a != 0; a <<= 1)
            {
                if ((a & One) != 0) { product ^= b; a ^= One; }
                b = (b >> 1) ^ Normalize[(byte)(b & 1)];
            }
            return product;
        }
    }
}

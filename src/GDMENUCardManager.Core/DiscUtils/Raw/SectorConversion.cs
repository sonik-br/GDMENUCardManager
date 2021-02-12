/*
 * Sector conversion code borrowed from bizhawk: http://code.google.com/p/bizhawk/
 * */
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;

namespace DiscUtils.Raw
{
    /// <summary>
    /// Used for converting sectors from 2048 byte size to 2352
    /// </summary>
    public class SectorConversion
    {
        private static byte BCD_Byte(byte val)
        {
            byte ret = (byte)(val % 10);
            ret += (byte)(16 * (val / 10));
            return ret;
        }

        /// <summary>
        /// Convert sector from 2048 bytes to 2352
        /// </summary>
        /// <param name="data">A 2048 byte sector</param>
        /// <param name="lba">The LBA offset of the sector we're converting</param>
        /// <returns>The sector data</returns>
        public static byte[] ConvertSectorToRawMode1(byte[] data, int lba)
        {
            byte[] buffer = new byte[2352];
            Array.Copy(data, 0, buffer, 16, 2048);

            int aba = lba + 150;
            byte bcd_aba_min = BCD_Byte((byte)(aba / 60 / 75));
            byte bcd_aba_sec = BCD_Byte((byte)((aba / 75) % 60));
            byte bcd_aba_frac = BCD_Byte((byte)(aba % 75));

            //sync
            buffer[0] = 0x00; buffer[1] = 0xFF; buffer[2] = 0xFF; buffer[3] = 0xFF;
            buffer[4] = 0xFF; buffer[5] = 0xFF; buffer[6] = 0xFF; buffer[7] = 0xFF;
            buffer[8] = 0xFF; buffer[9] = 0xFF; buffer[10] = 0xFF; buffer[11] = 0x00;
            //sector address
            buffer[12] = bcd_aba_min;
            buffer[13] = bcd_aba_sec;
            buffer[14] = bcd_aba_frac;
            //mode 1
            buffer[15] = 1;

            //calculate EDC and poke into the sector
            uint edc = ECM.EDC_Calc(buffer, 2064);
            ECM.PokeUint(buffer, 2064, edc);

            //intermediate
            for (int i = 0; i < 8; i++) buffer[2068 + i] = 0;
            //ECC
            ECM.ECC_Populate(buffer, 0, buffer, 0);

            return buffer;
        }
    }
}

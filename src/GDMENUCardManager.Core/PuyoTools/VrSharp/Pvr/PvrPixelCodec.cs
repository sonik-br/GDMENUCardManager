using System;
using System.Drawing;

namespace VrSharp.Pvr
{
    public abstract class PvrPixelCodec : VrPixelCodec
    {
        #region Argb1555
        // Argb1555
        public class Argb1555 : PvrPixelCodec
        {
            public override bool CanEncode
            {
                get { return true; }
            }

            public override int Bpp
            {
                get { return 16; }
            }

            public override void DecodePixel(byte[] source, int sourceIndex, byte[] destination, int destinationIndex)
            {
                ushort pixel = BitConverter.ToUInt16(source, sourceIndex);

                destination[destinationIndex + 3] = (byte)(((pixel >> 15) & 0x01) * 0xFF);
                destination[destinationIndex + 2] = (byte)(((pixel >> 10) & 0x1F) * 0xFF / 0x1F);
                destination[destinationIndex + 1] = (byte)(((pixel >> 5) & 0x1F) * 0xFF / 0x1F);
                destination[destinationIndex + 0] = (byte)(((pixel >> 0) & 0x1F) * 0xFF / 0x1F);
            }

            public override void EncodePixel(byte[] source, int sourceIndex, byte[] destination, int destinationIndex)
            {
                ushort pixel = 0x0000;
                pixel |= (ushort)((source[sourceIndex + 3] >> 7) << 15);
                pixel |= (ushort)((source[sourceIndex + 2] >> 3) << 10);
                pixel |= (ushort)((source[sourceIndex + 1] >> 3) << 5);
                pixel |= (ushort)((source[sourceIndex + 0] >> 3) << 0);

                destination[destinationIndex + 1] = (byte)((pixel >> 8) & 0xFF);
                destination[destinationIndex + 0] = (byte)(pixel & 0xFF);
            }
        }
        #endregion

        #region Rgb565
        // Rgb565
        public class Rgb565 : PvrPixelCodec
        {
            public override bool CanEncode
            {
                get { return true; }
            }

            public override int Bpp
            {
                get { return 16; }
            }

            public override void DecodePixel(byte[] source, int sourceIndex, byte[] destination, int destinationIndex)
            {
                ushort pixel = BitConverter.ToUInt16(source, sourceIndex);

                destination[destinationIndex + 3] = 0xFF;
                destination[destinationIndex + 2] = (byte)(((pixel >> 11) & 0x1F) * 0xFF / 0x1F);
                destination[destinationIndex + 1] = (byte)(((pixel >> 5)  & 0x3F) * 0xFF / 0x3F);
                destination[destinationIndex + 0] = (byte)(((pixel >> 0)  & 0x1F) * 0xFF / 0x1F);
            }

            public override void EncodePixel(byte[] source, int sourceIndex, byte[] destination, int destinationIndex)
            {
                ushort pixel = 0x0000;
                pixel |= (ushort)((source[sourceIndex + 2] >> 3) << 11);
                pixel |= (ushort)((source[sourceIndex + 1] >> 2) << 5);
                pixel |= (ushort)((source[sourceIndex + 0] >> 3) << 0);

                destination[destinationIndex + 1] = (byte)((pixel >> 8) & 0xFF);
                destination[destinationIndex + 0] = (byte)(pixel & 0xFF);
            }
        }
        #endregion

        #region Argb4444
        // Argb4444
        public class Argb4444 : PvrPixelCodec
        {
            public override bool CanEncode
            {
                get { return true; }
            }

            public override int Bpp
            {
                get { return 16; }
            }

            public override void DecodePixel(byte[] source, int sourceIndex, byte[] destination, int destinationIndex)
            {
                ushort pixel = BitConverter.ToUInt16(source, sourceIndex);

                destination[destinationIndex + 3] = (byte)(((pixel >> 12) & 0x0F) * 0xFF / 0x0F);
                destination[destinationIndex + 2] = (byte)(((pixel >> 8)  & 0x0F) * 0xFF / 0x0F);
                destination[destinationIndex + 1] = (byte)(((pixel >> 4)  & 0x0F) * 0xFF / 0x0F);
                destination[destinationIndex + 0] = (byte)(((pixel >> 0)  & 0x0F) * 0xFF / 0x0F);
            }

            public override void EncodePixel(byte[] source, int sourceIndex, byte[] destination, int destinationIndex)
            {
                ushort pixel = 0x0000;
                pixel |= (ushort)((source[sourceIndex + 3] >> 4) << 12);
                pixel |= (ushort)((source[sourceIndex + 2] >> 4) << 8);
                pixel |= (ushort)((source[sourceIndex + 1] >> 4) << 4);
                pixel |= (ushort)((source[sourceIndex + 0] >> 4) << 0);

                destination[destinationIndex + 1] = (byte)((pixel >> 8) & 0xFF);
                destination[destinationIndex + 0] = (byte)(pixel & 0xFF);
            }
        }
        #endregion

        #region Get Codec
        public static PvrPixelCodec GetPixelCodec(PvrPixelFormat format)
        {
            switch (format)
            {
                case PvrPixelFormat.Argb1555:
                    return new Argb1555();
                case PvrPixelFormat.Rgb565:
                    return new Rgb565();
                case PvrPixelFormat.Argb4444:
                    return new Argb4444();
            }

            return null;
        }
        #endregion
    }
}
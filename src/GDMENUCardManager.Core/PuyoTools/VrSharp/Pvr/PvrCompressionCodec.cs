using System;
using System.IO;

namespace VrSharp.Pvr
{
    public abstract class PvrCompressionCodec
    {
        #region Rle Compression
        // Rle Compression
        public class Rle : PvrCompressionCodec
        {
            public override byte[] Decompress(byte[] input, int DataOffset, VrPixelCodec PixelCodec, VrDataCodec DataCodec)
            {
                byte[] output     = new byte[BitConverter.ToUInt32(input, 0x00)];
                int SourcePointer = DataOffset;
                int DestPointer   = 0x00;
                int PixelSize     = (DataCodec.Bpp >> 3);

                // Copy the header
                if (DataOffset - 4 > 0)
                {
                    Array.Copy(input, 0x04, output, 0x00, DataOffset - 4);
                    DestPointer += (DataOffset - 4);
                }

                // Decompress
                while (SourcePointer < input.Length && DestPointer < output.Length)
                {
                    int amount = input[SourcePointer + PixelSize] + 1;
                    for (int i = 0; i < amount; i++)
                    {
                        Array.Copy(input, SourcePointer, output, DestPointer, PixelSize);
                        DestPointer += PixelSize;
                    }

                    SourcePointer += PixelSize + 1;
                }

                return output;
            }

            public override byte[] Compress(byte[] input, int DataOffset, VrPixelCodec PixelCodec, VrDataCodec DataCodec)
            {
                // We can't compress 4-bit textures
                if (DataCodec.Bpp < 8)
                    return input;

                MemoryStream output = new MemoryStream();
                int SourcePointer   = DataOffset;
                int DestPointer     = DataOffset + 4;
                int PixelSize = (DataCodec.Bpp >> 3);

                using (BinaryWriter Writer = new BinaryWriter(output))
                {
                    Writer.Write(input.Length); // Decompressed filesize
                    Writer.Write(input, 0x00, DataOffset); // Header

                    while (SourcePointer < input.Length)
                    {
                        byte[] pixel = new byte[PixelSize];
                        Array.Copy(input, SourcePointer, pixel, 0x00, PixelSize);
                        Writer.Write(pixel);
                        SourcePointer += PixelSize;
                        DestPointer   += PixelSize;

                        int repeat = 0;
                        while (SourcePointer + PixelSize < input.Length && repeat < 255)
                        {
                            bool match = true;

                            for (int i = 0; i < PixelSize && match; i++)
                            {
                                if (input[SourcePointer + i] != pixel[i])
                                {
                                    match = false;
                                    break;
                                }
                            }

                            if (match)
                            {
                                repeat++;
                                SourcePointer += PixelSize;
                            }
                            else
                                break;
                        }

                        Writer.Write((byte)repeat);
                        DestPointer++;
                    }

                    Writer.Flush();
                }

                return output.ToArray();
            }
        }
        #endregion

        public abstract byte[] Decompress(byte[] input, int DataOffset, VrPixelCodec PixelCodec, VrDataCodec DataCodec);
        public abstract byte[] Compress(byte[] input, int DataOffset, VrPixelCodec PixelCodec, VrDataCodec DataCodec);

        public static PvrCompressionCodec GetCompressionCodec(PvrCompressionFormat format)
        {
            switch (format)
            {
                case PvrCompressionFormat.Rle:
                    return new Rle();
            }

            return null;
        }
    }
}
using System;

namespace VrSharp.Pvr
{
    public abstract class PvrDataCodec : VrDataCodec
    {
        #region Square Twiddled
        // Square Twiddled
        public class SquareTwiddled : PvrDataCodec
        {
            public override bool CanEncode
            {
                get { return true; }
            }

            public override int Bpp
            {
                get { return PixelCodec.Bpp; }
            }

            public override byte[] Decode(byte[] source, int sourceIndex, int width, int height)
            {
                // Destination data & index
                byte[] destination = new byte[width * height * 4];
                int destinationIndex = 0;

                // Twiddle map
                int[] twiddleMap = MakeTwiddleMap(width);
                
                // Decode texture data
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        PixelCodec.DecodePixel(source, sourceIndex + (((twiddleMap[x] << 1) | twiddleMap[y]) << (PixelCodec.Bpp >> 4)), destination, destinationIndex);
                        destinationIndex += 4;
                    }
                }

                return destination;
            }

            public override byte[] Encode(byte[] source, int sourceIndex, int width, int height)
            {
                // Destination data
                byte[] destination = new byte[width * height * (PixelCodec.Bpp >> 3)];

                // Twiddle map
                int[] twiddleMap = MakeTwiddleMap(width);

                // Encode texture data
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        PixelCodec.EncodePixel(source, sourceIndex, destination, ((twiddleMap[x] << 1) | twiddleMap[y]) << (PixelCodec.Bpp >> 4));
                        sourceIndex += 4;
                    }
                }

                return destination;
            }
        }
        #endregion

        #region Square Twiddled with Mipmaps
        // Square Twiddled with Mipmaps
        public class SquareTwiddledMipmaps : PvrDataCodec
        {
            public override bool CanEncode
            {
                get { return true; }
            }

            public override int Bpp
            {
                get { return PixelCodec.Bpp; }
            }

            public override bool HasMipmaps
            {
                get { return true; }
            }

            public override byte[] Decode(byte[] source, int sourceIndex, int width, int height)
            {
                // Destination data & index
                byte[] destination = new byte[width * height * 4];
                int destinationIndex = 0;

                // Twiddle map
                int[] twiddleMap = MakeTwiddleMap(width);

                // Decode texture data
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        PixelCodec.DecodePixel(source, sourceIndex + (((twiddleMap[x] << 1) | twiddleMap[y]) << (PixelCodec.Bpp >> 4)), destination, destinationIndex);
                        destinationIndex += 4;
                    }
                }

                return destination;
            }

            public override byte[] Encode(byte[] source, int sourceIndex, int width, int height)
            {
                // Destination data
                byte[] destination = new byte[width * height * (PixelCodec.Bpp >> 3)];

                // Twiddle map
                int[] twiddleMap = MakeTwiddleMap(width);

                // Encode texture data
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        PixelCodec.EncodePixel(source, sourceIndex, destination, ((twiddleMap[x] << 1) | twiddleMap[y]) << (PixelCodec.Bpp >> 4));
                        sourceIndex += 4;
                    }
                }

                return destination;
            }
        }
        #endregion

        #region Vq
        // Vq
        public class Vq : PvrDataCodec
        {
            public override bool CanEncode
            {
                get { return false; }
            }

            public override int Bpp
            {
                get { return 2; }
            }

            public override int PaletteEntries
            {
                get { return 1024; } // Actually 256
            }

            public override byte[] Decode(byte[] source, int sourceIndex, int width, int height)
            {
                // Destination data & index
                byte[] destination = new byte[width * height * 4];
                int destinationIndex;

                // Twiddle map
                int[] twiddleMap = MakeTwiddleMap(width);

                // Decode texture data
                for (int y = 0; y < height; y += 2)
                {
                    for (int x = 0; x < width; x += 2)
                    {
                        int index = source[sourceIndex + ((twiddleMap[x >> 1] << 1) | twiddleMap[y >> 1])] * 4;

                        for (int x2 = 0; x2 < 2; x2++)
                        {
                            for (int y2 = 0; y2 < 2; y2++)
                            {
                                destinationIndex = ((((y + y2) * width) + (x + x2)) * 4);

                                for (int i = 0; i < 4; i++)
                                {
                                    destination[destinationIndex] = palette[index][i];
                                    destinationIndex++;
                                }

                                index++;
                            }
                        }
                    }
                }

                return destination;
            }

            public override byte[] Encode(byte[] source, int sourceIndex, int width, int height)
            {
                return null;
            }
        }
        #endregion

        #region Vq with Mipmaps
        // Vq with Mipmaps
        public class VqMipmaps : PvrDataCodec
        {
            public override bool CanEncode
            {
                get { return false; }
            }

            public override int Bpp
            {
                get { return 2; }
            }

            public override int PaletteEntries
            {
                get { return 1024; } // Actually 256
            }

            public override bool HasMipmaps
            {
                get { return true; }
            }

            public override byte[] Decode(byte[] source, int sourceIndex, int width, int height)
            {
                // Destination data & index
                byte[] destination = new byte[width * height * 4];
                int destinationIndex;

                // Decode a 1x1 texture (for mipmaps)
                // No need to make use of a twiddle map in this case
                if (width == 1 && height == 1)
                {
                    int index = source[sourceIndex] * 4;

                    destinationIndex = 0;

                    for (int i = 0; i < 4; i++)
                    {
                        destination[destinationIndex] = palette[index][i];
                        destinationIndex++;
                    }

                    return destination;
                }

                // Twiddle map
                int[] twiddleMap = MakeTwiddleMap(width);

                // Decode texture data
                for (int y = 0; y < height; y += 2)
                {
                    for (int x = 0; x < width; x += 2)
                    {
                        int index = source[sourceIndex + ((twiddleMap[x >> 1] << 1) | twiddleMap[y >> 1])] * 4;

                        for (int x2 = 0; x2 < 2; x2++)
                        {
                            for (int y2 = 0; y2 < 2; y2++)
                            {
                                destinationIndex = ((((y + y2) * width) + (x + x2)) * 4);

                                for (int i = 0; i < 4; i++)
                                {
                                    destination[destinationIndex] = palette[index][i];
                                    destinationIndex++;
                                }

                                index++;
                            }
                        }
                    }
                }

                return destination;
            }

            public override byte[] Encode(byte[] source, int sourceIndex, int width, int height)
            {
                return null;
            }
        }
        #endregion

        #region 4-bit Indexed with External Palette
        // 4-bit Indexed with External Palette
        public class Index4 : PvrDataCodec
        {
            public override bool CanEncode
            {
                get { return true; }
            }

            public override int Bpp
            {
                get { return 4; }
            }

            public override int PaletteEntries
            {
                get { return 16; }
            }

            public override bool NeedsExternalPalette
            {
                get { return true; }
            }

            public override byte[] Decode(byte[] source, int sourceIndex, int width, int height)
            {
                // Destination data & index
                byte[] destination = new byte[width * height * 4];
                int destinationIndex;

                // Get the size of each block to process.
                int size = Math.Min(width, height);

                // Twiddle map
                int[] twiddleMap = MakeTwiddleMap(size);

                // Decode texture data
                for (int y = 0; y < height; y += size)
                {
                    for (int x = 0; x < width; x += size)
                    {
                        for (int y2 = 0; y2 < size; y2++)
                        {
                            for (int x2 = 0; x2 < size; x2++)
                            {
                                byte index = (byte)((source[sourceIndex + (((twiddleMap[x2] << 1) | twiddleMap[y2]) >> 1)] >> ((y2 & 0x1) * 4)) & 0xF);
                                destinationIndex = ((((y + y2) * width) + (x + x2)) * 4);

                                for (int i = 0; i < 4; i++)
                                {
                                    destination[destinationIndex] = palette[index][i];
                                    destinationIndex++;
                                }
                            }
                        }

                        sourceIndex += (size * size) >> 1;
                    }
                }

                return destination;
            }

            public override byte[] Encode(byte[] source, int sourceIndex, int width, int height)
            {
                // Destination data & index
                byte[] destination = new byte[(width * height) >> 1];
                int destinationIndex = 0;

                // Get the size of each block to process.
                int size = Math.Min(width, height);

                // Twiddle map
                int[] twiddleMap = MakeTwiddleMap(size);

                // Encode texture data
                for (int y = 0; y < height; y += size)
                {
                    for (int x = 0; x < width; x += size)
                    {
                        for (int y2 = 0; y2 < size; y2++)
                        {
                            for (int x2 = 0; x2 < size; x2++)
                            {
                                byte index = destination[destinationIndex + (((twiddleMap[x2] << 1) | twiddleMap[y2]) >> 1)];
                                index |= (byte)((source[sourceIndex + (((y + y2) * width) + (x + x2))] & 0xF) << ((y2 & 0x1) * 4));

                                destination[destinationIndex + (((twiddleMap[x2] << 1) | twiddleMap[y2]) >> 1)] = index;
                            }
                        }

                        destinationIndex += (size * size) >> 1;
                    }
                }

                return destination;
            }
        }
        #endregion

        #region 8-bit Indexed with External Palette
        // 8-bit Indexed with External Palette
        public class Index8 : PvrDataCodec
        {
            public override bool CanEncode
            {
                get { return true; }
            }

            public override int Bpp
            {
                get { return 8; }
            }

            public override int PaletteEntries
            {
                get { return 256; }
            }

            public override bool NeedsExternalPalette
            {
                get { return true; }
            }

            public override byte[] Decode(byte[] source, int sourceIndex, int width, int height)
            {
                // Destination data & index
                byte[] destination = new byte[width * height * 4];
                int destinationIndex;

                // Get the size of each block to process.
                int size = Math.Min(width, height);

                // Twiddle map
                int[] twiddleMap = MakeTwiddleMap(size);

                // Decode texture data
                for (int y = 0; y < height; y += size)
                {
                    for (int x = 0; x < width; x += size)
                    {
                        for (int y2 = 0; y2 < size; y2++)
                        {
                            for (int x2 = 0; x2 < size; x2++)
                            {
                                byte index = source[sourceIndex + ((twiddleMap[x2] << 1) | twiddleMap[y2])];
                                destinationIndex = ((((y + y2) * width) + (x + x2)) * 4);

                                for (int i = 0; i < 4; i++)
                                {
                                    destination[destinationIndex] = palette[index][i];
                                    destinationIndex++;
                                }
                            }
                        }

                        sourceIndex += (size * size);
                    }
                }

                return destination;
            }

            public override byte[] Encode(byte[] source, int sourceIndex, int width, int height)
            {
                // Destination data & index
                byte[] destination = new byte[width * height];
                int destinationIndex = 0;

                // Get the size of each block to process.
                int size = Math.Min(width, height);

                // Twiddle map
                int[] twiddleMap = MakeTwiddleMap(size);

                // Encode texture data
                for (int x = 0; x < width; x += size)
                {
                    for (int y = 0; y < height; y += size)
                    {
                        for (int y2 = 0; y2 < size; y2++)
                        {
                            for (int x2 = 0; x2 < size; x2++)
                            {
                                destination[destinationIndex + ((twiddleMap[x2] << 1) | twiddleMap[y2])] = source[sourceIndex + (((y + y2) * width) + (x + x2))];
                            }
                        }

                        destinationIndex += (size * size);
                    }
                }

                return destination;
            }
        }
        #endregion

        #region Rectangle
        // Rectangle
        public class Rectangle : PvrDataCodec
        {
            public override bool CanEncode
            {
                get { return true; }
            }

            public override int Bpp
            {
                get { return PixelCodec.Bpp; }
            }

            public override byte[] Decode(byte[] source, int sourceIndex, int width, int height)
            {
                // Destination data & index
                byte[] destination = new byte[width * height * 4];
                int destinationIndex = 0;

                // Decode texture data
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        PixelCodec.DecodePixel(source, sourceIndex, destination, destinationIndex);
                        sourceIndex += (PixelCodec.Bpp >> 3);
                        destinationIndex += 4;
                    }
                }

                return destination;
            }

            public override byte[] Encode(byte[] source, int sourceIndex, int width, int height)
            {
                // Destination data & index
                byte[] destination = new byte[width * height * (PixelCodec.Bpp >> 3)];
                int destinationIndex = 0;

                // Twiddle map
                int[] twiddleMap = MakeTwiddleMap(width);

                // Encode texture data
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        PixelCodec.EncodePixel(source, sourceIndex, destination, destinationIndex);
                        sourceIndex += 4;
                        destinationIndex += (PixelCodec.Bpp >> 3);
                    }
                }

                return destination;
            }
        }
        #endregion

        #region Rectangle Twiddled
        // Rectangle Twiddled
        public class RectangleTwiddled : PvrDataCodec
        {
            public override bool CanEncode
            {
                get { return true; }
            }

            public override int Bpp
            {
                get { return PixelCodec.Bpp; }
            }

            public override byte[] Decode(byte[] source, int sourceIndex, int width, int height)
            {
                // Destination data
                byte[] destination = new byte[width * height * 4];

                // Get the size of each block to process.
                int size = Math.Min(width, height);

                // Twiddle map
                int[] twiddleMap = MakeTwiddleMap(size);

                // Decode texture data
                for (int y = 0; y < height; y += size)
                {
                    for (int x = 0; x < width; x += size)
                    {
                        for (int y2 = 0; y2 < size; y2++)
                        {
                            for (int x2 = 0; x2 < size; x2++)
                            {
                                PixelCodec.DecodePixel(source, sourceIndex + (((twiddleMap[x2] << 1) | twiddleMap[y2]) << (PixelCodec.Bpp >> 4)), destination, ((((y + y2) * width) + (x + x2)) * 4));
                            }
                        }

                        sourceIndex += (size * size) * (PixelCodec.Bpp >> 3);
                    }
                }

                return destination;
            }

            public override byte[] Encode(byte[] source, int sourceIndex, int width, int height)
            {
                // Destination data & index
                byte[] destination = new byte[width * height * (PixelCodec.Bpp >> 3)];
                int destinationIndex = 0;

                // Get the size of each block to process.
                int size = Math.Min(width, height);

                // Twiddle map
                int[] twiddleMap = MakeTwiddleMap(size);

                // Encode texture data
                for (int y = 0; y < height; y += size)
                {
                    for (int x = 0; x < width; x += size)
                    {
                        for (int y2 = 0; y2 < size; y2++)
                        {
                            for (int x2 = 0; x2 < size; x2++)
                            {
                                PixelCodec.EncodePixel(source, sourceIndex + ((((y + y2) * width) + (x + x2)) * 4), destination, destinationIndex + (((twiddleMap[x2] << 1) | twiddleMap[y2]) << (PixelCodec.Bpp >> 4)));
                            }
                        }

                        destinationIndex += (size * size) * (PixelCodec.Bpp >> 3);
                    }
                }

                return destination;
            }
        }
        #endregion

        #region Small Vq
        // Small Vq
        public class SmallVq : PvrDataCodec
        {
            public override bool CanEncode
            {
                get { return false; }
            }

            public override int Bpp
            {
                get { return 2; }
            }

            public override int PaletteEntries
            {
                get { return 1024; } // Varies, 1024 (actually 256) is the largest
            }

            public override byte[] Decode(byte[] source, int sourceIndex, int width, int height)
            {
                // Destination data & index
                byte[] destination = new byte[width * height * 4];
                int destinationIndex;

                // Twiddle map
                int[] twiddleMap = MakeTwiddleMap(width);

                // Decode texture data
                for (int y = 0; y < height; y += 2)
                {
                    for (int x = 0; x < width; x += 2)
                    {
                        int index = (source[sourceIndex + ((twiddleMap[x >> 1] << 1) | twiddleMap[y >> 1])]) * 4;

                        for (int x2 = 0; x2 < 2; x2++)
                        {
                            for (int y2 = 0; y2 < 2; y2++)
                            {
                                destinationIndex = ((((y + y2) * width) + (x + x2)) * 4);

                                for (int i = 0; i < 4; i++)
                                {
                                    destination[destinationIndex] = palette[index][i];
                                    destinationIndex++;
                                }

                                index++;
                            }
                        }
                    }
                }

                return destination;
            }

            public override byte[] Encode(byte[] source, int sourceIndex, int width, int height)
            {
                return null;
            }
        }
        #endregion

        #region Small Vq with Mipmaps
        // Small Vq with Mipmaps
        public class SmallVqMipmaps : PvrDataCodec
        {
            public override bool CanEncode
            {
                get { return false; }
            }

            public override int Bpp
            {
                get { return 2; }
            }

            public override int PaletteEntries
            {
                get { return 1024; } // Varies, 1024 (actually 256) is the largest
            }

            public override bool HasMipmaps
            {
                get { return true; }
            }

            public override byte[] Decode(byte[] source, int sourceIndex, int width, int height)
            {
                // Destination data & index
                byte[] destination = new byte[width * height * 4];
                int destinationIndex;

                // Decode a 1x1 texture (for mipmaps)
                // No need to make use of a twiddle map in this case
                if (width == 1 && height == 1)
                {
                    int index = source[sourceIndex] * 4;

                    destinationIndex = 0;

                    for (int i = 0; i < 4; i++)
                    {
                        destination[destinationIndex] = palette[index][i];
                        destinationIndex++;
                    }

                    return destination;
                }

                // Twiddle map
                int[] twiddleMap = MakeTwiddleMap(width);

                // Decode texture data
                for (int y = 0; y < height; y += 2)
                {
                    for (int x = 0; x < width; x += 2)
                    {
                        int index = (source[sourceIndex + ((twiddleMap[x >> 1] << 1) | twiddleMap[y >> 1])]) * 4;

                        for (int x2 = 0; x2 < 2; x2++)
                        {
                            for (int y2 = 0; y2 < 2; y2++)
                            {
                                destinationIndex = ((((y + y2) * width) + (x + x2)) * 4);

                                for (int i = 0; i < 4; i++)
                                {
                                    destination[destinationIndex] = palette[index][i];
                                    destinationIndex++;
                                }

                                index++;
                            }
                        }
                    }
                }

                return destination;
            }

            public override byte[] Encode(byte[] source, int sourceIndex, int width, int height)
            {
                return null;
            }
        }
        #endregion

        #region Twiddle Code
        // Makes a twiddle map for the specified size texture
        private int[] MakeTwiddleMap(int size)
        {
            int[] twiddleMap = new int[size];

            for (int i = 0; i < size; i++)
            {
                twiddleMap[i] = 0;

                for (int j = 0, k = 1; k <= i; j++, k <<= 1)
                {
                    twiddleMap[i] |= (i & k) << j;
                }
            }

            return twiddleMap;
        }
        #endregion

        #region Get Codec
        public static PvrDataCodec GetDataCodec(PvrDataFormat format)
        {
            switch (format)
            {
                case PvrDataFormat.SquareTwiddled:
                    return new SquareTwiddled();
                case PvrDataFormat.SquareTwiddledMipmaps:
                case PvrDataFormat.SquareTwiddledMipmapsAlt:
                    return new SquareTwiddledMipmaps();
                case PvrDataFormat.Vq:
                    return new Vq();
                case PvrDataFormat.VqMipmaps:
                    return new VqMipmaps();
                case PvrDataFormat.Index4:
                    return new Index4();
                case PvrDataFormat.Index8:
                    return new Index8();
                case PvrDataFormat.Rectangle:
                    return new Rectangle();
                case PvrDataFormat.RectangleTwiddled:
                    return new RectangleTwiddled();
                case PvrDataFormat.SmallVq:
                    return new SmallVq();
                case PvrDataFormat.SmallVqMipmaps:
                    return new SmallVqMipmaps();
            }

            return null;
        }
        #endregion
    }
}
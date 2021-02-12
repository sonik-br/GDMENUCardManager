using System;

namespace VrSharp
{
    #region Pixel Codec
    // Base codec for the pixel codecs
    public abstract class VrPixelCodec
    {
        // Returns if we can encode using this codec.
        public abstract bool CanEncode { get; }

        // Returns the bits per pixel for this pixel format.
        public abstract int Bpp { get; }

        // Decode & Encode a pixel
        public abstract void DecodePixel(byte[] source, int sourceIndex, byte[] destination, int destinationIndex);
        public abstract void EncodePixel(byte[] source, int sourceIndex, byte[] destination, int destinationIndex);

        // Decode & Encode a palette
        public byte[][] DecodePalette(byte[] source, int sourceIndex, int numEntries)
        {
            byte[][] palette = new byte[numEntries][];

            for (int i = 0; i < numEntries; i++)
            {
                palette[i] = new byte[4];
                DecodePixel(source, sourceIndex + (i * (Bpp >> 3)), palette[i], 0);
            }

            return palette;
        }

        public byte[] EncodePalette(byte[][] palette, int numEntries)
        {
            byte[] destination = new byte[numEntries * (Bpp >> 3)];
            int destinationIndex = 0;

            for (int i = 0; i < numEntries; i++)
            {
                EncodePixel(palette[i], 0, destination, destinationIndex);
                destinationIndex += (Bpp >> 3);
            }

            return destination;
        }
    }
    #endregion

    #region Data Codec
    // Base codec for the data codecs
    public abstract class VrDataCodec
    {
        // The pixel codec to use for this data codec.
        public VrPixelCodec PixelCodec;

        // Returns if we can encode using this codec.
        public abstract bool CanEncode { get; }

        // Returns the bits per pixel for this data format.
        public abstract int Bpp { get; }

        // Returns the number of palette entries for this data format.
        // Returns -1 if this is not a palettized data format.
        public virtual int PaletteEntries
        {
            get { return 0; }
        }

        // Returns if an external palette file is necessary for the texture.
        public virtual bool NeedsExternalPalette
        {
            get { return false; }
        }

        // Returns if the texture has mipmaps.
        public virtual bool HasMipmaps
        {
            get { return false; }
        }

        // Palette
        protected byte[][] palette;
        public void SetPalette(byte[] palette, int offset, int numEntries)
        {
            this.palette = PixelCodec.DecodePalette(palette, offset, numEntries);
        }

        // Decode & Encode texture data
        public virtual byte[] Decode(byte[] source, int sourceIndex, int width, int height)
        {
            return Decode(source, sourceIndex, width, height, null);
        }
        public virtual byte[] Encode(byte[] source, int sourceIndex, int width, int height)
        {
            return Encode(source, width, height, null);
        }

        // Decode texture data
        public virtual byte[] Decode(byte[] input, int offset, int width, int height, VrPixelCodec PixelCodec)
        {
            return Decode(input, offset, width, height);
        }
        // Decode a mipmap in the texture data
        //public virtual byte[] DecodeMipmap(byte[] input, int offset, int mipmap, int width, int height, VrPixelCodec PixelCodec)
        //{
        //    return Decode(input, offset, width, height, PixelCodec);
        //}
        // Encode texture data
        public virtual byte[] Encode(byte[] input, int width, int height, VrPixelCodec PixelCodec)
        {
            return Encode(input, 0, width, height);
        }
    }
    #endregion
}
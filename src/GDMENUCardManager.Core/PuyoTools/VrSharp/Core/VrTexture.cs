using System;
using System.Drawing;
//using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace VrSharp
{
    public abstract class VrTexture
    {
        #region Fields
        protected bool initalized = false; // Is the texture initalized?
        protected bool canDecode = false; // Can we decode this texture?

        protected byte[] encodedData; // Encoded texture data (VR data)

        protected VrPixelCodec pixelCodec; // Pixel codec
        protected VrDataCodec dataCodec;   // Data codec

        protected int paletteEntries; // Number of palette entries in the palette data
        protected int paletteOffset;  // Offset of the palette data in the texture (-1 if there is none)
        protected int dataOffset;     // Offset of the actual data in the texture

        protected int[] mipmapOffsets; // Mipmap offsets
        #endregion

        #region Texture Properties
        /// <summary>
        /// Returns if this texture has a global index.
        /// </summary>
        public bool HasGlobalIndex
        {
            get
            {
                if (!initalized)
                {
                    throw new TextureNotInitalizedException("Cannot access this property as the texture is not initalized.");
                }

                return gbixOffset != -1;
            }
        }

        /// <summary>
        /// The texture's global index, or 0 if this texture does not have a global index defined.
        /// </summary>
        public uint GlobalIndex
        {
            get
            {
                if (!initalized)
                {
                    throw new TextureNotInitalizedException("Cannot access this property as the texture is not initalized.");
                }

                return globalIndex;
            }
        }
        protected uint globalIndex;

        /// <summary>
        /// Width of the texture (in pixels).
        /// </summary>
        public ushort TextureWidth
        {
            get
            {
                if (!initalized)
                {
                    throw new TextureNotInitalizedException("Cannot access this property as the texture is not initalized.");
                }

                return textureWidth;
            }
        }
        protected ushort textureWidth;

        /// <summary>
        /// Height of the texture (in pixels).
        /// </summary>
        public ushort TextureHeight
        {
            get
            {
                if (!initalized)
                {
                    throw new TextureNotInitalizedException("Cannot access this property as the texture is not initalized.");
                }

                return textureHeight;
            }
        }
        protected ushort textureHeight;

        /// <summary>
        /// Offset of the GBIX (or GCIX) chunk in the texture file, or -1 if this chunk is not present.
        /// </summary>
        public int GbixOffset
        {
            get
            {
                if (!initalized)
                {
                    throw new TextureNotInitalizedException("Cannot access this property as the texture is not initalized.");
                }

                return gbixOffset;
            }
        }
        protected int gbixOffset;

        /// <summary>
        /// Offset of the PVRT (or GVRT) chunk in the texture file.
        /// </summary>
        public int PvrtOffset
        {
            get
            {
                if (!initalized)
                {
                    throw new TextureNotInitalizedException("Cannot access this property as the texture is not initalized.");
                }

                return pvrtOffset;
            }
        }
        protected int pvrtOffset;
        #endregion

        #region Constructors & Initalizers
        // Open a texture from a file.
        public VrTexture(string file)
        {
            encodedData = File.ReadAllBytes(file);

            if (encodedData != null)
            {
                Initalize();
            }
        }

        // Open a texture from a byte array.
        public VrTexture(byte[] source) : this(source, 0, source.Length) { }

        public VrTexture(byte[] source, int offset, int length)
        {
            if (source == null || (offset == 0 && source.Length == length))
            {
                encodedData = source;
            }
            else if (source != null)
            {
                encodedData = new byte[length];
                Array.Copy(source, offset, encodedData, 0, length);
            }

            if (encodedData != null)
            {
                Initalize();
            }
        }

        // Open a texture from a stream.
        public VrTexture(Stream source) : this(source, (int)(source.Length - source.Position)) { }

        public VrTexture(Stream source, int length)
        {
            encodedData = new byte[length];
            source.Read(encodedData, 0, length);

            if (encodedData != null)
            {
                Initalize();
            }
        }

        protected abstract void Initalize();

        /// <summary>
        /// Returns if the texture was loaded successfully.
        /// </summary>
        /// <returns></returns>
        public bool Initalized
        {
            get { return initalized; }
        }

        /// <summary>
        /// Returns if the texture can be decoded.
        /// </summary>
        public bool CanDecode
        {
            get { return canDecode; }
        }
        #endregion

        #region Texture Retrieval
        /// <summary>
        /// Returns the decoded texture as an array containg raw 32-bit ARGB data.
        /// </summary>
        /// <returns></returns>
        public byte[] ToArray()
        {
            if (!initalized)
            {
                throw new TextureNotInitalizedException("Cannot decode this texture as it is not initalized.");
            }

            return DecodeTexture();
        }

        /// <summary>
        /// Returns the decoded texture as a bitmap.
        /// </summary>
        /// <returns></returns>
        //public Bitmap ToBitmap()
        //{
        //    if (!initalized)
        //    {
        //        throw new TextureNotInitalizedException("Cannot decode this texture as it is not initalized.");
        //    }

        //    byte[] data = DecodeTexture();

        //    Bitmap img = new Bitmap(textureWidth, textureHeight, PixelFormat.Format32bppArgb);
        //    BitmapData bitmapData = img.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.WriteOnly, img.PixelFormat);
        //    Marshal.Copy(data, 0, bitmapData.Scan0, data.Length);
        //    img.UnlockBits(bitmapData);

        //    return img;
        //}

        /// <summary>
        /// Returns the decoded texture as a stream containg a PNG.
        /// </summary>
        /// <returns></returns>
        //public MemoryStream ToStream()
        //{
        //    if (!initalized)
        //    {
        //        throw new TextureNotInitalizedException("Cannot decode this texture as it is not initalized.");
        //    }

        //    MemoryStream destination = new MemoryStream();
        //    ToBitmap().Save(destination, ImageFormat.Png);
        //    destination.Position = 0;

        //    return destination;
        //}

        /// <summary>
        /// Saves the decoded texture to the specified file.
        /// </summary>
        /// <param name="file">Name of the file to save the data to.</param>
        //public void Save(string file)
        //{
        //    if (!initalized)
        //    {
        //        throw new TextureNotInitalizedException("Cannot decode this texture as it is not initalized.");
        //    }

        //    ToBitmap().Save(file, ImageFormat.Png);
        //}

        /// <summary>
        /// Saves the decoded texture to the specified stream.
        /// </summary>
        /// <param name="destination">The stream to save the texture to.</param>
        public void Save(Stream destination)
        {
            if (!initalized)
            {
                throw new TextureNotInitalizedException("Cannot decode this texture as it is not initalized.");
            }

            //ToBitmap().Save(destination, ImageFormat.Png);

            byte[] data = DecodeTexture();

            //Bitmap img = new Bitmap(textureWidth, textureHeight, PixelFormat.Format32bppArgb);
            //BitmapData bitmapData = img.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.WriteOnly, img.PixelFormat);
            //Marshal.Copy(data, 0, bitmapData.Scan0, data.Length);
            //img.UnlockBits(bitmapData);

        }

        // Decodes a texture
        internal byte[] DecodeTexture()
        {
            // Make sure we can decode this texture
            if (!canDecode)
            {
                throw new CannotDecodeTextureException("Cannot decode texture. The pixel format and/or data format may not be supported.");
            }

            if (paletteOffset != -1) // The texture contains an embedded palette
            {
                dataCodec.SetPalette(encodedData, paletteOffset, paletteEntries);
            }

            if (HasMipmaps)
            {
                return dataCodec.Decode(encodedData, dataOffset + mipmapOffsets[0], textureWidth, textureHeight, pixelCodec);
            }

            return dataCodec.Decode(encodedData, dataOffset, textureWidth, textureHeight, pixelCodec);
        }
        #endregion

        #region Mipmaps & Mipmap Retrieval
        /// <summary>
        /// Returns if the texture has mipmaps.
        /// </summary>
        public virtual bool HasMipmaps
        {
            get
            {
                if (!initalized)
                {
                    throw new TextureNotInitalizedException("Cannot access this property as the texture is not initalized.");
                }

                return dataCodec.HasMipmaps;
            }
        }

        /// <summary>
        /// Returns the mipmaps of a texture as an array of byte arrays. The first index will contain the largest, original sized texture and the last index will contain the smallest texture.
        /// </summary>
        /// <returns></returns>
        public byte[][] MipmapsToArray()
        {
            if (!initalized)
            {
                throw new TextureNotInitalizedException("Cannot decode this texture as it is not initalized.");
            }

            // If this texture does not contain mipmaps, just return the texture
            if (!HasMipmaps)
            {
                return new byte[][] { ToArray() };
            }

            return DecodeMipmaps();
        }

        /// <summary>
        /// Returns the mipmaps of a texture as an array of bitmaps. The first index will contain the largest, original sized texture and the last index will contain the smallest texture.
        /// </summary>
        /// <returns></returns>
        //public Bitmap[] MipmapsToBitmap()
        //{
        //    if (!initalized)
        //    {
        //        throw new TextureNotInitalizedException("Cannot decode this texture as it is not initalized.");
        //    }

        //    // If this texture does not contain mipmaps, just return the texture
        //    if (!HasMipmaps)
        //    {
        //        return new Bitmap[] { ToBitmap() };
        //    }

        //    byte[][] data = DecodeMipmaps();

        //    Bitmap[] img = new Bitmap[data.Length];
        //    for (int i = 0, size = textureWidth; i < img.Length; i++, size >>= 1)
        //    {
        //        img[i] = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        //        BitmapData bitmapData = img[i].LockBits(new Rectangle(0, 0, img[i].Width, img[i].Height), ImageLockMode.WriteOnly, img[i].PixelFormat);
        //        Marshal.Copy(data[i], 0, bitmapData.Scan0, data[i].Length);
        //        img[i].UnlockBits(bitmapData);
        //    }

        //    return img;
        //}

        /// <summary>
        /// Returns the mipmaps of a texture as an array of streams. The first index will contain the largest, original sized texture and the last index will contain the smallest texture.
        /// </summary>
        /// <returns></returns>
        //public MemoryStream[] MipmapsToStream()
        //{
        //    if (!initalized)
        //    {
        //        throw new TextureNotInitalizedException("Cannot decode this texture as it is not initalized.");
        //    }

        //    // If this texture does not contain mipmaps, just return the texture
        //    if (!HasMipmaps)
        //    {
        //        return new MemoryStream[] { ToStream() };
        //    }

        //    Bitmap[] img = MipmapsToBitmap();

        //    MemoryStream[] destination = new MemoryStream[img.Length];
        //    for (int i = 0; i < img.Length; i++)
        //    {
        //        img[i].Save(destination[i], ImageFormat.Png);
        //        destination[i].Position = 0;
        //    }

        //    return destination;
        //}

        // Decodes mipmaps
        private byte[][] DecodeMipmaps()
        {
            // Make sure we can decode this texture
            if (!canDecode)
            {
                throw new CannotDecodeTextureException("Cannot decode texture. The pixel format and/or data format may not be supported.");
            }

            if (paletteOffset != -1) // The texture contains an embedded palette
            {
                dataCodec.SetPalette(encodedData, paletteOffset, paletteEntries);
            }

            byte[][] mipmaps = new byte[mipmapOffsets.Length][];
            for (int i = 0, size = textureWidth; i < mipmaps.Length; i++, size >>= 1)
            {
                mipmaps[i] = dataCodec.Decode(encodedData, dataOffset + mipmapOffsets[i], size, size, pixelCodec);
            }

            return mipmaps;
        }
        #endregion

        #region Palette
        /// <summary>
        /// Returns if the texture needs an external palette file.
        /// </summary>
        /// <returns></returns>
        public virtual bool NeedsExternalPalette
        {
            get
            {
                if (!initalized)
                {
                    throw new TextureNotInitalizedException("Cannot access this property as the texture is not initalized.");
                }

                return dataCodec.NeedsExternalPalette;
            }
        }
        #endregion
    }
}
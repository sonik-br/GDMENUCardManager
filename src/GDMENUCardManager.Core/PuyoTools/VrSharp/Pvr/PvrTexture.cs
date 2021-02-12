using System;
using System.IO;

namespace VrSharp.Pvr
{
    public class PvrTexture : VrTexture
    {
        #region Fields
        private PvrCompressionCodec compressionCodec; // Compression Codec
        // Perhaps these could be moved to VrTexture?
        // Size of the entire GBIX header in bytes.
        private const int gbixSizeInBytes = 12;
        // FourCC for GBIX headers.
        private static readonly byte[] gbixFourCC = { (byte)'G', (byte)'B', (byte)'I', (byte)'X' };
        // FourCC for PVRT headers.
        private static readonly byte[] pvrtFourCC = { (byte)'P', (byte)'V', (byte)'R', (byte)'T' };
        #endregion

        #region Texture Properties
        /// <summary>
        /// The texture's pixel format.
        /// </summary>
        public PvrPixelFormat PixelFormat
        {
            get
            {
                if (!initalized)
                {
                    throw new TextureNotInitalizedException("Cannot access this property as the texture is not initalized.");
                }

                return pixelFormat;
            }
        }
        private PvrPixelFormat pixelFormat;

        /// <summary>
        /// The texture's data format.
        /// </summary>
        public PvrDataFormat DataFormat
        {
            get
            {
                if (!initalized)
                {
                    throw new TextureNotInitalizedException("Cannot access this property as the texture is not initalized.");
                }

                return dataFormat;
            }
        }
        private PvrDataFormat dataFormat;

        /// <summary>
        /// The texture's compression format (if it is compressed).
        /// </summary>
        public PvrCompressionFormat CompressionFormat
        {
            get
            {
                if (!initalized)
                {
                    throw new TextureNotInitalizedException("Cannot access this property as the texture is not initalized.");
                }

                return compressionFormat;
            }
        }
        private PvrCompressionFormat compressionFormat;
        #endregion

        #region Constructors & Initalizers
        /// <summary>
        /// Open a PVR texture from a file.
        /// </summary>
        /// <param name="file">Filename of the file that contains the texture data.</param>
        public PvrTexture(string file) : base(file) { }

        /// <summary>
        /// Open a PVR texture from a byte array.
        /// </summary>
        /// <param name="source">Byte array that contains the texture data.</param>
        public PvrTexture(byte[] source) : base(source) { }

        /// <summary>
        /// Open a PVR texture from a byte array.
        /// </summary>
        /// <param name="source">Byte array that contains the texture data.</param>
        /// <param name="offset">Offset of the texture in the array.</param>
        /// <param name="length">Number of bytes to read.</param>
        public PvrTexture(byte[] source, int offset, int length) : base(source, offset, length) { }

        /// <summary>
        /// Open a PVR texture from a stream.
        /// </summary>
        /// <param name="source">Stream that contains the texture data.</param>
        public PvrTexture(Stream source) : base(source) { }

        /// <summary>
        /// Open a PVR texture from a stream.
        /// </summary>
        /// <param name="source">Stream that contains the texture data.</param>
        /// <param name="length">Number of bytes to read.</param>
        public PvrTexture(Stream source, int length) : base(source, length) { }

        protected override void Initalize()
        {
            // Check to see if what we are dealing with is a PVR texture
            if (!Is(encodedData))
            {
                throw new NotAValidTextureException("This is not a valid PVR texture.");
            }

            // Determine the offsets of the GBIX (if present) and PVRT header chunks.
            if (PTMethods.Contains(encodedData, 0x00, gbixFourCC))
            {
                gbixOffset = 0x00;
                pvrtOffset = 0x08 + BitConverter.ToInt32(encodedData, gbixOffset + 4);
            }
            else if (PTMethods.Contains(encodedData, 0x04, gbixFourCC))
            {
                gbixOffset = 0x04;
                pvrtOffset = 0x0C + BitConverter.ToInt32(encodedData, gbixOffset + 4);
            }
            else if (PTMethods.Contains(encodedData, 0x04, pvrtFourCC))
            {
                gbixOffset = -1;
                pvrtOffset = 0x04;
            }
            else
            {
                gbixOffset = -1;
                pvrtOffset = 0x00;
            }

            // Read the global index (if it is present). If it is not present, just set it to 0.
            if (gbixOffset != -1)
            {
                globalIndex = BitConverter.ToUInt32(encodedData, gbixOffset + 0x08);
            }
            else
            {
                globalIndex = 0;
            }

            // Read information about the texture
            textureWidth  = BitConverter.ToUInt16(encodedData, pvrtOffset + 0x0C);
            textureHeight = BitConverter.ToUInt16(encodedData, pvrtOffset + 0x0E);

            pixelFormat = (PvrPixelFormat)encodedData[pvrtOffset + 0x08];
            dataFormat  = (PvrDataFormat)encodedData[pvrtOffset + 0x09];

            // Get the codecs and make sure we can decode using them
            pixelCodec = PvrPixelCodec.GetPixelCodec(pixelFormat);
            dataCodec = PvrDataCodec.GetDataCodec(dataFormat);

            if (dataCodec != null && pixelCodec != null)
            {
                dataCodec.PixelCodec = pixelCodec;
                canDecode = true;
            }

            // Set the number of palette entries
            // The number in a Small Vq encoded texture various based on its size
            paletteEntries = dataCodec.PaletteEntries;
            if (dataFormat == PvrDataFormat.SmallVq)
            {
                if (textureWidth <= 16)
                {
                    paletteEntries = 64; // Actually 16
                }
                else if (textureWidth <= 32)
                {
                    paletteEntries = 128; // Actually 32
                }
                else if (textureWidth <= 64)
                {
                    paletteEntries = 512; // Actually 128
                }
                else
                {
                    paletteEntries = 1024; // Actually 256
                }
            }
            else if (dataFormat == PvrDataFormat.SmallVqMipmaps)
            {
                if (textureWidth <= 16)
                {
                    paletteEntries = 64; // Actually 16
                }
                else if (textureWidth <= 32)
                {
                    paletteEntries = 256; // Actually 64
                }
                else
                {
                    paletteEntries = 1024; // Actually 256
                }
            }

            // Set the palette and data offsets
            if (!canDecode || paletteEntries == 0 || dataCodec.NeedsExternalPalette)
            {
                paletteOffset = -1;
                dataOffset = pvrtOffset + 0x10;
            }
            else
            {
                paletteOffset = pvrtOffset + 0x10;
                dataOffset = paletteOffset + (paletteEntries * (pixelCodec.Bpp >> 3));
            }

            // Get the compression format and determine if we need to decompress this texture
            compressionFormat = GetCompressionFormat(encodedData, pvrtOffset, dataOffset);
            compressionCodec = PvrCompressionCodec.GetCompressionCodec(compressionFormat);

            if (compressionFormat != PvrCompressionFormat.None && compressionCodec != null)
            {
                encodedData = compressionCodec.Decompress(encodedData, dataOffset, pixelCodec, dataCodec);

                // Now place the offsets in the appropiate area
                if (compressionFormat == PvrCompressionFormat.Rle)
                {
                    if (gbixOffset != -1) gbixOffset -= 4;
                    pvrtOffset -= 4;
                    if (paletteOffset != -1) paletteOffset -= 4;
                    dataOffset -= 4;
                }
            }

            // If the texture contains mipmaps, gets the offsets of them
            if (canDecode && dataCodec.HasMipmaps)
            {
                mipmapOffsets = new int[(int)Math.Log(textureWidth, 2) + 1];

                int mipmapOffset = 0;
                
                // Calculate the padding for the first mipmap offset
                if (dataFormat == PvrDataFormat.SquareTwiddledMipmaps)
                {
                    // A 1x1 mipmap takes up as much space as a 2x1 mipmap
                    mipmapOffset = (dataCodec.Bpp) >> 3;
                }
                else if (dataFormat == PvrDataFormat.SquareTwiddledMipmapsAlt)
                {
                    // A 1x1 mipmap takes up as much space as a 2x2 mipmap
                    mipmapOffset = (3 * dataCodec.Bpp) >> 3;
                }

                for (int i = mipmapOffsets.Length - 1, size = 1; i >= 0; i--, size <<= 1)
                {
                    mipmapOffsets[i] = mipmapOffset;

                    mipmapOffset += Math.Max((size * size * dataCodec.Bpp) >> 3, 1);
                }
            }

            initalized = true;
        }
        #endregion

        #region Compression Format
        // Gets the compression format used on the PVR
        private PvrCompressionFormat GetCompressionFormat(byte[] data, int pvrtOffset, int dataOffset)
        {
            // RLE compression
            if (BitConverter.ToUInt32(data, 0x00) == BitConverter.ToUInt32(data, pvrtOffset + 4) - pvrtOffset + dataOffset + 8)
                return PvrCompressionFormat.Rle;

            return PvrCompressionFormat.None;
        }
        #endregion

        #region Texture Check
        /// <summary>
        /// Checks for the PVRT header and validates it.
        /// <para>See also: <seealso cref="IsValidGbix"/></para>
        /// </summary>
        /// <param name="source">Byte array containing the data.</param>
        /// <param name="offset">The offset in the byte array to start at.</param>
        /// <param name="length">The expected length of the PVR data minus the preceding header sizes.</param>
        /// <returns>True if the header is PVRT and it passes validation, false otherwise.</returns>
        private static bool IsValidPvrt(byte[] source, int offset, int length)
        {
            return PTMethods.Contains(source, offset, pvrtFourCC)
                && source[offset + 0x09] < 0x60
                && BitConverter.ToUInt32(source, offset + 0x04) == length - 8;
        }

        /// <summary>
        /// Checks for and validates GBIX headers as well as PVRT.
        /// <para>See also: <seealso cref="IsValidPvrt"/></para>
        /// </summary>
        /// <param name="source">Byte array containing the data.</param>
        /// <param name="offset">The offset in the byte array to start at.</param>
        /// <param name="length">The expected length of the data minus the preceding header sizes.</param>
        /// <returns>True if the header is GBIX and it passes validation, false otherwise.</returns>
        private static bool IsValidGbix(byte[] source, int offset, int length)
        {
            if (!PTMethods.Contains(source, offset, gbixFourCC))
            {
                return false;
            }

            // Immediately after the "GBIX" part of the GBIX header, there is
            // an offset indicating where the PVRT header begins relative to 0x08.
            int pvrtOffset = BitConverter.ToInt32(source, offset + 0x04) + 8;
            return IsValidPvrt(source, offset + pvrtOffset, length - pvrtOffset);
        }

        /// <summary>
        /// Determines if this is a PVR texture.
        /// </summary>
        /// <param name="source">Byte array containing the data.</param>
        /// <param name="offset">The offset in the byte array to start at.</param>
        /// <param name="length">Length of the data (in bytes).</param>
        /// <returns>True if this is a PVR texture, false otherwise.</returns>
        public static bool Is(byte[] source, int offset, int length)
        {
            // GBIX and PVRT
            if (length >= 32 && IsValidGbix(source, offset, length))
            {
                return true;
            }

            // PVRT (and no GBIX chunk)
            if (length >= 16 && IsValidPvrt(source, offset, length))
            {
                return true;
            }

            // GBIX and PVRT with RLE compression
            if (length >= 36 && IsValidGbix(source, offset + 0x04, BitConverter.ToInt32(source, offset + 0x00)))
            {
                return true;
            }

            // PVRT (and no GBIX chunk) with RLE compression 
            if (length >= 20 && IsValidPvrt(source, offset + 0x04, BitConverter.ToInt32(source, offset + 0x00)))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines if this is a PVR texture.
        /// </summary>
        /// <param name="source">Byte array containing the data.</param>
        /// <returns>True if this is a PVR texture, false otherwise.</returns>
        public static bool Is(byte[] source)
        {
            return Is(source, 0, source.Length);
        }

        /// <summary>
        /// Determines if this is a PVR texture.
        /// </summary>
        /// <param name="source">The stream to read from. The stream position is not changed.</param>
        /// <param name="length">Number of bytes to read.</param>
        /// <returns>True if this is a PVR texture, false otherwise.</returns>
        public static bool Is(Stream source, int length)
        {
            // If the length is < 16, then there is no way this is a valid texture.
            if (length < 16)
            {
                return false;
            }

            // Let's see if we should check 16 bytes, 32 bytes, or 36 bytes
            int amountToRead = 0;
            if (length < 32)
            {
                amountToRead = 16;
            }
            else if (length < 36)
            {
                amountToRead = 32;
            }
            else
            {
                amountToRead = 36;
            }

            byte[] buffer = new byte[amountToRead];
            source.Read(buffer, 0, amountToRead);
            source.Position -= amountToRead;

            return Is(buffer, 0, length);
        }

        /// <summary>
        /// Determines if this is a PVR texture.
        /// </summary>
        /// <param name="source">The stream to read from. The stream position is not changed.</param>
        /// <returns>True if this is a PVR texture, false otherwise.</returns>
        public static bool Is(Stream source)
        {
            return Is(source, (int)(source.Length - source.Position));
        }

        /// <summary>
        /// Determines if this is a PVR texture.
        /// </summary>
        /// <param name="file">Filename of the file that contains the data.</param>
        /// <returns>True if this is a PVR texture, false otherwise.</returns>
        public static bool Is(string file)
        {
            using (FileStream stream = File.OpenRead(file))
            {
                return Is(stream);
            }
        }
        #endregion
    }
}
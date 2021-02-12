using System;
using VrSharpPvrTexture = VrSharp.Pvr.PvrTexture;

namespace PuyoTools
{
    public class PvrTexture// : TextureBase
    {
        public Tuple<byte[], int, int> GetDecoded(byte[] source)
        {
            // Reading PVR textures is done through VrSharp, so just pass it to that
            VrSharpPvrTexture texture = new VrSharpPvrTexture(source);
            
            // Check to see if this texture requires an external palette and throw an exception
            if (texture.NeedsExternalPalette)
                throw new Exception("Can't load. Texture needs palette.");
            
            return new Tuple<byte[], int, int>(texture.DecodeTexture(), texture.TextureWidth, texture.TextureHeight);
        }
    }
}

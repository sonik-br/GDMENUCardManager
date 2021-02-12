using System;

namespace VrSharp
{
    public class TextureNotInitalizedException : Exception
    {
        public TextureNotInitalizedException() { }

        public TextureNotInitalizedException(string message) : base(message) { }
    }

    public class NotAValidTextureException : Exception
    {
        public NotAValidTextureException() { }

        public NotAValidTextureException(string message) : base(message) { }
    }

    public class CannotDecodeTextureException : Exception
    {
        public CannotDecodeTextureException() { }

        public CannotDecodeTextureException(string message) : base(message) { }
    }
}
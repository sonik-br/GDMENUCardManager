namespace SevenZip
{
    using System;
    using System.Runtime.Serialization;

    /// <summary>
    /// Exception class for LZMA operations.
    /// </summary>
    [Serializable]
    public class LzmaException : SevenZipException
    {
        /// <summary>
        /// Exception dafault message which is displayed if no extra information is specified
        /// </summary>
        public const string DEFAULT_MESSAGE = "Specified stream is not a valid LZMA compressed stream!";

        /// <summary>
        /// Initializes a new instance of the LzmaException class
        /// </summary>
        public LzmaException() : base(DEFAULT_MESSAGE) { }

    }
}

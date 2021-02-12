namespace SevenZip
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Diagnostics;
    using System.Security.Permissions;
    using System.IO;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Text;

#if UNMANAGED
    /// <summary>
    /// 7-zip library low-level wrapper.
    /// </summary>
    internal static class SevenZipLibraryManager
    {
        /// <summary>
        /// Synchronization root for all locking.
        /// </summary>
        private static readonly object _syncRoot = new object();

        /// <summary>
        /// Path to the 7-zip dll.
        /// </summary>
        /// <remarks>7zxa.dll supports only decoding from .7z archives.
        /// Features of 7za.dll: 
        ///     - Supporting 7z format;
        ///     - Built encoders: LZMA, PPMD, BCJ, BCJ2, COPY, AES-256 Encryption.
        ///     - Built decoders: LZMA, PPMD, BCJ, BCJ2, COPY, AES-256 Encryption, BZip2, Deflate.
        /// 7z.dll (from the 7-zip distribution) supports every InArchiveFormat for encoding and decoding.
        /// </remarks>
        private static string _libraryFileName;

        private static string DetermineLibraryFilePath()
        {
            if (string.IsNullOrEmpty(Assembly.GetExecutingAssembly().Location)) 
            {
                return null;
            }

            return Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), Environment.Is64BitProcess ? "7z64.dll" : "7z.dll");
        }

        /// <summary>
        /// 7-zip library handle.
        /// </summary>
        private static IntPtr _modulePtr;

        /// <summary>
        /// 7-zip library features.
        /// </summary>
        //private static LibraryFeature? _features;

        private static Dictionary<object, Dictionary<InArchiveFormat, IInArchive>> _inArchives;

        private static Dictionary<object, Dictionary<OutArchiveFormat, IOutArchive>> _outArchives;

        private static int _totalUsers;

        // private static string _LibraryVersion;
        private static bool? _modifyCapable;

        private static void InitUserInFormat(object user, InArchiveFormat format)
        {
            if (!_inArchives.ContainsKey(user))
            {
                _inArchives.Add(user, new Dictionary<InArchiveFormat, IInArchive>());
            }
            if (!_inArchives[user].ContainsKey(format))
            {
                _inArchives[user].Add(format, null);
                _totalUsers++;
            }
        }

        private static void InitUserOutFormat(object user, OutArchiveFormat format)
        {
            if (!_outArchives.ContainsKey(user))
            {
                _outArchives.Add(user, new Dictionary<OutArchiveFormat, IOutArchive>());
            }
            if (!_outArchives[user].ContainsKey(format))
            {
                _outArchives[user].Add(format, null);
                _totalUsers++;
            }
        }

        private static void Init()
        {
            _inArchives = new Dictionary<object, Dictionary<InArchiveFormat, IInArchive>>();
            _outArchives = new Dictionary<object, Dictionary<OutArchiveFormat, IOutArchive>>();
        }

        /// <summary>
        /// Loads the 7-zip library if necessary and adds user to the reference list
        /// </summary>
        /// <param name="user">Caller of the function</param>
        /// <param name="format">Archive format</param>
        public static void LoadLibrary(object user, Enum format)
        {
            lock (_syncRoot)
            {
                if (_inArchives == null || _outArchives == null)
                {
                    Init();
                }

                if (_modulePtr == IntPtr.Zero)
                {
                    if (_libraryFileName == null)
                    {
                        _libraryFileName = DetermineLibraryFilePath();
                    }

                    if (!File.Exists(_libraryFileName))
                    {
                        throw new SevenZipLibraryException("DLL file does not exist.");
                    }
                    if ((_modulePtr = NativeMethods.LoadLibrary(_libraryFileName)) == IntPtr.Zero)
                    {
                        throw new SevenZipLibraryException($"failed to load library from \"{_libraryFileName}\".");
                    }
                    if (NativeMethods.GetProcAddress(_modulePtr, "GetHandlerProperty") == IntPtr.Zero)
                    {
                        NativeMethods.FreeLibrary(_modulePtr);
                        throw new SevenZipLibraryException("library is invalid.");
                    }
                }

                if (format is InArchiveFormat archiveFormat)
                {
                    InitUserInFormat(user, archiveFormat);
                    return;
                }

                if (format is OutArchiveFormat outArchiveFormat)
                {
                    InitUserOutFormat(user, outArchiveFormat);
                    return;
                }

                throw new ArgumentException(
                    "Enum " + format + " is not a valid archive format attribute!");
            }
        }

        /// <summary>
        /// Gets the value indicating whether the library supports modifying archives.
        /// </summary>
        public static bool ModifyCapable
        {
            get
            {
                lock (_syncRoot)
                {
                    if (!_modifyCapable.HasValue)
                    {
                        if (_libraryFileName == null)
                        {
                            _libraryFileName = DetermineLibraryFilePath();
                        }

                        FileVersionInfo dllVersionInfo = FileVersionInfo.GetVersionInfo(_libraryFileName);
                        _modifyCapable = dllVersionInfo.FileMajorPart >= 9;
                    }
                    return _modifyCapable.Value;
                }
            }
        }

        static readonly string Namespace = Assembly.GetExecutingAssembly().GetManifestResourceNames()[0].Split('.')[0];


        /// <summary>
        /// Removes user from reference list and frees the 7-zip library if it becomes empty
        /// </summary>
        /// <param name="user">Caller of the function</param>
        /// <param name="format">Archive format</param>
        public static void FreeLibrary(object user, Enum format)
        {
            var sp = new SecurityPermission(SecurityPermissionFlag.UnmanagedCode);
            sp.Demand();

            lock (_syncRoot)
			{
                if (_modulePtr != IntPtr.Zero)
            {
                if (format is InArchiveFormat archiveFormat)
                {
                    if (_inArchives != null && _inArchives.ContainsKey(user) &&
                        _inArchives[user].ContainsKey(archiveFormat) &&
                        _inArchives[user][archiveFormat] != null)
                    {
                        try
                        {                            
                            Marshal.ReleaseComObject(_inArchives[user][archiveFormat]);
                        }
                        catch (InvalidComObjectException) {}
                        _inArchives[user].Remove(archiveFormat);
                        _totalUsers--;
                        if (_inArchives[user].Count == 0)
                        {
                            _inArchives.Remove(user);
                        }
                    }
                }

                if (format is OutArchiveFormat outArchiveFormat)
                {
                    if (_outArchives != null && _outArchives.ContainsKey(user) &&
                        _outArchives[user].ContainsKey(outArchiveFormat) &&
                        _outArchives[user][outArchiveFormat] != null)
                    {
                        try
                        {
                            Marshal.ReleaseComObject(_outArchives[user][outArchiveFormat]);
                        }
                        catch (InvalidComObjectException) {}
                        _outArchives[user].Remove(outArchiveFormat);
                        _totalUsers--;
                        if (_outArchives[user].Count == 0)
                        {
                            _outArchives.Remove(user);
                        }
                    }
                }

                if ((_inArchives == null || _inArchives.Count == 0) && (_outArchives == null || _outArchives.Count == 0))
                {
                    _inArchives = null;
                    _outArchives = null;

                    if (_totalUsers == 0)
                    {
                        NativeMethods.FreeLibrary(_modulePtr);
                        _modulePtr = IntPtr.Zero;
                    }
                }
            }
			}
        }

        /// <summary>
        /// Gets IInArchive interface to extract 7-zip archives.
        /// </summary>
        /// <param name="format">Archive format.</param>
        /// <param name="user">Archive format user.</param>
        public static IInArchive InArchive(InArchiveFormat format, object user)
        {
            lock (_syncRoot)
            {
                if (_inArchives[user][format] == null)
                {
                    var sp = new SecurityPermission(SecurityPermissionFlag.UnmanagedCode);
                    sp.Demand();

                    if (_modulePtr == IntPtr.Zero)
                    {
                        LoadLibrary(user, format);

                        if (_modulePtr == IntPtr.Zero)
                        {
                            throw new SevenZipLibraryException();
                        }
                    }

                    var createObject = (NativeMethods.CreateObjectDelegate)
                        Marshal.GetDelegateForFunctionPointer(
                            NativeMethods.GetProcAddress(_modulePtr, "CreateObject"),
                            typeof(NativeMethods.CreateObjectDelegate));

                    if (createObject == null)
                    {
                        throw new SevenZipLibraryException();
                    }
                    object result;
                    Guid interfaceId = typeof(IInArchive).GUID;

                    Guid classID = Formats.InFormatGuids[format];

                    try
                    {
                        createObject(ref classID, ref interfaceId, out result);
                    }
                    catch (Exception)
                    {
                        throw new SevenZipLibraryException("Your 7-zip library does not support this archive type.");
                    }

                    InitUserInFormat(user, format);									
                    _inArchives[user][format] = result as IInArchive;
                }

                return _inArchives[user][format];
            }
        }

        /// <summary>
        /// Gets IOutArchive interface to pack 7-zip archives.
        /// </summary>
        /// <param name="format">Archive format.</param>  
        /// <param name="user">Archive format user.</param>
        public static IOutArchive OutArchive(OutArchiveFormat format, object user)
        {
            lock (_syncRoot)
            {
                if (_outArchives[user][format] == null)
                {
                    var sp = new SecurityPermission(SecurityPermissionFlag.UnmanagedCode);
                    sp.Demand();
                    if (_modulePtr == IntPtr.Zero)
                    {
                        throw new SevenZipLibraryException();
                    }
                    var createObject = (NativeMethods.CreateObjectDelegate)
                        Marshal.GetDelegateForFunctionPointer(
                            NativeMethods.GetProcAddress(_modulePtr, "CreateObject"),
                            typeof(NativeMethods.CreateObjectDelegate));
                    object result;
                    Guid interfaceId = typeof(IOutArchive).GUID;

                    Guid classID = Formats.OutFormatGuids[format];

                    try
                    {
                        createObject(ref classID, ref interfaceId, out result);
                    }
                    catch (Exception)
                    {
                        throw new SevenZipLibraryException("Your 7-zip library does not support this archive type.");
                    }

                    InitUserOutFormat(user, format);
                    _outArchives[user][format] = result as IOutArchive;
                }

                return _outArchives[user][format];
            }
        }

        public static void SetLibraryPath(string libraryPath)
        {
            if (_modulePtr != IntPtr.Zero && !Path.GetFullPath(libraryPath).Equals( 
                Path.GetFullPath(_libraryFileName), StringComparison.OrdinalIgnoreCase))
            {
                throw new SevenZipLibraryException(
                    "can not change the library path while the library \"" + _libraryFileName + "\" is being used.");
            }
            if (!File.Exists(libraryPath))
            {
                throw new SevenZipLibraryException(
                    "can not change the library path because the file \"" + libraryPath + "\" does not exist.");
            }
            _libraryFileName = libraryPath;
            //_features = null;
        }
    }
#endif
}

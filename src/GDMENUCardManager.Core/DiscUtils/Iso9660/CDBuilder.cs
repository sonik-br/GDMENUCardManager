//
// Copyright (c) 2008-2011, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

namespace DiscUtils.Iso9660
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Class that creates ISO images.
    /// </summary>
    /// <example>
    /// <code>
    ///   CDBuilder builder = new CDBuilder();
    ///   builder.VolumeIdentifier = "MYISO";
    ///   builder.UseJoliet = true;
    ///   builder.AddFile("Hello.txt", Encoding.ASCII.GetBytes("hello world!"));
    ///   builder.Build(@"C:\TEMP\myiso.iso");
    /// </code>
    /// </example>
    public sealed class CDBuilder : StreamBuilder
    {
        private const long DiskStart = 0x8000;

        private List<BuildFileInfo> _files;
        private List<BuildDirectoryInfo> _dirs;
        private BuildDirectoryInfo _rootDirectory;
        
        private BuildParameters _buildParams;

        /// <summary>
        /// Initializes a new instance of the CDBuilder class.
        /// </summary>
        public CDBuilder()
        {
            _files = new List<BuildFileInfo>();
            _dirs = new List<BuildDirectoryInfo>();
            _rootDirectory = new BuildDirectoryInfo("\0", null);
            _dirs.Add(_rootDirectory);

            _buildParams = new BuildParameters();
            _buildParams.UseJoliet = true;
        }

        /// <summary>
        /// Gets or sets the Volume Identifier for the ISO file.
        /// </summary>
        /// <remarks>
        /// Must be a valid identifier, i.e. max 32 characters in the range A-Z, 0-9 or _.
        /// Lower-case characters are not permitted.
        /// </remarks>
        public string VolumeIdentifier
        {
            get
            {
                return _buildParams.VolumeIdentifier;
            }

            set
            {
                if (value.Length > 32)
                {
                    throw new ArgumentException("Not a valid volume identifier");
                }
                else
                {
                    _buildParams.VolumeIdentifier = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether Joliet file-system extensions should be used.
        /// </summary>
        public bool UseJoliet
        {
            get { return _buildParams.UseJoliet; }
            set { _buildParams.UseJoliet = value; }
        }

        public string SystemIdentifier
        {
            get { return _buildParams.SystemIdentifier; }
            set
            {
                if (value.Length > 32)
                {
                    throw new ArgumentException("Not a valid system identifier");
                }
                else
                {
                    _buildParams.SystemIdentifier = value;
                }
            }
        }

        public string VolumeSetIdentifier
        {
            get { return _buildParams.VolumeSetIdentifier; }
            set
            {
                if (value.Length > 128)
                {
                    throw new ArgumentException("Not a valid volume set identifier");
                }
                else
                {
                    _buildParams.VolumeSetIdentifier = value;
                }
            }
        }
        public string PublisherIdentifier
        {
            get { return _buildParams.PublisherIdentifier; }
            set
            {
                if (value.Length > 128)
                {
                    throw new ArgumentException("Not a valid publisher identifier");
                }
                else
                {
                    _buildParams.PublisherIdentifier = value;
                }
            }
        }
        public string DataPreparerIdentifier
        {
            get { return _buildParams.DataPreparerIdentifier; }
            set
            {
                if (value.Length > 128)
                {
                    throw new ArgumentException("Not a valid data preparer identifier");
                }
                else
                {
                    _buildParams.DataPreparerIdentifier = value;
                }
            }
        }
        public string ApplicationIdentifier
        {
            get { return _buildParams.ApplicationIdentifier; }
            set
            {
                if (value.Length > 128)
                {
                    throw new ArgumentException("Not a valid application identifier");
                }
                else
                {
                    _buildParams.ApplicationIdentifier = value;
                }
            }
        }

        public uint LBAoffset
        {
            get { return _buildParams.LBAoffset; }
            set { _buildParams.LBAoffset = value; }
        }

        public uint LastFileStartSector
        {
            get { return _buildParams.LastFileStartSector; }
            set { _buildParams.LastFileStartSector = value; }
        }

        public uint? EndSector
        {
            get { return _buildParams.EndSector; }
            set { _buildParams.EndSector = value; }
        }
        
        /// <summary>
        /// Adds a directory to the ISO image.
        /// </summary>
        /// <param name="name">The name of the directory on the ISO image.</param>
        /// <returns>The object representing this directory.</returns>
        /// <remarks>
        /// The name is the full path to the directory, for example:
        /// <example><code>
        ///   builder.AddDirectory(@"DIRA\DIRB\DIRC");
        /// </code></example>
        /// </remarks>
        public BuildDirectoryInfo AddDirectory(string name)
        {
            string[] nameElements = name.Split(new char[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            return GetDirectory(nameElements, nameElements.Length, true);
        }

        /// <summary>
        /// Adds a byte array to the ISO image as a file.
        /// </summary>
        /// <param name="name">The name of the file on the ISO image.</param>
        /// <param name="content">The contents of the file.</param>
        /// <returns>The object representing this file.</returns>
        /// <remarks>
        /// The name is the full path to the file, for example:
        /// <example><code>
        ///   builder.AddFile(@"DIRA\DIRB\FILE.TXT;1", new byte[]{0,1,2});
        /// </code></example>
        /// <para>Note the version number at the end of the file name is optional, if not
        /// specified the default of 1 will be used.</para>
        /// </remarks>
        public BuildFileInfo AddFile(string name, byte[] content)
        {
            string[] nameElements = name.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            BuildDirectoryInfo dir = GetDirectory(nameElements, nameElements.Length - 1, true);

            BuildDirectoryMember existing;
            if (dir.TryGetMember(nameElements[nameElements.Length - 1], out existing))
            {
                throw new IOException("File already exists");
            }
            else
            {
                BuildFileInfo fi = new BuildFileInfo(nameElements[nameElements.Length - 1], dir, content);
                _files.Add(fi);
                dir.Add(fi);
                return fi;
            }
        }

        /// <summary>
        /// Adds a disk file to the ISO image as a file.
        /// </summary>
        /// <param name="name">The name of the file on the ISO image.</param>
        /// <param name="sourcePath">The name of the file on disk.</param>
        /// <returns>The object representing this file.</returns>
        /// <remarks>
        /// The name is the full path to the file, for example:
        /// <example><code>
        ///   builder.AddFile(@"DIRA\DIRB\FILE.TXT;1", @"C:\temp\tempfile.bin");
        /// </code></example>
        /// <para>Note the version number at the end of the file name is optional, if not
        /// specified the default of 1 will be used.</para>
        /// </remarks>
        public BuildFileInfo AddFile(string name, string sourcePath)
        {
            string[] nameElements = name.Split(new char[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            BuildDirectoryInfo dir = GetDirectory(nameElements, nameElements.Length - 1, true);

            BuildDirectoryMember existing;
            if (dir.TryGetMember(nameElements[nameElements.Length - 1], out existing))
            {
                throw new IOException("File already exists");
            }
            else
            {
                BuildFileInfo fi = new BuildFileInfo(nameElements[nameElements.Length - 1], dir, sourcePath);
                _files.Add(fi);
                dir.Add(fi);
                return fi;
            }
        }

        /// <summary>
        /// Adds a stream to the ISO image as a file.
        /// </summary>
        /// <param name="name">The name of the file on the ISO image.</param>
        /// <param name="source">The contents of the file.</param>
        /// <returns>The object representing this file.</returns>
        /// <remarks>
        /// The name is the full path to the file, for example:
        /// <example><code>
        ///   builder.AddFile(@"DIRA\DIRB\FILE.TXT;1", stream);
        /// </code></example>
        /// <para>Note the version number at the end of the file name is optional, if not
        /// specified the default of 1 will be used.</para>
        /// </remarks>
        public BuildFileInfo AddFile(string name, Stream source)
        {
            if (!source.CanSeek)
            {
                throw new ArgumentException("source doesn't support seeking", "source");
            }

            string[] nameElements = name.Split(new char[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            BuildDirectoryInfo dir = GetDirectory(nameElements, nameElements.Length - 1, true);

            BuildDirectoryMember existing;
            if (dir.TryGetMember(nameElements[nameElements.Length - 1], out existing))
            {
                throw new IOException("File already exists");
            }
            else
            {
                BuildFileInfo fi = new BuildFileInfo(nameElements[nameElements.Length - 1], dir, source);
                _files.Add(fi);
                dir.Add(fi);
                return fi;
            }
        }

        internal long FirstDataExtent
        {
            get
            {
                if (!_buildParams.UseJoliet)
                {
                    return DiskStart + (2 * IsoUtilities.SectorSize); // Primary, End (fixed at end...)
                }
                return DiskStart + (3 * IsoUtilities.SectorSize); // Primary, Supplementary, End (fixed at end...)
            }
        }

        internal override List<BuilderExtent> FixExtents(out long totalLength)
        {
            List<BuilderExtent> fixedRegions = new List<BuilderExtent>();

            DateTime buildTime = DateTime.UtcNow;

            Encoding suppEncoding = _buildParams.UseJoliet ? Encoding.BigEndianUnicode : Encoding.ASCII;

            Dictionary<BuildDirectoryMember, uint> primaryLocationTable = new Dictionary<BuildDirectoryMember, uint>();
            Dictionary<BuildDirectoryMember, uint> supplementaryLocationTable = new Dictionary<BuildDirectoryMember, uint>();

            // ####################################################################
            // # 1. Fix file locations
            // ####################################################################
            long focus = FirstDataExtent;
            long highestFileLocation = 0;

            // Find end of the file data, fixing the files in place as we go
            foreach (BuildFileInfo fi in _files)
            {
                primaryLocationTable.Add(fi, (uint)(focus / IsoUtilities.SectorSize));
                supplementaryLocationTable.Add(fi, (uint)(focus / IsoUtilities.SectorSize));
                FileExtent extent = new FileExtent(fi, focus);

                // Only remember files of non-zero length (otherwise we'll stomp on a valid file)
                if (extent.Length != 0)
                {
                    fixedRegions.Add(extent);
                }
                highestFileLocation = focus;
                focus += Utilities.RoundUp(extent.Length, IsoUtilities.SectorSize);
            }

            // ####################################################################
            // # 2. Fix directory locations
            // ####################################################################

            // There are two directory tables
            //  1. Primary        (std ISO9660)
            //  2. Supplementary  (Joliet)
            long pushFilesBackAmt = 0;
            int regionIdx = 0;
            long pushAmt = 0;
            //I actually want the path and directory tables before the files, 
            //so lets find out how big it is then move all of the files
            long unfocused = focus;
            focus = FirstDataExtent;
            
            // Find start of the second set of directory data, fixing ASCII directories in place.
            long startOfFirstDirData = focus;
            foreach (BuildDirectoryInfo di in _dirs)
            {
                primaryLocationTable.Add(di, (uint)(focus / IsoUtilities.SectorSize));
                DirectoryExtent extent = new DirectoryExtent(di, primaryLocationTable, Encoding.ASCII, focus);
                fixedRegions.Insert(regionIdx++, extent);
                pushAmt = Utilities.RoundUp(extent.Length, IsoUtilities.SectorSize);
                pushFilesBackAmt += pushAmt;
                focus += pushAmt;
            }

            // Find end of the second directory table, fixing supplementary directories in place.
            long startOfSecondDirData = 0;
            if (_buildParams.UseJoliet)
            {
                startOfSecondDirData = focus;
                foreach (BuildDirectoryInfo di in _dirs)
                {
                    supplementaryLocationTable.Add(di, (uint)(focus / IsoUtilities.SectorSize));
                    DirectoryExtent extent = new DirectoryExtent(di, supplementaryLocationTable, suppEncoding, focus);
                    fixedRegions.Insert(regionIdx++, extent);
                    pushAmt = Utilities.RoundUp(extent.Length, IsoUtilities.SectorSize);
                    pushFilesBackAmt += pushAmt;
                    focus += pushAmt;
                }
            }

            //Push these back now because I'm going to throw the path tables in before them
            PushDataBack(primaryLocationTable, supplementaryLocationTable, fixedRegions, pushFilesBackAmt, regionIdx, false);
            unfocused += pushFilesBackAmt;
            highestFileLocation += pushFilesBackAmt;
            focus = FirstDataExtent;
            pushFilesBackAmt = 0;
            int numDirExtents = regionIdx;
            regionIdx = 0;

            // ####################################################################
            // # 3. Fix path tables
            // ####################################################################

            // There are four path tables:
            //  1. LE, ASCII
            //  2. BE, ASCII
            //  3. LE, Supp Encoding (Joliet)
            //  4. BE, Supp Encoding (Joliet)
            
            // Find end of the path table
            long startOfFirstPathTable = focus;
            PathTable pathTable = new PathTable(false, Encoding.ASCII, _dirs, primaryLocationTable, focus);
            fixedRegions.Insert(regionIdx++, pathTable);
            pushAmt = Utilities.RoundUp(pathTable.Length, IsoUtilities.SectorSize);
            focus += pushAmt;
            pushFilesBackAmt += pushAmt;
            long primaryPathTableLength = pathTable.Length;

            long startOfSecondPathTable = focus;
            pathTable = new PathTable(true, Encoding.ASCII, _dirs, primaryLocationTable, focus);
            fixedRegions.Insert(regionIdx++, pathTable);
            pushAmt = Utilities.RoundUp(pathTable.Length, IsoUtilities.SectorSize);
            focus += pushAmt;
            pushFilesBackAmt += pushAmt;

            long startOfThirdPathTable = 0;
            long startOfFourthPathTable = 0;
            long supplementaryPathTableLength = 0;
            if (_buildParams.UseJoliet)
            {
                startOfThirdPathTable = focus;
                pathTable = new PathTable(false, suppEncoding, _dirs, supplementaryLocationTable, focus);
                fixedRegions.Insert(regionIdx++, pathTable);
                pushAmt = Utilities.RoundUp(pathTable.Length, IsoUtilities.SectorSize);
                focus += pushAmt;
                pushFilesBackAmt += pushAmt;
                supplementaryPathTableLength = pathTable.Length;

                startOfFourthPathTable = 0;
                pathTable = new PathTable(true, suppEncoding, _dirs, supplementaryLocationTable, focus);
                fixedRegions.Insert(regionIdx++, pathTable);
                pushAmt = Utilities.RoundUp(pathTable.Length, IsoUtilities.SectorSize);
                focus += pushAmt;
                pushFilesBackAmt += pushAmt;
                startOfSecondDirData += pushFilesBackAmt;
            }
            startOfFirstDirData += pushFilesBackAmt;
            highestFileLocation += pushFilesBackAmt;

            PushDataBack(primaryLocationTable, supplementaryLocationTable, fixedRegions, pushFilesBackAmt, regionIdx, true);
            unfocused += pushFilesBackAmt;
            pushFilesBackAmt = 0; //Done pushing, unless we want to expand the disc next

            // ####################################################################
            // # 3a. Correct the file locations
            // ####################################################################

            if(_buildParams.LastFileStartSector > 0)
            {
                long highFileSector = highestFileLocation / IsoUtilities.SectorSize;
                if (_buildParams.LastFileStartSector < highFileSector + _buildParams.LBAoffset)
                {
                    throw new Exception("Disc image is too big for GD-ROM, can't build");
                }
                else
                {
                    pushFilesBackAmt = ((_buildParams.LastFileStartSector - highFileSector - _buildParams.LBAoffset) * IsoUtilities.SectorSize);
                }
                PushDataBack(primaryLocationTable, supplementaryLocationTable, fixedRegions, pushFilesBackAmt, regionIdx + numDirExtents, false);
            }
            
            // Find the end of the disk
            totalLength = unfocused;
            if (_buildParams.EndSector.HasValue)
            {
                long desiredLength = (_buildParams.EndSector.Value - _buildParams.LBAoffset) * IsoUtilities.SectorSize;
                if (totalLength < desiredLength)
                {
                    totalLength = desiredLength;
                }
                else
                {
                    throw new Exception("Disc is too big, exceeds the desired end sector.");
                }
            }

            // ####################################################################
            // # 4. Prepare volume descriptors now other structures are fixed
            // ####################################################################
            regionIdx = 0;
            focus = DiskStart;
            PrimaryVolumeDescriptor pvDesc = new PrimaryVolumeDescriptor(
                (uint)(totalLength / IsoUtilities.SectorSize),             // VolumeSpaceSize
                (uint)primaryPathTableLength,                              // PathTableSize
                (uint)(startOfFirstPathTable / IsoUtilities.SectorSize) + _buildParams.LBAoffset,   // TypeLPathTableLocation
                (uint)(startOfSecondPathTable / IsoUtilities.SectorSize) + _buildParams.LBAoffset,  // TypeMPathTableLocation
                (uint)(startOfFirstDirData / IsoUtilities.SectorSize) + _buildParams.LBAoffset,     // RootDirectory.LocationOfExtent
                (uint)_rootDirectory.GetDataSize(Encoding.ASCII),          // RootDirectory.DataLength
                buildTime);
            pvDesc.VolumeIdentifier = _buildParams.VolumeIdentifier;
            pvDesc.SystemIdentifier = _buildParams.SystemIdentifier;
            pvDesc.VolumeSetIdentifier = _buildParams.VolumeSetIdentifier;
            pvDesc.PublisherIdentifier = _buildParams.PublisherIdentifier;
            pvDesc.DataPreparerIdentifier = _buildParams.DataPreparerIdentifier;
            pvDesc.ApplicationIdentifier = _buildParams.ApplicationIdentifier;

            PrimaryVolumeDescriptorRegion pvdr = new PrimaryVolumeDescriptorRegion(pvDesc, focus);
            fixedRegions.Insert(regionIdx++, pvdr);
            focus += IsoUtilities.SectorSize;

            if (_buildParams.UseJoliet)
            {
                //If you're not using Joilet, this is a copy of the regular descriptor and a waste of space to include.
                SupplementaryVolumeDescriptor svDesc = new SupplementaryVolumeDescriptor(
                    (uint)(totalLength / IsoUtilities.SectorSize),             // VolumeSpaceSize
                    (uint)supplementaryPathTableLength,                        // PathTableSize
                    (uint)(startOfThirdPathTable / IsoUtilities.SectorSize) + _buildParams.LBAoffset,   // TypeLPathTableLocation
                    (uint)(startOfFourthPathTable / IsoUtilities.SectorSize) + _buildParams.LBAoffset,  // TypeMPathTableLocation
                    (uint)(startOfSecondDirData / IsoUtilities.SectorSize) + _buildParams.LBAoffset,    // RootDirectory.LocationOfExtent
                    (uint)_rootDirectory.GetDataSize(suppEncoding),            // RootDirectory.DataLength
                    buildTime,
                    suppEncoding);
                svDesc.VolumeIdentifier = _buildParams.VolumeIdentifier;
                SupplementaryVolumeDescriptorRegion svdr = new SupplementaryVolumeDescriptorRegion(svDesc, focus);
                fixedRegions.Insert(regionIdx++, svdr);
                focus += IsoUtilities.SectorSize;
            }

            VolumeDescriptorSetTerminator evDesc = new VolumeDescriptorSetTerminator();
            VolumeDescriptorSetTerminatorRegion evdr = new VolumeDescriptorSetTerminatorRegion(evDesc, focus);
            fixedRegions.Insert(regionIdx++, evdr);

            return fixedRegions;
        }

        /// <summary>
        /// Push all of the data back. Note that LBA should only be applied once, otherwise they'll be wrong!
        /// </summary>
        private void PushDataBack(Dictionary<BuildDirectoryMember, uint> primaryLocationTable,
            Dictionary<BuildDirectoryMember, uint> supplementaryLocationTable, List<BuilderExtent> fixedRegions,
            long pushFilesBackAmt, int firstRegionIdx, bool applyLBA)
        {
            //Now that we just intruded by shoving the file tables in there, let's push back the files
            Dictionary<BuildDirectoryMember, uint> rebuiltPrimary = new Dictionary<BuildDirectoryMember, uint>();
            Dictionary<BuildDirectoryMember, uint> rebuiltSupplementary = new Dictionary<BuildDirectoryMember, uint>();
            foreach (KeyValuePair<BuildDirectoryMember, uint> item in primaryLocationTable)
            {
                if (item.Key is BuildFileInfo || applyLBA) //The directory entries are in here too. :-(
                {
                    rebuiltPrimary.Add(item.Key, (uint)(item.Value + (pushFilesBackAmt / IsoUtilities.SectorSize)) + (applyLBA ? _buildParams.LBAoffset : 0));
                }
                else
                {
                    //Don't modify directories, those already have their moved offsets
                    rebuiltPrimary.Add(item.Key, item.Value + (applyLBA?_buildParams.LBAoffset:0));
                }
            }
            primaryLocationTable.Clear();
            foreach (KeyValuePair<BuildDirectoryMember, uint> item in rebuiltPrimary)
            {
                //Can't just do a simple re-assign, because Directory extents have a reference to it
                primaryLocationTable.Add(item.Key, item.Value);
            }
            foreach (KeyValuePair<BuildDirectoryMember, uint> item in supplementaryLocationTable)
            {
                if (item.Key is BuildFileInfo || applyLBA)
                {
                    rebuiltSupplementary.Add(item.Key, (uint)(item.Value + (pushFilesBackAmt / IsoUtilities.SectorSize)) + (applyLBA ? _buildParams.LBAoffset : 0));
                }
                else
                {
                    rebuiltSupplementary.Add(item.Key, item.Value + (applyLBA ? _buildParams.LBAoffset : 0));
                }
            }
            supplementaryLocationTable.Clear();
            foreach (KeyValuePair<BuildDirectoryMember, uint> item in rebuiltSupplementary)
            {
                //Can't just do a simple re-assign, because Directory extents have a reference to it
                supplementaryLocationTable.Add(item.Key, item.Value);
            }

            for (int i = firstRegionIdx; i < fixedRegions.Count; i++)
            {
                BuilderExtent extent = fixedRegions[i];
                extent.Start += pushFilesBackAmt;
            }
        }

        private BuildDirectoryInfo GetDirectory(string[] path, int pathLength, bool createMissing)
        {
            BuildDirectoryInfo di = TryGetDirectory(path, pathLength, createMissing);

            if (di == null)
            {
                throw new DirectoryNotFoundException("Directory not found");
            }

            return di;
        }

        private BuildDirectoryInfo TryGetDirectory(string[] path, int pathLength, bool createMissing)
        {
            BuildDirectoryInfo focus = _rootDirectory;

            for (int i = 0; i < pathLength; ++i)
            {
                BuildDirectoryMember next;
                if (!focus.TryGetMember(path[i], out next))
                {
                    if (createMissing)
                    {
                        // This directory doesn't exist, create it...
                        BuildDirectoryInfo di = new BuildDirectoryInfo(path[i], focus);
                        focus.Add(di);
                        _dirs.Add(di);
                        focus = di;
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    BuildDirectoryInfo nextAsBuildDirectoryInfo = next as BuildDirectoryInfo;
                    if (nextAsBuildDirectoryInfo == null)
                    {
                        throw new IOException("File with conflicting name exists");
                    }
                    else
                    {
                        focus = nextAsBuildDirectoryInfo;
                    }
                }
            }

            return focus;
        }
    }
}

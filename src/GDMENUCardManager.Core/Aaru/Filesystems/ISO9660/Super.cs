// /***************************************************************************
// Aaru Data Preservation Suite
// ----------------------------------------------------------------------------
//
// Filename       : Super.cs
// Author(s)      : Natalia Portillo <claunia@claunia.com>
//
// Component      : ISO9660 filesystem plugin.
//
// --[ Description ] ----------------------------------------------------------
//
//     Mounts ISO9660, CD-i and High Sierra Format filesystems.
//
// --[ License ] --------------------------------------------------------------
//
//     This library is free software; you can redistribute it and/or modify
//     it under the terms of the GNU Lesser General Public License as
//     published by the Free Software Foundation; either version 2.1 of the
//     License, or (at your option) any later version.
//
//     This library is distributed in the hope that it will be useful, but
//     WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//     Lesser General Public License for more details.
//
//     You should have received a copy of the GNU Lesser General Public
//     License along with this library; if not, see <http://www.gnu.org/licenses/>.
//
// ----------------------------------------------------------------------------
// Copyright © 2011-2021 Natalia Portillo
// In the loving memory of Facunda "Tata" Suárez Domínguez, R.I.P. 2019/07/24
// ****************************************************************************/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Aaru.CommonTypes;
using Aaru.CommonTypes.Interfaces;
using Aaru.CommonTypes.Structs;
using Aaru.Console;
//using Aaru.Decoders.Sega;
using Aaru.Helpers;
//using Schemas;

namespace Aaru.Filesystems
{
    public sealed partial class ISO9660
    {
        public Errno Mount(IMediaImage imagePlugin, Partition partition, Encoding encoding,
                           Dictionary<string, string> options, string @namespace)
        {
            Encoding = encoding ?? Encoding.GetEncoding(1252);
            byte[] vdMagic = new byte[5]; // Volume Descriptor magic "CD001"
            //byte[] hsMagic = new byte[5]; // Volume Descriptor magic "CDROM"

            options ??= GetDefaultOptions();

            if(options.TryGetValue("debug", out string debugString))
                bool.TryParse(debugString, out _debug);

            if(options.TryGetValue("use_path_table", out string usePathTableString))
                bool.TryParse(usePathTableString, out _usePathTable);

            if(options.TryGetValue("use_trans_tbl", out string useTransTblString))
                bool.TryParse(useTransTblString, out _useTransTbl);

            if(options.TryGetValue("use_evd", out string useEvdString))
                bool.TryParse(useEvdString, out _useEvd);

            // Default namespace
            @namespace ??= "joliet";

            switch(@namespace.ToLowerInvariant())
            {
                case "normal":
                    _namespace = Namespace.Normal;

                    break;
                case "vms":
                    _namespace = Namespace.Vms;

                    break;
                case "joliet":
                    _namespace = Namespace.Joliet;

                    break;
                case "rrip":
                    _namespace = Namespace.Rrip;

                    break;
                case "romeo":
                    _namespace = Namespace.Romeo;

                    break;
                default: return Errno.InvalidArgument;
            }

            PrimaryVolumeDescriptor?           pvd      = null;
            PrimaryVolumeDescriptor?           jolietvd = null;
            //BootRecord?                        bvd      = null;
            //HighSierraPrimaryVolumeDescriptor? hsvd     = null;
            //FileStructureVolumeDescriptor?     fsvd     = null;

            // ISO9660 is designed for 2048 bytes/sector devices
            if(imagePlugin.Info.SectorSize < 2048)
                return Errno.InvalidArgument;

            // ISO9660 Primary Volume Descriptor starts at sector 16, so that's minimal size.
            if(partition.End < 16)
                return Errno.InvalidArgument;

            ulong counter = 0;

            byte[] vdSector = imagePlugin.ReadSector(16 + counter + partition.Start);
            int    xaOff    = vdSector.Length == 2336 ? 8 : 0;
            //Array.Copy(vdSector, 0x009 + xaOff, hsMagic, 0, 5);
            //_highSierra = Encoding.GetString(hsMagic) == HIGH_SIERRA_MAGIC;
            int hsOff = 0;

            //if(_highSierra)
            //    hsOff = 8;

            //_cdi = false;
            List<ulong> bvdSectors = new List<ulong>();
            List<ulong> pvdSectors = new List<ulong>();
            List<ulong> svdSectors = new List<ulong>();
            List<ulong> evdSectors = new List<ulong>();
            List<ulong> vpdSectors = new List<ulong>();

            while(true)
            {
                AaruConsole.DebugWriteLine("ISO9660 plugin", "Processing VD loop no. {0}", counter);

                // Seek to Volume Descriptor
                AaruConsole.DebugWriteLine("ISO9660 plugin", "Reading sector {0}", 16 + counter + partition.Start);
                byte[] vdSectorTmp = imagePlugin.ReadSector(16 + counter + partition.Start);
                vdSector = new byte[vdSectorTmp.Length - xaOff];
                Array.Copy(vdSectorTmp, xaOff, vdSector, 0, vdSector.Length);

                byte vdType = vdSector[0 + hsOff]; // Volume Descriptor Type, should be 1 or 2.
                AaruConsole.DebugWriteLine("ISO9660 plugin", "VDType = {0}", vdType);

                if(vdType == 255) // Supposedly we are in the PVD.
                {
                    if(counter == 0)
                        return Errno.InvalidArgument;

                    break;
                }

                Array.Copy(vdSector, 0x001, vdMagic, 0, 5);
                //Array.Copy(vdSector, 0x009, hsMagic, 0, 5);

                if(Encoding.GetString(vdMagic) != ISO_MAGIC) // Recognized, it is an ISO9660, now check for rest of data.
                {
                    if(counter == 0)
                        return Errno.InvalidArgument;

                    break;
                }

                //_cdi |= Encoding.GetString(vdMagic) == CDI_MAGIC;

                switch(vdType)
                {
                    case 0:
                    {
                        if(_debug)
                            bvdSectors.Add(16 + counter + partition.Start);

                        break;
                    }

                    case 1:
                    {
                        pvd = Marshal.ByteArrayToStructureLittleEndian<PrimaryVolumeDescriptor>(vdSector);

                        if(_debug)
                            pvdSectors.Add(16 + counter + partition.Start);

                        break;
                    }

                    case 2:
                    {
                        PrimaryVolumeDescriptor svd =
                            Marshal.ByteArrayToStructureLittleEndian<PrimaryVolumeDescriptor>(vdSector);

                        // TODO: Other escape sequences
                        // Check if this is Joliet
                        if(svd.version == 1)
                        {
                            if(svd.escape_sequences[0] == '%' &&
                               svd.escape_sequences[1] == '/')
                                if(svd.escape_sequences[2] == '@' ||
                                   svd.escape_sequences[2] == 'C' ||
                                   svd.escape_sequences[2] == 'E')
                                    jolietvd = svd;
                                else
                                    AaruConsole.DebugWriteLine("ISO9660 plugin",
                                                               "Found unknown supplementary volume descriptor");

                            if(_debug)
                                svdSectors.Add(16 + counter + partition.Start);
                        }
                        else
                        {
                            if(_debug)
                                evdSectors.Add(16 + counter + partition.Start);

                            if(_useEvd)
                            {
                                // Basically until escape sequences are implemented, let the user chose the encoding.
                                // This is the same as user choosing Romeo namespace, but using the EVD instead of the PVD
                                _namespace = Namespace.Romeo;
                                pvd        = svd;
                            }
                        }

                        break;
                    }

                    case 3:
                    {
                        if(_debug)
                            vpdSectors.Add(16 + counter + partition.Start);

                        break;
                    }
                }

                counter++;
            }

            DecodedVolumeDescriptor decodedVd;
            var                     decodedJolietVd = new DecodedVolumeDescriptor();

            //XmlFsType = new FileSystemType();

            if(pvd  == null) //&& hsvd == null &&fsvd == null)
            {
                AaruConsole.ErrorWriteLine("ERROR: Could not find primary volume descriptor");

                return Errno.InvalidArgument;
            }

            decodedVd = DecodeVolumeDescriptor(pvd.Value);

            if(jolietvd != null)
                decodedJolietVd = DecodeJolietDescriptor(jolietvd.Value);

            if(_namespace != Namespace.Romeo)
                Encoding = Encoding.ASCII;

            string fsFormat;
            byte[] pathTableData;

            uint pathTableMsbLocation;
            uint pathTableLsbLocation = 0; // Initialize to 0 as ignored in CD-i

            _image = imagePlugin;

            _blockSize = pvd.Value.logical_block_size;

            pathTableData = ReadSingleExtent(pvd.Value.path_table_size, Swapping.Swap(pvd.Value.type_m_path_table));

            fsFormat = "ISO9660";

            pathTableMsbLocation = pvd.Value.type_m_path_table;
            pathTableLsbLocation = pvd.Value.type_l_path_table;
            

            _pathTable = DecodePathTable(pathTableData);

            if(jolietvd is null &&
               _namespace == Namespace.Joliet)
                _namespace = Namespace.Normal;

            uint rootLocation;
            uint rootSize;
            byte rootXattrLength = 0;


            rootLocation = pvd.Value.root_directory_record.extent;

            rootXattrLength = pvd.Value.root_directory_record.xattr_len;

            rootSize = pvd.Value.root_directory_record.size;

            if (pathTableData.Length > 1 &&
               rootLocation != _pathTable[0].Extent)
            {
                AaruConsole.DebugWriteLine("ISO9660 plugin",
                                           "Path table and PVD do not point to the same location for the root directory!");

                byte[] firstRootSector = ReadSector(rootLocation);

                bool pvdWrongRoot = false;

                {
                    DirectoryRecord rootEntry =
                        Marshal.ByteArrayToStructureLittleEndian<DirectoryRecord>(firstRootSector);

                    if (rootEntry.extent != rootLocation)
                        pvdWrongRoot = true;
                }

                if (pvdWrongRoot)
                {
                    AaruConsole.DebugWriteLine("ISO9660 plugin",
                                               "PVD does not point to correct root directory, checking path table...");

                    bool pathTableWrongRoot = false;

                    rootLocation = _pathTable[0].Extent;

                    firstRootSector = ReadSector(_pathTable[0].Extent);

                    {
                        DirectoryRecord rootEntry =
                            Marshal.ByteArrayToStructureLittleEndian<DirectoryRecord>(firstRootSector);

                        if (rootEntry.extent != rootLocation)
                            pathTableWrongRoot = true;
                    }

                    if (pathTableWrongRoot)
                    {
                        AaruConsole.ErrorWriteLine("Cannot find root directory...");

                        return Errno.InvalidArgument;
                    }

                    _usePathTable = true;
                }
            }

            if(_usePathTable && pathTableData.Length == 1)
                _usePathTable = false;

            if(_usePathTable)
            {
                rootLocation = _pathTable[0].Extent;

                byte[] firstRootSector = ReadSector(rootLocation);

                {
                    DirectoryRecord rootEntry =
                        Marshal.ByteArrayToStructureLittleEndian<DirectoryRecord>(firstRootSector);

                    rootSize = rootEntry.size;
                }

                rootXattrLength = _pathTable[0].XattrLength;
            }

            try
            {
                _ = ReadSingleExtent(rootSize, rootLocation);
            }
            catch
            {
                return Errno.InvalidArgument;
            }

            if(_namespace == Namespace.Joliet ||
               _namespace == Namespace.Rrip)
            {
                _usePathTable = false;
                _useTransTbl  = false;
            }

            // Cannot traverse path table if we substitute the names for the ones in TRANS.TBL
            if(_useTransTbl)
                _usePathTable = false;

            if(_namespace != Namespace.Joliet)
                _rootDirectoryCache = DecodeIsoDirectory(rootLocation + rootXattrLength, rootSize);

            if(jolietvd != null &&
               (_namespace == Namespace.Joliet || _namespace == Namespace.Rrip))
            {
                rootLocation    = jolietvd.Value.root_directory_record.extent;
                rootXattrLength = jolietvd.Value.root_directory_record.xattr_len;

                rootSize = jolietvd.Value.root_directory_record.size;

                _joliet = true;

                _rootDirectoryCache = DecodeIsoDirectory(rootLocation + rootXattrLength, rootSize);

                decodedVd = decodedJolietVd;
            }

            if(_debug)
            {
                _rootDirectoryCache.Add("$", new DecodedDirectoryEntry
                {
                    Extents = new List<(uint extent, uint size)>
                    {
                        (rootLocation, rootSize)
                    },
                    Filename  = "$",
                    Size      = rootSize,
                    Timestamp = decodedVd.CreationTime
                });

                //if(!_cdi)
                _rootDirectoryCache.Add("$PATH_TABLE.LSB", new DecodedDirectoryEntry
                {
                    Extents = new List<(uint extent, uint size)>
                    {
                        (pathTableLsbLocation, (uint)pathTableData.Length)
                    },
                    Filename  = "$PATH_TABLE.LSB",
                    Size      = (uint)pathTableData.Length,
                    Timestamp = decodedVd.CreationTime
                });

                _rootDirectoryCache.Add("$PATH_TABLE.MSB", new DecodedDirectoryEntry
                {
                    Extents = new List<(uint extent, uint size)>
                    {
                        (Swapping.Swap(pathTableMsbLocation), (uint)pathTableData.Length)
                    },
                    Filename  = "$PATH_TABLE.MSB",
                    Size      = (uint)pathTableData.Length,
                    Timestamp = decodedVd.CreationTime
                });

                for(int i = 0; i < bvdSectors.Count; i++)
                    _rootDirectoryCache.Add(i == 0 ? "$BOOT" : $"$BOOT_{i}", new DecodedDirectoryEntry
                    {
                        Extents = new List<(uint extent, uint size)>
                        {
                            ((uint)i, 2048)
                        },
                        Filename  = i == 0 ? "$BOOT" : $"$BOOT_{i}",
                        Size      = 2048,
                        Timestamp = decodedVd.CreationTime
                    });

                for(int i = 0; i < pvdSectors.Count; i++)
                    _rootDirectoryCache.Add(i == 0 ? "$PVD" : $"$PVD{i}", new DecodedDirectoryEntry
                    {
                        Extents = new List<(uint extent, uint size)>
                        {
                            ((uint)i, 2048)
                        },
                        Filename  = i == 0 ? "$PVD" : $"PVD_{i}",
                        Size      = 2048,
                        Timestamp = decodedVd.CreationTime
                    });

                for(int i = 0; i < svdSectors.Count; i++)
                    _rootDirectoryCache.Add(i == 0 ? "$SVD" : $"$SVD_{i}", new DecodedDirectoryEntry
                    {
                        Extents = new List<(uint extent, uint size)>
                        {
                            ((uint)i, 2048)
                        },
                        Filename  = i == 0 ? "$SVD" : $"$SVD_{i}",
                        Size      = 2048,
                        Timestamp = decodedVd.CreationTime
                    });

                for(int i = 0; i < evdSectors.Count; i++)
                    _rootDirectoryCache.Add(i == 0 ? "$EVD" : $"$EVD_{i}", new DecodedDirectoryEntry
                    {
                        Extents = new List<(uint extent, uint size)>
                        {
                            ((uint)i, 2048)
                        },
                        Filename  = i == 0 ? "$EVD" : $"$EVD_{i}",
                        Size      = 2048,
                        Timestamp = decodedVd.CreationTime
                    });

                for(int i = 0; i < vpdSectors.Count; i++)
                    _rootDirectoryCache.Add(i == 0 ? "$VPD" : $"$VPD_{i}", new DecodedDirectoryEntry
                    {
                        Extents = new List<(uint extent, uint size)>
                        {
                            ((uint)i, 2048)
                        },
                        Filename  = i == 0 ? "$VPD" : $"$VPD_{i}",
                        Size      = 2048,
                        Timestamp = decodedVd.CreationTime
                    });
            }

            _statfs = new FileSystemInfo
            {
                Blocks = decodedVd.Blocks,
                FilenameLength = (ushort)(jolietvd != null ? _namespace == Namespace.Joliet
                                                                 ? 110
                                                                 : 255 : 255),
                PluginId = Id,
                Type     = fsFormat
            };

            _directoryCache = new Dictionary<string, Dictionary<string, DecodedDirectoryEntry>>();

            if(_usePathTable)
                foreach(DecodedDirectoryEntry subDirectory in GetSubdirsFromIsoPathTable(""))
                    _rootDirectoryCache[subDirectory.Filename] = subDirectory;

            _mounted = true;

            return Errno.NoError;
        }

        public Errno Unmount()
        {
            if(!_mounted)
                return Errno.AccessDenied;

            _rootDirectoryCache = null;
            _directoryCache     = null;
            _mounted            = false;

            return Errno.NoError;
        }

        public Errno StatFs(out FileSystemInfo stat)
        {
            stat = null;

            if(!_mounted)
                return Errno.AccessDenied;

            stat = _statfs.ShallowCopy();

            return Errno.NoError;
        }

        static public Errno GetDecodedPVD(IMediaImage imagePlugin, Partition partition, out DecodedVolumeDescriptor? decodedVd)
        {
            decodedVd = null;
            // ISO9660 is designed for 2048 bytes/sector devices
            if (imagePlugin.Info.SectorSize < 2048)
                return Errno.InvalidArgument;

            // ISO9660 Primary Volume Descriptor starts at sector 16, so that's minimal size.
            if (partition.End < 16)
                return Errno.InvalidArgument;

            ulong counter = 0;
            byte[] vdSector = imagePlugin.ReadSector(16 + counter + partition.Start);
            int xaOff = vdSector.Length == 2336 ? 8 : 0;
            int hsOff = 0;

            PrimaryVolumeDescriptor? pvd = null;

            while (true)
            {
                // Seek to Volume Descriptor
                byte[] vdSectorTmp = imagePlugin.ReadSector(16 + counter + partition.Start);
                vdSector = new byte[vdSectorTmp.Length - xaOff];
                Array.Copy(vdSectorTmp, xaOff, vdSector, 0, vdSector.Length);

                byte vdType = vdSector[0 + hsOff]; // Volume Descriptor Type, should be 1 or 2.

                if (vdType == 255) // Supposedly we are in the PVD.
                {
                    if (counter == 0)
                        return Errno.InvalidArgument;

                    break;
                }

                if (Encoding.ASCII.GetString(vdSector, 1, 5) != ISO_MAGIC) // Recognized, it is an ISO9660, now check for rest of data.
                {
                    if (counter == 0)
                        return Errno.InvalidArgument;

                    break;
                }

                switch (vdType)
                {
                    case 1:
                        pvd = Marshal.ByteArrayToStructureLittleEndian<PrimaryVolumeDescriptor>(vdSector);
                        break;
                    case 0:
                    case 2:
                    case 3:
                        break;
                }

                counter++;
            }

            if (!pvd.HasValue)
                return Errno.NoData;

            decodedVd = DecodeVolumeDescriptor(pvd.Value);

            return Errno.NoError;
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace GDMENUCardManager.Core
{
    public enum CardCacheState
    {
        NotUsed,
        Valid,
        Partial
    }

    internal sealed class CardCacheEntry
    {
        public string Folder { get; set; }
        public string Hash { get; set; }
        public IpBin Ip { get; set; }
        public string Name { get; set; }
        public string Serial { get; set; }
        public string OpenMenuFolder { get; set; } = string.Empty;
        public List<string> OpenMenuAltFolders { get; set; } = new List<string>();
    }

    internal sealed class CardCacheData
    {
        public string Hash { get; set; }
        public List<CardCacheEntry> Entries { get; set; } = new List<CardCacheEntry>();
    }

    // Files of one numbered folder, in the order the card stores them
    internal sealed class CardFolderScan
    {
        public string Path { get; set; }
        public string Hash { get; set; }
        public List<FileInfo> Files { get; set; }
    }

    internal static class CardCache
    {
        public const string FileName = "CARDCACHE.JSON";
        private const string TempFileName = "CARDCACHE.TMP";
        private const string AppName = "GDMENUCardManager";
        private const int FormatVersion = 1;
        private const string CrcPlaceholder = "\"crc32\": \"00000000\"";

        private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly string[] ignoredFiles = { "Thumbs.db", "desktop.ini" };

        private sealed class CardCacheFile
        {
            public string App { get; set; }
            public int Version { get; set; }
            public string Crc32 { get; set; }
            public CardCacheData Data { get; set; }
        }

        public static string GetPath(string sdPath) => Path.Combine(sdPath, FileName);

        private static string Crc32Hex(string text) => Crc32.HashToUInt32(Encoding.UTF8.GetBytes(text)).ToString("X8");

        // Null when missing, unreadable, from an unknown format version or with a bad checksum
        public static async Task<CardCacheData> ReadAsync(string sdPath)
        {
            var path = GetPath(sdPath);
            if (!await Helper.FileExistsAsync(path))
                return null;

            try
            {
                var text = await Helper.ReadAllTextAsync(path);
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;

                if (!root.TryGetProperty("version", out var version) || version.GetInt32() != FormatVersion)
                    return null;

                if (!root.TryGetProperty("crc32", out var crc) || !root.TryGetProperty("data", out var data))
                    return null;

                var dataText = data.GetRawText();
                if (!string.Equals(crc.GetString(), Crc32Hex(dataText), StringComparison.OrdinalIgnoreCase))
                    return null;

                return JsonSerializer.Deserialize<CardCacheData>(dataText, jsonOptions);
            }
            catch
            {
                return null;
            }
        }

        public static async Task WriteAsync(string sdPath, CardCacheData data)
        {
            var file = new CardCacheFile { App = AppName, Version = FormatVersion, Crc32 = "00000000", Data = data };
            var text = JsonSerializer.Serialize(file, jsonOptions);

            using (var doc = JsonDocument.Parse(text))
            {
                var crc = Crc32Hex(doc.RootElement.GetProperty("data").GetRawText());
                text = text.Replace(CrcPlaceholder, $"\"crc32\": \"{crc}\"");
            }

            var tempPath = Path.Combine(sdPath, TempFileName);
            await Helper.WriteTextFileAsync(tempPath, text);
            await Task.Run(() => File.Move(tempPath, GetPath(sdPath), true));
        }

        public static async Task DeleteAsync(string sdPath)
        {
            foreach (var name in new[] { FileName, TempFileName })
            {
                var path = Path.Combine(sdPath, name);
                if (await Helper.FileExistsAsync(path))
                    await Helper.DeleteFileAsync(path);
            }
        }

        public static List<string> GetNumberedFolders(string sdPath)
        {
            return Directory.EnumerateDirectories(sdPath).Where(x => int.TryParse(Path.GetFileName(x), out _)).ToList();
        }

        public static string RootHash(IEnumerable<string> numberedFolders)
        {
            var sb = new StringBuilder();
            foreach (var folder in numberedFolders)
                sb.Append(NormalizeName(Path.GetFileName(folder))).Append('\n');
            return Crc32Hex(sb.ToString());
        }

        public static CardFolderScan ScanFolder(string folderPath)
        {
            var files = new DirectoryInfo(folderPath).EnumerateFiles()
                .Where(x => !x.Name.StartsWith(".") && !ignoredFiles.Contains(x.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            var sb = new StringBuilder();
            foreach (var file in files)
                sb.Append(NormalizeName(file.Name)).Append('\t').Append(file.Length).Append('\n');

            return new CardFolderScan { Path = folderPath, Hash = Crc32Hex(sb.ToString()), Files = files };
        }

        private static string NormalizeName(string name) => name.Normalize().ToUpperInvariant();

        // Main image by extension, plus the companions that share its base name
        public static List<string> RebuildImageFiles(CardFolderScan scan)
        {
            var result = new List<string>();
            var main = scan.Files.FirstOrDefault(x => Manager.supportedImageFormats.Any(y => y.Equals(x.Extension, StringComparison.OrdinalIgnoreCase)));
            if (main == null)
                return result;

            result.Add(main.Name);

            string[] companions;
            switch (main.Extension.ToLowerInvariant())
            {
                case ".ccd": companions = new[] { ".img", ".sub" }; break;
                case ".mds": companions = new[] { ".mdf" }; break;
                default: companions = Array.Empty<string>(); break;
            }

            var baseName = Path.GetFileNameWithoutExtension(main.Name);
            foreach (var ext in companions)
            {
                var companion = scan.Files.FirstOrDefault(x => x.Name.Equals(baseName + ext, StringComparison.OrdinalIgnoreCase));
                if (companion != null)
                    result.Add(companion.Name);
            }

            return result;
        }
    }
}

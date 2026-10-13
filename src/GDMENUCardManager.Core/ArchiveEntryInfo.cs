using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GDMENUCardManager.Core
{
    /// <summary>
    /// Carries the adapter identity needed to reopen the same archive entry.
    /// </summary>
    public sealed class ArchiveEntryInfo
    {
        public ArchiveEntryInfo(int ordinal, string fullName, long size)
        {
            if (ordinal < 0)
                throw new ArgumentOutOfRangeException(nameof(ordinal));
            if (string.IsNullOrWhiteSpace(fullName))
                throw new ArgumentException("Archive entry name cannot be empty.", nameof(fullName));
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size));

            Ordinal = ordinal;
            FullName = fullName;
            Size = size;
        }

        public int Ordinal { get; }
        public string FullName { get; }
        public long Size { get; }
    }

    /// <summary>
    /// Applies consistent path rules to raw keys reported by archive adapters.
    /// </summary>
    public static class ArchiveEntryPath
    {
        /// <summary>
        /// Returns a canonical archive key without changing its case.
        /// </summary>
        public static string NormalizeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Archive entry name cannot be empty.", nameof(key));

            var replaced = key.Replace('\\', '/');
            if (replaced.StartsWith("/", StringComparison.Ordinal) ||
                (replaced.Length >= 2 && replaced[1] == ':'))
                throw new ArgumentException("Archive entry name must be relative.", nameof(key));

            var segments = new List<string>();
            foreach (var segment in replaced.Split('/'))
            {
                if (segment.Length == 0 || segment == ".")
                    continue;

                if (segment == "..")
                {
                    if (segments.Count == 0)
                        throw new ArgumentException("Archive entry name escapes the archive root.", nameof(key));

                    segments.RemoveAt(segments.Count - 1);
                    continue;
                }

                segments.Add(segment);
            }

            if (segments.Count == 0)
                throw new ArgumentException("Archive entry name cannot be empty.", nameof(key));

            return string.Join("/", segments);
        }

        /// <summary>
        /// Returns the canonical parent key, or an empty string for a root entry.
        /// </summary>
        public static string GetDirectoryKey(string key)
        {
            var normalized = NormalizeKey(key);
            int separator = normalized.LastIndexOf('/');
            return separator < 0 ? string.Empty : normalized.Substring(0, separator);
        }

        /// <summary>
        /// Returns the final component of a canonical archive key.
        /// </summary>
        public static string GetLeafName(string key)
        {
            var normalized = NormalizeKey(key);
            int separator = normalized.LastIndexOf('/');
            return separator < 0 ? normalized : normalized.Substring(separator + 1);
        }

        /// <summary>
        /// Compares entry keys while allowing archive-library separator differences.
        /// </summary>
        public static bool HasSameIdentityKey(string first, string second)
        {
            if (first == null || second == null)
                return false;

            return string.Equals(
                first.Replace('\\', '/'),
                second.Replace('\\', '/'),
                StringComparison.Ordinal);
        }

        internal static ArchiveEntryInfo FindRelativeEntry(
            IReadOnlyList<ArchiveEntryInfo> entries,
            ArchiveEntryInfo owner,
            string referencedName)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrWhiteSpace(referencedName))
                return null;

            var reference = referencedName.Replace('\\', '/');
            if (reference.StartsWith("/", StringComparison.Ordinal) ||
                (reference.Length >= 2 && reference[1] == ':'))
                return null;

            var ownerDirectory = GetDirectoryKey(owner.FullName);
            string combined = string.IsNullOrEmpty(ownerDirectory)
                ? reference
                : ownerDirectory + "/" + reference;

            string expected;
            try
            {
                expected = NormalizeKey(combined);
            }
            catch (ArgumentException)
            {
                return null;
            }

            return entries.FirstOrDefault(entry => string.Equals(
                NormalizeKey(entry.FullName),
                expected,
                StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Chooses the entries needed to flatten one disc image from an archive.
    /// </summary>
    public static class ArchiveEntrySelection
    {
        /// <summary>
        /// Returns root files and files beside the selected image without other image manifests.
        /// </summary>
        public static IReadOnlyList<ArchiveEntryInfo> SelectForFlatExtraction(
            IReadOnlyList<ArchiveEntryInfo> entries,
            ArchiveEntryInfo selectedEntry)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));
            if (selectedEntry == null)
                throw new ArgumentNullException(nameof(selectedEntry));

            var selected = entries.ElementAtOrDefault(selectedEntry.Ordinal);
            if (!HasSameIdentity(selected, selectedEntry))
                throw new InvalidDataException("The selected archive entry is no longer available.");

            string selectedDirectory = ArchiveEntryPath.GetDirectoryKey(selected.FullName);
            var selectedByLeaf = new Dictionary<string, SelectedEntry>(
                StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                if (entry == null || entry.Ordinal != index)
                    throw new InvalidDataException("The archive entry order is invalid.");

                string directory = ArchiveEntryPath.GetDirectoryKey(entry.FullName);
                bool isRoot = directory.Length == 0;
                bool isSelectedDirectory = selectedDirectory.Length > 0 &&
                    string.Equals(directory, selectedDirectory, StringComparison.OrdinalIgnoreCase);
                if (!isRoot && !isSelectedDirectory)
                    continue;

                bool isSelected = HasSameIdentity(entry, selectedEntry);
                string leaf = ArchiveEntryPath.GetLeafName(entry.FullName);
                if (!isSelected && Manager.supportedImageFormats.Any(extension =>
                    extension.Equals(Path.GetExtension(leaf), StringComparison.OrdinalIgnoreCase)))
                    continue;

                int priority = isSelectedDirectory ? 1 : 0;
                if (selectedByLeaf.TryGetValue(leaf, out var existing) &&
                    existing.Priority > priority)
                    continue;

                selectedByLeaf[leaf] = new SelectedEntry(entry, priority);
            }

            if (!selectedByLeaf.Values.Any(value => HasSameIdentity(value.Entry, selectedEntry)))
                throw new InvalidDataException("The selected archive entry is no longer available.");

            return selectedByLeaf.Values
                .Select(value => value.Entry)
                .OrderBy(entry => entry.Ordinal)
                .ToArray();
        }

        private static bool HasSameIdentity(ArchiveEntryInfo first, ArchiveEntryInfo second)
        {
            return first != null &&
                second != null &&
                first.Ordinal == second.Ordinal &&
                first.Size == second.Size &&
                ArchiveEntryPath.HasSameIdentityKey(first.FullName, second.FullName);
        }

        private sealed class SelectedEntry
        {
            internal SelectedEntry(ArchiveEntryInfo entry, int priority)
            {
                Entry = entry;
                Priority = priority;
            }

            internal ArchiveEntryInfo Entry { get; }
            internal int Priority { get; }
        }
    }
}

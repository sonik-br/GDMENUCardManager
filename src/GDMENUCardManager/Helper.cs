using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace GDMENUCardManager
{
    internal static class Helper
    {
        public static Task MoveDirectoryAsync(string from, string to)
        {
            return Task.Run(() => Directory.Move(from, to));
        }

        public static Task CreateDirectoryAsync(string path)
        {
            return Task.Run(() => Directory.CreateDirectory(path));
        }

        public static Task DeleteDirectoryAsync(string path)
        {
            return Task.Run(() => Directory.Delete(path, true));
        }

        public static Task MoveFileAsync(string from, string to)
        {
            return Task.Run(() => File.Move(from, to));
        }

        public static Task WriteTextFileAsync(string path, string text)
        {
            return Task.Run(() => File.WriteAllText(path, text));
        }

        public static async Task CopyDirectoryAsync(string sourceDirName, string destDirName)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
                throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + sourceDirName);

            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destDirName))
                await CreateDirectoryAsync(destDirName);

            // Get the files in the directory and copy them to the new location.
            await Task.Run(() =>
            {
                FileInfo[] files = dir.GetFiles();
                foreach (FileInfo file in files)
                    file.CopyTo(Path.Combine(destDirName, file.Name), true);
            });
        }

        public static string RemoveDiacritics(string text)
        {
            //from https://stackoverflow.com/questions/249087/how-do-i-remove-diacritics-accents-from-a-string-in-net

            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}

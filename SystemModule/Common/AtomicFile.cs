using System;
using System.IO;
using System.Text;

namespace SystemModule.Common
{
    public static class AtomicFile
    {
        public static void WriteAllText(string fileName, string contents, Encoding encoding)
        {
            if (encoding == null)
            {
                throw new ArgumentNullException(nameof(encoding));
            }

            WriteAllBytes(fileName, encoding.GetBytes(contents ?? string.Empty));
        }

        public static void WriteAllBytes(string fileName, byte[] contents)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("A target file name is required.", nameof(fileName));
            }
            if (contents == null)
            {
                throw new ArgumentNullException(nameof(contents));
            }

            var targetPath = Path.GetFullPath(fileName);
            var directory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new IOException($"Cannot determine the target directory for '{targetPath}'.");
            }

            Directory.CreateDirectory(directory);
            if (File.Exists(targetPath) &&
                (File.GetAttributes(targetPath) & FileAttributes.ReadOnly) != 0)
            {
                throw new UnauthorizedAccessException($"Target file is read-only: '{targetPath}'.");
            }

            var tempPath = Path.Combine(directory,
                $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write,
                           FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(contents, 0, contents.Length);
                    stream.Flush(true);
                }

                Commit(tempPath, targetPath);
            }
            catch (Exception ex) when (ex is not IOException &&
                                       ex is not UnauthorizedAccessException &&
                                       ex is not ArgumentException)
            {
                throw new IOException($"Atomic write failed for '{targetPath}'.", ex);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        private static void Commit(string tempPath, string targetPath)
        {
            if (!File.Exists(targetPath))
            {
                File.Move(tempPath, targetPath);
                return;
            }

            try
            {
                File.Replace(tempPath, targetPath, null);
                return;
            }
            catch (Exception replaceException) when (replaceException is IOException ||
                                                       replaceException is PlatformNotSupportedException)
            {
                CommitWithBackupMove(tempPath, targetPath, replaceException);
            }
        }

        private static void CommitWithBackupMove(string tempPath, string targetPath, Exception replaceException)
        {
            var directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
            var backupPath = Path.Combine(directory,
                $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.bak.tmp");
            var originalMoved = false;

            try
            {
                File.Move(targetPath, backupPath);
                originalMoved = true;
                File.Move(tempPath, targetPath);
                originalMoved = false;
                TryDelete(backupPath);
            }
            catch (Exception moveException)
            {
                Exception restoreException = null;
                if (originalMoved && !File.Exists(targetPath))
                {
                    try
                    {
                        File.Move(backupPath, targetPath);
                        originalMoved = false;
                    }
                    catch (Exception ex)
                    {
                        restoreException = ex;
                    }
                }

                if (restoreException != null)
                {
                    throw new IOException($"Atomic write and restore both failed for '{targetPath}'. " +
                                          $"The original remains at '{backupPath}'.",
                        new AggregateException(replaceException, moveException, restoreException));
                }

                throw new IOException($"Atomic replacement failed for '{targetPath}'.",
                    new AggregateException(replaceException, moveException));
            }
        }

        private static void TryDelete(string fileName)
        {
            try
            {
                if (File.Exists(fileName))
                {
                    File.Delete(fileName);
                }
            }
            catch
            {
                // The primary write/commit exception must not be hidden by temp-file cleanup.
            }
        }
    }
}

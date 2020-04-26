using System;
using System.IO.Enumeration;

namespace OneDriveUploadTool
{
    partial class Program
    {
        private readonly struct EnumeratedFileData
        {
            public EnumeratedFileData(string fullPath, long length, DateTimeOffset creationTimeUtc, DateTimeOffset lastWriteTimeUtc, DateTimeOffset lastAccessTimeUtc)
            {
                FullPath = fullPath;
                Length = length;
                CreationTimeUtc = creationTimeUtc;
                LastWriteTimeUtc = lastWriteTimeUtc;
                LastAccessTimeUtc = lastAccessTimeUtc;
            }

            public string FullPath { get; }
            public long Length { get; }
            public DateTimeOffset CreationTimeUtc { get; }
            public DateTimeOffset LastWriteTimeUtc { get; }
            public DateTimeOffset LastAccessTimeUtc { get; }

            public static EnumeratedFileData FromFileSystemEntry(ref FileSystemEntry entry)
            {
                if (entry.IsDirectory) return default;

                return new EnumeratedFileData(
                    entry.ToFullPath(),
                    entry.Length,
                    entry.CreationTimeUtc,
                    entry.LastWriteTimeUtc,
                    entry.LastAccessTimeUtc);
            }
        }
    }
}

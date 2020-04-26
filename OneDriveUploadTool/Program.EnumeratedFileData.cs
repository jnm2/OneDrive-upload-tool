using System;
using System.IO.Enumeration;

namespace OneDriveUploadTool
{
    partial class Program
    {
        private readonly struct EnumeratedFileData
        {
            public EnumeratedFileData(string fullPath, DateTimeOffset creationTimeUtc, DateTimeOffset lastWriteTimeUtc, DateTimeOffset lastAccessTimeUtc)
            {
                FullPath = fullPath;
                CreationTimeUtc = creationTimeUtc;
                LastWriteTimeUtc = lastWriteTimeUtc;
                LastAccessTimeUtc = lastAccessTimeUtc;
            }

            public string FullPath { get; }
            public DateTimeOffset CreationTimeUtc { get; }
            public DateTimeOffset LastWriteTimeUtc { get; }
            public DateTimeOffset LastAccessTimeUtc { get; }

            public static EnumeratedFileData FromFileSystemEntry(ref FileSystemEntry entry)
            {
                return new EnumeratedFileData(
                    entry.ToFullPath(),
                    entry.CreationTimeUtc,
                    entry.LastWriteTimeUtc,
                    entry.LastAccessTimeUtc);
            }
        }
    }
}

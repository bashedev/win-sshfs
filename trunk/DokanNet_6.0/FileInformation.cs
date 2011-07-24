using System;
using System.IO;

namespace DokanNet
{
    public struct FileInformation
    {
        public string FileName;
        public FileAttributes Attributes;
        public DateTime CreationTime;
        public DateTime LastAccessTime;
        public DateTime LastWriteTime;
        public long Length;
    }
}
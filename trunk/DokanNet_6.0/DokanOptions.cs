namespace DokanNet
{
    public class DokanOptions
    {
        public bool DebugMode { get; set; }
        public string FilesystemName { get; set; }
        public string MountPoint { get; set; }
        public bool NetworkDrive { get; set; }
        public bool RemovableDrive { get; set; }
        public ushort ThreadCount { get; set; }
        public bool UseAltStream { get; set; }
        public bool UseKeepAlive { get; set; }
        public bool UseStdErr { get; set; }
        public ushort Version { get; set; }
        public string VolumeLabel { get; set; }
    }
}
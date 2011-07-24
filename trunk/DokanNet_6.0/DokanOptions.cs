namespace DokanNet
{
    public class DokanOptions
    {
        public bool DebugMode { get; set; }
        public string MountPoint { get; set; }
        public bool NetworkDrive { get; set; }
        public bool RemovableDrive { get; set; }
        public ushort ThreadCount { get; set; }
        public bool UseAlternativeStreams { get; set; }
        public bool UseKeepAlive { get; set; }
        public bool UseStandardError { get; set; }
        public ushort Version { get; set; }
    }
}
namespace DokanNet
{
    public enum DokanStatus
    {
        DokanSuccess = 0,
        DokanError = -1, // General Error
        DokanDriveLetterError = -2, // Bad Drive letter
        DokanDriverInstallError = -3, // Can't install driver
        DokanStartError = -4, // Driver something wrong
        DokanMountError = -5, // Can't assign drive letter 
    }
}
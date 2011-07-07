using System;
using System.Diagnostics;
using DokanNet.Native;

namespace DokanNet
{
    public static class Dokan
    {
        #region Dokan Driver Options

        private const ushort DOKAN_VERSION = 600; // ver 0.6.0
        private const uint DOKAN_OPTION_DEBUG = 1;
        private const uint DOKAN_OPTION_STDERR = 2;
        private const uint DOKAN_OPTION_ALT_STREAM = 4;
        private const uint DOKAN_OPTION_KEEP_ALIVE = 8;
        private const uint DOKAN_OPTION_NETWORK = 16;
        private const uint DOKAN_OPTION_REMOVABLE = 32;

        #endregion

        public static DokanStatus Mount(DokanOptions options, IDokanOperations operations)
        {
            Debug.Print("Starting... DokanMain");
            if (String.IsNullOrEmpty(options.VolumeLabel))
            {
                options.VolumeLabel = "DOKAN";
            }
            if (String.IsNullOrEmpty(options.FilesystemName))
            {
                options.FilesystemName = "DOKAN";
            }
            var dokanOperationProxy = new DokanOperationProxy(options, operations);

            var dokanOptions = new DOKAN_OPTIONS
                                   {
                                       Version = options.Version != 0 ? options.Version : DOKAN_VERSION,
                                       MountPoint = options.MountPoint,
                                       ThreadCount = options.ThreadCount
                                   };

            dokanOptions.Options |= options.RemovableDrive ? DOKAN_OPTION_REMOVABLE : 0;
            dokanOptions.Options |= options.DebugMode ? DOKAN_OPTION_DEBUG : 0;
            dokanOptions.Options |= options.UseStdErr ? DOKAN_OPTION_STDERR : 0;
            dokanOptions.Options |= options.UseAltStream ? DOKAN_OPTION_ALT_STREAM : 0;
            dokanOptions.Options |= options.UseKeepAlive ? DOKAN_OPTION_KEEP_ALIVE : 0;
            dokanOptions.Options |= options.NetworkDrive ? DOKAN_OPTION_NETWORK : 0;

            var dokanOperations = new DOKAN_OPERATIONS
                                      {
                                          CreateFile = dokanOperationProxy.CreateFileProxy,
                                          OpenDirectory = dokanOperationProxy.OpenDirectoryProxy,
                                          CreateDirectory = dokanOperationProxy.CreateDirectoryProxy,
                                          Cleanup = dokanOperationProxy.CleanupProxy,
                                          CloseFile = dokanOperationProxy.CloseFileProxy,
                                          ReadFile = dokanOperationProxy.ReadFileProxy,
                                          WriteFile = dokanOperationProxy.WriteFileProxy,
                                          FlushFileBuffers = dokanOperationProxy.FlushFileBuffersProxy,
                                          GetFileInformation = dokanOperationProxy.GetFileInformationProxy,
                                          FindFiles = dokanOperationProxy.FindFilesProxy,
                                          SetFileAttributes = dokanOperationProxy.SetFileAttributesProxy,
                                          SetFileTime = dokanOperationProxy.SetFileTimeProxy,
                                          DeleteFile = dokanOperationProxy.DeleteFileProxy,
                                          DeleteDirectory = dokanOperationProxy.DeleteDirectoryProxy,
                                          MoveFile = dokanOperationProxy.MoveFileProxy,
                                          SetEndOfFile = dokanOperationProxy.SetEndOfFileProxy,
                                          SetAllocationSize = dokanOperationProxy.SetAllocationSizeProxy,
                                          LockFile = dokanOperationProxy.LockFileProxy,
                                          UnlockFile = dokanOperationProxy.UnlockFileProxy,
                                          GetDiskFreeSpace = dokanOperationProxy.GetDiskFreeSpaceProxy,
                                          GetVolumeInformation = dokanOperationProxy.GetVolumeInformationProxy,
                                          Unmount = dokanOperationProxy.UnmountProxy
                                      };

            return (DokanStatus) NativeMethods.DokanMain(ref dokanOptions, ref dokanOperations);
        }


        public static DokanStatus Unmount(char driveLetter)
        {
            return (DokanStatus) NativeMethods.DokanUnmount(driveLetter);
        }

        public static DokanStatus RemoveMountPoint(string mountPoint)
        {
            return (DokanStatus) NativeMethods.DokanRemoveMountPoint(mountPoint);
        }

        public static uint Version()
        {
            return NativeMethods.DokanVersion();
        }

        public static uint DriverVersion()
        {
            return NativeMethods.DokanDriverVersion();
        }
    }
}
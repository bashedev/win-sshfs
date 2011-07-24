using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using DokanNet.Native;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace DokanNet
{
    internal class DokanOperationProxy
    {
     

        #region Delegates

        public delegate int CleanupDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName,
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo);

        public delegate int CloseFileDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName,
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo);

        public delegate int CreateDirectoryDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName,
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo);

        public delegate int CreateFileDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName, uint rawAccessMode, uint rawShare,
            uint rawCreationDisposition, uint rawFlagsAndAttributes,
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo dokanFileInfo);

        public delegate int DeleteDirectoryDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName,
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo);

        public delegate int DeleteFileDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName,
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo);

        public delegate int FindFilesDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName, IntPtr rawFillFindData, // function pointer
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo);

        /*     public delegate int FindFilesWithPatternDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName,
            [MarshalAs(UnmanagedType.LPWStr)] string rawSearchPattern,
            IntPtr rawFillFindData, // function pointer
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo);*/

        public delegate int FlushFileBuffersDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName,
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo);

        public delegate int GetDiskFreeSpaceDelegate(ref ulong rawFreeBytesAvailable, ref ulong rawTotalNumberOfBytes,
                                                     ref ulong rawTotalNumberOfFreeBytes,
                                                     [MarshalAs(UnmanagedType.LPStruct), In] DokanFileInfo rawFileInfo);

        public delegate int GetFileInformationDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string fileName, ref BY_HANDLE_FILE_INFORMATION handleFileInfo,
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo fileInfo);

        public delegate int GetFileSecurityDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName, ref SECURITY_INFORMATION rawRequestedInformation,
            IntPtr rawSecurityDescriptor, uint rawSecurityDescriptorLength,
            ref uint rawSecurityDescriptorLengthNeeded,
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo);

        public delegate int GetVolumeInformationDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder rawVolumeNameBuffer, uint rawVolumeNameSize,
            ref uint rawVolumeSerialNumber,
            ref uint rawMaximumComponentLength, ref FileSystemFeature rawFileSystemFlags,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder rawFileSystemNameBuffer,
            uint rawFileSystemNameSize, [MarshalAs(UnmanagedType.LPStruct), In] DokanFileInfo rawFileInfo);

        public delegate int LockFileDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName, long rawByteOffset, long rawLength,
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo);

        public delegate int MoveFileDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName,
            [MarshalAs(UnmanagedType.LPWStr)] string rawNewFileName,
            [MarshalAs(UnmanagedType.Bool)] bool rawReplaceIfExisting,
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo);

        public delegate int OpenDirectoryDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string fileName,
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo fileInfo);

        public delegate int ReadFileDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2), Out] byte[] rawBuffer, uint rawBufferLength,
            ref uint rawReadLength, long rawOffset,
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo);

        public delegate int SetAllocationSizeDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName, long rawLength,
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo);

        public delegate int SetEndOfFileDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName, long rawByteOffset,
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo);

        public delegate int SetFileAttributesDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName, uint rawAttributes,
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo);

        public delegate int SetFileSecurityDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName, ref SECURITY_INFORMATION rawSecurityInformation,
            IntPtr rawSecurityDescriptor, uint rawSecurityDescriptorLength,
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo);

        public delegate int SetFileTimeDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName,
            FILETIME rawCreationTime,
            FILETIME rawLastAccessTime,
            FILETIME rawLastWriteTime, [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo);

        public delegate int UnlockFileDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName, long rawByteOffset, long rawLength,
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo);

        public delegate int UnmountDelegate([MarshalAs(UnmanagedType.LPStruct), In] DokanFileInfo rawFileInfo);

        public delegate int WriteFileDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string rawFileName,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] rawBuffer, uint rawNumberOfBytesToWrite,
            ref uint rawNumberOfBytesWritten, long rawOffset,
            [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo);

        #endregion
        private const int ERROR_FILE_NOT_FOUND = -2;
        private const int ERROR_INVALID_FUNCTION = -1;
        private const int ERROR_SUCCESS = 0;
        private const int ERROR_INSUFFICIENT_BUFFER = -122;

        private readonly IDokanOperations _operations;

        public DokanOperationProxy(IDokanOperations operations)
        {
            _operations = operations;
        }

        public int CreateFileProxy(string rawFileName, uint rawAccessMode,
                                   uint rawShare, uint rawCreationDisposition, uint rawFlagsAndAttributes,
                                   DokanFileInfo rawFileInfo)
        {
            try
            {
                return (int) _operations.CreateFile(rawFileName, (FileAccess) rawAccessMode, (FileShare) rawShare,
                                                    (FileMode) rawCreationDisposition,
                                                    (FileOptions) (rawFlagsAndAttributes & 0xffffc000), //& 0xffffc000
                                                    (FileAttributes) (rawFlagsAndAttributes & 0x3fff), rawFileInfo);
                //& 0x3ffflower 14 bits i think are file atributes and rest are file options WRITE_TROUGH etc.
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_FILE_NOT_FOUND;
            }
        }

        ////

        public int OpenDirectoryProxy(string rawFileName,
                                      DokanFileInfo rawFileInfo)
        {
            try
            {
                return (int) _operations.OpenDirectory(rawFileName, rawFileInfo);
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }

        ////

        public int CreateDirectoryProxy(string rawFileName,
                                        DokanFileInfo rawFileInfo)
        {
            try
            {
                return (int) _operations.CreateDirectory(rawFileName, rawFileInfo);
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }

        ////

        public int CleanupProxy(string rawFileName,
                                [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo)
        {
            try
            {
                return (int) _operations.Cleanup(rawFileName, rawFileInfo);
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }

        ////

        public int CloseFileProxy(string rawFileName,
                                  [MarshalAs(UnmanagedType.LPStruct), In, Out] DokanFileInfo rawFileInfo)
        {
            try
            {
                return (int) _operations.CloseFile(rawFileName, rawFileInfo);
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }

        ////


        public int ReadFileProxy(string rawFileName, byte[] rawBuffer,
                                 uint rawBufferLength, ref uint rawReadLength, long rawOffset,
                                 DokanFileInfo rawFileInfo)
        {
            try
            {
                return (int) _operations.ReadFile(rawFileName, rawBuffer, ref rawReadLength, rawOffset,
                                                  rawFileInfo);
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }

        ////

        public int WriteFileProxy(string rawFileName, byte[] rawBuffer,
                                  uint rawNumberOfBytesToWrite, ref uint rawNumberOfBytesWritten, long rawOffset,
                                  DokanFileInfo rawFileInfo)
        {
            try
            {
                return (int) _operations.WriteFile(rawFileName, rawBuffer,
                                                   ref rawNumberOfBytesWritten, rawOffset,
                                                   rawFileInfo);
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }

        ////

        public int FlushFileBuffersProxy(string rawFileName,
                                         DokanFileInfo rawFileInfo)
        {
            try
            {
                return (int) _operations.FlushFileBuffers(rawFileName, rawFileInfo);
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }

        ////

        public int GetFileInformationProxy(string rawFileName,
                                           ref BY_HANDLE_FILE_INFORMATION rawHandleFileInformation,
                                           DokanFileInfo rawFileInfo)
        {
            var fi = new FileInformation();
            try
            {
                int ret = (int) _operations.GetFileInformation(rawFileName, ref fi, rawFileInfo);

                if (ret == ERROR_SUCCESS)
                {
                    rawHandleFileInformation.dwFileAttributes = (uint) fi.Attributes /* + FILE_ATTRIBUTE_VIRTUAL*/;

                    long ctime = fi.CreationTime.ToFileTime();
                    long atime = fi.LastAccessTime.ToFileTime();
                    long mtime = fi.LastWriteTime.ToFileTime();
                    rawHandleFileInformation.ftCreationTime.dwHighDateTime = (int) (ctime >> 32);
                    rawHandleFileInformation.ftCreationTime.dwLowDateTime =
                        (int) (ctime & 0xffffffff);

                    rawHandleFileInformation.ftLastAccessTime.dwHighDateTime =
                        (int) (atime >> 32);
                    rawHandleFileInformation.ftLastAccessTime.dwLowDateTime =
                        (int) (atime & 0xffffffff);

                    rawHandleFileInformation.ftLastWriteTime.dwHighDateTime =
                        (int) (mtime >> 32);
                    rawHandleFileInformation.ftLastWriteTime.dwLowDateTime =
                        (int) (mtime & 0xffffffff);

                    rawHandleFileInformation.dwVolumeSerialNumber = 0x20101112;

                    rawHandleFileInformation.nFileSizeLow = (uint) (fi.Length & 0xffffffff);
                    rawHandleFileInformation.nFileSizeHigh = (uint) (fi.Length >> 32);
                    rawHandleFileInformation.dwNumberOfLinks = 1;
                    rawHandleFileInformation.nFileIndexHigh = 0;
                    rawHandleFileInformation.nFileIndexLow = (uint) fi.FileName.GetHashCode();
                }

                return ret;
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }

        ////



        public int FindFilesProxy(string rawFileName, IntPtr rawFillFindData,
                                  // function pointer
                                  DokanFileInfo rawFileInfo)
        {
            try
            {
                FileInformation[] files = null;


                int ret = (int) _operations.FindFiles(rawFileName, ref files, rawFileInfo);

                FILL_FIND_DATA fill =
                    (FILL_FIND_DATA) Marshal.GetDelegateForFunctionPointer(rawFillFindData, typeof (FILL_FIND_DATA));

                if ((ret == ERROR_SUCCESS)
                    && (files != null)
                    )
                {
                   
                    // Used a single entry call to speed up the "enumeration" of the list
                    for (int index = 0; index < files.Length; index++)
                     
                    {
                        Addto(fill, rawFileInfo, files[index]);
                    }
                }
                return ret;
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }


        private static void Addto(FILL_FIND_DATA fill, DokanFileInfo rawFileInfo, FileInformation fi)
        {
            long ctime = fi.CreationTime.ToFileTime();
            long atime = fi.LastAccessTime.ToFileTime();
            long mtime = fi.LastWriteTime.ToFileTime();
            var data = new WIN32_FIND_DATA
                           {
                               dwFileAttributes = fi.Attributes,
                               ftCreationTime =
                                   {
                                       dwHighDateTime = (int) (ctime >> 32),
                                       dwLowDateTime = (int) (ctime & 0xffffffff)
                                   },
                               ftLastAccessTime =
                                   {
                                       dwHighDateTime = (int) (atime >> 32),
                                       dwLowDateTime = (int) (atime & 0xffffffff)
                                   },
                               ftLastWriteTime =
                                   {
                                       dwHighDateTime = (int) (mtime >> 32),
                                       dwLowDateTime = (int) (mtime & 0xffffffff)
                                   },
                               nFileSizeLow = (uint) (fi.Length & 0xffffffff),
                               nFileSizeHigh = (uint) (fi.Length >> 32),
                               cFileName = fi.FileName
                           };
            //ZeroMemory(&data, sizeof(WIN32_FIND_DATAW));

            fill(ref data, rawFileInfo);
        }

        ////

        public int SetEndOfFileProxy(string rawFileName, long rawByteOffset,
                                     DokanFileInfo rawFileInfo)
        {
            try
            {
                return (int) _operations.SetEndOfFile(rawFileName, rawByteOffset, rawFileInfo);
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }


        public int SetAllocationSizeProxy(string rawFileName, long rawLength,
                                          DokanFileInfo rawFileInfo)
        {
            try
            {
                return (int) _operations.SetAllocationSize(rawFileName, rawLength, rawFileInfo);
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }


        ////

        public int SetFileAttributesProxy(string rawFileName, uint rawAttributes,
                                          DokanFileInfo rawFileInfo)
        {
            try
            {
                return (int) _operations.SetFileAttributes(rawFileName, (FileAttributes) rawAttributes, rawFileInfo);
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }

        ////

        public int SetFileTimeProxy(string rawFileName,
                                    FILETIME rawCreationTime,
                                    FILETIME rawLastAccessTime,
                                    FILETIME rawLastWriteTime,
                                    DokanFileInfo rawFileInfo)
        {
            DateTime? ctime = rawCreationTime.dwHighDateTime != ERROR_INVALID_FUNCTION
                                  ? DateTime.FromFileTime(((long) rawCreationTime.dwHighDateTime << 32) |
                                                          (uint) rawCreationTime.dwLowDateTime)
                                  : (DateTime?) null;
            DateTime? atime = rawLastAccessTime.dwHighDateTime != ERROR_INVALID_FUNCTION
                                  ? DateTime.FromFileTime(((long) rawLastAccessTime.dwHighDateTime << 32) |
                                                          (uint) rawLastAccessTime.dwLowDateTime)
                                  : (DateTime?) null;
            DateTime? mtime = rawLastWriteTime.dwHighDateTime != ERROR_INVALID_FUNCTION
                                  ? DateTime.FromFileTime(((long) rawLastWriteTime.dwHighDateTime << 32) |
                                                          (uint) rawLastWriteTime.dwLowDateTime)
                                  : (DateTime?) null;


            try
            {
                return (int) _operations.SetFileTime(rawFileName, ctime, atime,
                                                     mtime, rawFileInfo);
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }

        ////

        public int DeleteFileProxy(string rawFileName, DokanFileInfo rawFileInfo)
        {
            try
            {
                return (int) _operations.DeleteFile(rawFileName, rawFileInfo);
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }

        ////

        public int DeleteDirectoryProxy(string rawFileName,
                                        DokanFileInfo rawFileInfo)
        {
            try
            {
                return (int) _operations.DeleteDirectory(rawFileName, rawFileInfo);
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }

        ////

        public int MoveFileProxy(string rawFileName,
                                 string rawNewFileName, bool rawReplaceIfExisting,
                                 DokanFileInfo rawFileInfo)
        {
            try
            {
                return (int) _operations.MoveFile(rawFileName, rawNewFileName, rawReplaceIfExisting,
                                                  rawFileInfo);
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }

        ////

        public int LockFileProxy(string rawFileName, long rawByteOffset,
                                 long rawLength, DokanFileInfo rawFileInfo)
        {
            try
            {
                return
                    (int) _operations.LockFile(rawFileName, rawByteOffset, rawLength, rawFileInfo);
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }

        ////

        public int UnlockFileProxy(string rawFileName, long rawByteOffset,
                                   long rawLength, DokanFileInfo rawFileInfo)
        {
            try
            {
                return
                    (int)
                    _operations.UnlockFile(rawFileName, rawByteOffset, rawLength, rawFileInfo);
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }

        ////

        public int GetDiskFreeSpaceProxy(ref ulong rawFreeBytesAvailable, ref ulong rawTotalNumberOfBytes,
                                         ref ulong rawTotalNumberOfFreeBytes, DokanFileInfo rawFileInfo)
        {
            try
            {
                return (int) _operations.GetDiskFreeSpace(ref rawFreeBytesAvailable, ref rawTotalNumberOfBytes,
                                                          ref rawTotalNumberOfFreeBytes,
                                                          rawFileInfo);
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }

        public int GetVolumeInformationProxy(StringBuilder rawVolumeNameBuffer,
                                             uint rawVolumeNameSize,
                                             ref uint rawVolumeSerialNumber,
                                             ref uint rawMaximumComponentLength, ref FileSystemFeature rawFileSystemFlags,
                                             StringBuilder rawFileSystemNameBuffer,
                                             uint rawFileSystemNameSize,
                                             DokanFileInfo fileInfo)
        {
            string label = null;
            string name = null;
            
            rawMaximumComponentLength = 256;
            try
            {
                int ret = (int) _operations.GetVolumeInformation(ref label, ref rawVolumeSerialNumber,
                                                                 ref rawFileSystemFlags, ref name,
                                                                 fileInfo);

                if (ret == ERROR_SUCCESS)
                {
                    rawVolumeNameBuffer.Append(label);
                    rawFileSystemNameBuffer.Append(name);
                   
                }
                return ret;
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }


        public int UnmountProxy(DokanFileInfo rawFileInfo)
        {
            try
            {
                return (int) _operations.Unmount(rawFileInfo);
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }

        public int GetFileSecurityProxy(string rawFileName, ref SECURITY_INFORMATION rawRequestedInformation,
                                        IntPtr rawSecurityDescriptor, uint rawSecurityDescriptorLength,
                                        ref uint rawSecurityDescriptorLengthNeeded,
                                        DokanFileInfo rawFileInfo)
        {
            FileSystemSecurity sec = null;
            var sect = AccessControlSections.None;
            if (rawRequestedInformation.HasFlag(SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION))
            {
                sect |= AccessControlSections.Owner;
            }
            if (rawRequestedInformation.HasFlag(SECURITY_INFORMATION.GROUP_SECURITY_INFORMATION))
            {
                sect |= AccessControlSections.Group;
            }
            if (rawRequestedInformation.HasFlag(SECURITY_INFORMATION.DACL_SECURITY_INFORMATION) ||
                rawRequestedInformation.HasFlag(SECURITY_INFORMATION.PROTECTED_DACL_SECURITY_INFORMATION) ||
                rawRequestedInformation.HasFlag(SECURITY_INFORMATION.UNPROTECTED_DACL_SECURITY_INFORMATION))
            {
                sect |= AccessControlSections.Access;
            }
            if (rawRequestedInformation.HasFlag(SECURITY_INFORMATION.SACL_SECURITY_INFORMATION) ||
                rawRequestedInformation.HasFlag(SECURITY_INFORMATION.PROTECTED_SACL_SECURITY_INFORMATION) ||
                rawRequestedInformation.HasFlag(SECURITY_INFORMATION.UNPROTECTED_SACL_SECURITY_INFORMATION))
            {
                sect |= AccessControlSections.Audit;
            }
            try
            {

                int ret = (int) _operations.GetFileSecurity(rawFileName, ref sec, sect, rawFileInfo);
                if (ret == ERROR_SUCCESS && sec != null)
                {
                    var buffer = sec.GetSecurityDescriptorBinaryForm();
                    if (buffer.Length > rawSecurityDescriptorLength)
                    {
                        rawSecurityDescriptorLengthNeeded = (uint) buffer.Length;
                        return ERROR_INSUFFICIENT_BUFFER;
                    }
                    Marshal.Copy(buffer, 0, rawSecurityDescriptor, buffer.Length);

                   
                }
                return ret;
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }

        public int SetFileSecurityProxy(
            string rawFileName, ref SECURITY_INFORMATION rawSecurityInformation,
            IntPtr rawSecurityDescriptor, uint rawSecurityDescriptorLength,
            DokanFileInfo rawFileInfo)

        {
            var sect = AccessControlSections.None;
            if (rawSecurityInformation.HasFlag(SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION))
            {
                sect |= AccessControlSections.Owner;
            }
            if (rawSecurityInformation.HasFlag(SECURITY_INFORMATION.GROUP_SECURITY_INFORMATION))
            {
                sect |= AccessControlSections.Group;
            }
            if (rawSecurityInformation.HasFlag(SECURITY_INFORMATION.DACL_SECURITY_INFORMATION) ||
                rawSecurityInformation.HasFlag(SECURITY_INFORMATION.PROTECTED_DACL_SECURITY_INFORMATION) ||
                rawSecurityInformation.HasFlag(SECURITY_INFORMATION.UNPROTECTED_DACL_SECURITY_INFORMATION))
            {
                sect |= AccessControlSections.Access;
            }
            if (rawSecurityInformation.HasFlag(SECURITY_INFORMATION.SACL_SECURITY_INFORMATION) ||
                rawSecurityInformation.HasFlag(SECURITY_INFORMATION.PROTECTED_SACL_SECURITY_INFORMATION) ||
                rawSecurityInformation.HasFlag(SECURITY_INFORMATION.UNPROTECTED_SACL_SECURITY_INFORMATION))
            {
                sect |= AccessControlSections.Audit;
            }
            var buffer = new byte[rawSecurityDescriptorLength];
            try
            {
                Marshal.Copy(rawSecurityDescriptor, buffer, 0, (int) rawSecurityDescriptorLength);
                var sec = rawFileInfo.IsDirectory ? (FileSystemSecurity) new DirectorySecurity() : new FileSecurity();
                sec.SetSecurityDescriptorBinaryForm(buffer);

                return (int) _operations.SetFileSecurity(rawFileName, sec, sect, rawFileInfo);
            }
            catch
            {
#if DEBUG
                throw;
#endif
                return ERROR_INVALID_FUNCTION;
            }
        }

        #region Nested type: FILL_FIND_DATA

        private delegate int FILL_FIND_DATA(
            ref WIN32_FIND_DATA rawFindData, [MarshalAs(UnmanagedType.LPStruct), In] DokanFileInfo rawFileInfo);

        #endregion
    }
}
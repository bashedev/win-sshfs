using System;
using System.IO;
using System.Security.AccessControl;

namespace DokanNet
{
    public interface IDokanOperations
    {
        DokanError CreateFile(string fileName, FileAccess access, FileShare share, FileMode mode,
                              FileOptions options, FileAttributes attributes, DokanFileInfo info);

        DokanError OpenDirectory(string fileName, DokanFileInfo info);

        DokanError CreateDirectory(string fileName, DokanFileInfo info);

        DokanError Cleanup(string fileName, DokanFileInfo info);

        DokanError CloseFile(string fileName, DokanFileInfo info);

        DokanError ReadFile(string fileName, byte[] buffer, ref uint bytesRead, long offset,
                            DokanFileInfo info);

        DokanError WriteFile(string fileName, byte[] buffer, ref uint bytesWritten,
                             long offset, DokanFileInfo info);

        DokanError FlushFileBuffers(string fileName, DokanFileInfo info);

        DokanError GetFileInformation(string fileName, ref FileInformation fileInfo, DokanFileInfo info);

        DokanError FindFiles(string fileName, ref FileInformation[] files, DokanFileInfo info);

        DokanError SetFileAttributes(string fileName, FileAttributes attributes, DokanFileInfo info);

        DokanError SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime,
                               DateTime? lastWriteTime, DokanFileInfo info);

        DokanError DeleteFile(string fileName, DokanFileInfo info);

        DokanError DeleteDirectory(string fileName, DokanFileInfo info);

        DokanError MoveFile(string oldName, string newName, bool replace, DokanFileInfo info);

        DokanError SetEndOfFile(string fileName, long length, DokanFileInfo info);

        DokanError SetAllocationSize(string fileName, long length, DokanFileInfo info);

        DokanError LockFile(string fileName, long offset, long length, DokanFileInfo info);

        DokanError UnlockFile(string fileName, long offset, long length, DokanFileInfo info);

        DokanError GetDiskFreeSpace(ref ulong available, ref ulong total, ref ulong free,
                                    DokanFileInfo info);

        DokanError GetVolumeInformation(ref string volumeLabel, ref uint serialNumber, ref FileSystemFeature features,
                                        ref string fileSystemName, DokanFileInfo info);

        DokanError GetFileSecurity(string fileName, ref FileSystemSecurity security, AccessControlSections sections,
                                   DokanFileInfo info);

        DokanError SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections,
                                   DokanFileInfo info);

        DokanError Unmount(DokanFileInfo info);
    }
}
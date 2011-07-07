using System;
using System.IO;
using System.Security.AccessControl;

namespace DokanNet
{
    public interface IDokanOperations
    {
        DokanError CreateFile(string filename, FileAccess access, FileShare share, FileMode mode,
                              FileOptions flags, FileAttributes attributes, DokanFileInfo info);

        DokanError OpenDirectory(string filename, DokanFileInfo info);

        DokanError CreateDirectory(string filename, DokanFileInfo info);

        DokanError Cleanup(string filename, DokanFileInfo info);

        DokanError CloseFile(string filename, DokanFileInfo info);

        DokanError ReadFile(string filename, byte[] buffer, ref uint bytesread, long offset,
                            DokanFileInfo info);

        DokanError WriteFile(string filename, byte[] buffer, ref uint byteswritten,
                             long offset, DokanFileInfo info);

        DokanError FlushFileBuffers(string filename, DokanFileInfo info);

        DokanError GetFileInformation(string filename, ref FileInformation fileinfo, DokanFileInfo info);

        DokanError FindFiles(string filename, ref FileInformation[] files, DokanFileInfo info);

        DokanError SetFileAttributes(string filename, FileAttributes attributes, DokanFileInfo info);

        DokanError SetFileTime(string filename, DateTime? creationtime, DateTime? lastaccesstime,
                               DateTime? lastwritetime, DokanFileInfo info);

        DokanError DeleteFile(string filename, DokanFileInfo info);

        DokanError DeleteDirectory(string filename, DokanFileInfo info);

        DokanError MoveFile(string oldname, string newname, bool replace, DokanFileInfo info);

        DokanError SetEndOfFile(string filename, long length, DokanFileInfo info);

        DokanError SetAllocationSize(string filename, long length, DokanFileInfo info);

        DokanError LockFile(string filename, long offset, long length, DokanFileInfo info);

        DokanError UnlockFile(string filename, long offset, long length, DokanFileInfo info);

        DokanError GetDiskFreeSpace(ref ulong available, ref ulong total, ref ulong free,
                                    DokanFileInfo info);

        DokanError GetFileSecurity(string filename, ref FileSystemSecurity security, AccessControlSections sections,
                                   DokanFileInfo info);

        DokanError SetFileSecurity(string filename, FileSystemSecurity security, AccessControlSections sections,
                                   DokanFileInfo info);

        DokanError Unmount(DokanFileInfo info);
    }
}
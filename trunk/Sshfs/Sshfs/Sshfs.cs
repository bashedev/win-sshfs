// Copyright (c) 2011 Dragan Mladjenovic
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.


using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Caching;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using DokanNet;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using FileAccess = DokanNet.FileAccess;


namespace Sshfs
{
    internal class Sshfs : BaseClient, IDokanOperations
    {
        #region Constants

        // ReSharper disable InconsistentNaming
        private static readonly string[] _filter = {
                                                       ".svn", ".hg", "desktop.ini", "Desktop.ini", "autorun.inf",
                                                       "AutoRun.inf", "Thumbs.db",
                                                   };

        private static readonly Regex _dfregex = new Regex(@"^[a-z0-9/]+\s+(?<blocks>[0-9]+)K\s+(?<used>[0-9]+)K"
                                                           , RegexOptions.Compiled);

        // ReSharper restore InconsistentNaming 

        #endregion

        #region Fields

        private readonly MemoryCache _dirlistcache = new MemoryCache("dirlistcache");

        private readonly TimeSpan _operationTimeout = new TimeSpan(0, 0, 0, 0, -1);
        private string _root;
        private readonly bool _useofflineatribute;
        private readonly bool _debug;
        private SftpSession _generalSftpSession;
        private SftpSession _rwSftpSession;
        private ulong _nexthandle = 1;
        private int _userId = -1;
        private int[] _userGroups;


        private ConcurrentDictionary<ulong, SftpNode> _openedfiles =
            new ConcurrentDictionary<ulong, SftpNode>();

        private ConcurrentDictionary<string, string> _linkmap = new ConcurrentDictionary<string, string>();


        // private const ulong _rwmode = 0xe0010027;
        // private const ulong _wmode = 0x40010006;

        #endregion

        #region Constructors

        public Sshfs(ConnectionInfo connectionInfo, string root, bool useofflineatribute = false, bool debug = false)
            : base(connectionInfo)
        {
            _root = root;
            _useofflineatribute = useofflineatribute;
            _debug = debug;
        }

        #endregion

        #region Method overrides

        protected override void OnConnected()
        {
            base.OnConnected();
            _generalSftpSession = new SftpSession(Session, _operationTimeout);
            _rwSftpSession = new SftpSession(Session, _operationTimeout);
            _generalSftpSession.Connect();
            _rwSftpSession.Connect();
            _userId = GetUserId();
            _userGroups = GetUserGroups();
            if (String.IsNullOrEmpty(_root))
            {
                _root = _generalSftpSession.GetRealPath(".");
            }
            // KeepAliveInterval=TimeSpan.FromSeconds(5);
            // Session.Disconnected+= (sender, args) => Environment.Exit(0);
        }

        protected override void Dispose(bool disposing)
        {
            if (_openedfiles != null)
            {
                foreach (var file in _openedfiles)
                {
                    file.Value.Dispose();
                }
                _openedfiles = null;
            }

            if (_dirlistcache != null) _dirlistcache.Dispose();
            if (_rwSftpSession != null)
            {
                _rwSftpSession.Dispose();
                _rwSftpSession = null;
            }
            if (_generalSftpSession != null)
            {
                _generalSftpSession.Dispose();
                _generalSftpSession = null;
            }
            base.Dispose(disposing);
        }

        #endregion

        #region  Methods

        internal string GetUnixPath(string path)
        {
            return String.Format("{0}{1}", _root, path.Replace('\\', '/'));
        }

        internal string GetLinkTarget(string link)
        {
            using (var cmd = new ReadLinkCommand(_generalSftpSession, link))
            {
                cmd.Execute();
                return cmd.Path;
            }
        }

        internal void CloseAndRemove(DokanFileInfo info)
        {
            if (info.Context != 0)
            {
                SftpNode file;
                if (_openedfiles.TryRemove(info.Context, out file))
                    file.Release();

                info.Context = 0;
            }
        }

        [Conditional("DEBUG")]
        internal void Log(string format, params object[] arg)
        {
            if (_debug)
            {
                Console.WriteLine(format, arg);
                Debug.Print(String.Format(format, arg));
            }
        }

        internal int[] GetUserGroups()
        {
            using (var cmd = new SshCommand(Session, "id -G ", Encoding.ASCII))
            {
                try
                {
                    cmd.Execute();
                    return
                        cmd.Result.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries).Select(
                            group => Convert.ToInt32(group)).ToArray();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return null;
                }
            }
        }

        internal int GetUserId()
        {
            using (var cmd = new SshCommand(Session, "id -u ", Encoding.ASCII))
            {
                try
                {
                    cmd.Execute();
                    return Convert.ToInt32(cmd.Result);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return -1;
                }
            }
        }

        internal bool UserCanRead(SftpFileAttributes attributes)
        {
            if (_userId == -1) return true;
            return attributes.OwnerCanRead && attributes.UserId == _userId ||
                   (attributes.GroupCanRead && _userGroups.Contains(attributes.GroupId) || attributes.OthersCanRead);
        }

        internal bool UserCanWrite(SftpFileAttributes attributes)
        {
            if (_userId == -1) return true;
            return attributes.OwnerCanWrite && attributes.UserId == _userId ||
                   (attributes.GroupCanWrite && _userGroups.Contains(attributes.GroupId) || attributes.OthersCanWrite);
        }

        internal bool UserCanExecute(SftpFileAttributes attributes)
        {
            if (_userId == -1) return true;
            return attributes.OwnerCanExecute && attributes.UserId == _userId ||
                   (attributes.GroupCanExecute && _userGroups.Contains(attributes.GroupId) ||
                    attributes.OthersCanExecute);
        }

        internal void GetFreeSpaceFromDf(ref ulong total, ref ulong free, ref ulong available)
        {
            using (var cmd = new SshCommand(Session, String.Format(" df -Bk  {0}", _root), Encoding.ASCII))
            {
                try
                {
                    cmd.Execute();
                    using (var reader = new StringReader(cmd.Result))
                    {
                        reader.ReadLine();
                        Match match = _dfregex.Match(reader.ReadLine());
                        total = Convert.ToUInt64(match.Groups["blocks"].Value)*1024;
                        free = Convert.ToUInt64(match.Groups["used"].Value)*1024;
                        available = total - free;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    total = 0;
                    free = 0;
                    available = 0;
                }
            }
        }

        #endregion

        #region DokanOperations

        DokanError IDokanOperations.CreateFile(string filename, FileAccess access, FileShare share,
                                               FileMode creationDisposition, FileOptions flags,
                                               FileAttributes attributes, DokanFileInfo info)
        {
            if (_filter.Any(filename.Contains))
            {
                return DokanError.ErrorFileNotFound;
            }
            //    if (filename.Equals("\\")) { info.IsDirectory = true; info.Context = _nexthandle++; return DokanError.ErrorSuccess; }

            string path = GetUnixPath(filename);

            SftpFileAttributes sftpattributes;
            try
            {
                sftpattributes = _generalSftpSession.GetFileAttributes(path);
            }
            catch (SftpPermissionDeniedException)
            {
                return DokanError.ErrorAccessDenied;
            }

            Log("Open| Name:{0},\n Mode:{1},\n Share{2},\n Disp:{3},\n Flags{4},\n Attr:{5}\n", filename, access,
                share, creationDisposition, flags, attributes);


            switch (creationDisposition)
            {
                case FileMode.Open:
                    if (sftpattributes != null)
                    {
                        info.Context = _nexthandle++;
                        if (((uint) access & 0xe0000027) == 0 || sftpattributes.IsDirectory)
                            //check if only wants to read attributes,security info or open directory
                        {
                            info.IsDirectory = sftpattributes.IsDirectory;
                            Log("JustInfo -{0}", filename);
                            _openedfiles.TryAdd(info.Context, new SftpNode(sftpattributes));
                            return DokanError.ErrorSuccess;
                        }
                        break;
                    }
                    return DokanError.ErrorFileNotFound;
                case FileMode.CreateNew:
                    if (sftpattributes != null)
                        return DokanError.ErrorAlreadyExists;
                    info.Context = _nexthandle++;
                    break;
                case FileMode.Create:
                    creationDisposition = sftpattributes == null
                                              ? FileMode.CreateNew
                                              : FileMode.Truncate;
                    info.Context = _nexthandle++;
                    break;
                case FileMode.OpenOrCreate:
                case FileMode.Append:
                    info.Context = _nexthandle++;
                    break;
                case FileMode.Truncate:
                    if (sftpattributes == null)
                        return DokanError.ErrorFileNotFound;
                    break;
                default:
                    break;
            }
            Log("NotJustInfo:{0}-{1}", info.Context, creationDisposition);

            try
            {
                _openedfiles.TryAdd(info.Context,
                                    new SftpNode(new SftpSizableBufferStream(_rwSftpSession, path, creationDisposition,
                                                                             ((ulong) access & 0x40010006) == 0
                                                                                 ? System.IO.FileAccess.Read
                                                                                 : System.IO.FileAccess.ReadWrite,
                                                                             1024*1024,
                                                                             sftpattributes)));
            }
            catch (SftpPermissionDeniedException)
            {
                return DokanError.ErrorAccessDenied;
            }


            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.OpenDirectory(string filename, DokanFileInfo info)
        {
            Log("OpenDir:{0}", filename);
            SftpFileAttributes sftpattributes;
            try
            {
                sftpattributes = _generalSftpSession.GetFileAttributes(GetUnixPath(filename));
            }
            catch (SftpPermissionDeniedException)
            {
                return DokanError.ErrorAccessDenied;
            }


            if (sftpattributes != null && sftpattributes.IsDirectory)
            {
                if (!UserCanExecute(sftpattributes))
                    return DokanError.ErrorAccessDenied;


                info.Context = _nexthandle++;
                info.IsDirectory = true;
                _openedfiles.TryAdd(info.Context, new SftpNode(sftpattributes));
                if (_dirlistcache.Contains(filename) &&
                    ((Tuple<DateTime, FileInformation[]>) _dirlistcache[filename]).Item1 !=
                    sftpattributes.LastWriteTime)
                    _dirlistcache.Remove(filename);
                return DokanError.ErrorSuccess;
            }
            return DokanError.ErrorPathNotFound;
        }

        DokanError IDokanOperations.CreateDirectory(string filename, DokanFileInfo info)
        {
            Log("CreateDir:{0}", filename);

            using (
                var cmd = new CreateDirectoryCommand(_generalSftpSession, GetUnixPath(filename))
                              {CommandTimeout = _operationTimeout})
            {
                try
                {
                    cmd.Execute();
                }
                catch (SftpPermissionDeniedException)
                {
                    return DokanError.ErrorAccessDenied;
                }
                catch (SshException)
                {
                    return DokanError.ErrorAlreadyExists;
                }
            }
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.Cleanup(string filename, DokanFileInfo info)
        {
            Console.WriteLine("Cleanup:{0}", info.Context);
            CloseAndRemove(info);

            if (info.DeleteOnClose || filename.Contains(".~lock.")) //ugly oo hack
            {
                using (
                    SftpCommand cmd = info.IsDirectory
                                          ? (SftpCommand)
                                            new RemoveDirectoryCommand(_generalSftpSession, GetUnixPath(filename))
                                          : new RemoveFileCommand(_generalSftpSession, GetUnixPath(filename)))
                {
                    cmd.CommandTimeout = _operationTimeout;
                    cmd.Execute();
                }
            }

            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.CloseFile(string filename, DokanFileInfo info)
        {
            Console.WriteLine("Close:{0}", info.Context);
            CloseAndRemove(info);
            return DokanError.ErrorSuccess;
        }


        DokanError IDokanOperations.ReadFile(string filename, byte[] buffer, ref uint readLength, long offset,
                                             DokanFileInfo info)
        {
            Console.WriteLine("READ:{0}:{1}|lenght:[{2}]|offset:[{3}]", filename, info.Context, buffer.Length, offset);

            if (info.Context == 0)
            {
                using (
                    var stream = new SftpFileStream(_rwSftpSession, GetUnixPath(filename), FileMode.Open,
                                                    System.IO.FileAccess.Read, buffer.Length))
                {
                    stream.Seek(offset, SeekOrigin.Begin);
                    readLength = (uint) stream.Read(buffer, 0, buffer.Length);
                }
            }
            else
            {
                Stream stream = _openedfiles[info.Context].Content;
                stream.Seek(offset, SeekOrigin.Begin);
                readLength = (uint) stream.Read(buffer, 0, buffer.Length);
            }

            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.WriteFile(string filename, byte[] buffer, ref uint numberOfBytesWritten, long offset,
                                              DokanFileInfo info)
        {
            Console.WriteLine("WRITE:{0}:{1}|lenght:[{2}]|offset:[{3}]", filename, info.Context, buffer.Length, offset);
            var stream = _openedfiles[info.Context].Content;
            stream.Seek(offset, SeekOrigin.Begin);
            stream.Write(buffer, 0, buffer.Length);
            //    stream.Flush();
            numberOfBytesWritten = (uint) buffer.Length;
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.FlushFileBuffers(string filename, DokanFileInfo info)
        {
            Log("FLUSH:{0}", filename);
            _openedfiles[info.Context].Content.Flush();

            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.GetFileInformation(string filename, ref FileInformation fileinfo,
                                                       DokanFileInfo info)
        {
            Log("GetInfo:{0}", filename);
            SftpFileAttributes sftpattributes = _openedfiles[info.Context].Attributes;
            fileinfo = new FileInformation
                           {
                               Attributes =
                                   FileAttributes.NotContentIndexed | FileAttributes.Offline,
                               CreationTime = sftpattributes.LastWriteTime,
                               FileName = String.Empty,
                               // GetInfo info doesn't use it maybe for sorting
                               LastAccessTime = sftpattributes.LastAccessTime,
                               LastWriteTime = sftpattributes.LastWriteTime,
                               Length = sftpattributes.Size
                           };
            if (sftpattributes.IsDirectory)
            {
                fileinfo.Attributes |= FileAttributes.Directory;
                fileinfo.Length = 0;
            }
            if (filename.Length != 1 && filename[filename.LastIndexOf('\\') + 1] == '.')
            {
                fileinfo.Attributes |= FileAttributes.Hidden;
            }
            if (_useofflineatribute)
            {
                fileinfo.Attributes |= FileAttributes.Offline;
            }
            if (!UserCanWrite(sftpattributes))
            {
                fileinfo.Attributes |= FileAttributes.ReadOnly;
            }
            //  Console.WriteLine(sftpattributes.UserId + "|" + sftpattributes.GroupId + "L" +
            //  sftpattributes.OthersCanExecute + "K" + sftpattributes.OwnerCanExecute);
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.FindFiles(string filename, ref FileInformation[] dokanFiles, DokanFileInfo info)
        {
            Log("FindFiles:{0}", filename);
            if (_dirlistcache.Contains(filename))
            {
                dokanFiles = ((Tuple<DateTime, FileInformation[]>) _dirlistcache[filename]).Item2;
                Log("CacheHit:{0}", filename);
                return DokanError.ErrorSuccess;
            }

            using (
                var cmd = new ListDirectoryCommand(_generalSftpSession, GetUnixPath(filename))
                              {CommandTimeout = _operationTimeout})
            {
                try
                {
                    cmd.Execute();
                }
                catch (SftpPermissionDeniedException)
                {
                    return DokanError.ErrorAccessDenied;
                }


                dokanFiles = cmd.Files.Where(
                    file => !Equals(file.Name, ".") && !Equals(file.Name, "..")).Select(file =>
                                                                                            {
                                                                                                var fileinfo = new FileInformation
                                                                                                                   {
                                                                                                                       Attributes
                                                                                                                           =
                                                                                                                           FileAttributes
                                                                                                                           .
                                                                                                                           NotContentIndexed,
                                                                                                                       CreationTime
                                                                                                                           =
                                                                                                                           file
                                                                                                                           .
                                                                                                                           LastWriteTime,
                                                                                                                       FileName
                                                                                                                           =
                                                                                                                           file
                                                                                                                           .
                                                                                                                           Name,
                                                                                                                       LastAccessTime
                                                                                                                           =
                                                                                                                           file
                                                                                                                           .
                                                                                                                           LastAccessTime,
                                                                                                                       LastWriteTime
                                                                                                                           =
                                                                                                                           file
                                                                                                                           .
                                                                                                                           LastWriteTime,
                                                                                                                       Length
                                                                                                                           =
                                                                                                                           file
                                                                                                                           .
                                                                                                                           Size
                                                                                                                   };
                                                                                                if (file.IsDirectory)
                                                                                                {
                                                                                                    fileinfo.Attributes
                                                                                                        |=
                                                                                                        FileAttributes.
                                                                                                            Directory;
                                                                                                    fileinfo.Length = 0;
                                                                                                }
                                                                                                if (file.Name[0] == '.')
                                                                                                {
                                                                                                    fileinfo.Attributes
                                                                                                        |=
                                                                                                        FileAttributes.
                                                                                                            Hidden;
                                                                                                }
                                                                                                if (_useofflineatribute)
                                                                                                {
                                                                                                    fileinfo.Attributes
                                                                                                        |=
                                                                                                        FileAttributes.
                                                                                                            Offline;
                                                                                                }
                                                                                                if (
                                                                                                    !UserCanWrite(
                                                                                                        file.Attributes))
                                                                                                {
                                                                                                    fileinfo.Attributes
                                                                                                        |=
                                                                                                        FileAttributes.
                                                                                                            ReadOnly;
                                                                                                }
                                                                                                return fileinfo;
                                                                                            }).ToArray();
            }
            _dirlistcache.Add(filename, new Tuple<DateTime, FileInformation[]>(
                                            _openedfiles[info.Context].Attributes.LastWriteTime,
                                            dokanFiles), ObjectCache.InfiniteAbsoluteExpiration);
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.SetFileAttributes(string filename, FileAttributes attr, DokanFileInfo info)
        {
            Log("TrySetAttributes:{0}\n{1};", filename, attr);
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.SetFileTime(string filename, DateTime? creationTime, DateTime? lastAccessTime,
                                                DateTime? lastWriteTime, DokanFileInfo info)
        {
            Log(string.Format("TrySetFileTime:{0}\n|c:{1}\n|a:{2}\n|w:{3}", filename, creationTime, lastAccessTime,
                              lastWriteTime));
            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.DeleteFile(string filename, DokanFileInfo info)
        {
            Log("DeleteFile:{0}", filename);

            return String.IsNullOrEmpty(_generalSftpSession.GetRealPath(GetUnixPath(filename)))
                       ? DokanError.ErrorPathNotFound
                       : DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.DeleteDirectory(string filename, DokanFileInfo info)
        {
            Log("DeleteDirectory:{0}", filename);

            using (
                var cmd = new IsDirEmptyCommand(_generalSftpSession, GetUnixPath(filename))
                              {CommandTimeout = _operationTimeout})
            {
                cmd.Execute();

                if (cmd.IsEmpty.HasValue)
                {
                    return cmd.IsEmpty == false ? DokanError.ErrorDirNotEmpty : DokanError.ErrorSuccess;
                }
                return DokanError.ErrorPathNotFound;
            }
        }

        DokanError IDokanOperations.MoveFile(string filename, string newname, bool replace, DokanFileInfo info)
        {
            Log("MoveFile |Name:{0} ,NewName:{3},Reaplace{4},IsDirectory:{1} ,Context:{2}",
                filename, info.IsDirectory,
                info.Context, newname, replace);
            string oldpath = GetUnixPath(filename);
            if (_generalSftpSession.GetFileAttributes(oldpath) == null)
                return DokanError.ErrorPathNotFound;
            if (filename.Equals(newname))
                return DokanError.ErrorSuccess;

            string newpath = GetUnixPath(newname);

            if (replace || _generalSftpSession.GetFileAttributes(newpath) == null)
            {
                CloseAndRemove(info);
                using (
                    var cmd = new RenameFileCommand(_generalSftpSession, oldpath, newpath)
                                  {CommandTimeout = _operationTimeout}) //not tested onn sftp3
                {
                    cmd.Execute();
                }
                return DokanError.ErrorSuccess;
            }
            return DokanError.ErrorFileExists;
        }

        DokanError IDokanOperations.SetEndOfFile(string filename, long length, DokanFileInfo info)
        {
            _openedfiles[info.Context].Content.SetLength(length);
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.SetAllocationSize(string filename, long length, DokanFileInfo info)
        {
            _openedfiles[info.Context].Content.SetLength(length);
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.LockFile(string filename, long offset, long length, DokanFileInfo info)
        {
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.UnlockFile(string filename, long offset, long length, DokanFileInfo info)
        {
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.GetDiskFreeSpace(ref ulong freeBytesAvailable, ref ulong totalBytes,
                                                     ref ulong totalFreeBytes, DokanFileInfo info)
        {
            GetFreeSpaceFromDf(ref totalBytes, ref totalFreeBytes, ref freeBytesAvailable);
            if (totalBytes == 0)
            {
                totalBytes = 0x1900000000;
                freeBytesAvailable = totalBytes/2;
                totalFreeBytes = totalBytes/2;
            }
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.GetFileSecurity(string filename, ref FileSystemSecurity security,
                                                    AccessControlSections sections, DokanFileInfo info)
        {
            Log("GetSecurrityInfo:{0}:{1}", filename, sections);
            security = info.IsDirectory ? (FileSystemSecurity) new DirectorySecurity() : new FileSecurity();

            security.SetAccessRule(new FileSystemAccessRule("Everyone", FileSystemRights.FullControl,
                                                            AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule("Everyone",
                                                            FileSystemRights.WriteAttributes |
                                                            FileSystemRights.WriteExtendedAttributes |
                                                            FileSystemRights.ChangePermissions, AccessControlType.Deny));
            security.SetOwner(new NTAccount(Environment.UserDomainName, Environment.UserName));
            security.SetGroup(new NTAccount("Users"));
            var sftpattributes = _openedfiles[info.Context].Attributes;

            if (!UserCanWrite(sftpattributes))
            {
                security.AddAccessRule(new FileSystemAccessRule("Everyone", FileSystemRights.Write,
                                                                AccessControlType.Deny));
            }
            if (!UserCanRead(sftpattributes))
            {
                security.AddAccessRule(new FileSystemAccessRule("Everyone", FileSystemRights.Read,
                                                                AccessControlType.Deny));
            }
            if (!UserCanExecute(sftpattributes) && info.IsDirectory)
            {
                security.AddAccessRule(new FileSystemAccessRule("Everyone",
                                                                FileSystemRights.Traverse |
                                                                FileSystemRights.ListDirectory |
                                                                FileSystemRights.ReadData, AccessControlType.Deny));
            }
            //not shure tis works at all needs testing
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.SetFileSecurity(string filename, FileSystemSecurity security,
                                                    AccessControlSections sections, DokanFileInfo info)
        {
            Log("TrySetSecurity:{0}", filename);

            return DokanError.ErrorAccessDenied;
        }

        DokanError IDokanOperations.Unmount(DokanFileInfo info)
        {
            Log("UNMOUNT");
            Disconnect();
            return DokanError.ErrorSuccess;
        }

        #endregion

        #region Events

        public event EventHandler<EventArgs> Disconnected
        {
            add { Session.Disconnected += value; }
            remove { Session.Disconnected -= value; }
        }

        #endregion
    }
}
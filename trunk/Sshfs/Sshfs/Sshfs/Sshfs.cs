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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Caching;
using System.Security.AccessControl;
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
                                                       "AutoRun.inf",// "Thumbs.db",
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
                _root = _generalSftpSession.RequestRealPath(".").Keys.First();
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
            using (var cmd = new SshCommand(Session, "id -G ", Encoding.ASCII)) //Only tested on Ubuntu
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
            using (var cmd = new SshCommand(Session, "id -u ", Encoding.ASCII)) // Only tested on Ubuntu 
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

        internal bool GetFreeSpaceFromDf(ref ulong total, ref ulong free, ref ulong available)
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
                    return true;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return false;
                }
            }
        }

        #endregion

        #region DokanOperations

        DokanError IDokanOperations.CreateFile(string fileName, FileAccess access, FileShare share,
                                               FileMode mode, FileOptions options,
                                               FileAttributes attributes, DokanFileInfo info)
        {
            if (_filter.Any(fileName.Contains))
            {
                return DokanError.ErrorFileNotFound;
            }

            string path = GetUnixPath(fileName);

            var sftpattributes = _generalSftpSession.RequestLStat(path, true);


            Log("Open| Name:{0},\n Mode:{1},\n Share{2},\n Disp:{3},\n Flags{4},\n Attr:{5}\n", fileName, access,
                share, mode, options, attributes);


            switch (mode)
            {
                case FileMode.Open:
                    if (sftpattributes != null)
                    {
                        info.Context = _nexthandle++;
                        if (((uint) access & 0xe0000027) == 0 || sftpattributes.IsDirectory)
                            //check if only wants to read attributes,security info or open directory
                        {
                            info.IsDirectory = sftpattributes.IsDirectory;
                            Log("JustInfo:{0}", fileName);
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
            Log("NotJustInfo:{0}-{1}", info.Context, mode);

            try
            {
                _openedfiles.TryAdd(info.Context,
                                    new SftpNode(new SftpNodeStream(_rwSftpSession, path, mode,
                                                                    ((ulong) access & 0x40010006) == 0
                                                                        ? System.IO.FileAccess.Read
                                                                        : System.IO.FileAccess.ReadWrite,
                                                                    sftpattributes)));
            }
            catch (SftpPermissionDeniedException)
            {
                return DokanError.ErrorAccessDenied;
            }


            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.OpenDirectory(string fileName, DokanFileInfo info)
        {
            Log("OpenDir:{0}", fileName);

            var sftpattributes = _generalSftpSession.RequestLStat(GetUnixPath(fileName), true);


            if (sftpattributes != null && sftpattributes.IsDirectory)
            {
                if (!UserCanExecute(sftpattributes)) //if it doesn't fail here when it shoud if will fail on find files
                    return DokanError.ErrorAccessDenied;


                info.Context = _nexthandle++;
                info.IsDirectory = true;
                _openedfiles.TryAdd(info.Context, new SftpNode(sftpattributes));

                if (_dirlistcache.Contains(fileName) &&
                    ((Tuple<DateTime, FileInformation[]>) _dirlistcache[fileName]).Item1 !=
                    sftpattributes.LastWriteTime)
                    _dirlistcache.Remove(fileName);
                return DokanError.ErrorSuccess;
            }
            return DokanError.ErrorPathNotFound;
        }

        DokanError IDokanOperations.CreateDirectory(string fileName, DokanFileInfo info)
        {
            Log("CreateDir:{0}", fileName);


            try
            {
                _generalSftpSession.RequestMkDir(GetUnixPath(fileName));
            }
            catch (SftpPermissionDeniedException)
            {
                return DokanError.ErrorAccessDenied;
            }
            catch (SshException) // operatin should fail with generic error if file already exists
            {
                return DokanError.ErrorAlreadyExists;
            }

            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.Cleanup(string fileName, DokanFileInfo info)
        {
            Log("Cleanup:{0}", info.Context);
            CloseAndRemove(info);

            if (info.DeleteOnClose )
            {
                if (info.IsDirectory)
                {
                    _generalSftpSession.RequestRmDir(GetUnixPath(fileName));
                }
                else
                {
                    _generalSftpSession.RequestRemove(GetUnixPath(fileName));
                }
            }

            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.CloseFile(string fileName, DokanFileInfo info)
        {
            Log("Close:{0}", info.Context);
            CloseAndRemove(info);
            return DokanError.ErrorSuccess;
        }


        DokanError IDokanOperations.ReadFile(string fileName, byte[] buffer, ref uint bytesRead, long offset,
                                             DokanFileInfo info)
        {
            Log("READ:{0}:{1}|lenght:[{2}]|offset:[{3}]", fileName, info.Context, buffer.Length, offset);

            if (info.Context == 0)
            {
                //called when file is read as memory memory mapeded file usualy notepad and stuff
                using (
                    var stream = new SftpNodeStream(_rwSftpSession, GetUnixPath(fileName), FileMode.Open,
                                                    System.IO.FileAccess.Read
                                                    , buffer.Length))
                {
                    stream.Seek(offset, SeekOrigin.Begin);
                    bytesRead = (uint) stream.Read(buffer, 0, buffer.Length);
                }
            }
            else
            {
                Stream stream = _openedfiles[info.Context].Content;
                stream.Seek(offset, SeekOrigin.Begin);
                bytesRead = (uint) stream.Read(buffer, 0, buffer.Length);
            }

            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.WriteFile(string fileName, byte[] buffer, ref uint bytesWritten, long offset,
                                              DokanFileInfo info)
        {
            Console.WriteLine("WRITE:{0}:{1}|lenght:[{2}]|offset:[{3}]", fileName, info.Context, buffer.Length, offset);
            var stream = _openedfiles[info.Context].Content;
            stream.Seek(offset, SeekOrigin.Begin);
            stream.Write(buffer, 0, buffer.Length);
            //    stream.Flush();
            bytesWritten = (uint) buffer.Length;
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.FlushFileBuffers(string fileName, DokanFileInfo info)
        {
            Log("FLUSH:{0}", fileName);
            _openedfiles[info.Context].Content.Flush();//I newer saw it get caled ,but ..

            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.GetFileInformation(string fileName, ref FileInformation fileInfo,
                                                       DokanFileInfo info)
        {
            Log("GetInfo:{0}", fileName);
            SftpFileAttributes sftpattributes = _openedfiles[info.Context].Attributes;
            fileInfo = new FileInformation
                           {
                               Attributes =
                                   FileAttributes.NotContentIndexed | FileAttributes.Offline,
                               CreationTime = sftpattributes.LastWriteTime,
                               FileName = String.Empty,
                               // GetInfo info doesn't use it maybe for sorting .                             
                               LastAccessTime = sftpattributes.LastAccessTime,
                               LastWriteTime = sftpattributes.LastWriteTime,
                               Length = sftpattributes.Size
                           };
            if (sftpattributes.IsDirectory)
            {
                fileInfo.Attributes |= FileAttributes.Directory;
                fileInfo.Length = 0; // Windows directories use length of 0 
            }
            if (fileName.Length != 1 && fileName[fileName.LastIndexOf('\\') + 1] == '.')
                //aditional check if filename isn't \\
            {
                fileInfo.Attributes |= FileAttributes.Hidden;
            }
            if (_useofflineatribute)
            {
                fileInfo.Attributes |= FileAttributes.Offline;
            }
            if (!UserCanWrite(sftpattributes))
            {
                fileInfo.Attributes |= FileAttributes.ReadOnly;
            }
            //  Console.WriteLine(sftpattributes.UserId + "|" + sftpattributes.GroupId + "L" +
            //  sftpattributes.OthersCanExecute + "K" + sftpattributes.OwnerCanExecute);
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.FindFiles(string fileName, ref FileInformation[] files, DokanFileInfo info)
        {
            Log("FindFiles:{0}", fileName);
            if (_dirlistcache.Contains(fileName))
            {
                files = ((Tuple<DateTime, FileInformation[]>) _dirlistcache[fileName]).Item2;
                Log("CacheHit:{0}", fileName);
                return DokanError.ErrorSuccess;
            }


            byte[] handle;
            try
            {
                handle = _generalSftpSession.RequestOpenDir(GetUnixPath(fileName));
            }
            catch (SftpPermissionDeniedException)
            {
                return DokanError.ErrorAccessDenied;
            }

            var informations = new List<FileInformation>();
            var sftpfiles = _generalSftpSession.RequestReadDir(handle);

            sftpfiles.Remove(".");
            sftpfiles.Remove("..");


            while (sftpfiles != null)
            {
                informations.AddRange(sftpfiles.Select(file =>
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
                                                                                      file.Value
                                                                                      .
                                                                                      LastWriteTime,
                                                                                  FileName
                                                                                      =
                                                                                      file.Key
                                                                                  ,
                                                                                  LastAccessTime
                                                                                      =
                                                                                      file.Value
                                                                                      .
                                                                                      LastAccessTime,
                                                                                  LastWriteTime
                                                                                      =
                                                                                      file.Value
                                                                                      .
                                                                                      LastWriteTime,
                                                                                  Length
                                                                                      =
                                                                                      file.Value
                                                                                      .
                                                                                      Size
                                                                              };
                                                           if (file.Value.IsDirectory)
                                                           {
                                                               fileinfo.Attributes
                                                                   |=
                                                                   FileAttributes.
                                                                       Directory;
                                                               fileinfo.Length = 0;
                                                           }
                                                           if (file.Key[0] == '.')
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
                                                                   file.Value))
                                                           {
                                                               fileinfo.Attributes
                                                                   |=
                                                                   FileAttributes.
                                                                       ReadOnly;
                                                           }
                                                           return fileinfo;
                                                       }));
                
                sftpfiles = _generalSftpSession.RequestReadDir(handle);
            }
            _generalSftpSession.RequestClose(handle);
            files = informations.ToArray();


            _dirlistcache.Add(fileName, new Tuple<DateTime, FileInformation[]>(
                                            _openedfiles[info.Context].Attributes.LastWriteTime,
                                            files), ObjectCache.InfiniteAbsoluteExpiration);

            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.SetFileAttributes(string fileName, FileAttributes attributes, DokanFileInfo info)
        {
            Log("TrySetAttributes:{0}\n{1};", fileName, attributes);
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.SetFileTime(string filename, DateTime? creationTime, DateTime? lastAccessTime,
                                                DateTime? lastWriteTime, DokanFileInfo info)
        {
            Log(string.Format("TrySetFileTime:{0}\n|c:{1}\n|a:{2}\n|w:{3}", filename, creationTime, lastAccessTime,
                              lastWriteTime));
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.DeleteFile(string fileName, DokanFileInfo info)
        {
            Log("DeleteFile:{0}", fileName);

            return String.IsNullOrEmpty(_generalSftpSession.RequestRealPath(GetUnixPath(fileName)).Keys.First())
                       ? DokanError.ErrorPathNotFound
                       : DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.DeleteDirectory(string fileName, DokanFileInfo info)
        {
            Log("DeleteDirectory:{0}", fileName);


            var handle = _generalSftpSession.RequestOpenDir(GetUnixPath(fileName),true);

            if (handle == null)
                return DokanError.ErrorPathNotFound;

            bool empty = _generalSftpSession.RequestReadDir(handle).Keys.Count <= 2;
                // usualy there are two entries . and ..
            _generalSftpSession.RequestClose(handle);
            return empty ? DokanError.ErrorSuccess: DokanError.ErrorDirNotEmpty ;
        }

        DokanError IDokanOperations.MoveFile(string oldName, string newName, bool replace, DokanFileInfo info)
        {
            Log("MoveFile |Name:{0} ,NewName:{3},Reaplace{4},IsDirectory:{1} ,Context:{2}",
                oldName, info.IsDirectory,
                info.Context, newName, replace);
            string oldpath = GetUnixPath(oldName);
            if (_generalSftpSession.RequestLStat(oldpath, true) == null)
                return DokanError.ErrorPathNotFound;
            if (oldName.Equals(newName))
                return DokanError.ErrorSuccess;

            if ( _generalSftpSession.RequestLStat(GetUnixPath(newName), true) == null)
            {
                CloseAndRemove(info);
                _generalSftpSession.RequestRename(oldpath, GetUnixPath(newName));
                return DokanError.ErrorSuccess;
            }
            else if(replace)
            {
                CloseAndRemove(info);
                if(!info.IsDirectory)
                _generalSftpSession.RequestRemove(GetUnixPath(newName));
                _generalSftpSession.RequestRename(oldpath, GetUnixPath(newName));  // not tested on sftp3
                return DokanError.ErrorSuccess;   
            }
            return DokanError.ErrorFileExists;
        }

        DokanError IDokanOperations.SetEndOfFile(string fileName, long length, DokanFileInfo info)
        {
            _openedfiles[info.Context].Content.SetLength(length);
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.SetAllocationSize(string fileName, long length, DokanFileInfo info)
        {
            _openedfiles[info.Context].Content.SetLength(length);
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.LockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.UnlockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.GetDiskFreeSpace(ref ulong available, ref ulong total,
                                                     ref ulong free, DokanFileInfo info)
        {

            if (!GetFreeSpaceFromDf(ref total, ref free, ref available))
            {
                total = 0x1900000000; //100 GiB
                available = total/2;
                free = available;
            }
            return DokanError.ErrorSuccess;
        }

        public DokanError GetVolumeInformation(ref string volumeLabel, ref uint serialNumber, ref FileSystemFeature features, ref string filesystemName, DokanFileInfo info)
        {
            Log("GetVolumeInformation");

            volumeLabel = String.Format("{0} on '{1}'", ConnectionInfo.Username, ConnectionInfo.Host);

            filesystemName = "SSHFS";

            features = FileSystemFeature.CasePreservedNames | FileSystemFeature.CaseSensitiveSearch | FileSystemFeature.PersistentAcls | FileSystemFeature.SupportsRemoteStorage | FileSystemFeature.UnicodeOnDisk;
         
            serialNumber = (uint) String.Format("SSHFS {0}", Assembly.GetEntryAssembly().GetName().Version).GetHashCode();
           
            return DokanError.ErrorSuccess;
        }

        DokanError IDokanOperations.GetFileSecurity(string filename, ref FileSystemSecurity security,
                                                    AccessControlSections sections, DokanFileInfo info)
        {
            Log("GetSecurrityInfo:{0}:{1}", filename, sections);


            var sftpattributes = _openedfiles[info.Context].Attributes;
            var rights = FileSystemRights.ReadPermissions | FileSystemRights.ReadExtendedAttributes |
                         FileSystemRights.ReadAttributes | FileSystemRights.Synchronize;


            if (UserCanRead(sftpattributes))
            {
                rights |= FileSystemRights.ReadData;
            }
            if (UserCanWrite(sftpattributes))
            {
                rights |= FileSystemRights.Write;
            }
            if (UserCanExecute(sftpattributes) && info.IsDirectory)
            {
                rights |= FileSystemRights.Traverse;
            }
            security = info.IsDirectory ? new DirectorySecurity() as FileSystemSecurity : new FileSecurity();

            security.SetAccessRule(new FileSystemAccessRule("Everyone", rights, AccessControlType.Allow));
            //not sure this works at all, needs testing
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
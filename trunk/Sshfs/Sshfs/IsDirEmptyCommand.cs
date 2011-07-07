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
using System.Collections.Generic;
using Renci.SshNet.Sftp;
using Renci.SshNet.Sftp.Messages;

namespace Sshfs
{
    internal class IsDirEmptyCommand : SftpCommand
    {
        private readonly string _path;
        private byte[] _handle;

        public IsDirEmptyCommand(SftpSession sftpSession, string path)
            : base(sftpSession)
        {
            _path = path;
        }

        public bool? IsEmpty { get; private set; }

        protected override void OnExecute()
        {
            SendOpenDirMessage(_path);
        }

        protected override void OnHandle(byte[] handle)
        {
            base.OnHandle(handle);

            _handle = handle;

            SendReadDirMessage(_handle);
        }

        protected override void OnName(IDictionary<string, SftpFileAttributes> files)
        {
            base.OnName(files);
            IsEmpty = files.Count <= 2;
            SendCloseMessage(_handle);
            CompleteExecution();
        }

        protected override void OnStatus(StatusCodes statusCode, string errorMessage, string language)
        {
            base.OnStatus(statusCode, errorMessage, language);

            if (statusCode == StatusCodes.NoSuchFile)
            {
                CompleteExecution();
                IsStatusHandled = true;
            }
        }
    }
}
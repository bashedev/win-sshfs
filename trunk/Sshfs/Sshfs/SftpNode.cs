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
using System.IO;
using Renci.SshNet.Sftp;

namespace Sshfs
{
    internal class SftpNode : IDisposable
    {
        private SftpFileAttributes _attributes;
        private bool _disposed;
        private SftpSizableBufferStream _stream;

        public SftpNode(SftpFileAttributes attributes)
        {
            _attributes = attributes;
        }

        public SftpNode(SftpSizableBufferStream stream)
        {
            _stream = stream;
            _attributes = stream.Attributes;
        }

        public SftpFileAttributes Attributes
        {
            get { return _attributes ?? _stream.Attributes; }
        }

        public Stream Content
        {
            get { return _stream; }
        }

        #region IDisposable Members

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _attributes = null;
                if (disposing)
                {
                    if (_stream != null)
                    {
                        _stream.Flush();
                        _stream.Close();
                        _stream = null;
                    }
                }
                _disposed = true;
            }
        }

        public void Release()
        {
            Dispose();
        }
    }
}
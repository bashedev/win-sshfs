using System;
using System.IO;
using Renci.SshNet.Sftp;

namespace Sshfs
{
    public class SftpNodeStream : Stream
    {
        // Internal state.
        private readonly FileAccess _access;
        private readonly byte[] _buffer;
        private readonly int _bufferSize;
        private readonly object _lock = new object();
        private readonly bool _ownsHandle;
        private readonly SftpSession _session;
        private SftpFileAttributes _attributes;

        // Buffer information.
        private int _bufferLen;
        private bool _bufferOwnedByWrite;
        private int _bufferPosn;
        private byte[] _handle;
        private long _position;

        public SftpNodeStream(SftpSession session, string path, FileMode mode, FileAccess access, int bufferSize)
            : this(session, path, mode, access, null, bufferSize)
        {
        }

        public SftpNodeStream(SftpSession session, string path, FileMode mode, FileAccess access,
                              SftpFileAttributes attributes) : this(session, path, mode, access, attributes, 0)
        {
        }

        public SftpNodeStream(SftpSession session, string path, FileMode mode, FileAccess access,
                              SftpFileAttributes attributes, int bufferSize)
        {
            // Initialize the object state.
            _session = session;
            _access = access;


            Flags flags = Flags.None;

            switch (access)
            {
                case FileAccess.Read:
                    flags |= Flags.Read;
                    break;
                case FileAccess.Write:
                    flags |= Flags.Write;
                    break;
                case FileAccess.ReadWrite:
                    flags |= Flags.Read;
                    flags |= Flags.Write;
                    break;
                default:
                    break;
            }

            switch (mode)
            {
                case FileMode.Append:
                    flags |= Flags.Append;
                    break;
                case FileMode.Create:
                    if (attributes == null)
                    {
                        flags |= Flags.CreateNew;
                    }
                    else
                    {
                        flags |= Flags.Truncate;
                    }
                    break;
                case FileMode.CreateNew:
                    flags |= Flags.CreateNew;
                    break;
                case FileMode.Open:
                    break;
                case FileMode.OpenOrCreate:
                    flags |= Flags.CreateNewOrOpen;
                    break;
                case FileMode.Truncate:
                    flags |= Flags.Truncate;
                    break;
                default:
                    break;
            }


            _handle = _session.RequestOpen(path, flags);

            _attributes = attributes ?? _session.RequestFStat(_handle);

            _ownsHandle = true;
            _bufferSize = bufferSize == 0 ? GetBufferSize(_attributes.Size) : bufferSize;
            _buffer = new byte[_bufferSize];
            _bufferPosn = 0;
            _bufferLen = 0;
            _bufferOwnedByWrite = false;
            _position = 0;

            if (mode == FileMode.Append)
            {
                _position = _attributes.Size;
            }
        }


        public override bool CanRead
        {
            get { return ((_access & FileAccess.Read) != 0); }
        }


        public override bool CanSeek
        {
            get { return true; }
        }


        public override bool CanWrite
        {
            get { return ((_access & FileAccess.Write) != 0); }
        }

        public override long Length
        {
            get
            {
                // Lock down the file stream while we do this.
                lock (_lock)
                {
                    if (_handle == null)
                    {
                        // ECMA says this should be IOException even though
                        // everywhere else uses ObjectDisposedException.
                        throw new IOException("Stream is closed.");
                    }

                    // Flush the write buffer, because it may
                    // affect the length of the stream.
                    if (_bufferOwnedByWrite)
                    {
                        FlushWriteBuffer();
                    }

                    //  Update file attributes
                    _attributes = _session.RequestFStat(_handle);

                    if (Attributes != null && Attributes.Size > -1)
                    {
                        return Attributes.Size;
                    }
                    else
                    {
                        throw new IOException("Seek operation failed.");
                    }
                }
            }
        }


        public override long Position
        {
            get { return _position; }
            set { Seek(value, SeekOrigin.Begin); }
        }


        public SftpFileAttributes Attributes
        {
            get { return _attributes; }
        }


        ~SftpNodeStream()
        {
            Dispose(false);
        }


        public override void Close()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }


        public override void Flush()
        {
            lock (_lock)
            {
                if (_handle != null)
                {
                    if (_bufferOwnedByWrite)
                    {
                        FlushWriteBuffer();
                    }
                    else
                    {
                        FlushReadBuffer();
                    }
                }
                else
                {
                    throw new ObjectDisposedException("Stream is closed.");
                }
            }
        }


        public override int Read(byte[] buffer, int offset, int count)
        {
            int readLen = 0;


            if ((buffer.Length - offset) < count)
            {
                throw new ArgumentException("Invalid array range.");
            }

            // Lock down the file stream while we do this.
            lock (_lock)
            {
                // Set up for the read operation.
                SetupRead();

                // Read data into the caller's buffer.
                while (count > 0)
                {
                    // How much data do we have available in the buffer?
                    int tempLen = _bufferLen - _bufferPosn;
                    if (tempLen <= 0)
                    {
                        _bufferPosn = 0;

                        var data = _session.RequestRead(_handle, (ulong) Position, (uint) _bufferSize);
                        Console.WriteLine("Position:{0},_bufferSize:{1}", Position, _bufferSize);
                     
                        if (data == null)
                            break;
                        _bufferLen = data.Length;
                         Buffer.BlockCopy(data, 0, _buffer, 0, _bufferLen);
                         Console.WriteLine("data.Lenght:{0},_buffer.Lenght:{1}",_bufferLen,_buffer.Length);
                     

                        if (_bufferLen < 0)
                        {
                            _bufferLen = 0;
                            throw new IOException("Read operation failed.");
                        }
  
                        tempLen = _bufferLen;
                    }

                    // Don't read more than the caller wants.
                    if (tempLen > count)
                    {
                        tempLen = count;
                    }

                    // Copy stream data to the caller's buffer.
                    Buffer.BlockCopy(_buffer, _bufferPosn, buffer, offset, tempLen);

                    // Advance to the next buffer positions.
                    readLen += tempLen;
                    offset += tempLen;
                    count -= tempLen;
                    _bufferPosn += tempLen;
                    _position += tempLen;
                }
            }

            // Return the number of bytes that were read to the caller.
            return readLen;
        }


        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPosn = -1;


            // Lock down the file stream while we do this.
            lock (_lock)
            {
                // Bail out if the handle is invalid.
                if (_handle == null)
                {
                    throw new ObjectDisposedException("Stream is closed.");
                }

                // Don't do anything if the position won't be moving.
                if (origin == SeekOrigin.Begin && offset == _position)
                {
                    return offset;
                }
                if (origin == SeekOrigin.Current && offset == 0)
                {
                    return _position;
                }

                _attributes = _session.RequestFStat(_handle);

                // The behaviour depends upon the read/write mode.
                if (_bufferOwnedByWrite)
                {
                    // Flush the write buffer and then seek.
                    FlushWriteBuffer();

                    switch (origin)
                    {
                        case SeekOrigin.Begin:
                            newPosn = offset;
                            break;
                        case SeekOrigin.Current:
                            newPosn = _position + offset;
                            break;
                        case SeekOrigin.End:
                            newPosn = _attributes.Size - offset;
                            break;
                        default:
                            break;
                    }

                    if (newPosn == -1)
                    {
                        throw new EndOfStreamException("End of stream.");
                    }
                    _position = newPosn;
                }
                else
                {
                    // Determine if the seek is to somewhere inside
                    // the current read buffer bounds.
                    if (origin == SeekOrigin.Begin)
                    {
                        newPosn = _position - _bufferPosn;
                        if (offset >= newPosn && offset <
                            (newPosn + _bufferLen))
                        {
                            _bufferPosn = (int) (offset - newPosn);
                            _position = offset;
                            return _position;
                        }
                    }
                    else if (origin == SeekOrigin.Current)
                    {
                        newPosn = _position + offset;
                        if (newPosn >= (_position - _bufferPosn) &&
                            newPosn < (_position - _bufferPosn + _bufferLen))
                        {
                            _bufferPosn =
                                (int) (newPosn - (_position - _bufferPosn));
                            _position = newPosn;
                            return _position;
                        }
                    }

                    // Abandon the read buffer.
                    _bufferPosn = 0;
                    _bufferLen = 0;

                    // Seek to the new position.
                    switch (origin)
                    {
                        case SeekOrigin.Begin:
                            newPosn = offset;
                            break;
                        case SeekOrigin.Current:
                            newPosn = _position + offset;
                            break;
                        case SeekOrigin.End:
                            newPosn = Attributes.Size - offset;
                            break;
                        default:
                            break;
                    }

                    if (newPosn < 0)
                    {
                        throw new EndOfStreamException();
                    }

                    _position = newPosn;
                }
                return _position;
            }
        }


        public override void SetLength(long value)
        {
            // Validate the parameters and setup the object for writing.
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException("value");
            }


            // Lock down the file stream while we do this.
            lock (_lock)
            {
                // Setup this object for writing.
                SetupWrite();

                Attributes.Size = value;

                _session.RequestFSetStat(_handle, Attributes);
            }
        }


        public override void Write(byte[] buffer, int offset, int count)
        {
            if ((buffer.Length - offset) < count)
            {
                throw new ArgumentException("Invalid array range.");
            }

            // Lock down the file stream while we do this.
            lock (_lock)
            {
                // Setup this object for writing.
                SetupWrite();

                // Write data to the file stream.
                while (count > 0)
                {
                    // Determine how many bytes we can write to the buffer.
                    int tempLen = _bufferSize - _bufferPosn;
                    if (tempLen <= 0)
                    {
                        var data = new byte[_bufferPosn];

                        Buffer.BlockCopy(_buffer, 0, data, 0, _bufferPosn);

                        _session.RequestWrite(_handle, (ulong) Position, data);

                        _bufferPosn = 0;
                        tempLen = _bufferSize;
                    }
                    if (tempLen > count)
                    {
                        tempLen = count;
                    }

                    // Can we short-cut the internal buffer?
                    if (_bufferPosn == 0 && tempLen == _bufferSize)
                    {
                        // Yes: write the data directly to the file.
                        var data = new byte[tempLen];

                        Buffer.BlockCopy(buffer, offset, data, 0, tempLen);

                        _session.RequestWrite(_handle, (ulong) Position, data);
                    }
                    else
                    {
                        // No: copy the data to the write buffer first.
                        Buffer.BlockCopy(buffer, offset, _buffer,
                                         _bufferPosn, tempLen);
                        _bufferPosn += tempLen;
                    }

                    // Advance the buffer and stream positions.
                    _position += tempLen;
                    offset += tempLen;
                    count -= tempLen;
                }

                // If the buffer is full, then do a speculative flush now,
                // rather than waiting for the next call to this method.
                if (_bufferPosn >= _bufferSize)
                {
                    var data = new byte[_bufferPosn];

                    Buffer.BlockCopy(_buffer, 0, data, 0, _bufferPosn);

                    _session.RequestWrite(_handle, (ulong) Position, data);

                    _bufferPosn = 0;
                }
            }
        }


        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            lock (_lock)
            {
                if (_handle != null)
                {
                    if (_bufferOwnedByWrite)
                    {
                        FlushWriteBuffer();
                    }

                    if (_ownsHandle)
                    {
                        _session.RequestClose(_handle);
                    }
                    _handle = null;
                }
            }
        }


        private void FlushReadBuffer()
        {
            if (_bufferPosn < _bufferLen)
            {
                _position -= _bufferPosn;
            }
            _bufferPosn = 0;
            _bufferLen = 0;
        }


        private void FlushWriteBuffer()
        {
            if (_bufferPosn > 0)
            {
                var data = new byte[_bufferPosn];

                Buffer.BlockCopy(_buffer, 0, data, 0, _bufferPosn);

                _session.RequestWrite(_handle, (ulong) (Position - _bufferPosn), data);

                _bufferPosn = 0;
            }
        }


        private void SetupRead()
        {
            if ((_access & FileAccess.Read) == 0)
            {
                throw new NotSupportedException("Read not supported.");
            }
            if (_handle == null)
            {
                throw new ObjectDisposedException("Stream is closed.");
            }
            if (_bufferOwnedByWrite)
            {
                FlushWriteBuffer();
                _bufferOwnedByWrite = false;
            }
        }


        private void SetupWrite()
        {
            if ((_access & FileAccess.Write) == 0)
            {
                throw new NotSupportedException("Write not supported.");
            }
            if (_handle == null)
            {
                throw new ObjectDisposedException("Stream is closed.");
            }
            if (!_bufferOwnedByWrite)
            {
                FlushReadBuffer();
                _bufferOwnedByWrite = true;
            }
        }

        internal static int GetBufferSize(long filesize)
        {
            if (filesize == 0)
                return 512*1024;

            int size;

            if (filesize < 1024*1024)
                size = (int) filesize;
            else
                size = 512*1024;

            if (filesize > 10*1024*1024)
                size = 512*1024;
            if (filesize > 100*1024*1024)
                size = 1024*1024;

            return size;
        }
    }
}
using System;
using System.IO;
using System.Threading;
using Renci.SshNet.Sftp;
using Renci.SshNet.Sftp.Messages;

namespace Sshfs
{
    /// <summary>
    /// Exposes a System.IO.Stream around a remote SFTP file, supporting both synchronous and asynchronous read and write operations.
    /// </summary>
    public class SftpNodeStream : Stream
    {
        //  TODO:   Add security method to set userid, groupid and other permission settings
        // Internal state.
        private readonly FileAccess _access;
        private readonly byte[] _buffer;
        private readonly int _bufferSize;
        private readonly object _lock = new object();
        private readonly string _path;
        private readonly SftpSession _session;
        private SftpFileAttributes _attributes;

        // Buffer information.
        private int _bufferLen;
        private bool _bufferOwnedByWrite;
        private int _bufferPosn;
        private byte[] _handle;
        private long _position;

        public SftpNodeStream(SftpSession session, string path, FileMode mode, FileAccess access, int bufferSize,
                              SftpFileAttributes attributes)
        {
            // Validate the parameters.
            if (path == null)
            {
                throw new ArgumentNullException("path");
            }
            if (bufferSize <= 0)
            {
                throw new ArgumentOutOfRangeException("bufferSize");
            }
            if (access < FileAccess.Read || access > FileAccess.ReadWrite)
            {
                throw new ArgumentOutOfRangeException("access");
            }
            if (mode < FileMode.CreateNew || mode > FileMode.Append)
            {
                throw new ArgumentOutOfRangeException("mode");
            }

            Timeout = TimeSpan.FromSeconds(30);


            // Initialize the object state.
            _session = session;
            _access = access;


            _path = path;

            _bufferPosn = 0;
            _bufferLen = 0;
            _bufferOwnedByWrite = false;
            _position = 0;

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
                    flags |= Flags.CreateNew;
                    flags |= Flags.Truncate;
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

            _handle = _session.OpenFile(_path, flags);

            _attributes = attributes ?? _session.GetFileAttributes(_handle);
            _bufferSize = CalculateBufferSize(_attributes.Size);
            _buffer = new byte[_bufferSize];
            if (mode == FileMode.Append)
            {
                //  TODO:   Validate Size property value exists
                _position = _attributes.Size;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the current stream supports reading.
        /// </summary>
        /// <returns>true if the stream supports reading; otherwise, false.</returns>
        public override bool CanRead
        {
            get { return ((_access & FileAccess.Read) != 0); }
        }

        /// <summary>
        /// Gets a value indicating whether the current stream supports seeking.
        /// </summary>
        /// <returns>true if the stream supports seeking; otherwise, false.</returns>
        public override bool CanSeek
        {
            get { return true; }
        }

        /// <summary>
        /// Gets a value indicating whether the current stream supports writing.
        /// </summary>
        /// <returns>true if the stream supports writing; otherwise, false.</returns>
        public override bool CanWrite
        {
            get { return ((_access & FileAccess.Write) != 0); }
        }

        /// <summary>
        /// Gets the length in bytes of the stream.
        /// </summary>
        /// <returns>A long value representing the length of the stream in bytes.</returns>
        ///   
        /// <exception cref="T:System.NotSupportedException">A class derived from Stream does not support seeking. </exception>
        ///   
        /// <exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception>
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
                    _attributes = _session.GetFileAttributes(_handle);

                    if (Attributes != null && Attributes.Size > -1)
                    {
                        return Attributes.Size;
                    }
                    throw new IOException("Seek operation failed.");
                }
            }
        }

        /// <summary>
        /// Gets or sets the position within the current stream.
        /// </summary>
        /// <returns>The current position within the stream.</returns>
        ///   
        /// <exception cref="T:System.IO.IOException">An I/O error occurs. </exception>
        ///   
        /// <exception cref="T:System.NotSupportedException">The stream does not support seeking. </exception>
        ///   
        /// <exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception>
        public override long Position
        {
            get { return _position; }
            set { Seek(value, SeekOrigin.Begin); }
        }

        /// <summary>
        /// Gets a value indicating whether the FileStream was opened asynchronously or synchronously.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is async; otherwise, <c>false</c>.
        /// </value>
        public virtual bool IsAsync
        {
            get { return false; }
        }


        /// <summary>
        /// Gets the operating system file handle for the file that the current SftpNodeStream object encapsulates.
        /// </summary>
        public virtual byte[] Handle
        {
            get
            {
                Flush();
                return _handle;
            }
        }

        /// <summary>
        /// Gets or sets the operation timeout.
        /// </summary>
        /// <value>
        /// The timeout.
        /// </value>
        public TimeSpan Timeout { get; set; }

        public SftpFileAttributes Attributes
        {
            get { return _attributes; }
        }


        /// <summary>
        /// Releases unmanaged resources and performs other cleanup operations before the
        /// <see cref="SftpNodeStream"/> is reclaimed by garbage collection.
        /// </summary>
        ~SftpNodeStream()
        {
            Dispose(false);
        }

        /// <summary>
        /// Closes the current stream and releases any resources (such as sockets and file handles) associated with the current stream.
        /// </summary>
        public override void Close()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Clears all buffers for this stream and causes any buffered data to be written to the file.
        /// </summary>
        /// <exception cref="T:System.IO.IOException">An I/O error occurs. </exception>
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

        /// <summary>
        /// Reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
        /// </summary>
        /// <param name="buffer">An array of bytes. When this method returns, the buffer contains the specified byte array with the values between <paramref name="offset"/> and (<paramref name="offset"/> + <paramref name="count"/> - 1) replaced by the bytes read from the current source.</param>
        /// <param name="offset">The zero-based byte offset in <paramref name="buffer"/> at which to begin storing the data read from the current stream.</param>
        /// <param name="count">The maximum number of bytes to be read from the current stream.</param>
        /// <returns>
        /// The total number of bytes read into the buffer. This can be less than the number of bytes requested if that many bytes are not currently available, or zero (0) if the end of the stream has been reached.
        /// </returns>
        /// <exception cref="T:System.ArgumentException">The sum of <paramref name="offset"/> and <paramref name="count"/> is larger than the buffer length. </exception>
        ///   
        /// <exception cref="T:System.ArgumentNullException">
        ///   <paramref name="buffer"/> is null. </exception>
        ///   
        /// <exception cref="T:System.ArgumentOutOfRangeException">
        ///   <paramref name="offset"/> or <paramref name="count"/> is negative. </exception>
        ///   
        /// <exception cref="T:System.IO.IOException">An I/O error occurs. </exception>
        ///   
        /// <exception cref="T:System.NotSupportedException">The stream does not support reading. </exception>
        ///   
        /// <exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception>
        public override int Read(byte[] buffer, int offset, int count)
        {
            int readLen = 0;

            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException("offset");
            }
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException("count");
            }
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
                        Console.WriteLine("SR:{0}||||{1}", Position, _bufferSize);

                        byte[] data = _session.Read(_handle, (ulong) Position, (uint) _bufferSize);

                        _bufferLen = data.Length;

                        Buffer.BlockCopy(data, 0, _buffer, 0, _bufferLen);

                        if (_bufferLen < 0)
                        {
                            _bufferLen = 0;
                            //  TODO:   Add SFTP error code or message if possible
                            throw new IOException("Read operation failed.");
                        }
                        if (_bufferLen == 0)
                        {
                            break;
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

        /// <summary>
        /// Reads a byte from the stream and advances the position within the stream by one byte, or returns -1 if at the end of the stream.
        /// </summary>
        /// <returns>
        /// The unsigned byte cast to an Int32, or -1 if at the end of the stream.
        /// </returns>
        /// <exception cref="T:System.NotSupportedException">The stream does not support reading. </exception>
        ///   
        /// <exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception>
        public override int ReadByte()
        {
            // Lock down the file stream while we do this.
            lock (_lock)
            {
                // Setup the object for reading.
                SetupRead();

                // Read more data into the internal buffer if necessary.
                if (_bufferPosn >= _bufferLen)
                {
                    _bufferPosn = 0;
                    //this._bufferLen = FileMethods.Read(this._handle, this._buffer, 0, this._bufferSize);
                    byte[] data = _session.Read(_handle, (ulong) Position, (uint) _bufferSize);
                    _bufferLen = data.Length;
                    Buffer.BlockCopy(data, 0, _buffer, 0, _bufferSize);

                    if (_bufferLen < 0)
                    {
                        _bufferLen = 0;
                        //  TODO:   Add SFTP error code or message if possible
                        throw new IOException("Read operation failed.");
                    }
                    if (_bufferLen == 0)
                    {
                        // We've reached EOF.
                        return -1;
                    }
                }

                // Extract the next byte from the buffer.
                ++_position;
                return _buffer[_bufferPosn++];
            }
        }

        /// <summary>
        /// Sets the position within the current stream.
        /// </summary>
        /// <param name="offset">A byte offset relative to the <paramref name="origin"/> parameter.</param>
        /// <param name="origin">A value of type <see cref="T:System.IO.SeekOrigin"/> indicating the reference point used to obtain the new position.</param>
        /// <returns>
        /// The new position within the current stream.
        /// </returns>
        /// <exception cref="T:System.IO.IOException">An I/O error occurs. </exception>
        ///   
        /// <exception cref="T:System.NotSupportedException">The stream does not support seeking, such as if the stream is constructed from a pipe or console output. </exception>
        ///   
        /// <exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception>
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
                    Console.WriteLine("SAME");
                    return offset;
                }
                if (origin == SeekOrigin.Current && offset == 0)
                {
                    return _position;
                }

                _attributes = _session.GetFileAttributes(_handle);

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
                            newPosn = Attributes.Size - offset;
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

        /// <summary>
        /// When overridden in a derived class, sets the length of the current stream.
        /// </summary>
        /// <param name="value">The desired length of the current stream in bytes.</param>
        /// <exception cref="T:System.IO.IOException">An I/O error occurs. </exception>
        ///   
        /// <exception cref="T:System.NotSupportedException">The stream does not support both writing and seeking, such as if the stream is constructed from a pipe or console output. </exception>
        ///   
        /// <exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception>
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

                _session.SetFileAttributes(_handle, Attributes);
            }
        }

        /// <summary>
        /// Writes a sequence of bytes to the current stream and advances the current position within this stream by the number of bytes written.
        /// </summary>
        /// <param name="buffer">An array of bytes. This method copies <paramref name="count"/> bytes from <paramref name="buffer"/> to the current stream.</param>
        /// <param name="offset">The zero-based byte offset in <paramref name="buffer"/> at which to begin copying bytes to the current stream.</param>
        /// <param name="count">The number of bytes to be written to the current stream.</param>
        /// <exception cref="T:System.ArgumentException">The sum of <paramref name="offset"/> and <paramref name="count"/> is greater than the buffer length. </exception>
        ///   
        /// <exception cref="T:System.ArgumentNullException">
        ///   <paramref name="buffer"/> is null. </exception>
        ///   
        /// <exception cref="T:System.ArgumentOutOfRangeException">
        ///   <paramref name="offset"/> or <paramref name="count"/> is negative. </exception>
        ///   
        /// <exception cref="T:System.IO.IOException">An I/O error occurs. </exception>
        ///   
        /// <exception cref="T:System.NotSupportedException">The stream does not support writing. </exception>
        ///   
        /// <exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception>
        public override void Write(byte[] buffer, int offset, int count)
        {
            int tempLen;

            // Validate the parameters
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }
            else if (offset < 0)
            {
                throw new ArgumentOutOfRangeException("offset");
            }
            else if (count < 0)
            {
                throw new ArgumentOutOfRangeException("count");
            }
            else if ((buffer.Length - offset) < count)
            {
                throw new ArgumentException("Invalid array range.");
            }

            // Lock down the file stream while we do this.
            lock (this._lock)
            {
                // Setup this object for writing.
                this.SetupWrite();

                // Write data to the file stream.
                while (count > 0)
                {
                    // Determine how many bytes we can write to the buffer.
                    tempLen = this._bufferSize - this._bufferPosn;
                    if (tempLen <= 0)
                    {
                        var data = new byte[this._bufferPosn];

                        Buffer.BlockCopy(this._buffer, 0, data, 0, this._bufferPosn);

                        this._session.Write(this._handle, (ulong)this.Position, data);

                        this._bufferPosn = 0;
                        tempLen = this._bufferSize;
                    }
                    if (tempLen > count)
                    {
                        tempLen = count;
                    }

                    // Can we short-cut the internal buffer?
                    if (this._bufferPosn == 0 && tempLen == this._bufferSize)
                    {
                        // Yes: write the data directly to the file.
                        var data = new byte[tempLen];

                        Buffer.BlockCopy(buffer, offset, data, 0, tempLen);

                        this._session.Write(this._handle, (ulong)this.Position, data);
                    }
                    else
                    {
                        // No: copy the data to the write buffer first.
                        Array.Copy(buffer, offset, this._buffer,
                                   this._bufferPosn, tempLen);
                        this._bufferPosn += tempLen;
                    }

                    // Advance the buffer and stream positions.
                    this._position += tempLen;
                    offset += tempLen;
                    count -= tempLen;
                }

                // If the buffer is full, then do a speculative flush now,
                // rather than waiting for the next call to this method.
                if (this._bufferPosn >= this._bufferSize)
                {
                    var data = new byte[this._bufferPosn];

                    Buffer.BlockCopy(this._buffer, 0, data, 0, this._bufferPosn);

                    this._session.Write(this._handle, (ulong)this.Position, data);

                    this._bufferPosn = 0;
                }
            }
        }

        /// <summary>
        /// Writes a byte to the current position in the stream and advances the position within the stream by one byte.
        /// </summary>
        /// <param name="value">The byte to write to the stream.</param>
        /// <exception cref="T:System.IO.IOException">An I/O error occurs. </exception>
        ///   
        /// <exception cref="T:System.NotSupportedException">The stream does not support writing, or the stream is already closed. </exception>
        ///   
        /// <exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception>
        public override void WriteByte(byte value)
        {
            // Lock down the file stream while we do this.
            lock (_lock)
            {
                // Setup the object for writing.
                SetupWrite();

                // Flush the current buffer if it is full.
                if (_bufferPosn >= _bufferSize)
                {
                    var data = new byte[_bufferPosn];

                    Buffer.BlockCopy(_buffer, 0, data, 0, _bufferPosn);

                    _session.Write(_handle, (ulong) Position, data);

                    _bufferPosn = 0;
                }

                // Write the byte into the buffer and advance the posn.
                _buffer[_bufferPosn++] = value;
                ++_position;
            }
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="T:System.IO.Stream"/> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
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

                    if (true)
                    {
                        _session.CloseHandle(_handle);
                    }
                    _handle = null;
                }
            }
        }

        /// <summary>
        /// Flushes the read data from the buffer.
        /// </summary>
        private void FlushReadBuffer()
        {
            if (_bufferPosn < _bufferLen)
            {
                _position -= _bufferPosn;
            }
            _bufferPosn = 0;
            _bufferLen = 0;
        }

        /// <summary>
        /// Flush any buffered write data to the file.
        /// </summary>
        private void FlushWriteBuffer()
        {
            if (_bufferPosn > 0)
            {
                var data = new byte[_bufferPosn];

                Buffer.BlockCopy(_buffer, 0, data, 0, _bufferPosn);

                _session.Write(_handle, (ulong) (Position - _bufferPosn), data);

                _bufferPosn = 0;
            }
        }

        /// <summary>
        /// Setups the read.
        /// </summary>
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
            if (!_bufferOwnedByWrite) return;
            FlushWriteBuffer();
            _bufferOwnedByWrite = false;
        }

        /// <summary>
        /// Setups the write.
        /// </summary>
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
            if (_bufferOwnedByWrite) return;
            FlushReadBuffer();
            _bufferOwnedByWrite = true;
        }

        internal static int CalculateBufferSize(long filesize)
        {
            int size = 0;
            if (filesize == 0)
                size = 512*1024;


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
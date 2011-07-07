using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using DokanNet.Native;

namespace DokanNet
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public class DokanFileInfo
    {
        private ulong _context;
#pragma warning disable 649
        private readonly ulong _dokanContext;
#pragma warning disable 169
        private readonly IntPtr DokanOptions;
#pragma warning restore 169
        private readonly uint _processId;
#pragma warning restore 649
        [MarshalAs(UnmanagedType.U1)] private bool _isDirectory;
        [MarshalAs(UnmanagedType.U1)] private bool _deleteOnClose;
        [MarshalAs(UnmanagedType.U1)] private bool _pagingIo;
        [MarshalAs(UnmanagedType.U1)] private bool _synchronousIo;
        [MarshalAs(UnmanagedType.U1)] private bool _nocache;
        [MarshalAs(UnmanagedType.U1)] private bool _writeToEndOfFile;

// ReSharper disable ConvertToAutoProperty
        public ulong Context
// ReSharper restore ConvertToAutoProperty
        {
            get { return _context; }
            set { _context = value; }
        }

        public ulong DokanContext
        {
            get { return _dokanContext; }
        }

        public uint ProcessId
        {
            get { return _processId; }
        }

        public bool IsDirectory
        {
            get { return _isDirectory; }
            set { _isDirectory = value; }
        }

        public bool DeleteOnClose
        {
            get { return _deleteOnClose; }
            set { _deleteOnClose = value; }
        }

        public bool PagingIo
        {
            get { return _pagingIo; }
        }

        public bool SynchronousIo
        {
            get { return _synchronousIo; }
        }

        public bool Nocache
        {
            get { return _nocache; }
        }

        public bool WriteToEndOfFile
        {
            get { return _writeToEndOfFile; }
        }

        public WindowsIdentity RequestorUser
        {
            get { return new WindowsIdentity(NativeMethods.DokanOpenRequestorToken(this)); }
        }

        public bool ResetTimeout(uint milliseconds)
        {
            return NativeMethods.DokanResetTimeout(milliseconds, this);
        }
    }
}
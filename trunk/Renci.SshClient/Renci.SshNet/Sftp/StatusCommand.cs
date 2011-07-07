using System.Collections.Generic;
using Renci.SshNet.Sftp.Messages;

namespace Renci.SshNet.Sftp
{
    internal class StatusCommand : SftpCommand
    {
        private string _path;
        private byte[] _handle;

        public SftpFileAttributes Attributes { get; private set; }

        public StatusCommand(SftpSession sftpSession, string path)
            : base(sftpSession)
        {
            this._path = path;
        }

        public StatusCommand(SftpSession sftpSession, byte[] handle)
            : base(sftpSession)
        {
            this._handle = handle;
        }

        protected override void OnExecute()
        {
            if (this._handle != null)
            {
                this.SendStatMessage(this._handle);
            }
            else if (this._path != null)
            {
                this.SendStatMessage(this._path);
            }
        }

        protected override void OnAttributes(SftpFileAttributes attributes)
        {
            base.OnAttributes(attributes);

            this.Attributes = attributes;

            this.CompleteExecution();
        }

        protected override void OnStatus(StatusCodes statusCode, string errorMessage, string language)
        {
            base.OnStatus(statusCode, errorMessage, language);

            if (statusCode == StatusCodes.NoSuchFile)
            {
                this.CompleteExecution();

                this.IsStatusHandled = true;
            }
        }
    }
}

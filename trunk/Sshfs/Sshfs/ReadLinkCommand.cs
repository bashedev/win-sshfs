using System.Linq;
using Renci.SshNet.Sftp;
using Renci.SshNet.Sftp.Messages;

namespace Sshfs
{
    internal class ReadLinkCommand : SftpCommand
    {
        private readonly string _linkPath;

        public string Path { get; private set; }

        public ReadLinkCommand(SftpSession sftpSession, string linkPath)
            : base(sftpSession)
        {
            this._linkPath = linkPath;
        }

        protected override void OnExecute()
        {
            this.SendReadLinkMessage(_linkPath);
        }

        protected override void OnName(System.Collections.Generic.IDictionary<string, SftpFileAttributes> files)
        {
            base.OnName(files);
            Path = files.Keys.FirstOrDefault();
            CompleteExecution();
        }

        protected override void OnStatus(StatusCodes statusCode, string errorMessage, string language)
        {
            base.OnStatus(statusCode, errorMessage, language);

            if (statusCode == StatusCodes.NoSuchFile)
            {
                IsStatusHandled = true;
                this.CompleteExecution();
            }
        }
    }
}
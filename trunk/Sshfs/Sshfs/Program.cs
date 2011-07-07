using System;
using System.IO;
using DokanNet;
using Renci.SshNet;
using CLAP;
using System.Linq;

namespace Sshfs
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                Parser<SshfsCli>.Run(args);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error:{0}\n", e.InnerException.Message);
            }
        }

        [DefaultVerb("mount")]
        private class SshfsCli
        {
            [Verb(Description = "Mounts the specified SSHFS drive.")]
            public static void Mount(
                [Parameter(Aliases = "u", Description = "Logon username.", Required = true)] string user,
                [Parameter(Aliases = "h", Description = "Remote host.", Required = true)] string host,
                [Parameter(Aliases = "p", Description = "Remote port number.", Default = 22)] int port,
                [Parameter(Aliases = "path", Description = "Remote path.")] string remotepath,
                [Parameter(Aliases = "m,l", Description = "Local mountpoint.")] char letter,
                [Parameter(Aliases = "n", Description = "Mount as networkdrive.")] bool networkdrive,
                [Parameter(Aliases = "r", Description = "Mount as removable drive")] bool removable,
                [Parameter(Aliases = "d", Description = "Dump debug output to console")] bool debug,
                [Parameter(Aliases = "pass", Description = "Passphrase for private key/s.")] string passphrase,
                [Parameter(Aliases = "k,key", Description = "Path for private key file/s")] params string[]
                    privatekeyspath)
            {
                Console.WriteLine("SSHFS 0.1 for Windows     <-- maybe \n");

                ConnectionInfo info;

                if (privatekeyspath == null)
                {
                    Console.WriteLine("Password: ");

                    var password = Console.ReadLine();
                    info = new PasswordConnectionInfo(host, port, user, password);
                }
                else
                {
                    var keys = String.IsNullOrEmpty(passphrase)
                                   ? privatekeyspath.Select(path => new PrivateKeyFile(path)).ToArray()
                                   : privatekeyspath.Select(path => new PrivateKeyFile(path, passphrase)).
                                         ToArray();

                    info = new PrivateKeyConnectionInfo(host, port, user, keys);
                }


                var sshfs = new Sshfs(info, remotepath, false, debug);
                sshfs.Connect();
                if (letter == 0)
                {
                    letter =
                        Enumerable.Range('D', 22).Select(value => (char) value).Except(
                            DriveInfo.GetDrives().Select(drive => drive.Name[0])).First();
                }
                var options = new DokanOptions
                                  {
                                      FilesystemName = "SSHFS",
                                      MountPoint = String.Format("{0}:\\", letter),
                                      NetworkDrive = networkdrive,
                                      RemovableDrive = removable,
                                      UseKeepAlive = true,
                                      ThreadCount = 0,
                                      VolumeLabel = String.Format("{0} on '{1}'", info.Username, info.Host)
                                  };
                Console.WriteLine("Connected..");


                Dokan.Mount(options, sshfs);
            }

            [Empty, Help, Verb(Description = "Print help message.")]
            public static void Help(string help)
            {
                Console.WriteLine("SSHFS 0.1 for Windows     <-- maybe \n{0}", help);
            }

            [Verb(Aliases = "un", Description = "Unmount specified SSHFS drive.")]
            public static void Unmount(
                [Parameter(Aliases = "l", Description = "Drive letter.", Required = true)] char letter)
            {
                Dokan.RemoveMountPoint(String.Format("{0}:\\", letter));
            }
        }
    }
}
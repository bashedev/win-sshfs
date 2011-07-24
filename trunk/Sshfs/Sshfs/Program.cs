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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
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
                Environment.Exit(-1);
            }
        }

        [DefaultVerb("mount")]
        private class SshfsCli
        {
            [Verb(Description = "Mounts the specified SSHFS drive.")]
            public static void Mount(
                [Parameter(Aliases = "u", Description = "Remote username.", Required = true)] string user,
                [Parameter(Aliases = "h", Description = "Remote host.", Required = true)] string host,
                [Parameter(Aliases = "p", Description = "Remote port number.", Default = 22)] int port,
                [Parameter(Aliases = "path", Description = "Remote path.")] string remotepath,
                [Parameter(Aliases = "l", Description = "Local drive letter.")] char letter,
                [Parameter(Aliases = "n", Description = "Mount as networkdrive.")] bool networkdrive,
                [Parameter(Aliases = "r", Description = "Mount as removable drive")] bool removable,
                [Parameter(Aliases = "d", Description = "Dump debug output to console")] bool debug,
                [Parameter(Aliases = "pass", Description = "Passphrase for private key/s.")] string passphrase,
                [Parameter(Aliases = "k", Description = "Path/s for private key file/s")] params string[]
                    key)
            {
                Console.WriteLine("SSHFS {0} For any problems and suggestions go to code.google.com/p/win-sshfs",
                                  Assembly.GetEntryAssembly().GetName().Version);

               
                ConnectionInfo info;

                if (key == null)
                {
                    Console.Write("Password: ");
                    var password = ReadSecureString();
                    Console.Write("\n\n");
                    info = new PasswordConnectionInfo(host, port, user, password);
                }
                else
                {
                    var keys = String.IsNullOrEmpty(passphrase)
                                   ? key.Select(path => new PrivateKeyFile(path)).ToArray()
                                   : key.Select(path => new PrivateKeyFile(path, passphrase)).
                                         ToArray();

                    info = new PrivateKeyConnectionInfo(host, port, user, keys);
                }


                var sshfs = new Sshfs(info, remotepath, false, debug);
                sshfs.ConnectionInfo.AuthenticationBanner +=
                    (o, args) => Console.WriteLine(args.BannerMessage);

                Console.Write("Connecting to {0}...", info.Host);
                sshfs.Connect();
                
                sshfs.Disconnected += (o, args) => Environment.Exit(-1);


                if (letter == 0)
                {
                    letter = GetFirstDriveAvailable();
                }
                var options = new DokanOptions
                                  {
                                     
                                      MountPoint = String.Format("{0}:\\", letter),
                                      NetworkDrive = networkdrive,
                                      RemovableDrive = removable,
                                      UseKeepAlive = true,
                                      ThreadCount = 0,
                                      
                                  };

                Console.CursorLeft = 0;
                Console.Write("Mounting...\t\t\t\t");
                Console.CursorLeft = 0;

                
                Dokan.Mount(sshfs,options);

                
            }


            [Empty, Help, Verb(Description = "Prints this message.")]
            public static void Help(string help)
            {
                help = Regex.Replace(help, @"[[\S]+, [\S]+.*]", String.Empty);
                help = Regex.Replace(help, @"[[\S]+]", String.Empty);
                Console.WriteLine("SSHFS {0} For any problems and suggestions go to code.google.com/p/win-sshfs\n{1}",
                                  Assembly.GetEntryAssembly().GetName().Version, help);
            }

            [Verb(Aliases = "u", Description = "Unmounts mounted SSHFS drive.")]
            public static void Unmount(
                [Parameter(Aliases = "l", Description = "Drive letter.", Required = true)] char letter)
            {
                if (!Dokan.RemoveMountPoint(String.Format("{0}:\\", letter)))
                {
                    Environment.Exit(-1);
                }
            }

            private static char GetFirstDriveAvailable()
            {
                return Enumerable.Range('D', 22).Select(value => (char) value).Except(
                    DriveInfo.GetDrives().Select(drive => drive.Name[0])).First();
            }

            private static string ReadSecureString()
            {
                var password = new Stack<char>();

                for (var keyInfo = Console.ReadKey(true);
                     keyInfo.Key != ConsoleKey.Enter;
                     keyInfo = Console.ReadKey(true))
                {
                    switch (keyInfo.Key)
                    {
                        case ConsoleKey.Backspace:
                            if (Console.CursorLeft == 0)
                            {
                                Console.CursorLeft = Console.BufferWidth - 1;
                                Console.CursorTop--;
                            }
                            else
                            {
                                Console.CursorLeft--;
                            }
                            Console.Write('\0');
                            if (password.Count != 0)
                            {
                                password.Pop();
                                if (Console.CursorLeft == 0)
                                {
                                    Console.CursorLeft = Console.BufferWidth - 1;
                                    Console.CursorTop--;
                                }
                                else
                                {
                                    Console.CursorLeft--;
                                }
                            }
                            break;
                        case ConsoleKey.Clear:
                        case ConsoleKey.UpArrow:
                            while
                                (password.Count != 0)
                            {
                                if (Console.CursorLeft == 0)
                                {
                                    Console.CursorLeft = Console.BufferWidth - 1;
                                    Console.CursorTop--;
                                }
                                else
                                {
                                    Console.CursorLeft--;
                                }

                                Console.Write('\0');
                                password.Pop();
                                if (Console.CursorLeft == 0)
                                {
                                    Console.CursorLeft = Console.BufferWidth - 1;
                                    Console.CursorTop--;
                                }
                                else
                                {
                                    Console.CursorLeft--;
                                }
                            }
                            break;
                        default:
                            if (Char.IsLetterOrDigit(keyInfo.KeyChar) || Char.IsPunctuation(keyInfo.KeyChar))
                            {
                                password.Push(keyInfo.KeyChar);
                                Console.Write('*');
                            }
                            break;
                    }
                }

                return new String(password.Reverse().ToArray());
            }
        }
    }
}
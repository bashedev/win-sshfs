SSH(SFTP) filesystem made using Dokan and  SSH.NET library.  It allows you to mount remote computers  via SFTP protocol like windows network drives .

**The project is currently on hiatus and probably will not have new releases till end of the year**

Looking for someone who would make neat 16x16 notify icon , preferably based on this project logo

Source code: http://github.com/apaka/win-sshfs


Dokan binding used by this project can be found at http://github.com/apaka/dokan-net


Patches, suggestions and bug reports (with as much info as possible) are always welcome. I will look at
them, but please be patient.

Supported Authentication methods

---


  * password
  * private-key (OpenSsh private key format)

Requirements

---

  * Windows XP SP3(x86) or Windows Vista SP1 (x86 and x64) or Windows 7 (x86 and x64)
  * Microsoft .NET Framework 4 Full Profile (http://www.microsoft.com/download/en/details.aspx?id=17718)
  * Dokan Library 0.6.0 (http://dokan-dev.net/wp-content/uploads/DokanInstall_0.6.0.exe)

Known bugs

---

  * Due the nature of symlink mapping(Windows is unaware of it) the deletion of symlink that points to directory will result in deleting of source directories content.
  * If you get disconnect(disconnected by server,internet connection malfunction and similar) drive may become unresponsive and you have to ~~terminate the Sshfs.exe process and start it again.~~ manually unmount him and mount again.

TODO

---

  * Fix compatability isues (Win7, different servers).
  * Implement reconnect and fail safe mechanisam.
  * Implement read-ahead alogorithm. - Uses crude one right now
  * Implement caching. - Done attribute and dir caching, file caching left
  * Add symlink support. - Done
  * Implement gui and  persistent mounting service. - Done
#region

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Sshfs.Properties;

#endregion

namespace Sshfs
{
    public partial class MainForm : Form
    {
        private readonly StringBuilder _balloonText = new StringBuilder(255);
        private readonly List<SftpDrive> _drives = new List<SftpDrive>();
        private readonly Queue<SftpDrive> _suspendedDrives = new Queue<SftpDrive>();
        private bool _balloonTipVisible;


        private int _lastindex = -1;
        private int _namecount;
        private bool _suspend;


        public MainForm()
        {
            InitializeComponent();
            driveListView.Columns[0].Width = driveListView.ClientRectangle.Width - 1;
            Opacity = 0;
        }


        protected override void OnLoad(EventArgs e)
        {
            notifyIcon.Text = Text = String.Format("Sshfs Manager {0}", Assembly.GetEntryAssembly().GetName().Version);
            portBox.Minimum = IPEndPoint.MinPort;
            portBox.Maximum = IPEndPoint.MaxPort;
            openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            startupMenuItem.Checked = Utilities.IsAppRegistredForStarup();

            // _drives.Presist("config.xml",true);
            _drives.Load("config.xml");

            driveListView.BeginUpdate();
            for (int i = 0; i < _drives.Count; i++)
            {
                driveListView.Items.Add((_drives[i].Tag =
                                         new ListViewItem(_drives[i].Name, 0) {Tag = _drives[i]}) as ListViewItem);
                _drives[i].StatusChanged += drive_StatusChanged;
                if (_drives[i].Name.StartsWith("New Drive")) _namecount++;
            }


            if (driveListView.Items.Count != 0)
            {
                driveListView.SelectedIndices.Add(0);
               
            }



            driveListView.EndUpdate();

            //just to remove HScroll
            if (driveListView.Items.Count > 10)
            {
                driveListView.Items[10].EnsureVisible();
                driveListView.Items[0].EnsureVisible();
            }

            SetupPanels();


            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            base.OnLoad(e);
        }


        private void startupMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            if (startupMenuItem.Checked)
            {
                Utilities.RegisterForStartup();
            }
            else
            {
                Utilities.UnregisterForStarup();
            }
        }

        private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            _suspend = e.Mode == PowerModes.Suspend;

            if (e.Mode == PowerModes.Resume)
            {
                while (_suspendedDrives.Count > 0)
                {
                    MountDrive(_suspendedDrives.Dequeue());
                }
            }
        }

        private void SetupPanels()
        {
            buttonPanel.Enabled = removeButton.Enabled = fieldsPanel.Enabled = driveListView.Items.Count != 0;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                Visible = false;
                e.Cancel = true;
            }
            base.OnFormClosing(e);
        }

        private void addButton_Click(object sender, EventArgs e)
        {
            char letter;
            try
            {
                letter = Utilities.GetAvailableDrives().Except(_drives.Select(d => d.Letter)).First();
            }
            catch
            {
                MessageBox.Show("No more drive letters available", Text);
                return;
            }


            var drive = new SftpDrive
                            {
                                Name = String.Format("New Drive {0}", ++_namecount),
                                Port = 22,
                                Root = ".",
                                Letter = letter
                            };
            drive.StatusChanged += drive_StatusChanged;
            _drives.Add(drive);
            var item =
                (drive.Tag = new ListViewItem(drive.Name, 0) {Tag = drive, Selected = true}) as
                ListViewItem;
          
            driveListView.Items.Add(item
                );
            item.EnsureVisible();
           
    
            SetupPanels();
        }

        private void drive_StatusChanged(object sender, EventArgs e)
        {
          
            var drive = sender as SftpDrive;

            Debug.WriteLine("Status Changed {0}:{1}", sender,drive.Status);

            if (_suspend && drive.Status == DriveStatus.Unmounted)
            {
                _suspendedDrives.Enqueue(drive);
            }

            if (!Visible)
            {
                ShowBallon(String.Format("{0} : {1}", drive.Name,
                                         drive.Status == DriveStatus.Mounted ? "Mounted" : "Unmounted"));
            }

            this.BeginInvoke(new MethodInvoker(() =>
                                                   {
                                                       var item =
                                                           drive.Tag as ListViewItem;


                                                       if (item.Selected)
                                                       {
                                                           muButton.Text = drive.Status == DriveStatus.Mounted
                                                                               ? "Unmount"
                                                                               : "Mount";
                                                           muButton.Image = drive.Status == DriveStatus.Mounted
                                                                                ? Resources.unmount
                                                                                : Resources.mount;
                                                           muButton.Enabled = true;
                                                       }
                                                       item.ImageIndex = drive.Status == DriveStatus.Mounted ? 1 : 0;
                                                   }));
        }

        private void removeButton_Click(object sender, EventArgs e)
        {
            if (driveListView.SelectedItems.Count != 0 &&
                MessageBox.Show("Do want to delete this drive ?", Text, MessageBoxButtons.YesNo) ==
                DialogResult.Yes)
            {
                var drive = driveListView.SelectedItems[0].Tag as SftpDrive;


                drive.StatusChanged -= drive_StatusChanged;
                drive.Unmount();
                _drives.Remove(drive);


                int next = driveListView.SelectedIndices[0] == driveListView.Items.Count - 1
                               ? driveListView.SelectedIndices[0] - 1
                               : driveListView.SelectedIndices[0];

                driveListView.Items.Remove(driveListView.SelectedItems[0]);
               
                if (next != -1)
                {
                    _lastindex = -1;
                    driveListView.SelectedIndices.Add(next);
                    driveListView.Items[next].EnsureVisible();
                    
                }

                SetupPanels();
            }
        }

        private void listView_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            if (e.IsSelected && _lastindex != e.ItemIndex)
            {
                _lastindex = e.ItemIndex;

                var drive = e.Item.Tag as SftpDrive;

                nameBox.Text = drive.Name;
                hostBox.Text = drive.Host;
                portBox.Value = drive.Port;
                userBox.Text = drive.Username;
                authCombo.SelectedIndex = drive.ConnectionType == ConnectionType.Password ? 0 : 1;
                letterBox.BeginUpdate();

                letterBox.Items.Clear();

                letterBox.Items.AddRange(
                    Utilities.GetAvailableDrives().Except(_drives.Select(d => d.Letter)).Select(
                        l => String.Format("{0} :", l)).ToArray());
                letterBox.Items.Add(String.Format("{0} :", drive.Letter));


                letterBox.SelectedIndex = letterBox.FindString(drive.Letter.ToString());

                letterBox.EndUpdate();

                passwordBox.Text = drive.Password;
                directoryBox.Text = drive.Root;
                mountCheck.Checked = drive.Automount;
                passwordBox.Text = drive.Password;
                privateKeyBox.Text = drive.PrivateKey;
                passphraseBox.Text = drive.Passphrase;
                muButton.Text = drive.Status == DriveStatus.Mounted ? "Unmount" : "Mount";
                muButton.Image = drive.Status == DriveStatus.Mounted ? Resources.unmount : Resources.mount;
                muButton.Enabled = (drive.Status == DriveStatus.Unmounted || drive.Status == DriveStatus.Mounted);
            }
        }

        private void authBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            authLabel.Text = authCombo.Text;
            passwordBox.Visible = authCombo.SelectedIndex == 0;
            privateKeyButton.Visible = passphraseBox.Visible = privateKeyBox.Visible = authCombo.SelectedIndex == 1;
        }

        private void keyButton_Click(object sender, EventArgs e)
        {
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                privateKeyBox.Text = openFileDialog.FileName;
            }
        }

        private void saveButton_Click(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(nameBox.Text))
            {
                MessageBox.Show("Drive name connot be empty", Text);
                nameBox.Focus();
                return;
            }
            var drive = driveListView.SelectedItems[0].Tag as SftpDrive;

            if ((nameBox.Text.StartsWith("New Drive") || nameBox.Text == String.Format("{0}@'{1}'", drive.Username, drive.Host)) && !String.IsNullOrEmpty(userBox.Text) && !String.IsNullOrEmpty(hostBox.Text))
            {
                nameBox.Text = String.Format("{0}@'{1}'", userBox.Text, hostBox.Text);
            }

           

            driveListView.SelectedItems[0].Text = drive.Name = nameBox.Text.Trim();
            drive.Host = hostBox.Text.Trim();
            drive.Port = (int) portBox.Value;
            drive.Username = userBox.Text.Trim();
            drive.ConnectionType = authCombo.SelectedIndex == 0 ? ConnectionType.Password : ConnectionType.PrivateKey;
            drive.Letter = letterBox.Text[0];
            drive.Password = passwordBox.Text.Trim();
            drive.Root = directoryBox.Text.Trim();
            drive.Automount = mountCheck.Checked;
            drive.Password = passwordBox.Text.Trim();
            drive.PrivateKey = privateKeyBox.Text.Trim();
            drive.Passphrase = passphraseBox.Text.Trim();
        }

        private void MountDrive(SftpDrive drive)
        {
            Task.Factory.StartNew(() =>
                                      {
                                          try
                                          {
                                              drive.Mount();
                                          }
                                          catch (Exception e)
                                          {
                                              this.BeginInvoke(new MethodInvoker(() =>
                                                                                     {
                                                                                         if (
                                                                                             (drive.Tag as ListViewItem)
                                                                                                 .Selected)
                                                                                         {
                                                                                             muButton.Enabled
                                                                                                 = true;
                                                                                         }
                                                                                     }));


                                              if (Visible)
                                              {
                                                  this.BeginInvoke(
                                                      new MethodInvoker(
                                                          () =>
                                                          MessageBox.Show(this,
                                                                          String.Format("{0} could not connect:\n{1}",
                                                                                        drive.Name, e.Message), Text)));
                                              }
                                              else
                                              {
                                                  ShowBallon(String.Format("{0} : {1}", drive.Name, e.Message));
                                              }
                                          }
                                      });
        }

        private void muButton_Click(object sender, EventArgs e)
        {
            var drive = driveListView.SelectedItems[0].Tag as SftpDrive;
            
            if (drive.Status == DriveStatus.Unmounted)
            {
                MountDrive(drive);
                muButton.Enabled = false;
            }
            else
            {
                drive.Unmount();
            }
        }


        private void driveListView_MouseUpDown(object sender, MouseEventArgs e)
        {
            if (driveListView.HitTest(e.X, e.Y).Item == null && driveListView.Items.Count != 0)
            {
                driveListView.SelectedIndices.Add(_lastindex);
            }
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            Visible = false;
            Opacity = 1;
            Shown -= MainForm_Shown;

            foreach (var drive in _drives.Where(d => d.Automount))
            {
                MountDrive(drive);
            }
            if (_drives.Count != 0 && _drives[0].Automount)
                muButton.Enabled = false;
            ;
        }

        private void openFileDialog_FileOk(object sender, CancelEventArgs e)
        {
            e.Cancel = !Utilities.IsValidPrivateKey(openFileDialog.FileName);
        }

        private void notifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            _balloonTipVisible = false;
            if (e.Button == MouseButtons.Left)
            {
                ReShow();
            }
        }

        private void exitMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void aboutMenuItem_Click(object sender, EventArgs e)
        {
            new AboutForm().ShowDialog(this);
        }

        private void showMenuItem_Click(object sender, EventArgs e)
        {
            ReShow();
        }

        public void ReShow()
        {
            TopMost = true;
            Visible = true;
            TopMost = false;
        }


        private void notifyIcon_BalloonTipClosed(object sender, EventArgs e)
        {
            _balloonTipVisible = false;
        }

        private void notifyIcon_BalloonTipShown(object sender, EventArgs e)
        {
            _balloonTipVisible = true;
        }

        private void ShowBallon(string text)
        {
            if (!_balloonTipVisible || (_balloonText.Length + text.Length) > 255)
            {
                _balloonText.Clear();
            }

            _balloonText.AppendLine(text);


            notifyIcon.ShowBalloonTip(0, Text, _balloonText.ToString().TrimEnd(), ToolTipIcon.Warning);
        }

        private void driveListView_ClientSizeChanged(object sender, EventArgs e)
        {
            Debug.WriteLine("CLIENT SIZE" + driveListView.ClientRectangle + driveListView.Columns[0].Width);

          //  driveListView.Scrollable = false;
           // driveListView.Refresh();
            driveListView.Columns[0].Width = driveListView.ClientRectangle.Width - 1;

            
           

        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            _drives.Presist("config.xml");

            Parallel.ForEach(_drives.Where(d => d.Status != DriveStatus.Unmounted), d =>
                                                                                      {
                                                                                          d.StatusChanged -=
                                                                                              drive_StatusChanged;
                                                                                          d.Unmount();
                                                                                      });
            base.OnFormClosed(e);
        }
    }
}
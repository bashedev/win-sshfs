using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Renci.SshNet;

namespace Sshfs
{
    public partial class AboutForm : Form
    {
        public AboutForm()
        {
            InitializeComponent();
        }

        private void AboutForm_Load(object sender, EventArgs e)
        {
            label1.Text = String.Format("Sshfs {0}", Assembly.GetEntryAssembly().GetName().Version);
            label2.Text = String.Format("Dokan {0}.{1}.{2}.{3}",DokanNet.Dokan.Version / 1000, (DokanNet.Dokan.Version%1000) / 100, (DokanNet.Dokan.Version%100) / 10, DokanNet.Dokan.Version % 10);
            label3.Text = String.Format("SSH.NET {0}", Assembly.GetAssembly(typeof (SshClient)).GetName().Version);

        }

        private void ok_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void linkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(String.Format("http:\\{0}", (sender as LinkLabel).Text));
        }
    }
}

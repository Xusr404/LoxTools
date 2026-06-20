using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using LoxTools.UI.Tray;

namespace LoxTools.UI.Tray.ContextMenuItems
{
    class FolderToolStripItem : LxToolStripitem
    {
        private string fileDirectory { get; set; }
        public string FileDirectory
        {
            get
            {
                return fileDirectory;
            }
            set
            {
                base.Text = value;
                fileDirectory = value;
            }
        }

        public FolderToolStripItem()
        {
            InitializeComponent();
        }

        public FolderToolStripItem(string fileDirectory)
        {
            InitializeComponent();
            FileDirectory = fileDirectory;
        }

        private void InitializeComponent()
        {
            MouseDown += FolderToolStripItem_Click;
        }

        private void openFolder()
        {
            ContextMenuManager.tryOpenFolder(FileDirectory);
        }

        private void executeWithHiddenCmdWindow(string filePath = null, string Args = null)
        {
            Process cmd = new Process();
            cmd.StartInfo.FileName = filePath;
            cmd.StartInfo.Arguments = Args;
            cmd.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            cmd.StartInfo.CreateNoWindow = true;
            cmd.Start();
        }

        #region Events
        private void FolderToolStripItem_Click(object sender, MouseEventArgs e)
        {
            if ((Control.ModifierKeys & Keys.Shift) != 0)
            {
                shiftPressed = true;
            }

            if (e.Button == MouseButtons.Left)
            {
                leftMouseButtonAction(sender, e);
            }

            shiftPressed = false;
        }

        private void leftMouseButtonAction(object sender, MouseEventArgs e)
        {
            openFolder();
        }

        #endregion
    }
}

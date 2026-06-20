using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using LoxTools.UI.Tray;

namespace LoxTools.UI.Tray.ContextMenuItems {
    class FileToolStripItem : LxToolStripitem {
        private string fileDirectory { get; set; }
        public string FileDirectory {
            get
            {
                return fileDirectory;
            }
            set
            {
                fileDirectory = value;
            }
        }
        private string fileName { get; set; }
        public string FileName {
            get
            {
                return fileName;
            }
            set
            {
                base.Text = value;
                fileName = value;
            }
        }


        public string getFullPath() {
            return $"{FileDirectory}\\{FileName}";
        }
        public void setFullPath(string Path) {
            try {
                FileName = Path.Substring(Path.LastIndexOf("\\") + 1);
                FileDirectory = System.IO.Path.GetDirectoryName(Path);
            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
            }
        }

        public FileToolStripItem() {
            InitializeComponent();
        }

        public FileToolStripItem(string fileDirectory, string fileName) {
            InitializeComponent();
            FileDirectory = fileDirectory;
            FileName = fileName;
        }
        private void InitializeComponent() {
            MouseDown += FileToolStripItem_Click;
        }

        #region Events
        //private void ExecToolStripItem_Click(object sender, MouseEventArgs e) {
        //    if ((Control.ModifierKeys & Keys.Shift) != 0) {
        //        shiftPressed = true;
        //    }

        //    if (e.Button == MouseButtons.Left) {
        //        if (!shiftPressed) {
        //            ContextMenuManager.tryOpenConfig(getFullPath(), shiftPressed);
        //        }
        //    }

        //    shiftPressed = false;
        //}

        #endregion



        #region Events
        private void FileToolStripItem_Click(object sender, MouseEventArgs e) {
            if ((Control.ModifierKeys & Keys.Shift) != 0) {
                shiftPressed = true;
            }

            if (e.Button == MouseButtons.Left) {
                ContextMenuManager.tryOpenFile(getFullPath(), shiftPressed);
            } else if (e.Button == MouseButtons.Middle) {
                ContextMenuManager.tryOpenFolder(FileDirectory);
            } else if (e.Button == MouseButtons.Right) {
                ContextMenuManager.tryOpenFile(getFullPath(), true);
            }

            shiftPressed = false;
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using LoxTools.UI.Tray;

namespace LoxTools.UI.Tray.ContextMenuItems.ToolStripItems {
	class ConfigToolStripItem : ActionToolStripItem {
        public string FileDirectory { get; set; }
        private string fileName;
        public string FileName {
            get => fileName;
            set {
                fileName = value;
                if (string.IsNullOrEmpty(DisplayName)) {
                    base.Text = value;
                }
            }
        }
        private string displayName;
        public string DisplayName {
            get => displayName;
            set {
                displayName = value;
                base.Text = value;
            }
        }
        private bool defaultVersion { get; set; }
        public bool DefaultVersion {
            get => defaultVersion;
            set {
                defaultVersion = value;
                UpdateImage();
            }
        }
        private void UpdateImage()
        {
            base.Image = defaultVersion ? toolStripMenuItemImage : null;
        }

        public ConfigToolStripItem() { }
        public ConfigToolStripItem(string fullPath) {
            setFullPath(fullPath);
        }
        public ConfigToolStripItem(string fileDirectory, string fileName) {
            FileDirectory = fileDirectory;
            FileName = fileName;
        }

        public string getFullExePath() {
            return $"{FileDirectory}\\LoxoneConfig.exe";
        }

        public void setFullPath(string Path) {
            try {
                FileName = Path.Substring(Path.LastIndexOf("\\") + 1);
                FileDirectory = System.IO.Path.GetDirectoryName(Path);
            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
            }
        }


        #region Events
        private void ExecToolStripItem_Click(object sender, MouseEventArgs e) {
            checkShiftActivated();

            if (textAreaClick(e)) {
                Image_Click(sender, e);
            } else {
                Text_Click(sender, e);
            }

            shiftPressed = false;
        }

        private void checkShiftActivated() {
			if ((Control.ModifierKeys & Keys.Shift) != 0) {
				shiftPressed = true;
			}
		}

        public override void ImageClickAction() {
            if(defaultVersion) {
                ContextMenuManager.setLatestDefaultVersion();
            } else {
                ContextMenuManager.unsetLatestItemSelection();
                ContextMenuManager.setDefaultVersion(this);
            }

            ContextMenuManager.AllowContextMenuClosing = false;
        }

		public override void TextClickAction() {
			ContextMenuManager.tryOpenConfig(FileDirectory, shiftPressed);
		}
		public override void TextMiddleMouseClickAction() {
			ContextMenuManager.tryOpenFolder(FileDirectory);
		}

        public override void Image_Click(object sender, MouseEventArgs e) {
            ContextMenuManager.AllowContextMenuClosing = false;
            ImageClickAction();
		}
		#endregion
    }
}

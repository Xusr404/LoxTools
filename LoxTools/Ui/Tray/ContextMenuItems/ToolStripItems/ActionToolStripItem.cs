using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LoxTools.UI.Tray.ContextMenuItems.ToolStripItems {
	class ActionToolStripItem : LxToolStripitem {

        public Image toolStripMenuItemImage { get; set; }

        public ActionToolStripItem() {
            InitializeComponent();
        }

        public virtual void TextClickAction() { }

        public virtual void TextMiddleMouseClickAction() { }

        public virtual void TextRightClickAction() { }

		public virtual void ImageClickAction() { }

		private void InitializeComponent() {
            MouseDown += ActionToolStripItem_Click;
        }

        #region Events
        public virtual void ActionToolStripItem_Click(object sender, MouseEventArgs e) {
			if (textAreaClick(e)) {
				Text_Click(sender, e);
			} else {
				Image_Click(sender, e);
			}
		}
		public bool textAreaClick(MouseEventArgs e) {
			if (e.Location.X > Height * 1.1) {
				return true;
			}
			return false;
		}

		public virtual void Image_Click(object sender, MouseEventArgs e) {
            ImageClickAction();
        }

        public virtual void Text_Click(object sender, MouseEventArgs e) {
            if (e.Button == MouseButtons.Left) {
                TextClickAction();
            } else if (e.Button== MouseButtons.Middle) {
                TextMiddleMouseClickAction();
			} else if (e.Button == MouseButtons.Right) {
                TextRightClickAction();
            }
		}
        #endregion
    }
}

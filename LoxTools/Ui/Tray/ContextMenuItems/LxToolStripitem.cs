using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LoxTools.UI.Tray.ContextMenuItems
{
    class LxToolStripitem : ToolStripMenuItem {
        public bool shiftPressed = false;

        public LxToolStripitem() {
            visualComponentSetup();
        }

        public virtual void visualComponentSetup() {
            base.Size = new Size(152, 22);
            base.ForeColor = Color.WhiteSmoke;
            base.BackColor = Color.Black;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LoxTools.UI.Tray {
    class DarkToolStripRenderer : ToolStripProfessionalRenderer {

        public DarkToolStripRenderer() : base(new DarkColorTable()) {

        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) {
            e.ArrowColor = Color.White;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e) {
            if (e?.Item != null && !e.Item.Enabled && e.Image != null) {
                e.Graphics.DrawImage(e.Image, e.ImageRectangle);
                return;
            }

            base.OnRenderItemImage(e);
        }
    }

    public class DarkColorTable : ProfessionalColorTable {
        public static Color LxAccentColor = Color.Green;
        public static Color DarkBackgroundColor = ColorTranslator.FromHtml("#404040");

        public override Color MenuItemBorder {
            get { return LxAccentColor; }
        }

        public override Color MenuBorder {
            get { return LxAccentColor; }
        }

        public override Color MenuItemPressedGradientBegin {
            get { return ColorTranslator.FromHtml("#4C4A48"); }
        }
        public override Color MenuItemPressedGradientEnd {
            get { return ColorTranslator.FromHtml("#5F5D5B"); }
        }

        public override Color ToolStripBorder {
            get { return LxAccentColor; }
        }

        public override Color MenuItemSelectedGradientBegin {
            get { return DarkBackgroundColor; }
        }

        public override Color MenuItemSelectedGradientEnd {
            get { return DarkBackgroundColor; }
        }

        public override Color ToolStripDropDownBackground {
            get { return ColorTranslator.FromHtml("#232323"); }
        }

        public override Color ToolStripGradientBegin {
            get { return DarkBackgroundColor; }
        }

        public override Color ToolStripGradientEnd {
            get { return DarkBackgroundColor; }
        }

        public override Color ToolStripGradientMiddle {
            get { return DarkBackgroundColor; }
        }

        public override Color SeparatorDark {
            get { return LxAccentColor; }
        }

        public override Color ImageMarginGradientBegin {
            get { return DarkBackgroundColor; }
        }

        public override Color ImageMarginGradientMiddle {
            get { return DarkBackgroundColor; }
        }

        public override Color ImageMarginGradientEnd {
            get { return DarkBackgroundColor; }
        }

        public override Color ImageMarginRevealedGradientBegin {
            get { return DarkBackgroundColor; }
        }

        public override Color ImageMarginRevealedGradientMiddle {
            get { return DarkBackgroundColor; }
        }

        public override Color ImageMarginRevealedGradientEnd {
            get { return DarkBackgroundColor; }
        }
    }
}

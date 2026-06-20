using LoxTools.Language;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Resources;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Configuration;

namespace LoxTools.UI.Tray.ContextMenuItems.ToolStripItems
{
    class ProcessToolStripItem : ActionToolStripItem {
        public Action OnClickAction  { get; private set; }
        public Action OnMiddleClickAction { get; private set; }
		public Func<ToolStripDropDownMenu>? DropDownFactory { get; private set; }

		public ProcessToolStripItem() { }
        public ProcessToolStripItem(Action action)  => OnClickAction  = action;

		public ProcessToolStripItem(string text, Action onClickAction = null, Func<ToolStripDropDownMenu> dropDownFactory = null, Action onMiddleClickAction = null) {
			Text = text;
			OnClickAction = onClickAction;
			DropDownFactory = dropDownFactory;
            OnMiddleClickAction = onMiddleClickAction;
		}

		public void BuildDropDownIfNecessary() {
			if (DropDownFactory != null)
				this.DropDown = DropDownFactory.Invoke();
		}

		public override void TextClickAction() {
			OnClickAction?.Invoke();
		}

        public override void TextMiddleMouseClickAction() {
            OnMiddleClickAction?.Invoke();
        }
	}
}

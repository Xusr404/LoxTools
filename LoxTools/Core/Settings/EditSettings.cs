using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Windows.Forms.CheckedListBox;

namespace LoxTools.Core.Settings {
	public class EditSettings {
		public List<string> deserializeString(string pathArray) {
			if(pathArray != "") {
				List<string> _pathArray = new List<string>(pathArray.Split(new[] { "," }, StringSplitOptions.None));
				return _pathArray;
			}
			return new List<string>();
		}
		public string serializeString(string[] Paths) {
			if(Paths != null) {
				return string.Join(",", Paths);
			}
			return string.Empty;
		}
	}
}

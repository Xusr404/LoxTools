using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LoxTools.Core.IO {
	public class WriteToFile {


		public void WriteAllToFile(List<string> listItems, string FilePathandName, bool appendText = false) {
			if(listItems != null) {
				foreach (var entry in listItems)
					WriteAllToFile($"{entry};", FilePathandName, true);

				WriteAllToFile(string.Empty, FilePathandName, false);
			}
		}


		public void WriteAllToFile(Dictionary<string, string> dictionary, string FilePathandName, bool appendText = false) {

			if (dictionary != null) {
				foreach (var entry in dictionary)
					WriteAllToFile($"{entry.Key} {entry.Value};", FilePathandName, true);

				WriteAllToFile(string.Empty, FilePathandName, false);
			}
		}

        public void WriteAllToFile(string TextToWrite, string FilePathandName, bool newFile = false) {
			if (newFile) {
				System.IO.File.AppendAllText(FilePathandName, TextToWrite);
			}
			else {
					System.IO.File.AppendAllText(FilePathandName, $"{TextToWrite} {Environment.NewLine}");
			}
		}
    }
}

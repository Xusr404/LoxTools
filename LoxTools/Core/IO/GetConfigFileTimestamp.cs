using System;
using System.IO;
using System.Diagnostics;

namespace LoxTools.Core.IO {
	class GetConfigFileTimestamp {
		public string Date { get; set; }
		public string Time { get; set; }
		public string Version { get; set; }

		public void getInfos(string configPath) {
			getFileData(configPath);
			getConfigVerion(configPath);
		}
		public void getFileData(string path) {
			FileInfo fileInfo = new FileInfo(path);

			Date = string.Format("{0:00}", fileInfo.LastWriteTime.Day) + "." + string.Format("{0:00}", fileInfo.LastWriteTime.Month) + "." + string.Format("{0:00}", fileInfo.LastWriteTime.Year);
			Time = string.Format("{0:00}", fileInfo.LastWriteTime.Hour) + ":" + string.Format("{0:00}", fileInfo.LastWriteTime.Minute);
		}

		public void getConfigVerion(string path) {
			FileVersionInfo myFileVersionInfo = FileVersionInfo.GetVersionInfo(path);
			string[] verionParts = myFileVersionInfo.FileVersion.Split(".");

			for(int i = 2; i<4; i++) {
				if (verionParts[i].Length != 2) {
					verionParts[i] = 0 + verionParts[i];
				}
			}

			Version = string.Join(".", verionParts);
		}



		
	}
}

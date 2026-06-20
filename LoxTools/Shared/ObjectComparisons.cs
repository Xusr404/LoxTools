using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LoxTools.Shared {
	public class ObjectComparisons {

		public bool DictionaryIsEqual(Dictionary<string, string> dict1, Dictionary<string, string> dict2) {

			if (dict1.Count() == dict2.Count()) {
				foreach (string key in dict2.Keys) {
					if (!dict1.ContainsKey(key)) {
						return false;
					}
				}
				foreach (string value in dict2.Values) {
					if (!dict1.ContainsValue(value)) {
						return false;
					}
				}
				return true;
			}
			else {
				return false;
			}
		}
		public bool ListIsEqual(List<string> list1, List<string> list2) {

			if (list1.Count() == list2.Count()) {
				foreach (string item in list2) {
					if (!list1.Contains(item)) {
						return false;
					}
				}
				return true;
			}
			else {
				return false;
			}
		}
	}
}

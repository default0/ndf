using System;
using System.Collections.Generic;
using System.Text;

namespace NDF
{
	/// <summary>
	/// Represents a nested dictionary, mapping string keys to CNestedDictionaryNode values.
	/// </summary>
	public class CNestedDictionary : Dictionary<String, CNestedDictionaryNode>
	{
		/// <summary>
		/// Returns a clone of this nested dictionary and all its descendants.
		/// </summary>
		/// <returns>A clone of this nested dictionary and all its descendants.</returns>
		public CNestedDictionary Clone()
		{
			CNestedDictionary p_clone = new CNestedDictionary();
			foreach (KeyValuePair<String, CNestedDictionaryNode> ndf_entry in this)
			{
				p_clone.Add(ndf_entry.Key, ndf_entry.Value.Clone());
			}
			return p_clone;
		}
	}
}

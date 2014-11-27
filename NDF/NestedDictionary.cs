using System;
using System.Collections.Generic;
using System.Text;

namespace NDF
{
	/// <summary>
	/// Represents a nested dictionary, mapping string keys to CNestedDictionaryNode values.
	/// </summary>
	public class NestedDictionary : Dictionary<String, NestedDictionaryNode>
	{
		/// <summary>
		/// Returns a clone of this nested dictionary and all its descendants.
		/// </summary>
		/// <returns>A clone of this nested dictionary and all its descendants.</returns>
		public NestedDictionary Clone()
		{
			NestedDictionary clone = new NestedDictionary();
			foreach (KeyValuePair<String, NestedDictionaryNode> ndfEntry in this)
			{
				clone.Add(ndfEntry.Key, ndfEntry.Value.Clone());
			}
			return clone;
		}
	}
}

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

		/// <summary>
		/// Escapes the given value for insertion as a value in an ndf file.
		/// </summary>
		/// <param name="value">The value to escape.</param>
		/// <returns>The escaped value.</returns>
		public static string EscapeValue(string value)
		{
			if (value == null)
				throw new ArgumentNullException("You must provide a value to escape.");
			else if (value.Length == 0)
				return "";

			return EscapeValueImpl(value);
		}

		private static string EscapeValueImpl(string value)
		{
			unsafe
			{
				int valLen = value.Length;
				char* chars = stackalloc char[valLen << 1];
				int charsIndex = 0;
				for (int i = 0; i < valLen; ++i)
				{
					char curChar = value[i];
					switch (curChar)
					{
						case ';':
						case '{':
						case '}':
							chars[charsIndex] = '\\';
							chars[charsIndex + 1] = curChar;
							charsIndex += 2;
							break;
						case '/':
							if ((i + 1) < valLen && value[i + 1] == '/')
							{
								chars[charsIndex] = '\\';
								chars[charsIndex + 1] = curChar;
								charsIndex += 2;
							}
							break;
						default:
							chars[charsIndex] = curChar;
							++charsIndex;
							break;

					}
				}
				return new string(chars, 0, charsIndex);
			}
		}
	}
}

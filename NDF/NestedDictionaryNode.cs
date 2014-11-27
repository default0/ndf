using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NDF
{
	/// <summary>
	/// Represents a node in a nested dictionary, which can be iterated to retrieve its descendants.
	/// </summary>
	public class NestedDictionaryNode : IEnumerable<NestedDictionary>
	{
		internal List<NestedDictionary> NestedDictionaries { get; set; }

		/// <summary>
		/// The key this nested dictionary node is associated with. Can be null if this node is the root node.
		/// </summary>
		public String Key { get; internal set; }
		/// <summary>
		/// The value this nested dictionary holds. Can be null if no value has been set. Avoid using this, as a CNestedDictionaryNode implicitly converts to a string by returning its value.
		/// </summary>
		public String Value { get; internal set; }

		/// <summary>
		/// Constructs an empty nested dictionary node.
		/// </summary>
		public NestedDictionaryNode()
		{
			NestedDictionaries = new List<NestedDictionary>();
		}

		/// <summary>
		/// Constructs the root node for the given file.
		/// </summary>
		/// <param name="filePath">The file to construct data from.</param>
		public NestedDictionaryNode(String filePath)
			: this(File.OpenText(filePath))
		{
		}

		internal NestedDictionaryNode(StreamReader reader) : this()
		{
			NestedDictionaries = NestedDictionaryParser.ParseNDF(reader);
			reader.Close();
		}
		internal NestedDictionaryNode(TextReader reader)
			: this()
		{
			NestedDictionaries = NestedDictionaryParser.ParseNDF(reader);
			reader.Close();
		}

		/// <summary>
		/// Saves this nested dictionary node to the given file.
		/// </summary>
		/// <param name="filePath">The file to save to.</param>
		public void Save(String filePath)
		{
			StreamWriter p_writer = new StreamWriter(File.OpenWrite(filePath));
			p_writer.AutoFlush = false;

			if (!IsRoot())
			{
				p_writer.Write(Key);
				p_writer.Write(':');
				p_writer.Write(Value);
				p_writer.WriteLine(';');
				if (NestedDictionaries.Count > 0) p_writer.WriteLine('{');
			}

			if (NestedDictionaries.Count > 0)
			{
				NestedDictionary p_last_dict = NestedDictionaries.FindLast(x => true);
				foreach (NestedDictionary p_nested_dictionary in NestedDictionaries)
				{
					foreach (KeyValuePair<string, NestedDictionaryNode> p_nested_dictionary_node in p_nested_dictionary)
					{
						p_nested_dictionary_node.Value.Save(p_writer, 0);
					}
					if (p_nested_dictionary != p_last_dict) p_writer.WriteLine();
				}
				if (!IsRoot()) p_writer.WriteLine('}');
			}

			p_writer.Flush();
			p_writer.Close();
		}
		private void Save(StreamWriter writer, Int32 indentationCount)
		{
			String p_indent = new String('\t', indentationCount);

			writer.Write(p_indent);
			writer.Write(Key);
			if (Value != null)
			{
				writer.Write(':');
				writer.Write(Value);
			}

			++indentationCount;
			if (NestedDictionaries.Count > 0)
			{
				writer.WriteLine();
				writer.Write(p_indent);
				writer.WriteLine('{');
				foreach (NestedDictionary p_nested_dictionary in NestedDictionaries)
				{
					NestedDictionary p_last_dict = NestedDictionaries.FindLast(x => true);
					foreach (KeyValuePair<String, NestedDictionaryNode> p_nested_dictionary_node in p_nested_dictionary)
					{
						p_nested_dictionary_node.Value.Save(writer, indentationCount);
					}
					if (p_nested_dictionary != p_last_dict) writer.WriteLine();
				}
				writer.Write(p_indent);
				writer.WriteLine('}');
			}
			else
			{
				writer.WriteLine(';');
			}
		}

		/// <summary>
		/// Determines whether this nested dictionary node is the root node of its nested dictionary. Returns true, if it is, false otherwise.
		/// </summary>
		/// <returns>True, if this nested dictionary node is the root node of its nested dictionary, false otherwise.</returns>
		public Boolean IsRoot()
		{
			return String.IsNullOrEmpty(Key) && String.IsNullOrEmpty(Value);
		}

		internal void ReplaceValues(String oldValue, String newValue)
		{
			if (Value == oldValue)
			{
				Value = newValue;
			}
			foreach (NestedDictionary p_dict in NestedDictionaries)
			{
				foreach (KeyValuePair<string, NestedDictionaryNode> nested_node in p_dict)
				{
					nested_node.Value.ReplaceValues(oldValue, newValue);
				}
			}
		}

		/// <summary>
		/// Returns a copy of this nested dictionary node and all its descendants.
		/// </summary>
		/// <returns>A copy of this nested dictionary node and all its descendants.</returns>
		public NestedDictionaryNode Clone()
		{
			NestedDictionaryNode clone = new NestedDictionaryNode();
			foreach (NestedDictionary nestedDict in this)
			{
				clone.NestedDictionaries.Add(nestedDict.Clone());
			}

			clone.Key = Key;
			clone.Value = Value;
			return clone;
		}

		public static implicit operator String(NestedDictionaryNode node)
		{
			return node.Value;
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return NestedDictionaries.GetEnumerator();
		}

		IEnumerator<NestedDictionary> IEnumerable<NestedDictionary>.GetEnumerator()
		{
			return NestedDictionaries.GetEnumerator();
		}
	}
}

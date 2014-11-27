using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NDF
{
	/// <summary>
	/// Represents a node in a nested dictionary, which can be iterated to retrieve its descendants.
	/// </summary>
	public class CNestedDictionaryNode : IEnumerable<CNestedDictionary>
	{
		internal List<CNestedDictionary> m_p_nested_dictionaries;

		/// <summary>
		/// The key this nested dictionary node is associated with. Can be null if this node is the root node.
		/// </summary>
		public String m_p_key { get; internal set; }
		/// <summary>
		/// The value this nested dictionary holds. Can be null if no value has been set. Avoid using this, as a CNestedDictionaryNode implicitly converts to a string by returning its value.
		/// </summary>
		public String m_p_value { get; internal set; }

		/// <summary>
		/// Constructs an empty nested dictionary node.
		/// </summary>
		public CNestedDictionaryNode()
		{
			m_p_nested_dictionaries = new List<CNestedDictionary>();
		}

		/// <summary>
		/// Constructs the root node for the given file.
		/// </summary>
		/// <param name="p_file">The file to construct data from.</param>
		public CNestedDictionaryNode(String p_file)
			: this(File.OpenText(p_file))
		{
		}

		internal CNestedDictionaryNode(StreamReader p_reader) : this()
		{
			m_p_nested_dictionaries = CNestedDictionaryParser.ParseNDF(p_reader);
			p_reader.Close();
		}
		internal CNestedDictionaryNode(TextReader p_reader)
			: this()
		{
			m_p_nested_dictionaries = CNestedDictionaryParser.ParseNDF(p_reader);
			p_reader.Close();
		}

		/// <summary>
		/// Saves this nested dictionary node to the given file.
		/// </summary>
		/// <param name="p_file">The file to save to.</param>
		public void Save(String p_file)
		{
			StreamWriter p_writer = new StreamWriter(File.OpenWrite(p_file));
			p_writer.AutoFlush = false;

			if (!IsRoot())
			{
				p_writer.Write(m_p_key);
				p_writer.Write(':');
				p_writer.Write(m_p_value);
				p_writer.WriteLine(';');
				if (m_p_nested_dictionaries.Count > 0) p_writer.WriteLine('{');
			}

			if (m_p_nested_dictionaries.Count > 0)
			{
				CNestedDictionary p_last_dict = m_p_nested_dictionaries.FindLast(x => true);
				foreach (CNestedDictionary p_nested_dictionary in m_p_nested_dictionaries)
				{
					foreach (KeyValuePair<string, CNestedDictionaryNode> p_nested_dictionary_node in p_nested_dictionary)
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
		private void Save(StreamWriter p_writer, Int32 indentation_count)
		{
			String p_indent = new String('\t', indentation_count);

			p_writer.Write(p_indent);
			p_writer.Write(m_p_key);
			if (m_p_value != null)
			{
				p_writer.Write(':');
				p_writer.Write(m_p_value);
			}

			++indentation_count;
			if (m_p_nested_dictionaries.Count > 0)
			{
				p_writer.WriteLine();
				p_writer.Write(p_indent);
				p_writer.WriteLine('{');
				foreach (CNestedDictionary p_nested_dictionary in m_p_nested_dictionaries)
				{
					CNestedDictionary p_last_dict = m_p_nested_dictionaries.FindLast(x => true);
					foreach (KeyValuePair<String, CNestedDictionaryNode> p_nested_dictionary_node in p_nested_dictionary)
					{
						p_nested_dictionary_node.Value.Save(p_writer, indentation_count);
					}
					if (p_nested_dictionary != p_last_dict) p_writer.WriteLine();
				}
				p_writer.Write(p_indent);
				p_writer.WriteLine('}');
			}
			else
			{
				p_writer.WriteLine(';');
			}
		}

		/// <summary>
		/// Determines whether this nested dictionary node is the root node of its nested dictionary. Returns true, if it is, false otherwise.
		/// </summary>
		/// <returns>True, if this nested dictionary node is the root node of its nested dictionary, false otherwise.</returns>
		public Boolean IsRoot()
		{
			return string.IsNullOrEmpty(m_p_key) && string.IsNullOrEmpty(m_p_value);
		}

		internal void ReplaceValues(String p_old_value, String p_new_value)
		{
			if (m_p_value == p_old_value)
			{
				m_p_value = p_new_value;
			}
			foreach (CNestedDictionary p_dict in m_p_nested_dictionaries)
			{
				foreach (KeyValuePair<string, CNestedDictionaryNode> nested_node in p_dict)
				{
					nested_node.Value.ReplaceValues(p_old_value, p_new_value);
				}
			}
		}

		/// <summary>
		/// Returns a copy of this nested dictionary node and all its descendants.
		/// </summary>
		/// <returns>A copy of this nested dictionary node and all its descendants.</returns>
		public CNestedDictionaryNode Clone()
		{
			CNestedDictionaryNode p_clone = new CNestedDictionaryNode();
			foreach (CNestedDictionary p_nested_dict in this)
			{
				p_clone.m_p_nested_dictionaries.Add(p_nested_dict.Clone());
			}

			p_clone.m_p_key = m_p_key;
			p_clone.m_p_value = m_p_value;
			return p_clone;
		}

		public static implicit operator String(CNestedDictionaryNode p_node)
		{
			return p_node.m_p_value;
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return m_p_nested_dictionaries.GetEnumerator();
		}

		IEnumerator<CNestedDictionary> IEnumerable<CNestedDictionary>.GetEnumerator()
		{
			return m_p_nested_dictionaries.GetEnumerator();
		}
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NDF
{
	internal static class CNestedDictionaryParser
	{
		private enum EParsingPosition
		{
			key = 0,
			value = 1,
			comment = 2,
		}

		internal static List<CNestedDictionary> ParseNDF(TextReader p_reader)
		{
			List<CNestedDictionary> p_ndf = new List<CNestedDictionary>();

			CNestedDictionary p_current_dict = new CNestedDictionary();
			CNestedDictionaryNode p_current_node = new CNestedDictionaryNode();

			EParsingPosition pos = EParsingPosition.key;
			StringBuilder p_buffer = new StringBuilder();
			while (p_reader.Peek() != -1)
			{
				switch (p_reader.Peek())
				{
						// check for comment
					case (Int32)'/':
						p_reader.Read();
						if (p_reader.Peek() == (Int32)'/')
						{
							while (p_reader.Peek() != (Int32)'\n' && p_reader.Peek() != -1) // comment terminates at EOF or line-end
							{
								p_reader.Read();
							}
							p_reader.Read();
						}
						else
						{
							p_buffer.Append('/');
						}
						break;
					case (Int32)':':
						// : are allowed for values, just not for keys
						if (pos == EParsingPosition.value)
						{
							goto default;
						}

						AddBuffer(p_current_node, p_current_dict, pos, p_buffer); // merge the key with the node
						
						p_reader.Read(); // consume
						pos = EParsingPosition.value;

						break;
					case (Int32)';':

						AddBuffer(p_current_node, p_current_dict, pos, p_buffer); // merge the value or the key with the node
						
						p_reader.Read(); // consume
						pos = EParsingPosition.key;

						AddNode(p_ndf, ref p_current_dict, ref p_current_node); // node end, so add the node

						break;
					case (Int32)'{':

						p_buffer = new StringBuilder(p_buffer.ToString().TrimEnd('\r', '\n', '\t', '\0', ' '));
						AddBuffer(p_current_node, p_current_dict, pos, p_buffer); // merge the value or the key with the node
						
						p_reader.Read(); // consume before parsing the nested
						p_current_node.m_p_nested_dictionaries = ParseNDF(p_reader); // parse the nested dictionary

						AddNode(p_ndf, ref p_current_dict, ref p_current_node); // node end, so add the node

						pos = EParsingPosition.key;

						break;
					case (Int32)'}':
						p_reader.Read(); // consume before stopping execution
						if (p_current_dict.Count > 0) p_ndf.Add(p_current_dict); // nested dictionary end
						return p_ndf;
					case (Int32)'\r':
					case (Int32)'\n':
					case (Int32)'\t':
					case (Int32)'\0':
					case (Int32)' ':
						if (pos == EParsingPosition.value)
						{
							p_buffer.Append((Char)p_reader.Read());
						}
						else
						{
							p_reader.Read(); // consume, ignored sign
						}
						break;
					default:
						p_buffer.Append((Char)p_reader.Read()); // add to buffer for key or value
						break;
				}
			}
			if (p_current_dict.Count > 0) p_ndf.Add(p_current_dict);

			PostProcess(p_ndf);

			return p_ndf;
		}

		private static void AddNode(List<CNestedDictionary> p_ndf, ref CNestedDictionary p_current_dict, ref CNestedDictionaryNode p_current_node)
		{
			if (p_current_node.m_p_key == null) return; // only occurs should the "node" be a reference

			if (!p_current_dict.ContainsKey(p_current_node.m_p_key))
			{
				p_current_dict.Add(p_current_node.m_p_key, p_current_node);
			}
			else
			{
				p_ndf.Add(p_current_dict);
				p_current_dict = new CNestedDictionary();
				p_current_dict.Add(p_current_node.m_p_key, p_current_node);
			}
			p_current_node = new CNestedDictionaryNode();
		}
		private static void AddBuffer(CNestedDictionaryNode p_node, CNestedDictionary p_current_dict, EParsingPosition pos, StringBuilder p_buffer)
		{
			switch (pos)
			{
				case EParsingPosition.key:
					p_node.m_p_key = p_buffer.ToString();
					break;
				case EParsingPosition.value:
					p_node.m_p_value = p_buffer.ToString();
					break;
			}
			p_buffer = new StringBuilder();
		}

		/*
		 * Semantic: @PP.Expand[value,value,...,value]
		 * 
		 * Range:@PP.Expand[100,150];
		 * Damage:@PP.Expand[20,25];
		 * 
		 * Converts to:
		 * 
		 * @PP.Resolve[SomeThing,OtherThing]
		 * {
		 *	SomeThing:@PP.Expand[10,20];
		 *	OtherThing:@PP.Expand[30,50];
		 * }
		 * Range:30;
		 * Damage:20;
		 * Nested
		 * {
		 *	Value:SomeThing;
		 *	Other:OtherThing;
		 * }
		 * 
		 * =>
		 * 
		 * Range:30;
		 * Damage:20;
		 * Nested
		 * {
		 *	Value:SomeThing;
		 *	Other:OtherThing;
		 * }
		*/
		private static void PostProcess(List<CNestedDictionary> p_ndf_data)
		{
			for (Int32 i = 0; i < p_ndf_data.Count; ++i)
			{
				List<CNestedDictionary> p_processed_dictionaries = PostProcessSingle(p_ndf_data[i]);
				if (p_processed_dictionaries.Count > 1)
				{
					p_ndf_data.RemoveAt(i);
					p_ndf_data.InsertRange(i, p_processed_dictionaries);
					--i; // the dictionary currently at i has not yet been expanded since its a new one!
				}
			}
		}

		private static List<CNestedDictionary> PostProcessSingle(CNestedDictionary p_ndf_dict)
		{
			List<CNestedDictionary> p_processed_dictionaries = new List<CNestedDictionary>();
			List<String> p_removed_keys = new List<String>();
			foreach (KeyValuePair<String, CNestedDictionaryNode> ndf_entry in p_ndf_dict)
			{
				PostProcess(ndf_entry.Value.m_p_nested_dictionaries);

				if (ndf_entry.Key.StartsWith("@PP.Replace[") && ndf_entry.Key.EndsWith("]") && ndf_entry.Value.m_p_nested_dictionaries.Count > 0)
				{
					String p_inner_key = ndf_entry.Key.Substring("@PP.Replace[".Length, ndf_entry.Key.Length - "@PP.Replace[".Length - "]".Length);
					String[] p_variable_names = p_inner_key.Split(',');

					foreach (CNestedDictionary p_nested_dict in ndf_entry.Value)
					{
						CNestedDictionary p_processed_dict = p_ndf_dict.Clone();
						foreach (String p_var_name in p_variable_names)
						{
							if (!p_nested_dict.ContainsKey(p_var_name))
							{
								throw new Exception("Post-Processor Replace does not have all required variables defined. Missing " + p_var_name);
							}
							ReplaceVariable(p_processed_dict, p_var_name, p_nested_dict[p_var_name]);
						}
						p_processed_dictionaries.Add(p_processed_dict);
					}
					p_removed_keys.Add(ndf_entry.Key);
					break;
				}

				String p_value = ndf_entry.Value.m_p_value;
				if (p_value == null || !p_value.StartsWith("@PP.Expand[") || !p_value.EndsWith("]")) continue;

				String p_inner = p_value.Substring("@PP.Expand[".Length, p_value.Length - "@PP.Expand[".Length - "]".Length);
				String[] p_expanded_values = p_inner.Split(',');

				foreach (String p_expanded_value in p_expanded_values)
				{
					CNestedDictionary p_processed_dict = p_ndf_dict.Clone();
					p_processed_dict[ndf_entry.Key].m_p_value = p_expanded_value;
					p_processed_dictionaries.Add(p_processed_dict);
				}
				break;
			}

			foreach (String p_removed_key in p_removed_keys)
			{
				foreach (CNestedDictionary p_nested_dict in p_processed_dictionaries)
				{
					p_nested_dict.Remove(p_removed_key);
				}
			}

			return p_processed_dictionaries;
		}

		private static void ReplaceVariable(CNestedDictionary p_dictionary, String p_variable_name, String p_substitute)
		{
			foreach(KeyValuePair<String, CNestedDictionaryNode> ndf_entry in p_dictionary)
			{
				if(ndf_entry.Value.m_p_value != null)
				{
					ndf_entry.Value.m_p_value = ndf_entry.Value.m_p_value.Replace(p_variable_name, p_substitute);
				}

				foreach(CNestedDictionary p_nested_dict in ndf_entry.Value)
				{
					ReplaceVariable(p_nested_dict, p_variable_name, p_substitute);
				}
			}
		}
	}
}

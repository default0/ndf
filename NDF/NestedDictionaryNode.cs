using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormats.NDF
{
    /// <summary>
    /// Represents a node in a nested dictionary, which can be iterated to retrieve its descendants.
    /// </summary>
    public class NestedDictionaryNode : IEnumerable<NestedDictionary>
    {
        internal List<NestedDictionary> NestedDictionaries;

        /// <summary>
        /// The key this nested dictionary node is associated with. Can be null if this node is the root node.
        /// </summary>
        public string Key { get; internal set; }

        /// <summary>
        /// The value this nested dictionary holds. Can be null if no value has been set. Avoid using this, as a CNestedDictionaryNode implicitly converts to a string by returning its value.
        /// </summary>
        public string Value { get; internal set; }

        /// <summary>
        /// Returns the number of direct descendants of this nested dictionary node.
        /// </summary>
        public int Count => NestedDictionaries.Count;

        /// <summary>
        /// Returns the descendant dictionary at the given index of this nested dictionary node.
        /// </summary>
        /// <param name="index">The index of the descendant to return.</param>
        /// <returns>The descendant dictionary at the given index of this dictionary node.</returns>
        public NestedDictionary this[int index]
        {
            get
            {
                return NestedDictionaries[index];
            }
            set
            {
                NestedDictionaries[index] = value;
            }
        }

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
        /// <param name="p_file">The file to construct data from.</param>
        public NestedDictionaryNode(string p_file)
            : this(File.OpenText(p_file))
        {
        }

        internal NestedDictionaryNode(StreamReader p_reader)
            : this()
        {
            NestedDictionaries = NestedDictionaryParser.ParseNDF(p_reader);
            p_reader.Close();
        }

        internal NestedDictionaryNode(TextReader p_reader)
            : this()
        {
            NestedDictionaries = NestedDictionaryParser.ParseNDF(p_reader);
            p_reader.Close();
        }

        /// <summary>
        /// Parses the given ndf data into this nested dictionary node. This node will act as the root node for the parsed ndf data.
        /// </summary>
        /// <param name="p_ndf">The ndf data to parse.</param>
        public static NestedDictionaryNode ParseFromNdfString(string p_ndf)
        {
            var node = new NestedDictionaryNode();

            StringReader p_reader = new StringReader(p_ndf);
            node.NestedDictionaries = NestedDictionaryParser.ParseNDF(p_reader);
            p_reader.Close();

            return node;
        }

        /// <summary>
        /// Parses the given ndf data into this nested dictionary node. This node will act as the root node for the parsed ndf data.
        /// </summary>
        /// <param name="p_ndf">The ndf data to parse.</param>
        public void ParseFromString(string p_ndf)
        {
            StringReader p_reader = new StringReader(p_ndf);
            NestedDictionaries = NestedDictionaryParser.ParseNDF(p_reader);
            p_reader.Close();
        }

        /// <summary>
        /// Adds the entry specified by the given key and value to the nested dictionary node.
        /// </summary>
        /// <param name="p_key">The key to add to the dictionary.</param>
        /// <param name="p_value">The value to associate with the key.</param>
        public void AddEntry(string p_key, string p_value)
        {
            NestedDictionary p_nested_dict;
            if (NestedDictionaries.Count == 0)
            {
                p_nested_dict = new NestedDictionary();
                NestedDictionaries.Add(p_nested_dict);
            }
            else
            {
                p_nested_dict = NestedDictionaries[NestedDictionaries.Count - 1];
            }

            if (p_nested_dict.ContainsKey(p_key))
            {
                p_nested_dict = new NestedDictionary();
                NestedDictionaries.Add(p_nested_dict);
            }
            p_nested_dict.Add(p_key, new NestedDictionaryNode()
            {
                Key = p_key,
                Value = p_value
            });
        }

        /// <summary>
        /// Saves this nested dictionary node to the given file.
        /// </summary>
        /// <param name="p_file">The file to save to.</param>
        public void Save(string p_file)
        {
            if (File.Exists(p_file))
                File.Delete(p_file);

            StreamWriter p_writer = new StreamWriter(File.OpenWrite(p_file));
            p_writer.AutoFlush = false;

            p_writer.Write(GetAsString());

            p_writer.Flush();
            p_writer.Close();
        }

        public string GetAsString(bool pretty = true)
        {
            return GetAsString(0, pretty);
        }

        private string GetAsString(int indentation_level, bool pretty)
        {
            StringBuilder p_result = new StringBuilder();

            string p_indent;
            if (indentation_level <= 0 || !pretty)
                p_indent = "";
            else
                p_indent = new string('\t', indentation_level);

            if (!IsRoot())
            {
                p_result.Append(p_indent);
                p_result.Append(Key);
                if (Value != null)
                {
                    p_result.Append(':');
                    p_result.Append(NestedDictionary.EscapeValue(Value));
                }

                if (NestedDictionaries.Count > 0)
                {
                    if(pretty)
                        p_result.AppendLine();
                    p_result.Append(p_indent);
                    if (pretty)
                        p_result.AppendLine("{");
                    else
                        p_result.Append('{');
                }
                else
                {
                    if (pretty)
                        p_result.AppendLine(";");
                    else
                        p_result.Append(';');
                }
            }

            if (NestedDictionaries.Count > 0)
            {
                for (int i = 0; i < NestedDictionaries.Count; ++i)
                {
                    NestedDictionary p_nested_dictionary = NestedDictionaries[i];
                    foreach (KeyValuePair<string, NestedDictionaryNode> p_nested_dictionary_node in p_nested_dictionary)
                    {
                        if (pretty)
                            p_result.AppendLine(p_nested_dictionary_node.Value.GetAsString(indentation_level + 1, pretty));
                        else
                            p_result.Append(p_nested_dictionary_node.Value.GetAsString(indentation_level + 1, pretty));
                    }
                    if ((i + 1) < NestedDictionaries.Count && pretty) p_result.AppendLine();
                }

                if (!IsRoot())
                {
                    if (pretty)
                    {
                        p_result.Append(p_indent);
                        p_result.AppendLine("}");
                    }
                    else
                    {
                        p_result.Append('}');
                    }
                }
            }

            if (p_result.Length > 0 && pretty)
            {
                if (p_result[p_result.Length - 2] == '\r' && p_result[p_result.Length - 1] == '\n')
                    p_result.Remove(p_result.Length - 2, 2);
                else if (p_result[p_result.Length - 1] == '\n')
                    p_result.Remove(p_result.Length - 1, 1);
            }

            return p_result.ToString();
        }

        /// <summary>
        /// Determines whether this nested dictionary node is the root node of its nested dictionary. Returns true, if it is, false otherwise.
        /// </summary>
        /// <returns>True, if this nested dictionary node is the root node of its nested dictionary, false otherwise.</returns>
        public bool IsRoot()
        {
            return Key == null && Value == null;
        }

        internal void ReplaceValues(string p_old_value, string p_new_value)
        {
            if (Value == p_old_value)
            {
                Value = p_new_value;
            }
            for (int i = 0; i < NestedDictionaries.Count; ++i)
            {
                NestedDictionary p_dict = NestedDictionaries[i];
                foreach (KeyValuePair<string, NestedDictionaryNode> nested_node in p_dict)
                {
                    nested_node.Value.ReplaceValues(p_old_value, p_new_value);
                }
            }
        }

        /// <summary>
        /// Returns a copy of this nested dictionary node and all its descendants.
        /// </summary>
        /// <returns>A copy of this nested dictionary node and all its descendants.</returns>
        public NestedDictionaryNode Clone()
        {
            NestedDictionaryNode p_clone = new NestedDictionaryNode();
            for (int i = 0; i < NestedDictionaries.Count; ++i)
            {
                p_clone.NestedDictionaries.Add(NestedDictionaries[i].Clone());
            }

            p_clone.Key = Key;
            p_clone.Value = Value;
            return p_clone;
        }

        public static implicit operator string(NestedDictionaryNode p_node)
        {
            return p_node.Value;
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
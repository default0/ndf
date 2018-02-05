using System;
using System.Collections.Generic;
using System.Text;

namespace FileFormats.NDF
{
    /// <summary>
    /// Represents a nested dictionary, mapping string keys to CNestedDictionaryNode values.
    /// </summary>
    public class NestedDictionary : Dictionary<string, NestedDictionaryNode>
    {
        /// <summary>
        /// Creates a new, empty nested dictionary.
        /// </summary>
        public NestedDictionary()
        {
        }

        /// <summary>
        /// Creates a new nested dictionary containing the same values as the first dictionary in the specified ndf data.
        /// </summary>
        /// <param name="p_ndf_data">The ndf data to fill the dictionary from.</param>
        public NestedDictionary(string p_ndf_data)
        {
            NestedDictionaryNode p_node = new NestedDictionaryNode();
            p_node.ParseFromString(p_ndf_data);
            if (p_node.Count > 0)
            {
                foreach (KeyValuePair<string, NestedDictionaryNode> ndf_entry in p_node[0])
                {
                    Add(ndf_entry.Key, ndf_entry.Value);
                }
            }
        }

        /// <summary>
        /// Returns a clone of this nested dictionary and all its descendants.
        /// </summary>
        /// <returns>A clone of this nested dictionary and all its descendants.</returns>
        public NestedDictionary Clone()
        {
            NestedDictionary p_clone = new NestedDictionary();
            foreach (KeyValuePair<string, NestedDictionaryNode> ndf_entry in this)
            {
                p_clone.Add(ndf_entry.Key, ndf_entry.Value.Clone());
            }
            return p_clone;
        }

        public string GetAsString()
        {
            StringBuilder p_result = new StringBuilder();
            foreach (KeyValuePair<string, NestedDictionaryNode> entry in this)
            {
                p_result.AppendLine(entry.Value.GetAsString());
            }
            return p_result.ToString();
        }

        public string GetOrNull(string p_value)
        {
            if (TryGetValue(p_value, out var result))
                return result;
            else
                return null;
        }

        /// <summary>
        /// Escapes the given value for insertion as a value in an ndf file.
        /// </summary>
        /// <param name="p_value">The value to escape.</param>
        /// <returns>The escaped value.</returns>
        public static string EscapeValue(string p_value)
        {
            if (p_value == null)
                throw new ArgumentNullException("You must provide a value to escape.");
            else if (p_value.Length == 0)
                return "";

            return EscapeValueImpl(p_value);
        }

        private static string EscapeValueImpl(string p_value)
        {
            unsafe
            {
                int val_len = p_value.Length;
                char* p_chars = stackalloc char[val_len << 1];
                int chars_index = 0;
                for (int i = 0; i < val_len; ++i)
                {
                    char cur_char = p_value[i];
                    switch (cur_char)
                    {
                        case ';':
                        case '{':
                        case '}':
                            p_chars[chars_index] = '\\';
                            p_chars[chars_index + 1] = cur_char;
                            chars_index += 2;
                            break;

                        case '/':
                            if ((i + 1) < val_len && p_value[i + 1] == '/')
                            {
                                p_chars[chars_index] = '\\';
                                p_chars[chars_index + 1] = cur_char;
                                chars_index += 2;
                            }
                            break;

                        default:
                            p_chars[chars_index] = cur_char;
                            ++chars_index;
                            break;
                    }
                }
                return new string(p_chars, 0, chars_index);
            }
        }
    }
}

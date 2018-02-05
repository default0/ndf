using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FileFormats.NDF
{
    internal static class NestedDictionaryParser
    {
        private enum EParsingPosition
        {
            key = 0,
            value = 1
        }

        internal static List<NestedDictionary> ParseNDF(TextReader p_reader)
        {
            List<NestedDictionary> p_ndf = new List<NestedDictionary>();

            NestedDictionary p_current_dict = new NestedDictionary();
            NestedDictionaryNode p_current_node = new NestedDictionaryNode();

            EParsingPosition pos = EParsingPosition.key;
            bool is_escaped = false;
            StringBuilder p_buffer = new StringBuilder();
            while (p_reader.Peek() != -1)
            {
                if (is_escaped)
                {
                    p_buffer.Append((char)p_reader.Read()); // add to buffer for key or value
                    continue;
                }
                switch (p_reader.Peek())
                {
                    // check for comment
                    case (int)'/':
                        p_reader.Read();
                        if (p_reader.Peek() == (int)'/')
                        {
                            int peek_val;
                            StringBuilder p_comment = new StringBuilder();
                            do
                            {
                                p_reader.Read();
                                peek_val = p_reader.Peek();
                                p_comment.Append((char)peek_val);
                            }
                            while (peek_val != (int)'\n' && peek_val != -1); // comment terminates at EOF or line-end
                            p_reader.Read();
                        }
                        else
                        {
                            p_buffer.Append('/');
                        }
                        break;

                    case (int)':':
                        // : are allowed for values, just not for keys
                        if (pos == EParsingPosition.value)
                        {
                            goto default;
                        }

                        AddBuffer(p_current_node, p_current_dict, pos, p_buffer); // merge the key with the node

                        p_reader.Read(); // consume
                        pos = EParsingPosition.value;

                        break;

                    case (int)';':

                        AddBuffer(p_current_node, p_current_dict, pos, p_buffer); // merge the value or the key with the node

                        p_reader.Read(); // consume
                        pos = EParsingPosition.key;

                        AddNode(p_ndf, ref p_current_dict, ref p_current_node); // node end, so add the node

                        break;

                    case (int)'{':
                        int trail_length = 0;
                        for (int i = p_buffer.Length - 1; i > -1; --i)
                        {
                            switch (p_buffer[i])
                            {
                                case '\r':
                                case '\n':
                                case '\t':
                                case '\0':
                                case ' ':
                                    ++trail_length;
                                    break;

                                default:
                                    goto loop_end;
                            }
                        }
                        loop_end:
                        if (trail_length > 0)
                        {
                            p_buffer.Remove(p_buffer.Length - trail_length, trail_length);
                        }
                        AddBuffer(p_current_node, p_current_dict, pos, p_buffer); // merge the value or the key with the node

                        p_reader.Read(); // consume before parsing the nested
                        p_current_node.NestedDictionaries = ParseNDF(p_reader); // parse the nested dictionary

                        AddNode(p_ndf, ref p_current_dict, ref p_current_node); // node end, so add the node

                        pos = EParsingPosition.key;

                        break;

                    case (int)'}':
                        p_reader.Read(); // consume before stopping execution
                        if (p_current_dict.Count > 0) p_ndf.Add(p_current_dict); // nested dictionary end
                        return p_ndf;

                    case (int)'\r':
                    case (int)'\n':
                    case (int)'\t':
                    case (int)'\0':
                    case (int)' ':
                        if (pos == EParsingPosition.value)
                        {
                            p_buffer.Append((char)p_reader.Read());
                        }
                        else
                        {
                            p_reader.Read(); // consume, ignored sign
                        }
                        break;

                    case (int)'\\':
                        is_escaped = true;
                        p_reader.Read();
                        break;

                    default:
                        p_buffer.Append((char)p_reader.Read()); // add to buffer for key or value
                        break;
                }
            }
            if (p_current_dict.Count > 0) p_ndf.Add(p_current_dict);

            PostProcess(p_ndf);

            return p_ndf;
        }

        private static void AddNode(List<NestedDictionary> p_ndf, ref NestedDictionary p_current_dict, ref NestedDictionaryNode p_current_node)
        {
            string p_node_key = p_current_node.Key;
            if (p_node_key == null) return; // only occurs should the "node" be a reference

            if (!p_current_dict.ContainsKey(p_node_key))
            {
                p_current_dict.Add(p_node_key, p_current_node);
            }
            else
            {
                p_ndf.Add(p_current_dict);
                p_current_dict = new NestedDictionary();
                p_current_dict.Add(p_node_key, p_current_node);
            }
            p_current_node = new NestedDictionaryNode();
        }

        private static void AddBuffer(NestedDictionaryNode p_node, NestedDictionary p_current_dict, EParsingPosition pos, StringBuilder p_buffer)
        {
            switch (pos)
            {
                case EParsingPosition.key:
                    p_node.Key = p_buffer.ToString();
                    break;

                case EParsingPosition.value:
                    p_node.Value = p_buffer.ToString();
                    break;
            }
            p_buffer.Clear();
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
         *  SomeThing:@PP.Expand[10,20];
         *  OtherThing:@PP.Expand[30,50];
         * }
         * Range:30;
         * Damage:20;
         * Nested
         * {
         *  Value:SomeThing;
         *  Other:OtherThing;
         * }
         *
         * =>
         *
         * Range:30;
         * Damage:20;
         * Nested
         * {
         *  Value:SomeThing;
         *  Other:OtherThing;
         * }
        */

        private static void PostProcess(List<NestedDictionary> p_ndf_data)
        {
            for (int i = 0; i < p_ndf_data.Count; ++i)
            {
                List<NestedDictionary> p_processed_dictionaries = PostProcessSingle(p_ndf_data[i]);
                if (p_processed_dictionaries.Count > 0)
                {
                    p_ndf_data.RemoveAt(i);
                    p_ndf_data.InsertRange(i, p_processed_dictionaries);
                    --i; // the dictionary currently at i has not yet been expanded since its a new one!
                }
            }
        }

        private static List<NestedDictionary> PostProcessSingle(NestedDictionary p_ndf_dict)
        {
            List<NestedDictionary> p_processed_dictionaries = new List<NestedDictionary>();
            List<string> p_removed_keys = new List<string>();
            foreach (KeyValuePair<string, NestedDictionaryNode> ndf_entry in p_ndf_dict)
            {
                PostProcess(ndf_entry.Value.NestedDictionaries);

                if (ndf_entry.Key.StartsWith("@PP.Replace[") && ndf_entry.Key.EndsWith("]") && ndf_entry.Value.NestedDictionaries.Count > 0)
                {
                    string p_inner_key = ndf_entry.Key.Substring("@PP.Replace[".Length, ndf_entry.Key.Length - "@PP.Replace[".Length - "]".Length);
                    string[] p_variable_names = p_inner_key.Split(',').OrderByDescending(key => key.Length).ToArray();

                    foreach (NestedDictionary p_nested_dict in ndf_entry.Value)
                    {
                        NestedDictionary p_processed_dict = p_ndf_dict.Clone();
                        foreach (string p_var_name in p_variable_names)
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

                string p_value = ndf_entry.Value.Value;
                if (p_value == null || !p_value.StartsWith("@PP.Expand[") || !p_value.EndsWith("]")) continue;

                string p_inner = p_value.Substring("@PP.Expand[".Length, p_value.Length - "@PP.Expand[".Length - "]".Length);
                string[] p_expanded_values = p_inner.Split(',');

                foreach (string p_expanded_value in p_expanded_values)
                {
                    NestedDictionary p_processed_dict = p_ndf_dict.Clone();
                    p_processed_dict[ndf_entry.Key].Value = p_expanded_value;
                    p_processed_dictionaries.Add(p_processed_dict);
                }
                break;
            }

            foreach (string p_removed_key in p_removed_keys)
            {
                foreach (NestedDictionary p_nested_dict in p_processed_dictionaries)
                {
                    p_nested_dict.Remove(p_removed_key);
                }
            }

            return p_processed_dictionaries;
        }

        private static void ReplaceVariable(NestedDictionary p_dictionary, string p_variable_name, string p_substitute)
        {
            foreach (KeyValuePair<string, NestedDictionaryNode> ndf_entry in p_dictionary)
            {
                if (ndf_entry.Value.Value != null)
                {
                    ndf_entry.Value.Value = ndf_entry.Value.Value.Replace(p_variable_name, p_substitute);
                }

                foreach (NestedDictionary p_nested_dict in ndf_entry.Value)
                {
                    ReplaceVariable(p_nested_dict, p_variable_name, p_substitute);
                }
            }
        }
    }
}

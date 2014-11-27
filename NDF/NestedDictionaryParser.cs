using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NDF
{
	internal static class NestedDictionaryParser
	{
		private enum ParsingPosition
		{
			key,
			value,
			comment
		}

		internal static List<NestedDictionary> ParseNDF(TextReader reader)
		{
			List<NestedDictionary> ndf = new List<NestedDictionary>();

			NestedDictionary currentDict = new NestedDictionary();
			NestedDictionaryNode currentNode = new NestedDictionaryNode();

			ParsingPosition pos = ParsingPosition.key;
			StringBuilder buffer = new StringBuilder();
			while (reader.Peek() != -1)
			{
				switch (reader.Peek())
				{
						// check for comment
					case (Int32)'/':
						reader.Read();
						if (reader.Peek() == (Int32)'/')
						{
							while (reader.Peek() != (Int32)'\n' && reader.Peek() != -1) // comment terminates at EOF or line-end
							{
								reader.Read();
							}
							reader.Read();
						}
						else
						{
							buffer.Append('/');
						}
						break;
					case (Int32)':':
						// : are allowed for values, just not for keys
						if (pos == ParsingPosition.value)
						{
							goto default;
						}

						AddBuffer(currentNode, currentDict, pos, buffer); // merge the key with the node
						
						reader.Read(); // consume
						pos = ParsingPosition.value;

						break;
					case (Int32)';':

						AddBuffer(currentNode, currentDict, pos, buffer); // merge the value or the key with the node
						
						reader.Read(); // consume
						pos = ParsingPosition.key;

						AddNode(ndf, ref currentDict, ref currentNode); // node end, so add the node

						break;
					case (Int32)'{':

						buffer = new StringBuilder(buffer.ToString().TrimEnd('\r', '\n', '\t', '\0', ' '));
						AddBuffer(currentNode, currentDict, pos, buffer); // merge the value or the key with the node
						
						reader.Read(); // consume before parsing the nested
						currentNode.NestedDictionaries = ParseNDF(reader); // parse the nested dictionary

						AddNode(ndf, ref currentDict, ref currentNode); // node end, so add the node

						pos = ParsingPosition.key;

						break;
					case (Int32)'}':
						reader.Read(); // consume before stopping execution
						if (currentDict.Count > 0) ndf.Add(currentDict); // nested dictionary end
						return ndf;
					case (Int32)'\r':
					case (Int32)'\n':
					case (Int32)'\t':
					case (Int32)'\0':
					case (Int32)' ':
						if (pos == ParsingPosition.value)
						{
							buffer.Append((Char)reader.Read());
						}
						else
						{
							reader.Read(); // consume, ignored sign
						}
						break;
					default:
						buffer.Append((Char)reader.Read()); // add to buffer for key or value
						break;
				}
			}
			if (currentDict.Count > 0) ndf.Add(currentDict);

			PostProcess(ndf);

			return ndf;
		}

		private static void AddNode(List<NestedDictionary> ndf, ref NestedDictionary currentDict, ref NestedDictionaryNode currentNode)
		{
			if (currentNode.Key == null) return; // only occurs should the "node" be a reference

			if (!currentDict.ContainsKey(currentNode.Key))
			{
				currentDict.Add(currentNode.Key, currentNode);
			}
			else
			{
				ndf.Add(currentDict);
				currentDict = new NestedDictionary();
				currentDict.Add(currentNode.Key, currentNode);
			}
			currentNode = new NestedDictionaryNode();
		}
		private static void AddBuffer(NestedDictionaryNode node, NestedDictionary currentDict, ParsingPosition pos, StringBuilder buffer)
		{
			switch (pos)
			{
				case ParsingPosition.key:
					node.Key = buffer.ToString();
					break;
				case ParsingPosition.value:
					node.Value = buffer.ToString();
					break;
			}
			buffer = new StringBuilder();
		}

		private static void PostProcess(List<NestedDictionary> ndfData)
		{
			for (Int32 i = 0; i < ndfData.Count; ++i)
			{
				List<NestedDictionary> processedDicts = PostProcessSingle(ndfData[i]);
				if (processedDicts.Count > 1)
				{
					ndfData.RemoveAt(i);
					ndfData.InsertRange(i, processedDicts);
					--i; // the dictionary currently at i has not yet been expanded since its a new one!
				}
			}
		}

		private static List<NestedDictionary> PostProcessSingle(NestedDictionary ndfDict)
		{
			List<NestedDictionary> processedDicts = new List<NestedDictionary>();
			List<String> removedKeys = new List<String>();
			foreach (KeyValuePair<String, NestedDictionaryNode> ndfEntry in ndfDict)
			{
				PostProcess(ndfEntry.Value.NestedDictionaries);

				if (ndfEntry.Key.StartsWith("@PP.Replace[") && ndfEntry.Key.EndsWith("]") && ndfEntry.Value.NestedDictionaries.Count > 0)
				{
					String innerKey = ndfEntry.Key.Substring("@PP.Replace[".Length, ndfEntry.Key.Length - "@PP.Replace[".Length - "]".Length);
					String[] variableNames = innerKey.Split(',');

					foreach (NestedDictionary nestedDict in ndfEntry.Value)
					{
						NestedDictionary processedDict = ndfDict.Clone();
						foreach (String varName in variableNames)
						{
							if (!nestedDict.ContainsKey(varName))
							{
								throw new Exception("Post-Processor Replace does not have all required variables defined. Missing " + varName);
							}
							ReplaceVariable(processedDict, varName, nestedDict[varName]);
						}
						processedDicts.Add(processedDict);
					}
					removedKeys.Add(ndfEntry.Key);
					break;
				}

				String value = ndfEntry.Value.Value;
				if (value == null || !value.StartsWith("@PP.Expand[") || !value.EndsWith("]")) continue;

				String inner = value.Substring("@PP.Expand[".Length, value.Length - "@PP.Expand[".Length - "]".Length);
				String[] expandedValues = inner.Split(',');

				foreach (String expandedValue in expandedValues)
				{
					NestedDictionary processedDict = ndfDict.Clone();
					processedDict[ndfEntry.Key].Value = expandedValue;
					processedDicts.Add(processedDict);
				}
				break;
			}

			foreach (String removedKey in removedKeys)
			{
				foreach (NestedDictionary nestedDict in processedDicts)
				{
					nestedDict.Remove(removedKey);
				}
			}

			return processedDicts;
		}

		private static void ReplaceVariable(NestedDictionary dictionary, String variableName, String substitute)
		{
			foreach(KeyValuePair<String, NestedDictionaryNode> ndfEntry in dictionary)
			{
				if(ndfEntry.Value.Value != null)
				{
					ndfEntry.Value.Value = ndfEntry.Value.Value.Replace(variableName, substitute);
				}

				foreach(NestedDictionary p_nested_dict in ndfEntry.Value)
				{
					ReplaceVariable(p_nested_dict, variableName, substitute);
				}
			}
		}
	}
}

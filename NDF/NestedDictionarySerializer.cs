using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace FileFormats.NDF
{
    public static class NestedDictionarySerializer
    {
        private static HashSet<Type> primitives = new HashSet<Type>
            {
                typeof(byte), typeof(sbyte),
                typeof(ushort), typeof(short),
                typeof(uint), typeof(int),
                typeof(ulong), typeof(long),
                typeof(float), typeof(double), typeof(decimal),
                typeof(char), typeof(string)
            };
        private static class Serializer<T>
        {
            private static Action<NestedDictionary, T> serializeMethod;
            private static Action<NestedDictionary, T, object[]> deserializeMethod;

            static Serializer()
            {
                // Problem is that the SetMethod of ItemUpgradeable.Upgrades (the ReadOnlyList<int>-Property) is null.
                // For some reason, when reflecting over ItemEquippable, the inherited private members aren't searched.
                var properties = typeof(T).GetProperties(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var baseType = typeof(T).BaseType;
                while (baseType != null)
                {
                    properties = properties.Concat(baseType.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance).Where(p => !properties.Select(p2 => p2.Name).Contains(p.Name))).ToArray();
                    baseType = baseType.BaseType;
                }

                properties = properties.Where(p => p.GetCustomAttribute<NdfNonSerializedAttribute>() == null).ToArray();
                properties = properties.Where(p =>
                {
                    var declaredProp = p.DeclaringType.GetProperty(p.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (declaredProp.GetMethod != null && declaredProp.SetMethod != null)
                        return true;
                    else
                        return false;
                }).Select(p => p.DeclaringType.GetProperty(p.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)).ToArray();

                var dictAddMethod = typeof(NestedDictionary).GetMethod(nameof(NestedDictionary.Add));
                var nodeCtor = typeof(NestedDictionaryNode).GetConstructor(new Type[0]);
                var nodeKeyProperty = typeof(NestedDictionaryNode).GetProperty(nameof(NestedDictionaryNode.Key));
                var nodeValueProperty = typeof(NestedDictionaryNode).GetProperty(nameof(NestedDictionaryNode.Value));
                var toStringMethod = typeof(object).GetMethod(nameof(object.ToString));
                var serializerSerializeMethod = typeof(NestedDictionarySerializer).GetMethod(nameof(NestedDictionarySerializer.Serialize));
                var serializerSerializeDynamicMethod = typeof(NestedDictionarySerializer).GetMethod(nameof(NestedDictionarySerializer.SerializeDynamic), BindingFlags.NonPublic | BindingFlags.Static);
                var serializerSerializeMultipleMethod = typeof(NestedDictionarySerializer).GetMethod(nameof(NestedDictionarySerializer.SerializeMultiple));
                var serializerBuildPrimitiveArrStrMethod = typeof(NestedDictionarySerializer).GetMethod(nameof(NestedDictionarySerializer.buildPrimitiveArrStr), BindingFlags.NonPublic | BindingFlags.Static);
                var nodeNestedDictionariesField = typeof(NestedDictionaryNode).GetField(nameof(NestedDictionaryNode.NestedDictionaries), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var listAddMethod = typeof(List<NestedDictionary>).GetMethod(nameof(List<NestedDictionary>.Add));

                DynamicMethod serializeMethod = new DynamicMethod(
                    Guid.NewGuid().ToString("N"),
                    typeof(void),
                    new[] { typeof(NestedDictionary), typeof(T) },
                    true
                );
                var ilGen = serializeMethod.GetILGenerator();

                var local = ilGen.DeclareLocal(typeof(NestedDictionary));

                ilGen.Emit(OpCodes.Ldarg_0);
                ilGen.Emit(OpCodes.Ldstr, "$type");
                ilGen.Emit(OpCodes.Newobj, nodeCtor);
                ilGen.Emit(OpCodes.Dup);
                ilGen.Emit(OpCodes.Dup);
                ilGen.Emit(OpCodes.Ldstr, "$type");
                ilGen.Emit(OpCodes.Callvirt, nodeKeyProperty.SetMethod);
                ilGen.Emit(OpCodes.Ldarg_1);
                ilGen.Emit(OpCodes.Callvirt, typeof(object).GetMethod(nameof(object.GetType)));
                ilGen.Emit(OpCodes.Callvirt, typeof(Type).GetProperty(nameof(Type.FullName)).GetMethod);
                ilGen.Emit(OpCodes.Callvirt, nodeValueProperty.SetMethod);
                ilGen.Emit(OpCodes.Callvirt, dictAddMethod);
                foreach (var property in properties)
                {
                    if (property.GetIndexParameters().Length != 0)
                        continue;

                    // emits code like:
                    // var node = new NestedDictionaryNode();
                    // node.Key = "propertyName";
                    // node.Value = obj.propertyName;
                    // dict.Add("propertyName", node);
                    ilGen.Emit(OpCodes.Ldarg_0); // Stack = {dict}
                    ilGen.Emit(OpCodes.Ldstr, property.Name); // Stack = {dict, "propertyName"}
                    if (property.PropertyType != typeof(string) && property.PropertyType.GetInterfaces().Where(i => i.IsGenericType).Select(i => i.GetGenericTypeDefinition()).Contains(typeof(IEnumerable<>)))
                    {
                        var enumerableType = property.PropertyType.GetInterfaces().Where(i => i.IsGenericType).First(i => i.GetGenericTypeDefinition() == typeof(IEnumerable<>)).GetGenericArguments()[0];
                        if (primitives.Contains(enumerableType) || enumerableType.IsEnum)
                        {
                            ilGen.Emit(OpCodes.Newobj, nodeCtor); // Stack = {dict, "propertyName", node}
                            ilGen.Emit(OpCodes.Dup); // Stack = {dict, "propertyName", node, node}
                            ilGen.Emit(OpCodes.Ldstr, property.Name); // Stack = {dict, "propertyName", node, node, "propertyName"}
                            ilGen.Emit(OpCodes.Callvirt, nodeKeyProperty.SetMethod); // Stack = {dict, "propertyName", node}
                            ilGen.Emit(OpCodes.Dup); // Stack = {dict, "propertyName", node, node}

                            ilGen.Emit(OpCodes.Ldarg_1); // Stack = {dict, "propertyName", value}
                            ilGen.Emit(OpCodes.Callvirt, property.GetMethod); // Stack = {dict, "propertyName", value.propertyName}

                            ilGen.Emit(OpCodes.Call, serializerBuildPrimitiveArrStrMethod.MakeGenericMethod(enumerableType));
                            ilGen.Emit(OpCodes.Callvirt, nodeValueProperty.SetMethod);
                        }
                        else
                        {
                            ilGen.Emit(OpCodes.Ldarg_1); // Stack = {dict, "propertyName", value}
                            ilGen.Emit(OpCodes.Callvirt, property.GetMethod); // Stack = {dict, "propertyName", value.propertyName}

                            ilGen.Emit(OpCodes.Call, serializerSerializeMultipleMethod.MakeGenericMethod(enumerableType)); // Stack = {dict, "propertyName", node}
                            ilGen.Emit(OpCodes.Dup); // Stack = {dict, "propertyName", node, node}
                            ilGen.Emit(OpCodes.Ldstr, property.Name); // Stack = {dict, "propertyName", node, node "propertyName"}
                            ilGen.Emit(OpCodes.Callvirt, nodeKeyProperty.SetMethod); // Stack = {dict, "propertyName", node}
                        }
                    }
                    else
                    {
                        ilGen.Emit(OpCodes.Newobj, nodeCtor); // Stack = {dict, "propertyName", node}
                        ilGen.Emit(OpCodes.Dup); // Stack = {dict, "propertyName", node, node}
                        ilGen.Emit(OpCodes.Ldstr, property.Name); // Stack = {dict, "propertyName", node, node, "propertyName"}
                        ilGen.Emit(OpCodes.Callvirt, nodeKeyProperty.SetMethod); // Stack = {dict, "propertyName", node}
                        ilGen.Emit(OpCodes.Dup); // Stack = {dict, "propertyName", node, node}
                        ilGen.Emit(OpCodes.Ldarg_1); // Stack = {dict, "propertyName", node, node, value}
                        ilGen.Emit(OpCodes.Callvirt, property.GetMethod); // Stack = {dict, "propertyName", node, node, value.propertyName}
                        if (primitives.Contains(property.PropertyType) || property.PropertyType.IsEnum)
                        {
                            if (!property.PropertyType.IsClass)
                                ilGen.Emit(OpCodes.Box, property.PropertyType);
                            // Stack = {dict, "propertyName", node, node, value.propertyName.ToString}
                            if (property.PropertyType != typeof(string))
                                ilGen.Emit(OpCodes.Callvirt, toStringMethod);
                            // Stack = {dict, "propertyName", node}
                            ilGen.Emit(OpCodes.Callvirt, nodeValueProperty.SetMethod);
                        }
                        else
                        {
                            // Stack = {dict, "propertyName", node, node, valueDict}
                            ilGen.Emit(OpCodes.Call, serializerSerializeDynamicMethod);
                            ilGen.Emit(OpCodes.Stloc_0); // Stack = {dict, "propertyName", node, node}
                            ilGen.Emit(OpCodes.Ldfld, nodeNestedDictionariesField); // Stack = {dict, "propertyName", node, nestedDicts}
                            ilGen.Emit(OpCodes.Ldloc_0); // Stack = {dict, "propertyName", node, nestedDicts, valueDict}
                            ilGen.Emit(OpCodes.Callvirt, listAddMethod); // Stack = {dict, "propertyName", node}
                        }
                    }
                    ilGen.Emit(OpCodes.Callvirt, dictAddMethod);
                }
                ilGen.Emit(OpCodes.Ret);

                Serializer<T>.serializeMethod = (Action<NestedDictionary, T>)serializeMethod.CreateDelegate(typeof(Action<NestedDictionary, T>));

                DynamicMethod deserializeMethod = new DynamicMethod(
                    Guid.NewGuid().ToString("N"),
                    typeof(void),
                    new[] { typeof(NestedDictionary), typeof(T), typeof(object[]) },
                    true
                );

                var nestedDictLookupProperty = typeof(NestedDictionary).GetProperty("Item");

                ilGen = deserializeMethod.GetILGenerator();
                foreach (var property in properties)
                {
                    ilGen.Emit(OpCodes.Ldarg_1);
                    ilGen.Emit(OpCodes.Ldarg_0);
                    ilGen.Emit(OpCodes.Ldstr, property.Name);
                    ilGen.Emit(OpCodes.Callvirt, nestedDictLookupProperty.GetMethod);
                    ilGen.Emit(OpCodes.Callvirt, nodeValueProperty.GetMethod);
                    if (primitives.Contains(property.PropertyType))
                    {
                        if (property.PropertyType == typeof(string))
                        {
                            ilGen.Emit(OpCodes.Callvirt, property.SetMethod);
                        }
                        else
                        {
                            ilGen.Emit(OpCodes.Call, property.PropertyType.GetMethod(nameof(int.Parse), new[] { typeof(string) }));
                            ilGen.Emit(OpCodes.Callvirt, property.SetMethod);
                        }
                    }
                    else if (property.PropertyType.GetInterfaces().Where(i => i.IsGenericType).Select(i => i.GetGenericTypeDefinition()).Contains(typeof(IEnumerable<>)))
                    {
                        var addRangeLabel = ilGen.DefineLabel();
                        ilGen.Emit(OpCodes.Ldarg_1);
                        ilGen.Emit(OpCodes.Callvirt, property.GetMethod);
                        ilGen.Emit(OpCodes.Brtrue, addRangeLabel);

                        var ctor = property.PropertyType.GetConstructor(new Type[0]);
                        if (ctor != null)
                        {
                            ilGen.Emit(OpCodes.Ldarg_1);
                            ilGen.Emit(OpCodes.Newobj, ctor);
                            ilGen.Emit(OpCodes.Callvirt, property.SetMethod);
                        }
                        else
                        {
                            var exceptionCtor = typeof(Exception).GetConstructor(new[] { typeof(string) });
                            ilGen.Emit(OpCodes.Ldstr, $"Invalid type for serialization. The type {typeof(T).FullName} has the property {property.Name} of Type {property.PropertyType.FullName}. The Type {property.PropertyType.FullName} is an enumerable type but does not have a default constructor (ie a constructor with no arguments) and is during the call to serialization not yet instantiated.");
                            ilGen.Emit(OpCodes.Newobj, exceptionCtor);
                            ilGen.Emit(OpCodes.Throw);
                        }

                        ilGen.MarkLabel(addRangeLabel);
                        var enumerableType = property.PropertyType.GetInterfaces().Where(i => i.IsGenericType).First(i => i.GetGenericTypeDefinition() == typeof(IEnumerable<>)).GetGenericArguments()[0];
                        if (primitives.Contains(enumerableType))
                        {
                            var readMethod = typeof(NestedDictionarySerializer).GetMethods(BindingFlags.NonPublic | BindingFlags.Static).Single(m => m.ReturnType == typeof(IEnumerable<>).MakeGenericType(enumerableType));
                            var loc = ilGen.DeclareLocal(readMethod.ReturnType);
                            ilGen.Emit(OpCodes.Call, readMethod);
                            ilGen.Emit(OpCodes.Stloc, loc);
                            var addRangeMethod = property.PropertyType.GetMethod("AddRange", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (addRangeMethod == null || addRangeMethod.GetParameters().Length != 1 || addRangeMethod.GetParameters()[0].ParameterType != readMethod.ReturnType)
                                throw new Exception($"Invalid type for serialization. The type {typeof(T).FullName} has the property {property.Name} of Type {property.PropertyType.FullName}. The Type {property.PropertyType.FullName} is an enumerable type over primitives (ints, floats, etc), but does not expose an AddRange-Method that accepts a single argument of type {readMethod.ReturnType}.");

                            ilGen.Emit(OpCodes.Ldarg_1);
                            ilGen.Emit(OpCodes.Callvirt, property.GetMethod);
                            ilGen.Emit(OpCodes.Ldloc, loc);
                            ilGen.Emit(OpCodes.Callvirt, addRangeMethod);

                            ilGen.Emit(OpCodes.Pop);
                        }
                        else
                        {
                            ilGen.Emit(OpCodes.Pop);

                            ilGen.Emit(OpCodes.Ldarg_0);
                            ilGen.Emit(OpCodes.Ldstr, property.Name);
                            ilGen.Emit(OpCodes.Callvirt, nestedDictLookupProperty.GetMethod);
                            ilGen.Emit(OpCodes.Ldarg_2);

                            var loc = ilGen.DeclareLocal(typeof(IEnumerable<>).MakeGenericType(enumerableType));
                            ilGen.Emit(OpCodes.Call, typeof(NestedDictionarySerializer).GetMethod(nameof(NestedDictionarySerializer.DeserializeMultiple)).MakeGenericMethod(enumerableType));
                            ilGen.Emit(OpCodes.Stloc, loc);

                            var addRangeMethod = property.PropertyType.GetMethod("AddRange", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (addRangeMethod == null || addRangeMethod.GetParameters().Length != 1 || addRangeMethod.GetParameters()[0].ParameterType != loc.LocalType)
                                throw new Exception($"Invalid type for serialization. The type {typeof(T).FullName} has the property {property.Name} of Type {property.PropertyType.FullName}. The Type {property.PropertyType.FullName} is an enumerable type, but does not expose an AddRange-Method that accepts a single argument of type {loc.LocalType}.");


                            ilGen.Emit(OpCodes.Ldarg_1);
                            ilGen.Emit(OpCodes.Callvirt, property.GetMethod);
                            ilGen.Emit(OpCodes.Ldloc, loc);
                            ilGen.Emit(OpCodes.Callvirt, addRangeMethod);

                            ilGen.Emit(OpCodes.Pop);
                        }
                    }
                    else
                    {
                        ilGen.Emit(OpCodes.Pop);

                        ilGen.Emit(OpCodes.Ldarg_0);
                        ilGen.Emit(OpCodes.Ldstr, property.Name);
                        ilGen.Emit(OpCodes.Callvirt, nestedDictLookupProperty.GetMethod);
                        ilGen.Emit(OpCodes.Ldc_I4_0);
                        ilGen.Emit(OpCodes.Callvirt, typeof(NestedDictionaryNode).GetProperty("Item").GetMethod);
                        ilGen.Emit(OpCodes.Ldarg_2);

                        ilGen.Emit(OpCodes.Call, typeof(NestedDictionarySerializer).GetMethod(nameof(NestedDictionarySerializer.Deserialize)).MakeGenericMethod(property.PropertyType));
                        ilGen.Emit(OpCodes.Callvirt, property.SetMethod);
                    }
                }
                ilGen.Emit(OpCodes.Ret);

                Serializer<T>.deserializeMethod = (Action<NestedDictionary, T, object[]>)deserializeMethod.CreateDelegate(typeof(Action<NestedDictionary, T, object[]>));
            }

            internal static void Serialize(NestedDictionary dict, T value)
                => serializeMethod(dict, value);

            internal static void Deserialize(NestedDictionary dict, T value, params object[] ctorArgs)
                => deserializeMethod(dict, value, ctorArgs);
        }

        private static IEnumerable<byte> readByteArrStr(string str)
            => str.Substring(1, str.Length - 2).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => byte.Parse(s));
        private static IEnumerable<sbyte> readSByteArrStr(string str)
            => str.Substring(1, str.Length - 2).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => sbyte.Parse(s));
        private static IEnumerable<short> readShortArrStr(string str)
            => str.Substring(1, str.Length - 2).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => short.Parse(s));
        private static IEnumerable<ushort> readUShortArrStr(string str)
            => str.Substring(1, str.Length - 2).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => ushort.Parse(s));
        private static IEnumerable<int> readIntArrStr(string str)
            => str.Substring(1, str.Length - 2).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => int.Parse(s));
        private static IEnumerable<uint> readUIntArrStr(string str)
            => str.Substring(1, str.Length - 2).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => uint.Parse(s));
        private static IEnumerable<long> readLongArrStr(string str)
            => str.Substring(1, str.Length - 2).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => long.Parse(s));
        private static IEnumerable<ulong> readULongArrStr(string str)
            => str.Substring(1, str.Length - 2).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => ulong.Parse(s));

        private static IEnumerable<float> readFloatArrStr(string str)
            => str.Substring(1, str.Length - 2).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => float.Parse(s));
        private static IEnumerable<double> readDoubleArrStr(string str)
            => str.Substring(1, str.Length - 2).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s));
        private static IEnumerable<decimal> readDecimalArrStr(string str)
            => str.Substring(1, str.Length - 2).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => decimal.Parse(s));

        private static IEnumerable<char> readCharArrStr(string str)
            => str.Substring(1, str.Length - 2).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => char.Parse(s));

        private static string buildPrimitiveArrStr<T>(IEnumerable<T> values)
            => $"[{string.Join(",", values.Select(v => v.ToString()))}]";

        public static NestedDictionaryNode SerializeMultiple<T>(IEnumerable<T> values)
        {
            var node = new NestedDictionaryNode();
            foreach (var value in values)
                node.NestedDictionaries.Add(SerializeDynamic(value));

            return node;
        }
        public static NestedDictionary Serialize<T>(T value)
        {
            var ndf = new NestedDictionary();
            ndf.Add("", new NestedDictionaryNode() { Key = "" });
            if (value != null)
                Serializer<T>.Serialize(ndf, value);
            return ndf;
        }
        private static NestedDictionary SerializeDynamic(dynamic value)
        {
            if (value != null)
                return Serialize(value);
            else
                return Serialize<object>(value);
        }

        public static T Deserialize<T>(NestedDictionary dict, params object[] ctorArgs)
        {
            T obj;
            if (!dict.ContainsKey("$type") && dict.Count > 1)
            {
                try
                {
                    obj = (T)Activator.CreateInstance(typeof(T), ctorArgs);
                }
                catch (Exception)
                {
                    obj = (T)Activator.CreateInstance(typeof(T));
                }
            }
            else if (dict.ContainsKey("$type"))
            {
                var type = Assembly.GetAssembly(typeof(T)).GetType(dict["$type"]);
                if (type.GetConstructor(ctorArgs.Select(a => a.GetType()).ToArray()) != null)
                {
                    obj = (T)Activator.CreateInstance(type, ctorArgs);
                }
                else
                {
                    obj = (T)Activator.CreateInstance(type);
                }
            }
            else
            {
                return default(T);
            }

            DeserializeDynamic(dict, obj, ctorArgs);
            return obj;
        }

        public static IEnumerable<T> DeserializeMultiple<T>(NestedDictionaryNode dicts, params object[] ctorArgs)
        {
            foreach (var dict in dicts)
                yield return Deserialize<T>(dict, ctorArgs);
        }

        private static void DeserializeDynamic(NestedDictionary dict, dynamic obj, params object[] ctorArgs)
        {
            genericHelper(dict, obj, ctorArgs);
        }
        static void genericHelper<T>(NestedDictionary dict2, T obj2, params object[] ctorArgs2)
            => Serializer<T>.Deserialize(dict2, obj2, ctorArgs2);

    }
}

﻿using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UniJSON;

namespace UniGLTF
{
    public static class glTFExtensions
    {
        struct ComponentVec
        {
            public glComponentType ComponentType;
            public int ElementCount;

            public ComponentVec(glComponentType componentType, int elementCount)
            {
                ComponentType = componentType;
                ElementCount = elementCount;
            }
        }

        static Dictionary<Type, ComponentVec> ComponentTypeMap = new Dictionary<Type, ComponentVec>
        {
            { typeof(Vector2), new ComponentVec(glComponentType.FLOAT, 2) },
            { typeof(Vector3), new ComponentVec(glComponentType.FLOAT, 3) },
            { typeof(Vector4), new ComponentVec(glComponentType.FLOAT, 4) },
            { typeof(UShort4), new ComponentVec(glComponentType.UNSIGNED_SHORT, 4) },
            { typeof(Matrix4x4), new ComponentVec(glComponentType.FLOAT, 16) },
            { typeof(Color), new ComponentVec(glComponentType.FLOAT, 4) },
        };

        public static glComponentType GetComponentType<T>()
        {
            var cv = default(ComponentVec);
            if (ComponentTypeMap.TryGetValue(typeof(T), out cv))
            {
                return cv.ComponentType;
            }
            else if (typeof(T) == typeof(sbyte))
            {
                return glComponentType.BYTE;
            }
            else if (typeof(T) == typeof(byte))
            {
                return glComponentType.UNSIGNED_BYTE;
            }
            else if (typeof(T) == typeof(ushort))
            {
                return glComponentType.UNSIGNED_SHORT;
            }
            else if (typeof(T) == typeof(uint))
            {
                return glComponentType.UNSIGNED_INT;
            }
            else if (typeof(T) == typeof(float))
            {
                return glComponentType.FLOAT;
            }
            else
            {
                throw new NotImplementedException(typeof(T).Name);
            }
        }

        public static string GetAccessorType<T>()
        {
            var cv = default(ComponentVec);
            if (ComponentTypeMap.TryGetValue(typeof(T), out cv))
            {
                switch (cv.ElementCount)
                {
                    case 2: return "VEC2";
                    case 3: return "VEC3";
                    case 4: return "VEC4";
                    case 16: return "MAT4";
                    default: throw new Exception();
                }
            }
            else
            {
                return "SCALAR";
            }
        }

        static int GetAccessorElementCount<T>()
        {
            var cv = default(ComponentVec);
            if (ComponentTypeMap.TryGetValue(typeof(T), out cv))
            {
                return cv.ElementCount;
            }
            else
            {
                return 1;
            }
        }

        public static int ExtendBufferAndGetAccessorIndex<T>(this glTF gltf, int bufferIndex, T[] array,
            glBufferTarget target = glBufferTarget.NONE) where T : struct
        {
            return gltf.ExtendBufferAndGetAccessorIndex(bufferIndex, new ArraySegment<T>(array), target);
        }

        public static int ExtendBufferAndGetAccessorIndex<T>(this glTF gltf, int bufferIndex,
            ArraySegment<T> array,
            glBufferTarget target = glBufferTarget.NONE) where T : struct
        {
            if (array.Count == 0)
            {
                return -1;
            }
            var viewIndex = ExtendBufferAndGetViewIndex(gltf, bufferIndex, array, target);

            // index buffer's byteStride is unnecessary
            gltf.bufferViews[viewIndex].byteStride = 0;

            var accessorIndex = gltf.accessors.Count;
            gltf.accessors.Add(new glTFAccessor
            {
                bufferView = viewIndex,
                byteOffset = 0,
                componentType = GetComponentType<T>(),
                type = GetAccessorType<T>(),
                count = array.Count,
            });
            return accessorIndex;
        }

        public static int ExtendBufferAndGetViewIndex<T>(this glTF gltf, int bufferIndex,
            T[] array,
            glBufferTarget target = glBufferTarget.NONE) where T : struct
        {
            return ExtendBufferAndGetViewIndex(gltf, bufferIndex, new ArraySegment<T>(array), target);
        }

        public static int ExtendBufferAndGetViewIndex<T>(this glTF gltf, int bufferIndex,
            ArraySegment<T> array,
            glBufferTarget target = glBufferTarget.NONE) where T : struct
        {
            if (array.Count == 0)
            {
                return -1;
            }
            var view = gltf.buffers[bufferIndex].Append(array, target);
            var viewIndex = gltf.bufferViews.Count;
            gltf.bufferViews.Add(view);
            return viewIndex;
        }

        /// <summary>
        /// sparseValues は間引かれた配列
        /// </summary>
        public static int ExtendSparseBufferAndGetAccessorIndex<T>(this glTF gltf, int bufferIndex,
            int accessorCount,
            T[] sparseValues, int[] sparseIndices, int sparseViewIndex,
            glBufferTarget target = glBufferTarget.NONE) where T : struct
        {
            return ExtendSparseBufferAndGetAccessorIndex(gltf, bufferIndex,
                accessorCount,
                new ArraySegment<T>(sparseValues), sparseIndices, sparseViewIndex,
                target);
        }

        public static int ExtendSparseBufferAndGetAccessorIndex<T>(this glTF gltf, int bufferIndex,
            int accessorCount,
            ArraySegment<T> sparseValues, int[] sparseIndices, int sparseIndicesViewIndex,
            glBufferTarget target = glBufferTarget.NONE) where T : struct
        {
            if (sparseValues.Count == 0)
            {
                return -1;
            }
            var sparseValuesViewIndex = ExtendBufferAndGetViewIndex(gltf, bufferIndex, sparseValues, target);
            var accessorIndex = gltf.accessors.Count;
            gltf.accessors.Add(new glTFAccessor
            {
                byteOffset = 0,
                componentType = GetComponentType<T>(),
                type = GetAccessorType<T>(),
                count = accessorCount,

                sparse = new glTFSparse
                {
                    count = sparseIndices.Length,
                    indices = new glTFSparseIndices
                    {
                        bufferView = sparseIndicesViewIndex,
                        componentType = glComponentType.UNSIGNED_INT
                    },
                    values = new glTFSparseValues
                    {
                        bufferView = sparseValuesViewIndex,
                    }
                }
            });
            return accessorIndex;
        }

        public static int AddBuffer(this glTF self, IBytesBuffer bytesBuffer)
        {
            var index = self.buffers.Count;
            self.buffers.Add(new glTFBuffer(bytesBuffer));
            return index;
        }

        static Utf8String s_extensions = Utf8String.From("extensions");

        static bool UsedExtension(this glTF self, string key)
        {
            if (self.extensionsUsed.Contains(key))
            {
                return true;
            }

            return false;
        }

        static void Traverse(this glTF self, JsonNode node, JsonFormatter f, Utf8String parentKey)
        {
            if (node.IsMap())
            {
                f.BeginMap();
                foreach (var kv in node.ObjectItems())
                {
                    if (parentKey == s_extensions)
                    {
                        if (!self.UsedExtension(kv.Key.GetString()))
                        {
                            continue;
                        }
                    }
                    f.Key(kv.Key.GetUtf8String());
                    self.Traverse(kv.Value, f, kv.Key.GetUtf8String());
                }
                f.EndMap();
            }
            else if (node.IsArray())
            {
                f.BeginList();
                foreach (var x in node.ArrayItems())
                {
                    self.Traverse(x, f, default(Utf8String));
                }
                f.EndList();
            }
            else
            {
                f.Value(node);
            }
        }

        static string RemoveUnusedExtensions(this glTF self, string json)
        {
            var f = new JsonFormatter();
            self.Traverse(JsonParser.Parse(json), f, default(Utf8String));
            return f.ToString();
        }

        public static byte[] ToGlbBytes(this glTF self)
        {
            var f = new JsonFormatter();
            GltfSerializer.Serialize(f, self);

            // remove unused extenions
            var json = f.ToString().ParseAsJson().ToString("  ");
            self.RemoveUnusedExtensions(json);

            return Glb.Create(json, self.buffers[0].GetBytes()).ToBytes();
        }

        public static (string, List<glTFBuffer>) ToGltf(this glTF self, string gltfPath)
        {
            var f = new JsonFormatter();

            // fix buffer path
            if (self.buffers.Count == 1)
            {
                var withoutExt = Path.GetFileNameWithoutExtension(gltfPath);
                self.buffers[0].uri = $"{withoutExt}.bin";
            }
            else
            {
                throw new NotImplementedException();
            }

            GltfSerializer.Serialize(f, self);
            var json = f.ToString().ParseAsJson().ToString("  ");
            self.RemoveUnusedExtensions(json);
            return (json, self.buffers);
        }

        public static bool IsGeneratedUniGLTFAndOlderThan(string generatorVersion, int major, int minor)
        {
            if (string.IsNullOrEmpty(generatorVersion)) return false;
            if (generatorVersion == "UniGLTF") return true;
            if (!generatorVersion.FastStartsWith("UniGLTF-")) return false;

            try
            {
                var splitted = generatorVersion.Substring(8).Split('.');
                var generatorMajor = int.Parse(splitted[0]);
                var generatorMinor = int.Parse(splitted[1]);

                if (generatorMajor < major)
                {
                    return true;
                }
                else if (generatorMajor > major)
                {
                    return false;
                }
                else
                {
                    if (generatorMinor >= minor)
                    {
                        return false;
                    }
                    else
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarningFormat("{0}: {1}", generatorVersion, ex);
                return false;
            }
        }

        public static bool IsGeneratedUniGLTFAndOlder(this glTF gltf, int major, int minor)
        {
            if (gltf == null) return false;
            if (gltf.asset == null) return false;
            return IsGeneratedUniGLTFAndOlderThan(gltf.asset.generator, major, minor);
        }
    }
}

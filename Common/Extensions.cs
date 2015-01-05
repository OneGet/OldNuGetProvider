// 
//  Copyright (c) Microsoft Corporation. All rights reserved. 
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  

namespace Microsoft.OneGet.NuGetProvider.Common {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;

    internal static class Extensions {
        public static Dictionary<TKey, TElement> ToDictionaryNicely<TSource, TKey, TElement>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TElement> elementSelector, IEqualityComparer<TKey> comparer) {
            if (source == null) {
                throw new ArgumentNullException("source");
            }
            if (keySelector == null) {
                throw new ArgumentNullException("keySelector");
            }
            if (elementSelector == null) {
                throw new ArgumentNullException("elementSelector");
            }

            var d = new Dictionary<TKey, TElement>(comparer);
            foreach (var element in source) {
                d.AddOrSet(keySelector(element), elementSelector(element));
            }
            return d;
        }

        public static TValue AddOrSet<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, TValue value) {
            lock (dictionary) {
                if (dictionary.ContainsKey(key)) {
                    dictionary[key] = value;
                } else {
                    dictionary.Add(key, value);
                }
            }
            return value;
        }

        public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, Func<TValue> valueFunction) {
            lock (dictionary) {
                return dictionary.ContainsKey(key) ? dictionary[key] : dictionary.AddOrSet(key, valueFunction());
            }
        }

        /// <summary>
        ///     This takes a string that is representative of a filename and tries to create a path that can be considered the
        ///     'canonical' path. path on drives that are mapped as remote shares are rewritten as their \\server\share\path
        /// </summary>
        /// <returns> </returns>
        public static string CanonicalizePath(this string path, bool isPotentiallyRelativePath) {
            Uri pathUri = null;
            try {
                pathUri = new Uri(path);
                if (!pathUri.IsFile) {
                    // perhaps try getting the fullpath
                    try {
                        pathUri = new Uri(Path.GetFullPath(path));
                    } catch {
                        throw new Exception(string.Format("PathIsNotUri {0} {1}", path, pathUri));
                    }
                }

                // is this a unc path?
                if (string.IsNullOrEmpty(pathUri.Host)) {
                    // no, this is a drive:\path path
                    // use API to resolve out the drive letter to see if it is a remote 
                    var drive = pathUri.Segments[1].Replace('/', '\\'); // the zero segment is always just '/' 

                    var sb = new StringBuilder(512);
                    var size = sb.Capacity;

                    var error = NativeMethods.WNetGetConnection(drive, sb, ref size);
                    if (error == 0) {
                        if (pathUri.Segments.Length > 2) {
                            return pathUri.Segments.Skip(2).Aggregate(sb.ToString().Trim(), (current, item) => current + item);
                        }
                    }
                }
                // not a remote (or resovably-remote) path or 
                // it is already a path that is in it's correct form (via localpath)
                return pathUri.LocalPath;
            } catch (UriFormatException) {
                // we could try to see if it is a relative path...
                if (isPotentiallyRelativePath) {
                    return CanonicalizePath(Path.GetFullPath(path), false);
                }
                throw new ArgumentException("specified path can not be resolved as a file name or path (unc, url, localpath)", path);
            }
        }

        public static void Dump(this Exception e, CommonRequest request) {
            var text = string.Format("{0}//{1}/{2}\r\n{3}", AppDomain.CurrentDomain.FriendlyName, e.GetType().Name, e.Message, e.StackTrace);
            request.Verbose("Exception : {0}", text);
        }

        public static bool DirectoryHasDriveLetter(this string input) {
            return !string.IsNullOrEmpty(input) && Path.IsPathRooted(input) && input.Length >= 2 && input[1] == ':';
        }

        public static string MakeSafeFileName(this string input) {
            return new Regex(@"-+").Replace(new Regex(@"[^\d\w\[\]_\-\.\ ]").Replace(input, "-"), "-").Replace(" ", "");
        }

        public static TSource SafeAggregate<TSource>(this IEnumerable<TSource> source, Func<TSource, TSource, TSource> func) {
            var src = source.ToArray();
            if (source != null && src.Any()) {
                return src.Aggregate(func);
            }
            return default(TSource);
        }

        public static string format(this string messageFormat, params object[] args) {
            return string.Format(messageFormat, args);
        }

        public static bool EqualsIgnoreCase(this string str, string str2) {
            if (str == null && str2 == null) {
                return true;
            }

            if (str == null || str2 == null) {
                return false;
            }

            return str.Equals(str2, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     Encodes the string as an array of UTF8 bytes.
        /// </summary>
        /// <param name="text"> The text. </param>
        /// <returns> </returns>
        /// <remarks>
        /// </remarks>
        public static byte[] ToByteArray(this string text) {
            return Encoding.UTF8.GetBytes(text);
        }

        /// <summary>
        ///     Creates a string from a collection of UTF8 bytes
        /// </summary>
        /// <param name="bytes"> The bytes. </param>
        /// <returns> </returns>
        /// <remarks>
        /// </remarks>
        public static string ToUtf8String(this IEnumerable<byte> bytes) {
            var data = bytes.ToArray();
            try {
                return Encoding.UTF8.GetString(data);
            } finally {
                Array.Clear(data, 0, data.Length);
            }
        }

        public static string ToUnicodeString(this IEnumerable<byte> bytes) {
            var data = bytes.ToArray();
            try {
                return Encoding.Unicode.GetString(data);
            } finally {
                Array.Clear(data, 0, data.Length);
            }
        }

        public static string ToBase64(this string text) {
            if (text == null) {
                return null;
            }
            return Convert.ToBase64String(text.ToByteArray());
        }

        public static string FromBase64(this string text) {
            if (text == null) {
                return null;
            }
            return Convert.FromBase64String(text).ToUtf8String();
        }

        public static bool Is(this string str) {
            return !string.IsNullOrEmpty(str);
        }

        public static bool IsEmptyOrNull(this string str) {
            return string.IsNullOrEmpty(str);
        }

        public static bool IsTrue(this string text) {
            return text.Is() && text.Equals("true", StringComparison.CurrentCultureIgnoreCase);
        }
    }
}
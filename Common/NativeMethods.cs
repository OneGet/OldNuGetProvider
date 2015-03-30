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

namespace Microsoft.PackageManagement.NuGetProvider.Common {
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Runtime.InteropServices;
    using System.Text;

    [Flags]
    internal enum MoveFileFlags {
        ReplaceExisting = 1,
        CopyAllowed = 2,
        DelayUntilReboot = 4,
        WriteThrough = 8
    }

    public class DisposableModule : IDisposable {
        private Module _module;

        public bool IsInvalid {
            get {
                return _module.IsInvalid;
            }
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Dispose(bool disposing) {
            if (disposing) {
                _module.Free();
            }
        }

        [SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates", Justification = "There is no need for such.")]
        public static implicit operator Module(DisposableModule instance) {
            return instance._module;
        }

        [SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates", Justification = "There is no need for such.")]
        public static implicit operator DisposableModule(Module module) {
            return new DisposableModule {
                _module = module
            };
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct Module {
        [FieldOffset(0)]
        public IntPtr handle;

        public Module(IntPtr ptr) {
            handle = ptr;
        }

        public bool IsInvalid {
            get {
                return handle == IntPtr.Zero;
            }
        }

        public void Free() {
            if (!IsInvalid) {
                NativeMethods.FreeLibrary(this);
            }

            handle = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct Unused {
        internal static Unused Nothing;

        [FieldOffset(0)]
        public IntPtr handle;

        public Unused(IntPtr ptr) {
            handle = ptr;
        }

        public bool IsInvalid {
            get {
                return handle == IntPtr.Zero;
            }
        }
    }

    [Flags]
    internal enum LoadLibraryFlags : uint {
        DontResolveDllReferences = 0x00000001,
        AsDatafile = 0x00000002,
        LoadWithAlteredSearchPath = 0x00000008,
        LoadIgnoreCodeAuthzLevel = 0x00000010,
        AsImageResource = 0x00000020,
    }

    public static class NativeMethods {
        [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int WNetGetConnection([MarshalAs(UnmanagedType.LPTStr)] string localName, [MarshalAs(UnmanagedType.LPTStr)] StringBuilder remoteName, ref int length);

        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessageTimeout(Int32 hwnd, Int32 msg, Int32 wparam, [MarshalAs(UnmanagedType.LPStr)] string lparam, Int32 fuFlags, Int32 timeout, IntPtr result);

        [DllImport("kernel32.dll", EntryPoint = "MoveFileEx", CharSet = CharSet.Unicode)]
        internal static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, MoveFileFlags dwFlags);

        [DllImport("kernel32")]
        internal static extern bool FreeLibrary(Module instance);

        /// <summary>
        ///     Loads the specified module into the address space of the calling process.
        /// </summary>
        /// <param name="filename">The name of the module.</param>
        /// <param name="unused">This parameter is reserved for future use.</param>
        /// <param name="dwFlags">The action to be taken when loading the module.</param>
        /// <returns></returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern Module LoadLibraryEx(string filename, Unused unused, LoadLibraryFlags dwFlags);

        [DllImport("user32")]
        internal static extern int LoadString(Module module, uint stringId, StringBuilder buffer, int bufferSize);
    }
}
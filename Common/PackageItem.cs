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
    using System.IO;
    using NuGet;

    internal class PackageItem {
        private string _canonicalId;
        private string _fullPath;
        private string _id;
        private string _version;
        internal IPackage Package {get; set;}
        // internal string Source {get; set;}

        internal PackageSource PackageSource {get; set;}
        internal string FastPath {get; set;}
        internal bool IsPackageFile {get; set;}
        internal string[] Sources {get; set;}

        internal string Id {
            get {
                if (_id == null) {
                    return Package.Id;
                }
                return _id;
            }
            set {
                _id = value;
            }
        }

        internal string InstalledDirectory {
            get {
                try {
                    // if this package file is in a folder with the same name,
                    // we'll consider that 'installed'
                    if (IsPackageFile) {
                        var dir = Path.GetDirectoryName(PackageSource.Location);
                        if (!string.IsNullOrEmpty(dir)) {
                            var dirName = Path.GetFileName(dir);

                            var name = Path.GetFileNameWithoutExtension(PackageSource.Location);
                            if (!string.IsNullOrEmpty(name) && name.Equals(dirName, StringComparison.OrdinalIgnoreCase) && Directory.Exists(dir)) {
                                return dir;
                            }
                        }
                    }
                } catch {
                }
                return null;
            }
        }

        internal bool IsInstalled {
            get {
                return !string.IsNullOrEmpty(InstalledDirectory);
            }
        }

        internal string Version {
            get {
                if (_version == null) {
                    return Package.Version.ToString();
                }
                return _version;
            }
            set {
                _version = value;
            }
        }

        internal string FullName {
            get {
                if (Package != null) {
                    return Package.GetFullName();
                }

                return string.Format("{0}.{1}", Id, Version);
            }
        }

        internal string PackageFilename {
            get {
                if (IsPackageFile) {
                    return Path.GetFileName(PackageSource.Location);
                }

                return Id + "." + Version + ".nupkg";
            }
        }

        internal string FullPath {
            get {
                if (IsPackageFile) {
                    return Path.GetFileName(PackageSource.Location);
                }
                return _fullPath;
            }
            set {
                _fullPath = value;
            }
        }

        internal string GetCanonicalId(NuGetRequest request) {
            return _canonicalId ?? (_canonicalId = request.ProviderServices.GetCanonicalPackageId(request.PackageProviderName, Id, Version, PackageSource == null ? null : PackageSource.Location));
        }
    }
}
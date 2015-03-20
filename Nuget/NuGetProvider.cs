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

namespace Microsoft.PackageManagement.NuGetProvider.Nuget {
    using System.Collections.Generic;
    using System.Reflection;
    using Common;
    using Sdk;
    using PackageSource = NuGet.PackageSource;

    public class NuGetProvider : CommonProvider {
        internal static readonly Dictionary<string, string[]> Features = new Dictionary<string, string[]> {
            {Constants.Features.SupportsPowerShellModules, Constants.FeaturePresent},
            {Constants.Features.SupportedSchemes, new[] {"http", "https", "file"}},
            {Constants.Features.SupportedExtensions, new[] {"nupkg"}},
            {Constants.Features.MagicSignatures, new[] {Constants.Signatures.Zip}},
            // add this back in when we're ready to hide the NuGet provider.
            // { Sdk.Constants.Features.AutomationOnly, Constants.FeaturePresent }
        };

        internal const string ProviderName = "NuGet";

        public override string PackageProviderName {
            get {
                return ProviderName;
            }
        }

        /// <summary>
        ///     Returns a collection of strings to the client advertizing features this provider supports.
        /// </summary>
        /// <param name="request">
        ///     An object passed in from the CORE that contains functions that can be used to interact with the
        ///     CORE and HOST
        /// </param>
        public void GetFeatures(NuGetRequest request) {
            // Nice-to-have put a debug message in that tells what's going on.
            request.Debug("Calling '{0}::GetFeatures' ", PackageProviderName);
            request.Yield(Features);
        }

        public void InitializeProvider(NuGetRequest request) {
            Features.AddOrSet("exe", new[] {
                Assembly.GetAssembly(typeof (PackageSource)).Location
            });

            // create a strongly-typed request object.
            // Nice-to-have put a debug message in that tells what's going on.
            request.Debug("Calling '{0}::InitializeProvider'", PackageProviderName);
        }

        public void GetDynamicOptions(string category, NuGetRequest request) {
            request.Debug("Calling '{0}::GetDynamicOptions' '{1}'", PackageProviderName, category);

            switch ((category ?? string.Empty).ToLowerInvariant()) {
                case "package":
                    request.YieldDynamicOption("FilterOnTag", Constants.OptionType.StringArray, false);
                    request.YieldDynamicOption("Contains", Constants.OptionType.String, false);
                    request.YieldDynamicOption("AllowPrereleaseVersions", Constants.OptionType.Switch, false);
                    break;

                case "source":
                    request.YieldDynamicOption("ConfigFile", Constants.OptionType.String, false);
                    request.YieldDynamicOption("SkipValidate", Constants.OptionType.Switch, false);
                    break;

                case "install":
                    request.YieldDynamicOption("Destination", Constants.OptionType.Path, true);
                    request.YieldDynamicOption("SkipDependencies", Constants.OptionType.Switch, false);
                    request.YieldDynamicOption("ContinueOnFailure", Constants.OptionType.Switch, false);
                    request.YieldDynamicOption("ExcludeVersion", Constants.OptionType.Switch, false);
                    request.YieldDynamicOption("PackageSaveMode", Constants.OptionType.String, false, new[] {
                        "nuspec", "nupkg", "nuspec;nupkg"
                    });
                    break;
            }
        }

        /// <summary>
        ///     This is called when the user is adding (or updating) a package source
        ///     If this PROVIDER doesn't support user-defined package sources, remove this method.
        /// </summary>
        /// <param name="name">
        ///     The name of the package source. If this parameter is null or empty the PROVIDER should use the
        ///     location as the name (if the PROVIDER actually stores names of package sources)
        /// </param>
        /// <param name="location">
        ///     The location (ie, directory, URL, etc) of the package source. If this is null or empty, the
        ///     PROVIDER should use the name as the location (if valid)
        /// </param>
        /// <param name="trusted">
        ///     A boolean indicating that the user trusts this package source. Packages returned from this source
        ///     should be marked as 'trusted'
        /// </param>
        /// <param name="request">
        ///     An object passed in from the CORE that contains functions that can be used to interact with the
        ///     CORE and HOST
        /// </param>
        public void AddPackageSource(string name, string location, bool trusted, NuGetRequest request) {
            AddPackageSourceImpl(name, location, trusted, request);
        }

        /// <summary>
        /// </summary>
        /// <param name="request">
        ///     An object passed in from the CORE that contains functions that can be used to interact with the
        ///     CORE and HOST
        /// </param>
        public void ResolvePackageSources(NuGetRequest request) {
            ResolvePackageSourcesImpl(request);
        }

        /// <summary>
        ///     Removes/Unregisters a package source
        /// </summary>
        /// <param name="name">The name or location of a package source to remove.</param>
        /// <param name="request">
        ///     An object passed in from the CORE that contains functions that can be used to interact with the
        ///     CORE and HOST
        /// </param>
        public void RemovePackageSource(string name, NuGetRequest request) {
            RemovePackageSourceImpl(name, request);
        }

        /// <summary>
        /// </summary>
        /// <param name="name"></param>
        /// <param name="requiredVersion"></param>
        /// <param name="minimumVersion"></param>
        /// <param name="maximumVersion"></param>
        /// <param name="id"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        public void FindPackage(string name, string requiredVersion, string minimumVersion, string maximumVersion, int id, NuGetRequest request) {
            FindPackageImpl(name, requiredVersion, minimumVersion, maximumVersion, id, request);
        }

        /// <summary>
        /// </summary>
        /// <param name="fastPackageReference"></param>
        /// <param name="request">
        ///     An object passed in from the CORE that contains functions that can be used to interact with the
        ///     CORE and HOST
        /// </param>
        public void InstallPackage(string fastPackageReference, NuGetRequest request) {
            InstallPackageImpl(fastPackageReference, request);
        }

        /// <summary>
        ///     Uninstalls a package
        /// </summary>
        /// <param name="fastPackageReference"></param>
        /// <param name="request">
        ///     An object passed in from the CORE that contains functions that can be used to interact with the
        ///     CORE and HOST
        /// </param>
        public void UninstallPackage(string fastPackageReference, NuGetRequest request) {
            UninstallPackageImpl(fastPackageReference, request);
        }

        /// <summary>
        ///     Downloads a remote package file to a local location.
        /// </summary>
        /// <param name="fastPackageReference"></param>
        /// <param name="location"></param>
        /// <param name="request">
        ///     An object passed in from the CORE that contains functions that can be used to interact with the
        ///     CORE and HOST
        /// </param>
        /// <returns></returns>
        public void DownloadPackage(string fastPackageReference, string location, NuGetRequest request) {
            DownloadPackageImpl(fastPackageReference, location, request);
        }

        /// <summary>
        ///     Finds a package given a local filename
        /// </summary>
        /// <param name="file"></param>
        /// <param name="id"></param>
        /// <param name="request"></param>
        public void FindPackageByFile(string file, int id, NuGetRequest request) {
            FindPackageByFileImpl(file, id, request);
        }

        /// <summary>
        /// Returns the packages that are installed
        /// </summary>
        /// <param name="name">the package name to match. Empty or null means match everything</param>
        /// <param name="requiredVersion">the specific version asked for. If this parameter is specified (ie, not null or empty string) then the minimum and maximum values are ignored</param>
        /// <param name="minimumVersion">the minimum version of packages to return . If the <code>requiredVersion</code> parameter is specified (ie, not null or empty string) this should be ignored</param>
        /// <param name="maximumVersion">the maximum version of packages to return . If the <code>requiredVersion</code> parameter is specified (ie, not null or empty string) this should be ignored</param>
        /// <param name="request">
        ///     An object passed in from the CORE that contains functions that can be used to interact with
        ///     the CORE and HOST
        /// </param>
        public void GetInstalledPackages(string name, string requiredVersion, string minimumVersion, string maximumVersion, NuGetRequest request) {
            GetInstalledPackagesImpl(name, requiredVersion, minimumVersion, maximumVersion, request);
        }
    }
}

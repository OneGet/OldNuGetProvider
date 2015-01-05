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

namespace Microsoft.OneGet.NuGetProvider.Chocolatey {
    using System.Collections.Generic;
    using Common;
    using Sdk;

    /// <summary>
    ///     Chocolatey Package provider for OneGet.
    ///     Important notes:
    ///     - Required Methods: Not all methods are required; some package providers do not support some features. If the
    ///     methods isn't used or implemented it should be removed (or commented out)
    ///     - Error Handling: Avoid throwing exceptions from these methods. To properly return errors to the user, use the
    ///     request.Error(...) method to notify the user of an error conditionm and then return.
    ///     - Communicating with the HOST and CORE: each method takes a IRequestObject (in reality, an alias for
    ///     System.Object), which can be used in one of two ways:
    ///     - use the c# 'dynamic' keyword, and call functions on the object directly.
    ///     - use the <code><![CDATA[ .As<Request>() ]]></code> extension method to strongly-type it to the Request type (which
    ///     calls upon the duck-typer to generate a strongly-typed wrapper).  The strongly-typed wrapper also implements
    ///     several helper functions to make using the request object easier.
    /// </summary>
    public class ChocolateyProvider : CommonProvider {
        internal static readonly Dictionary<string, string[]> Features = new Dictionary<string, string[]> {
            {Constants.Features.SupportedSchemes, new[] {"http", "https", "file"}},
            {Constants.Features.SupportedExtensions, new[] {"nupkg"}},
            {Constants.Features.MagicSignatures, new[] {Constants.Signatures.Zip}},
        };

        /// <summary>
        ///     The name of this Package Provider
        /// </summary>
        public override string PackageProviderName {
            get {
                return "Chocolatey";
            }
        }

        /// <summary>
        ///     Performs one-time initialization of the PROVIDER.
        /// </summary>
        /// <param name="request">
        ///     An object passed in from the CORE that contains functions that can be used to interact with the
        ///     CORE and HOST
        /// </param>
        public void InitializeProvider(ChocolateyRequest request) {
            // Nice-to-have put a debug message in that tells what's going on.
            request.Debug("Calling '{0}::InitializeProvider'", PackageProviderName);
        }

        /// <summary>
        ///     Returns a collection of strings to the client advertizing features this provider supports.
        /// </summary>
        /// <param name="request">
        ///     An object passed in from the CORE that contains functions that can be used to interact with the
        ///     CORE and HOST
        /// </param>
        public void GetFeatures(ChocolateyRequest request) {
            // Nice-to-have put a debug message in that tells what's going on.
            request.Debug("Calling '{0}::GetFeatures' ", PackageProviderName);
            request.Yield(Features);
        }

        public void GetDynamicOptions(string category, ChocolateyRequest request) {
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
                    request.YieldDynamicOption("SkipDependencies", Constants.OptionType.Switch, false);
                    request.YieldDynamicOption("ContinueOnFailure", Constants.OptionType.Switch, false);
                    request.YieldDynamicOption("ExcludeVersion", Constants.OptionType.Switch, false);
                    request.YieldDynamicOption("PackageSaveMode", Constants.OptionType.String, false, new[] {
                        "nuspec", "nupkg", "nuspec;nupkg"
                    });
                    break;
            }
        }

        public void ExecuteElevatedAction(string payload, ChocolateyRequest request) {
            // Nice-to-have put a debug message in that tells what's going on.
            request.Debug("Calling '{0}::ExecuteElevatedAction' '{1}'", PackageProviderName, payload);
            if (!request.Invoke(payload)) {
                request.Error(ErrorCategory.InvalidResult, "Chocolatey Install Script", Constants.Messages.PackageFailedInstall);
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
        public void AddPackageSource(string name, string location, bool trusted, ChocolateyRequest request) {
            AddPackageSourceImpl(name, location, trusted, request);
        }

        /// <summary>
        /// </summary>
        /// <param name="request">
        ///     An object passed in from the CORE that contains functions that can be used to interact with the
        ///     CORE and HOST
        /// </param>
        public void ResolvePackageSources(ChocolateyRequest request) {
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
        public void RemovePackageSource(string name, ChocolateyRequest request) {
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
        public void FindPackage(string name, string requiredVersion, string minimumVersion, string maximumVersion, int id, ChocolateyRequest request) {
            FindPackageImpl(name, requiredVersion, minimumVersion, maximumVersion, id, request);
        }

        /// <summary>
        /// </summary>
        /// <param name="fastPackageReference"></param>
        /// <param name="request">
        ///     An object passed in from the CORE that contains functions that can be used to interact with the
        ///     CORE and HOST
        /// </param>
        public void InstallPackage(string fastPackageReference, ChocolateyRequest request) {
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
        public void UninstallPackage(string fastPackageReference, ChocolateyRequest request) {
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
        public void DownloadPackage(string fastPackageReference, string location, ChocolateyRequest request) {
            DownloadPackageImpl(fastPackageReference, location, request);
        }

        /// <summary>
        ///     Returns package references for all the dependent packages
        /// </summary>
        /// <param name="fastPackageReference"></param>
        /// <param name="request">
        ///     An object passed in from the CORE that contains functions that can be used to interact with the
        ///     CORE and HOST
        /// </param>
        /// <returns></returns>
        public void GetPackageDependencies(string fastPackageReference, ChocolateyRequest request) {
            GetPackageDependenciesImpl(fastPackageReference, request);
        }

        /// <summary>
        ///     Finds a package given a local filename
        /// </summary>
        /// <param name="file"></param>
        /// <param name="id"></param>
        /// <param name="request"></param>
        public void FindPackageByFile(string file, int id, ChocolateyRequest request) {
            FindPackageByFileImpl(file, id, request);
        }

        /// <summary>
        ///     Gets the installed packages
        /// </summary>
        /// <param name="name"></param>
        /// <param name="request"></param>
        public void GetInstalledPackages(string name, ChocolateyRequest request) {
            GetInstalledPackagesImpl(name, request);
        }
    }
}
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
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Management.Automation;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using NuGet;
    using Sdk;
    using Constants = Sdk.Constants;
    using ErrorCategory = Sdk.ErrorCategory;

    public abstract class CommonRequest : Request {
        internal const string MultiplePackagesInstalledExpectedOne = "MSG:MultiplePackagesInstalledExpectedOne_package";
        private static readonly Regex _rxFastPath = new Regex(@"\$(?<source>[\w,\+,\/,=]*)\\(?<id>[\w,\+,\/,=]*)\\(?<version>[\w,\+,\/,=]*)\\(?<sources>[\w,\+,\/,=]*)");
        private static readonly Regex _rxPkgParse = new Regex(@"'(?<pkgId>\S*)\s(?<ver>.*?)'");
        protected string _configurationFileLocation;
        internal ImplictLazy<bool> AllowPrereleaseVersions;
        internal ImplictLazy<bool> AllVersions;
        internal ImplictLazy<string> Contains;
        internal ImplictLazy<bool> ContinueOnFailure;
        internal ImplictLazy<bool> ExcludeVersion;
        internal ImplictLazy<string[]> FilterOnTag;
        internal ImplictLazy<string> PackageSaveMode;
        internal ImplictLazy<bool> SkipDependencies;
        internal ImplictLazy<bool> SkipValidate;

        protected CommonRequest() {
            FilterOnTag = new ImplictLazy<string[]>(() => (GetOptionValues("FilterOnTag") ?? new string[0]).ToArray());
            Contains = new ImplictLazy<string>(() => GetOptionValue("Contains"));
            SkipValidate = new ImplictLazy<bool>(() => GetOptionValue("SkipValidate").IsTrue());
            AllowPrereleaseVersions = new ImplictLazy<bool>(() => GetOptionValue("AllowPrereleaseVersions").IsTrue());
            AllVersions = new ImplictLazy<bool>(() => GetOptionValue("AllVersions").IsTrue());
            SkipDependencies = new ImplictLazy<bool>(() => GetOptionValue("SkipDependencies").IsTrue());
            ContinueOnFailure = new ImplictLazy<bool>(() => GetOptionValue("ContinueOnFailure").IsTrue());
            ExcludeVersion = new ImplictLazy<bool>(() => GetOptionValue("ExcludeVersion").IsTrue());
            PackageSaveMode = new ImplictLazy<string>(() => {
                var sm = GetOptionValue("PackageSaveMode");
                if (string.IsNullOrEmpty(sm)) {
                    sm = "nupkg";
                }
                return sm;
            });
        }

        public abstract string PackageProviderName {get;}
        internal abstract IEnumerable<string> SupportedSchemes {get;}
        protected abstract string ConfigurationFileLocation {get;}
        internal abstract string Destination {get;}
        internal abstract IDictionary<string, PackageSource> RegisteredPackageSources {get;}

        internal string[] OriginalSources;

        internal IEnumerable<PackageSource> SelectedSources {
            get {
                var sources = (OriginalSources ?? Enumerable.Empty<string>()).Union(PackageSources ?? Enumerable.Empty<string>()).ToArray();
                var pkgSources = RegisteredPackageSources;

                if (sources.Length == 0) {
                    // return them all.
                    foreach (var src in pkgSources.Values) {
                        yield return src;
                    }
                    yield break;
                }

                // otherwise, return packaeg sources that match the items given.
                foreach (var src in sources) {
                    // check to see if we have a source with either that name 
                    // or that URI first.
                    if (pkgSources.ContainsKey(src)) {
                        yield return pkgSources[src];
                        continue;
                    }

                    var srcLoc = src;
                    var found = false;
                    foreach (var byLoc in pkgSources.Values.Where(each => each.Location == srcLoc)) {
                        yield return byLoc;
                        found = true;
                    }
                    if (found) {
                        continue;
                    }

                    // doesn't look like we have this as a source.
                    if (Uri.IsWellFormedUriString(src, UriKind.Absolute)) {
                        // we have been passed in an URI
                        var srcUri = new Uri(src);
                        if (SupportedSchemes.Contains(srcUri.Scheme.ToLower())) {
                            // it's one of our supported uri types.
                            var isValidated = false;

                            if (!SkipValidate) {
                                isValidated = ValidateSourceUri(srcUri);
                            }

                            if (SkipValidate || isValidated) {
                                yield return new PackageSource {
                                    Location = srcUri.ToString(),
                                    Name = srcUri.ToString(),
                                    Trusted = false,
                                    IsRegistered = false,
                                    IsValidated = isValidated,
                                };
                                continue;
                            }
                            Error(ErrorCategory.InvalidArgument, src, Constants.Messages.SourceLocationNotValid, src);
                            Warning(Constants.Messages.UnableToResolveSource, src);
                            continue;
                        }

                        // hmm. not a valid location?
                        Error(ErrorCategory.InvalidArgument, src, Constants.Messages.UriSchemeNotSupported, src);
                        Warning(Constants.Messages.UnableToResolveSource, src);
                        continue;
                    }

                    // is it a file path?
                    if (Directory.Exists(src)) {
                        yield return new PackageSource {
                            Location = src,
                            Name = src,
                            Trusted = true,
                            IsRegistered = false,
                            IsValidated = true,
                        };
                    } else {
                        // hmm. not a valid location?
                        Warning(Constants.Messages.UnableToResolveSource, src);
                    }
                }
            }
        }

        internal static string NuGetExePath {
            get {
                return typeof (AggregateRepository).Assembly.Location;
            }
        }

        internal abstract void RemovePackageSource(string id);
        internal abstract void AddPackageSource(string name, string location, bool isTrusted, bool isValidated);

        private bool LocationCloseEnoughMatch(string givenLocation, string knownLocation) {
            if (givenLocation.Equals(knownLocation, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            // make trailing slashes consistent
            if (givenLocation.TrimEnd('/').Equals(knownLocation.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            // and trailing backslashes
            if (givenLocation.TrimEnd('\\').Equals(knownLocation.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
            return false;
        }

        internal PackageSource FindRegisteredSource(string name) {
            var srcs = RegisteredPackageSources;
            if (srcs.ContainsKey(name)) {
                return srcs[name];
            }

            var src = srcs.Values.FirstOrDefault(each => LocationCloseEnoughMatch(name, each.Location));
            if (src != null) {
                return src;
            }

            return null;
        }

        internal bool ValidateSourceLocation(string location) {
            if (Uri.IsWellFormedUriString(location, UriKind.Absolute)) {
                return ValidateSourceUri(new Uri(location));
            }
            try {
                if (Directory.Exists(location) || File.Exists(location)) {
                    return true;
                }
            } catch {
            }
            return false;
        }

        private bool ValidateSourceUri(Uri srcUri) {
            if (!SupportedSchemes.Contains(srcUri.Scheme.ToLowerInvariant())) {
                return false;
            }

            if (srcUri.IsFile) {
                var path = srcUri.ToString().CanonicalizePath(false);

                if (Directory.Exists(path)) {
                    return true;
                }
                return false;
            }

            // todo: do a get on the uri and see if it responds.
            try {
                var repo = PackageRepositoryFactory.Default.CreateRepository(srcUri.ToString());
                var drepo = repo as DataServicePackageRepository;
                if (drepo != null) {
                    drepo.FindPackagesById("xx");
                }
                return true;
            } catch {
                // nope.
            }

            return false;
        }

        internal PackageSource ResolvePackageSource(string nameOrLocation) {
            var source = FindRegisteredSource(nameOrLocation);
            if (source != null) {
                return source;
            }

            try {
                // is the given value a filename?
                if (File.Exists(nameOrLocation)) {
                    return new PackageSource() {
                        IsRegistered = false,
                        IsValidated = true,
                        Location = nameOrLocation,
                        Name = nameOrLocation,
                        Trusted = true,
                    };
                }
            } catch {
            }

            try {
                // is the given value a directory?
                if (Directory.Exists(nameOrLocation)) {
                    return new PackageSource() {
                        IsRegistered = false,
                        IsValidated = true,
                        Location = nameOrLocation,
                        Name = nameOrLocation,
                        Trusted = true,
                    };
                }
            } catch {
            }

            if (Uri.IsWellFormedUriString(nameOrLocation, UriKind.Absolute)) {
                var uri = new Uri(nameOrLocation, UriKind.Absolute);
                if (!SupportedSchemes.Contains(uri.Scheme.ToLowerInvariant())) {
                    Error(ErrorCategory.InvalidArgument, uri.ToString(), Constants.Messages.UriSchemeNotSupported, uri);
                    return null;
                }

                // this is an URI, and it looks like one type that we support
                if (SkipValidate || ValidateSourceUri(uri)) {
                    return new PackageSource {
                        IsRegistered = false,
                        IsValidated = !SkipValidate,
                        Location = nameOrLocation,
                        Name = nameOrLocation,
                        Trusted = false,
                    };
                }
            }

            Error(ErrorCategory.InvalidArgument, nameOrLocation, Constants.Messages.UnableToResolveSource, nameOrLocation);
            return null;
        }

        internal IEnumerable<IPackage> FilterOnVersion(IEnumerable<IPackage> pkgs, string requiredVersion, string minimumVersion, string maximumVersion) {
            if (!String.IsNullOrEmpty(requiredVersion)) {
                pkgs = pkgs.Where(each => each.Version == new SemanticVersion(requiredVersion));
            } else {
                if (!String.IsNullOrEmpty(minimumVersion)) {
                    pkgs = pkgs.Where(each => each.Version >= new SemanticVersion(minimumVersion));
                }

                if (!String.IsNullOrEmpty(maximumVersion)) {
                    pkgs = pkgs.Where(each => each.Version <= new SemanticVersion(maximumVersion));
                }
            }
            return pkgs;
        }

        internal string MakeFastPath(PackageSource source, string id, string version) {
            return String.Format(@"${0}\{1}\{2}\{3}", source.Serialized, id.ToBase64(), version.ToBase64(), (Sources??new string[0]).Select(each => each.ToBase64()).SafeAggregate((current, each) => current + "|" + each));
        }

        public bool TryParseFastPath(string fastPath, out string source, out string id, out string version, out string[] sources) {
            var match = _rxFastPath.Match(fastPath);
            source = match.Success ? match.Groups["source"].Value.FromBase64() : null;
            id = match.Success ? match.Groups["id"].Value.FromBase64() : null;
            version = match.Success ? match.Groups["version"].Value.FromBase64() : null;
            var srcs = match.Success ? match.Groups["sources"].Value: string.Empty;
            sources = srcs.Split('|').Select(each => each.FromBase64()).ToArray();
            return match.Success;
        }

        internal bool YieldPackage(PackageItem pkg, string searchKey) {
            try {
                if (YieldSoftwareIdentity(pkg.FastPath, pkg.Package.Id, pkg.Package.Version.ToString(), "semver", pkg.Package.Summary, pkg.PackageSource.Name, searchKey, pkg.FullPath, pkg.PackageFilename) != null) {
                    
                    // iterate thru the dependencies and add them to the software identity.
                    foreach (var depSet in pkg.Package.DependencySets) {
                        foreach (var dep in depSet.Dependencies) {

                            var dependency = AddDependency(PackageProviderName, dep.Id, dep.VersionSpec == null ? null : dep.VersionSpec.ToString(), null, null);
                            // todo: determine if we can generate an appropriate "appliesTo" for the package.

                            if (IsCanceled) {
                                return false;
                            }
                        }
                    }

                    if (AddMetadata(pkg.FastPath, "copyright", pkg.Package.Copyright) == null) {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "description", pkg.Package.Description) == null) {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "language", pkg.Package.Language) == null) {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "releaseNotes", pkg.Package.ReleaseNotes) == null) {
                        return false;
                    }
                    if (pkg.Package.Published != null) {
                        // published time.
                        if (AddMetadata(pkg.FastPath, "published", pkg.Package.Published.ToString()) == null) {
                            return false;
                        }
                    }
                    if (AddMetadata(pkg.FastPath, "tags", pkg.Package.Tags) == null) {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "title", pkg.Package.Title) == null) {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "developmentDependency", pkg.Package.DevelopmentDependency.ToString()) == null) {
                        return false;
                    }
                    if (AddMetadata(pkg.FastPath, "FromTrustedSource", pkg.PackageSource.Trusted.ToString()) == null) {
                        return false;
                    }
                    if (pkg.Package.LicenseUrl != null && !String.IsNullOrEmpty(pkg.Package.LicenseUrl.ToString())) {
                        if (AddLink(pkg.Package.LicenseUrl, "license", null, null, null, null, null) == null) {
                            return false;
                        }
                    }
                    if (pkg.Package.ProjectUrl != null && !String.IsNullOrEmpty(pkg.Package.ProjectUrl.ToString())) {
                        if (AddLink(pkg.Package.ProjectUrl, "project", null, null, null, null, null) == null) {
                            return false;
                        }
                    }
                    if (pkg.Package.ReportAbuseUrl != null && !String.IsNullOrEmpty(pkg.Package.ReportAbuseUrl.ToString())) {
                        if (AddLink(pkg.Package.ReportAbuseUrl, "abuse", null, null, null, null, null) == null) {
                            return false;
                        }
                    }
                    if (pkg.Package.IconUrl != null && !String.IsNullOrEmpty(pkg.Package.IconUrl.ToString())) {
                        if (AddLink(pkg.Package.IconUrl, "icon", null, null, null, null, null) == null) {
                            return false;
                        }
                    }
                    if (pkg.Package.Authors.Any(author => AddEntity(author.Trim(), author.Trim(), "author", null) == null)) {
                        return false;
                    }

                    if (pkg.Package.Owners.Any(owner => AddEntity(owner.Trim(), owner.Trim(), "owner", null) == null)) {
                        return false;
                    }
                } else {
                    return false;
                }
            } catch (Exception e) {
                e.Dump(this);
                return false;
            }
            return true;
        }

        internal bool YieldPackages(IEnumerable<PackageItem> packageReferences, string searchKey) {
            var foundPackage = false;
            if (packageReferences == null) {
                return false;
            }
            Debug("Iterating");

            foreach (var pkg in packageReferences) {
                foundPackage = true;
                try {
                    Debug("Yielding");
                    if (!YieldPackage(pkg, searchKey)) {
                        break;
                    }
                } catch (Exception e) {
                    e.Dump(this);
                    return false;
                }
            }

            Debug("Done Iterating");
            return foundPackage;
        }

        internal IEnumerable<PackageItem> GetPackageById(PackageSource source, string name, string requiredVersion = null, string minimumVersion = null, string maximumVersion = null, bool allowUnlisted = false) {
            try {
                if (!string.IsNullOrWhiteSpace(requiredVersion) && requiredVersion.IndexOfAny(new char[] {'[', ']', '(', ')'}) > -1) {
                    IVersionSpec versionSpec;
                    if (VersionUtility.TryParseVersionSpec(requiredVersion, out versionSpec)) {
                        // we've got a IVersionSpec -- we can search with that.

                        // plus, if we're handling a version range like this, we don't want to do any tags or filters.
                        // so just return this set simply.
                        return source.Repository.FindPackages(name, versionSpec, AllowPrereleaseVersions, allowUnlisted).Select(pkg => new PackageItem {
                            Package = pkg,
                            PackageSource = source,
                            FastPath = MakeFastPath(source, pkg.Id, pkg.Version.ToString())
                        });
                    }
                } 

                // otherwise fall back to traditional behavior
                var pkgs = source.Repository.FindPackagesById(name);

                if (!AllVersions && (String.IsNullOrEmpty(requiredVersion) && String.IsNullOrEmpty(minimumVersion) && String.IsNullOrEmpty(maximumVersion))) {
                    pkgs = from p in pkgs where p.IsLatestVersion select p;
                }

                pkgs = FilterOnContains(pkgs);
                pkgs = FilterOnTags(pkgs);

                return FilterOnVersion(pkgs, requiredVersion, minimumVersion, maximumVersion)
                    .Select(pkg => new PackageItem {
                        Package = pkg,
                        PackageSource = source,
                        FastPath = MakeFastPath(source, pkg.Id, pkg.Version.ToString())
                    });
                
            } catch (Exception e) {
                e.Dump(this);
                return Enumerable.Empty<PackageItem>();
            }
        }

        internal IEnumerable<PackageItem> GetPackageById(string name, string requiredVersion = null, string minimumVersion = null, string maximumVersion = null, bool allowUnlisted = false) {
            if (String.IsNullOrEmpty(name)) {
                return Enumerable.Empty<PackageItem>();
            }
            return SelectedSources.AsParallel().WithMergeOptions(ParallelMergeOptions.NotBuffered).SelectMany(source => GetPackageById(source, name, requiredVersion, minimumVersion, maximumVersion, allowUnlisted));
        }

        internal IEnumerable<IPackage> FilterOnName(IEnumerable<IPackage> pkgs, string name) {
            return pkgs.Where(each => each.Id.IndexOf(name, StringComparison.OrdinalIgnoreCase) > -1);
        }

        internal IEnumerable<IPackage> FilterOnTags(IEnumerable<IPackage> pkgs) {
            if (FilterOnTag == null || FilterOnTag.Value.Length == 0) {
                return pkgs;
            }
            return pkgs.Where(each => FilterOnTag.Value.Any(tag => each.Tags.IndexOf(tag, StringComparison.OrdinalIgnoreCase) > -1));
        }

        internal IEnumerable<IPackage> FilterOnContains(IEnumerable<IPackage> pkgs) {
            if (string.IsNullOrEmpty(Contains)) {
                return pkgs;
            }
            return pkgs.Where(each => each.Description.IndexOf(Contains, StringComparison.OrdinalIgnoreCase) > -1 || each.Id.IndexOf(Contains, StringComparison.OrdinalIgnoreCase) > -1);
        }

        internal PackageItem GetPackageByFilePath(string filePath) {
            // todo: currently requires nupkg file in place.

            if (PackageHelper.IsPackageFile(filePath)) {
                var package = new ZipPackage(filePath);
                var source = ResolvePackageSource(filePath);

                return new PackageItem {
                    FastPath = MakeFastPath(source, package.Id, package.Version.ToString()),
                    PackageSource = source,
                    Package = package,
                    IsPackageFile = true,
                    FullPath = filePath,
                };
            }
            return null;
        }

        internal PackageItem GetPackageByFastpath(string fastPath) {
            string sourceLocation;
            string id;
            string version;
            string[] sources;

            if (TryParseFastPath(fastPath, out sourceLocation, out id, out version, out sources)) {
                var source = ResolvePackageSource(sourceLocation);

                if (source.IsSourceAFile) {
                    return GetPackageByFilePath(sourceLocation);
                }

                var pkg = source.Repository.FindPackage(id, new SemanticVersion(version));

                if (pkg != null) {
                    return new PackageItem {
                        FastPath = fastPath,
                        PackageSource = source,
                        Package = pkg,
                        Sources = sources
                    };
                }
            }
            return null;
        }

        internal IEnumerable<PackageItem> SearchForPackages(string name, string requiredVersion, string minimumVersion, string maximumVersion) {
            return SelectedSources.AsParallel().WithMergeOptions(ParallelMergeOptions.NotBuffered).SelectMany(source => SearchSourceForPackages(source, name, requiredVersion, minimumVersion, maximumVersion));
        }

        internal IEnumerable<PackageItem> FindPackageByNameFirst(PackageSource source, string name, string requiredVersion, string minimumVersion, string maximumVersion) {
            var package = GetPackageById(source, name, requiredVersion, minimumVersion, maximumVersion).ToArray();
            return package.Length > 0 ? package : SearchSourceForPackages(source, name, requiredVersion, minimumVersion, maximumVersion);
        }

        private IEnumerable<PackageItem> SearchSourceForPackages(PackageSource source, string name, string requiredVersion, string minimumVersion, string maximumVersion) {
            try {
                if (!String.IsNullOrEmpty(name) && WildcardPattern.ContainsWildcardCharacters(name)) {
                    // NuGet does not support PowerShell/POSIX style wildcards and supports only '*' in searchTerm with NuGet.exe
                    // Replace the range from '[' - to ']' with * and ? with * then wildcard pattern is applied on the results from NuGet.exe
                    var tempName = name;
                    var squareBracketPattern = Regex.Escape("[") + "(.*?)]";
                    foreach (Match match in Regex.Matches(tempName, squareBracketPattern)) {
                        tempName = tempName.Replace(match.Value, "*");
                    }
                    var searchTerm = tempName.Replace("?", "*");

                    // Wildcard pattern matching configuration.
                    const WildcardOptions wildcardOptions = WildcardOptions.CultureInvariant | WildcardOptions.IgnoreCase;
                    var wildcardPattern = new WildcardPattern(searchTerm, wildcardOptions);

                    IEnumerable<string> packageIds = null;
                    using (var p = AsyncProcess.Start(NuGetExePath, String.Format(@"list ""{0}"" -Source ""{1}"" ", searchTerm, source.Location))) {
                        packageIds = p.StandardOutput.Where(each => !String.IsNullOrEmpty(each)).Select(l => {
                            Verbose("NuGet: {0}", l);
                            if (l.Contains("No packages found.")) {
                                return null;
                            }
                            // ComicRack 0.9.162
                            var packageDetails = l.Split();

                            if (wildcardPattern.IsMatch(packageDetails[0])) {
                                return packageDetails[0];
                            }
                            return null;
                        }).Where(each => each != null).ToArray();

                        foreach (var l in p.StandardError.Where(l => !String.IsNullOrEmpty(l))) {
                            Warning("NuGet: {0}", l);
                        }
                    }
                    return packageIds.SelectMany(packageId => GetPackageById(source, packageId, requiredVersion, minimumVersion, maximumVersion));
                    // return packageIds.SelectMany(packageId => SearchSourceForPackages(source, packageId, requiredVersion, minimumVersion, maximumVersion));
                    // return packageIds.SelectMany(packageId => FindPackageByNameFirst(source, packageId, requiredVersion, minimumVersion, maximumVersion));
                    // return SearchSourceForPackages(source.Location, requiredVersion, minimumVersion, maximumVersion);
                    /* return FilterOnVersion(source.Repository.FindPackages(packageIds), requiredVersion, minimumVersion, maximumVersion)
                        .Select(pkg => new PackageItem {
                            Package = pkg,
                            PackageSource = source,
                            FastPath = MakeFastPath(source, pkg.Id, pkg.Version.ToString())
                        });*/
                }
            } catch (Exception e) {
                e.Dump(this);
                return Enumerable.Empty<PackageItem>();
            }

            try {
                var criteria = Contains.Value;
                if (String.IsNullOrEmpty(criteria)) {
                    criteria = name;
                }

                if (FilterOnTag != null) {
                    criteria = FilterOnTag.Value.Where(tag => !string.IsNullOrEmpty(tag)).Aggregate(criteria, (current, tag) => current + " tag:" + tag);
                }
                Debug("Searching repository '{0}' for '{1}'", source.Repository.Source, criteria);
                // var src = PackageRepositoryFactory.Default.CreateRepository(source.Repository.Source);
                //  var src = new AggregateRepository(new IPackageRepository[] {source.Repository});
                var src = source.Repository;
                /*
                IQueryable<IPackage> packages;
                if (src is IServiceBasedRepository) {
                    packages = (src as IServiceBasedRepository).Search(criteria, new string[0], AllowPrereleaseVersions);
                } else {
                    packages = src.Search(criteria, AllowPrereleaseVersions);    
                }
                */
                
                var packages = src.Search(criteria, AllowPrereleaseVersions);

                /*
                foreach (var p in pp) {
                    Console.WriteLine(p.GetFullName());
                }
                */

                // packages = packages.OrderBy(p => p.Id);

                // query filtering:
                if (!AllVersions && (String.IsNullOrEmpty(requiredVersion) && String.IsNullOrEmpty(minimumVersion) && String.IsNullOrEmpty(maximumVersion))) {
                    packages = packages.FindLatestVersion();
                }

                IEnumerable<IPackage> pkgs = new PackagesEnumerable(packages);

                // if they passed a name, restrict the search things that actually contain the name in the FullName.
                if (!String.IsNullOrEmpty(name)) {
                    pkgs = FilterOnName(pkgs, name);
                }

                pkgs = FilterOnTags(pkgs);

                pkgs = FilterOnContains(pkgs);

                return FilterOnVersion(pkgs, requiredVersion, minimumVersion, maximumVersion)
                    .Select(pkg => new PackageItem {
                        Package = pkg,
                        PackageSource = source,
                        FastPath = MakeFastPath(source, pkg.Id, pkg.Version.ToString())
                    });
            } catch (Exception e) {
                e.Dump(this);
                return Enumerable.Empty<PackageItem>();
            }
        }

        public bool IsPackageInstalled(string name, string version) {
#if find_installed_packages_with_nuspec 

            var nuspecs = from pkgFile in Directory.EnumerateFileSystemEntries(Destination, "*.nuspec", SearchOption.AllDirectories) select pkgFile ;

            foreach (var n in nuspecs) {
                // uh, do we have to parse these?
                // hmmm.
            }

            // or we could search in this folder for a directory with or without the version
            // then examine the contents.
            // hmmm. I'd rather let nuget do that if I can, it's better at it.

#endif
            return (from pkgFile in Directory.EnumerateFileSystemEntries(Destination, "*.nupkg", SearchOption.AllDirectories)
                where PackageHelper.IsPackageFile(pkgFile)
                select new ZipPackage(pkgFile))
                .Any(pkg => pkg.Id.Equals(name, StringComparison.OrdinalIgnoreCase) && pkg.Version.ToString().Equals(version, StringComparison.OrdinalIgnoreCase));
        }

        internal IEnumerable<PackageItem> GetUninstalledPackageDependencies(PackageItem packageItem) {
            
            foreach (var depSet in packageItem.Package.DependencySets) {
                foreach (var dep in depSet.Dependencies) {
                    // get all the packages that match this dependency
                    var depRefs = dep.VersionSpec == null ? GetPackageById(dep.Id).ToArray() : GetPackageByIdAndVersionSpec(dep.Id, dep.VersionSpec, true).ToArray();

                    if (depRefs.Length == 0) {
                        Error(ErrorCategory.ObjectNotFound, packageItem.GetCanonicalId(this), Constants.Messages.DependencyResolutionError, ProviderServices.GetCanonicalPackageId(PackageProviderName, dep.Id, ((object)dep.VersionSpec ?? "").ToString(),null));
                        throw new Exception("failed");
                    }

                    if (depRefs.Any(each => IsPackageInstalled(each.Id, each.Version))) {
                        // we have a compatible version installed.
                        continue;
                    }

                    yield return depRefs[0];

                    // it's not installed. return this as a needed package, but first, get it's dependencies.
                    foreach (var nestedDep in GetUninstalledPackageDependencies(depRefs[0])) {
                        yield return nestedDep;
                    }
                }
            }
        }

        private PackageItem ParseOutputFull(PackageSource source, string packageId, string version, string line) {
            var match = _rxPkgParse.Match(line);
            if (match.Success) {
                var pkg = new PackageItem {
                    Id = match.Groups["pkgId"].Value,
                    Version = match.Groups["ver"].Value,
                };

                // if this was the package we started with, we can assume a bit more info,
                if (pkg.Id == packageId && pkg.Version == version) {
                    pkg.PackageSource = source;
                }
                pkg.FullPath = Path.Combine(Destination, ExcludeVersion ? pkg.Id : pkg.FullName);
                return pkg;
            }
            return null;
        }

        internal InstallResult NuGetInstall(PackageItem item) {
            var result = new InstallResult();

            using (
                var p = AsyncProcess.Start(NuGetExePath,
                    String.Format(@"install ""{0}"" -Version ""{1}"" -Source ""{2}"" -PackageSaveMode ""{4}""  -OutputDirectory ""{3}"" -Verbosity detailed {5}", item.Id, item.Version, item.PackageSource.Location, Destination, PackageSaveMode.Value,
                        ExcludeVersion ? "-ExcludeVersion" : ""))
                ) {
                foreach (var l in p.StandardOutput) {
                    if (String.IsNullOrEmpty(l)) {
                        continue;
                    }

                    Verbose("NuGet: {0}", l);
                    // Successfully installed 'ComicRack 0.9.162'.
                    if (l.Contains("Successfully installed")) {
                        result.GetOrAdd(InstallStatus.Successful, () => new List<PackageItem>()).Add(ParseOutputFull(item.PackageSource, item.Id, item.Version, l));
                        continue;
                    }
                    ;

                    if (l.Contains("already installed")) {
                        result.GetOrAdd(InstallStatus.AlreadyPresent, () => new List<PackageItem>()).Add(ParseOutputFull(item.PackageSource, item.Id, item.Version, l));
                        continue;
                    }

                    if (l.Contains("not installed")) {
                        result.GetOrAdd(InstallStatus.Failed, () => new List<PackageItem>()).Add(ParseOutputFull(item.PackageSource, item.Id, item.Version, l));
                        continue;
                    }
                }

                foreach (var l in p.StandardError.Where(l => !String.IsNullOrEmpty(l))) {
                    Warning("NuGet: {0}", l);
                }

                // if anything failed, this is a failure.
                // if we have a success message (and no failure), we'll count this as a success.
                result.Status = result.ContainsKey(InstallStatus.Failed) ? InstallStatus.Failed : result.ContainsKey(InstallStatus.Successful) ? InstallStatus.Successful : InstallStatus.AlreadyPresent;

                return result;
            }
        }

        internal IEnumerable<PackageItem> GetPackageByIdAndVersionSpec(string name, IVersionSpec versionSpec, bool allowUnlisted = false) {
            if (String.IsNullOrEmpty(name)) {
                return Enumerable.Empty<PackageItem>();
            }

            return SelectedSources.AsParallel().WithMergeOptions(ParallelMergeOptions.NotBuffered).SelectMany(source => {
                try {
                    var pkgs = source.Repository.FindPackages(name, versionSpec, AllowPrereleaseVersions, allowUnlisted);

                    /*
                // necessary?
                pkgs = from p in pkgs where p.IsLatestVersion select p;
                */

                    var pkgs2 = pkgs.ToArray();

                    return pkgs2.Select(pkg => new PackageItem {
                        Package = pkg,
                        PackageSource = source,
                        FastPath = MakeFastPath(source, pkg.Id, pkg.Version.ToString())
                    });
                } catch {
                    return new PackageItem[0];
                }
            }).OrderByDescending(each => each.Package.Version);
        }

        internal virtual bool PreInstall(PackageItem packageItem) {
            return true;
        }

        internal virtual bool PostInstall(PackageItem packageItem) {
            return true;
        }

        internal virtual bool PreUninstall(PackageItem packageItem) {
            return true;
        }

        internal virtual bool PostUninstall(PackageItem packageItem) {
            return true;
        }

        internal bool InstallSinglePackage(PackageItem packageItem) {
            // Get NuGet to install the Package
            PreInstall(packageItem);
            var results = NuGetInstall(packageItem);

            if (results.Status == InstallStatus.Successful) {
                foreach (var installedPackage in results[InstallStatus.Successful]) {
                    if( IsCanceled ){
                        // the caller has expressed that they are cancelling the install.
                        Verbose("Package installation cancelled");
                        // todo: we should probablty uninstall this package unless the user said leave broken stuff behind
                        return false;
                    }

                    // run any extra steps 
                    if (!PostInstall(installedPackage)) {
                        // package failed installation. uninstall it.
                        UninstallPackage(installedPackage);

                        return false;
                    }

                    YieldPackage(packageItem, packageItem.PackageSource.Name);
                    // yay!
                }
                return true;
            }

            if (results.Status == InstallStatus.AlreadyPresent) {
                // hmm Weird.
                Verbose("Skipped Package '{0} v{1}' already installed", packageItem.Id, packageItem.Version);
                return true;
            }

            Error(ErrorCategory.InvalidResult, packageItem.GetCanonicalId(this), MultiplePackagesInstalledExpectedOne, packageItem.GetCanonicalId(this));

            return false;
        }

        public bool IsReady(bool b) {
            return true;
        }

        internal bool UninstallPackage(PackageItem pkg) {
            var dir = pkg.InstalledDirectory;

            if (!String.IsNullOrEmpty(dir) && Directory.Exists(dir)) {
                if (PreUninstall(pkg)) {
                    ProviderServices.DeleteFolder(pkg.InstalledDirectory, this);
                }
                var result = PostUninstall(pkg);
                YieldPackage(pkg, pkg.Id);
                return result;
            }
            return true;
        }

        internal class PackagesEnumerable : IEnumerable<IPackage> {
            private IQueryable<IPackage> _packages;

            internal PackagesEnumerable(IQueryable<IPackage> packages) {
                _packages = packages;
            }

            public IEnumerator<IPackage> GetEnumerator() {
                return new PackageEnumerator(_packages);
            }

            IEnumerator IEnumerable.GetEnumerator() {
                return GetEnumerator();
            }
        }

        internal class PackageEnumerator : IEnumerator<IPackage> {
            private bool _done;
            private int _index;
            private Task<IPackage[]> _nextSet;
            private IQueryable<IPackage> _packages;
            private IPackage[] _page;
            private int _resultIndex;

            internal PackageEnumerator(IQueryable<IPackage> packages) {
                _packages = packages;
                Reset();
                PullNextSet();
            }

            public void Dispose() {
                _done = true;
            }

            public bool MoveNext() {
                _index++;

                if (_index >= _page.Length && !_done) {
                    _index = 0;
                    // _page = _packages.Skip(_resultIndex).Take(40).ToArray();
                    _page = _nextSet.Result;
                    _resultIndex += _page.Length;
                    if (_page.Length == 0) {
                        _done = true;
                    } else {
                        PullNextSet();
                    }
                }

                if (_index >= _page.Length) {
                    return false;
                }

                return true;
            }

            public void Reset() {
                _done = false;
                _page = new IPackage[0];
                ;
                _index = -1;
                _resultIndex = 0;
            }

            public IPackage Current {
                get {
                    return _page[_index];
                }
            }

            object IEnumerator.Current {
                get {
                    return Current;
                }
            }

            private void PullNextSet() {
                _nextSet = Task.Factory.StartNew(() => {return _packages.Skip(_resultIndex).Take(40).ToArray();});
            }
        }
    }

    internal enum InstallStatus {
        Unknown,
        Successful,
        Failed,
        AlreadyPresent
    }

    internal class InstallResult : Dictionary<InstallStatus, List<PackageItem>> {
        internal InstallStatus Status = InstallStatus.Unknown;
    }
}
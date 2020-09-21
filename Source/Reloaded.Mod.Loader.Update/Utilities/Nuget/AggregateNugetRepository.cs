﻿using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Protocol.Core.Types;
using Reloaded.Mod.Loader.Update.Utilities.Nuget.Interfaces;
using Reloaded.Mod.Loader.Update.Utilities.Nuget.Structs;

namespace Reloaded.Mod.Loader.Update.Utilities.Nuget
{
    /// <summary>
    /// Helper class which wraps multiple <see cref="NugetRepository"/>(ies) to make it easier to interact with multiple NuGet sources.
    /// </summary>
    public class AggregateNugetRepository
    {
        public AggregateNugetRepository(INugetRepository[] sources) => Sources = sources.ToList();
        public AggregateNugetRepository(string[] sources) => Sources = sources.Select(x => (INugetRepository)NugetRepository.FromSourceUrlAsync(x).Result).ToList();
        public AggregateNugetRepository() { }

        /// <summary>
        /// All the sources for the helper.
        /// </summary>
        public List<INugetRepository> Sources { get; } = new List<INugetRepository>();

        /// <summary>
        /// Searches for packages using a specific term in all sources.
        /// </summary>
        /// <param name="searchString">The term to search packages using.</param>
        /// <param name="includePrereleases">True if to include prerelease packages, else false.</param>
        /// <param name="results">The max number of results to return from each source.</param>
        /// <param name="token">A cancellation token to allow cancellation of the task.</param>
        /// <returns>Tuples of the originating source and the search result.</returns>
        public async Task<List<NugetTuple<IEnumerable<IPackageSearchMetadata>>>> Search(string searchString, bool includePrereleases, int results = 50, CancellationToken token = default)
        {
            var result = new List<NugetTuple<IEnumerable<IPackageSearchMetadata>>>();
            foreach (var source in Sources)
            {
                var res = await source.Search(searchString, includePrereleases, results, token);
                result.Add(new NugetTuple<IEnumerable<IPackageSearchMetadata>>(source, res));
            }
            
            return result;
        }

        /// <summary>
        /// Tries to retrieve the package from all sources, returns tuples of package versions and source.
        /// </summary>
        /// <param name="includeUnlisted">Include unlisted packages.</param>
        /// <param name="packageId">The unique ID of the package.</param>
        /// <param name="includePrerelease">Include pre-release packages.</param>
        /// <param name="token">A cancellation token to allow cancellation of the task.</param>
        ///  <returns>Tuples of the originating source and the available versions.</returns>
        public async Task<List<NugetTuple<IEnumerable<IPackageSearchMetadata>>>> GetPackageDetails(string packageId, bool includePrerelease, bool includeUnlisted, CancellationToken token = default)
        {
            var result = new List<NugetTuple<IEnumerable<IPackageSearchMetadata>>>();
            foreach (var source in Sources)
            {
                var res = await source.GetPackageDetails(packageId, includePrerelease, includeUnlisted, token);
                result.Add(new NugetTuple<IEnumerable<IPackageSearchMetadata>>(source, res));
            }

            return result;
        }

        /// <summary>
        /// Retrieves the source and list of versions for the source with the highest version of the package.
        /// </summary>
        /// <param name="includeUnlisted">Include unlisted packages.</param>
        /// <param name="packageId">The unique ID of the package.</param>
        /// <param name="includePrerelease">Include pre-release packages.</param>
        /// <param name="token">A cancellation token to allow cancellation of the task.</param>
        /// <returns>Source with the latest version of the package and list of versions.</returns>
        public async Task<NugetTuple<IPackageSearchMetadata>> GetLatestPackageDetails(string packageId, bool includePrerelease, bool includeUnlisted, CancellationToken token = default)
        {
            var allPackageDetails = await GetPackageDetails(packageId, includePrerelease, includeUnlisted, token);
            return GetNewestPackage(allPackageDetails);
        }

        /// <summary>
        /// Retrieves the details of an individual package from the source with the latest version of the package.
        /// </summary>
        /// <param name="values">List of tuples of all sources and their packages where packages are ordered oldest to newest.</param>
        /// <returns>Source with the latest version of the package and list of versions.</returns>
        public NugetTuple<IPackageSearchMetadata> GetNewestPackage(List<NugetTuple<IEnumerable<IPackageSearchMetadata>>> values)
        {
            NugetTuple<IPackageSearchMetadata> newestTuple = null;

            foreach (var value in values)
            {
                if (value.Repository == null || value.Generic == null)
                    continue;

                // Has Versions
                var lastVersionMetadata = Nuget.GetNewestVersion(value.Generic);
                if (lastVersionMetadata == null)
                    continue;

                var version = lastVersionMetadata.Identity.Version;
                if (newestTuple != null && version <= newestTuple.Generic.Identity.Version) 
                    continue;

                newestTuple   = new NugetTuple<IPackageSearchMetadata>(value.Repository, lastVersionMetadata);
            }

            return newestTuple;
        }

        /// <summary>
        /// Finds all of the dependencies of a given package, including dependencies not available in the source repositories.
        /// </summary>
        /// <param name="packageId">The package Id for which to obtain the dependencies for.</param>
        /// <param name="includeUnlisted">Include unlisted packages.</param>
        /// <param name="includePrerelease">Include pre-release packages.</param>
        /// <param name="token">A cancellation token to allow cancellation of the task.</param>
        public async Task<AggregateFindDependenciesResult> FindDependencies(string packageId, bool includePrerelease, bool includeUnlisted, CancellationToken token = default)
        {
            var packages = await GetPackageDetails(packageId, includePrerelease, includeUnlisted, token);
            return await FindDependencies(GetNewestPackage(packages).Generic, includePrerelease, includeUnlisted, token);
        }

        /// <summary>
        /// Finds all of the dependencies of a given package, including dependencies not available in the source repositories.
        /// </summary>
        /// <param name="packageSearchMetadata">The package for which to obtain the dependencies for.</param>
        /// <param name="includeUnlisted">Include unlisted packages.</param>
        /// <param name="includePrerelease">Include pre-release packages.</param>
        /// <param name="token">A cancellation token to allow cancellation of the task.</param>
        public async Task<AggregateFindDependenciesResult> FindDependencies(IPackageSearchMetadata packageSearchMetadata, bool includePrerelease, bool includeUnlisted, CancellationToken token = default)
        {
            var result = new AggregateFindDependenciesResult(new HashSet<NugetTuple<IPackageSearchMetadata>>(), new HashSet<string>()); 
            await FindDependenciesRecursiveAsync(packageSearchMetadata, includePrerelease, includeUnlisted, result.Dependencies, result.PackagesNotFound, token);
            return result;
        }

        /// <summary>
        /// Finds all of the dependencies of a given package, including dependencies not available in the target repository.
        /// </summary>
        /// <param name="packageSearchMetadata">The package for which to obtain the dependencies for.</param>
        /// <param name="includeUnlisted">Include unlisted packages.</param>
        /// <param name="dependenciesAccumulator">A set which will contain all packages that are dependencies of the current package.</param>
        /// <param name="packagesNotFoundAccumulator">A set which will contain all dependencies of the package that were not found in the NuGet feed.</param>
        /// <param name="includePrerelease">Include pre-release packages.</param>
        /// <param name="token">A cancellation token to allow cancellation of the task.</param>
        private async Task FindDependenciesRecursiveAsync(IPackageSearchMetadata packageSearchMetadata, bool includePrerelease, bool includeUnlisted, HashSet<NugetTuple<IPackageSearchMetadata>> dependenciesAccumulator, HashSet<string> packagesNotFoundAccumulator, CancellationToken token = default)
        {
            // Check if package metadata resolved or has dependencies.
            if (packageSearchMetadata?.DependencySets == null)
                return;

            // Go over all agnostic dependency sets.
            foreach (var dependencySet in packageSearchMetadata.DependencySets)
            {
                foreach (var package in dependencySet.Packages)
                {
                    var metadata = (await GetPackageDetails(package.Id, includePrerelease, includeUnlisted, token));
                    if (metadata.Any(x => x.Generic.Any()))
                    {
                        var latest = GetNewestPackage(metadata);
                        if (dependenciesAccumulator.Contains(latest))
                            continue;

                        dependenciesAccumulator.Add(latest);
                        await FindDependenciesRecursiveAsync(latest.Generic, includePrerelease, includeUnlisted, dependenciesAccumulator, packagesNotFoundAccumulator, token);
                    }
                    else
                    {
                        packagesNotFoundAccumulator.Add(package.Id);
                    }
                }
            }
        }
    }
}

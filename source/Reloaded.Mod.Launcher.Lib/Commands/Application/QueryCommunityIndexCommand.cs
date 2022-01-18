﻿using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Reloaded.Mod.Launcher.Lib.Models.ViewModel.Dialog;
using Reloaded.Mod.Launcher.Lib.Static;
using Reloaded.Mod.Launcher.Lib.Utility;
using Reloaded.Mod.Loader.Community;
using Reloaded.Mod.Loader.Community.Config;
using Reloaded.Mod.Loader.Community.Utility;
using Reloaded.Mod.Loader.IO.Config;
using Reloaded.Mod.Loader.IO.Structs;
using Reloaded.Mod.Loader.Update.Interfaces;
using Reloaded.Mod.Loader.Update.Providers.GameBanana;
using Sewer56.Update.Misc;
using Standart.Hash.xxHash;

namespace Reloaded.Mod.Launcher.Lib.Commands.Application;

/// <summary>
///  queries the community index.
/// </summary>
public class QueryCommunityIndexCommand : ICommand
{
    private readonly PathTuple<ApplicationConfig> _application;

    /// <summary/>
    public QueryCommunityIndexCommand(PathTuple<ApplicationConfig> application)
    {
        _application = application;
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => File.Exists(_application.Config.AppLocation);

    /// <inheritdoc />
    public async void Execute(object? parameter) => await ExecuteAsync();

    /// <summary>
    /// Executes the task asynchronously.
    /// </summary>
    /// <returns></returns>
    public async Task ExecuteAsync()
    {
        var config = _application.Config;
        await using var fileStream = new FileStream(config.AppLocation, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 524288);
        var indexApi  = new IndexApi();
        var index = await indexApi.GetIndexAsync();
        var hash = Hashing.ToString(await xxHash64.ComputeHashAsync(fileStream));
        var applications = index.FindApplication(hash, config.AppId, out bool hashMatches);

        await await ActionWrappers.ExecuteWithApplicationDispatcherAsync(async () =>
        {
            if (applications.Count == 1 && hashMatches)
            {
                ApplyIndexEntry(await indexApi.GetApplicationAsync(applications[0]), _application, hash);
            }
            else if (applications.Count >= 1)
            {
                // Select application.
                var viewModel = new SelectAddedGameDialogViewModel(applications);
                var result = Actions.ShowSelectAddedGameDialog(viewModel);
                if (result == null)
                    return;

                ApplyIndexEntry(await indexApi.GetApplicationAsync(result), _application, hash);
            }
        });
    }

    /// <summary>
    /// Applies an individual application item to a given application configuration.
    /// </summary>
    /// <param name="indexApp">Index entry for the application.</param>
    /// <param name="pathTuple">The mod to apply the config to.</param>
    public static void ApplyIndexEntry(AppItem indexApp, PathTuple<ApplicationConfig> pathTuple)
    {
        using var fileStream = new FileStream(pathTuple.Config.AppLocation, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 524288);
        var hash = Hashing.ToString(xxHash64.ComputeHash(fileStream));
        ApplyIndexEntry(indexApp, pathTuple, hash);
    }

    /// <summary>
    /// Applies an individual application item to a given application configuration.
    /// </summary>
    /// <param name="indexApp">Index entry for the application.</param>
    /// <param name="pathTuple">The mod to apply the config to.</param>
    /// <param name="hash">Hash of the application's main executable.</param>
    public static void ApplyIndexEntry(AppItem indexApp, PathTuple<ApplicationConfig> pathTuple, string hash)
    {
        var hashMatches = hash.Equals(indexApp.Hash, StringComparison.OrdinalIgnoreCase);
        if (indexApp.AppStatus == Status.WrongExecutable && hashMatches)
            Actions.DisplayMessagebox(Resources.AddAppRepoBadExecutable.Get(), indexApp.BadStatusDescription!);

        pathTuple.Config.AppName = indexApp.AppName;
        // Apply GB Configurations
        Singleton<GameBananaPackageProviderFactory>.Instance.SetConfiguration(pathTuple,
            new GameBananaPackageProviderFactory.GameBananaProviderConfig()
            {
                GameId = (int)indexApp.GameBananaId
            });

        if (!hashMatches)
        {
            var viewModel = new AddAppHashMismatchDialogViewModel(indexApp.BadHashDescription!);
            Actions.ShowAddAppHashMismatchDialog(viewModel);
        }

        if (indexApp.TryGetError(Path.GetDirectoryName(pathTuple.Config.AppLocation)!, out var errors))
        {
            var viewModel = new AddApplicationWarningDialogViewModel(errors);
            Actions.ShowApplicationWarningDialog(viewModel);
        }
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;
}
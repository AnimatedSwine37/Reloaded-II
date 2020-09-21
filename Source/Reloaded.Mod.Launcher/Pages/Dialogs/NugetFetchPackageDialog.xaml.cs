﻿using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using NuGet.Protocol.Core.Types;
using Reloaded.Mod.Launcher.Models.Model.DownloadModsPage;
using Reloaded.Mod.Loader.IO.Config;
using Reloaded.Mod.Loader.Update.Utilities.Nuget;
using Reloaded.Mod.Loader.Update.Utilities.Nuget.Structs;
using Reloaded.WPF.MVVM;
using Reloaded.WPF.Theme.Default;

namespace Reloaded.Mod.Launcher.Pages.Dialogs
{
    /// <summary>
    /// Interaction logic for NugetFetchPackageDialog.xaml
    /// </summary>
    public partial class NugetFetchPackageDialog : ReloadedWindow
    {
        public new NugetFetchPackageDialogViewModel ViewModel { get; set; }

        public NugetFetchPackageDialog(List<NugetTuple<IPackageSearchMetadata>> packages, List<string> missingPackages)
        {
            InitializeComponent();
            ViewModel = new NugetFetchPackageDialogViewModel(packages, missingPackages);
        }

        private async void OK_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.DownloadEnabled = false;
            ViewModel.DownloadModStatus = DownloadModStatus.Downloading;

            var loaderConfig = IoC.Get<LoaderConfig>();
            foreach (var package in ViewModel.Packages)
            {
                var downloadPackage = await package.Repository.DownloadPackageAsync(package.Generic, CancellationToken.None);
                Nuget.ExtractPackage(downloadPackage, Path.Combine(loaderConfig.ModConfigDirectory, package.Generic.Identity.Id));
            }

            ViewModel.DownloadModStatus = DownloadModStatus.Default;
            ViewModel.DownloadEnabled = false;

            this.Close();
        }
    }

    public class NugetFetchPackageDialogViewModel : ObservableObject
    {
        public List<NugetTuple<IPackageSearchMetadata>> Packages { get; set; }
        public List<string>                 MissingPackages { get; set; }
        public Visibility                   ShowMissingPackages { get; set; } = Visibility.Hidden;
        public DownloadModStatus            DownloadModStatus { get; set; } = DownloadModStatus.Default;
        public bool                         DownloadEnabled { get; set; } = true;     

        public NugetFetchPackageDialogViewModel(List<NugetTuple<IPackageSearchMetadata>> packages, List<string> missingPackages)
        {
            Packages = packages;
            MissingPackages = missingPackages;

            if (missingPackages.Count > 0)
                ShowMissingPackages = Visibility.Visible;
            else
                ShowMissingPackages = Visibility.Collapsed;
        }
    }
}

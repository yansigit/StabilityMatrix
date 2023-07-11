﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AvaloniaEdit.Utils;
using Microsoft.Extensions.DependencyInjection;
using StabilityMatrix.Avalonia.Services;
using StabilityMatrix.Avalonia.ViewModels;
using StabilityMatrix.Avalonia.ViewModels.Dialogs;
using StabilityMatrix.Core.Api;
using StabilityMatrix.Core.Database;
using StabilityMatrix.Core.Helper;
using StabilityMatrix.Core.Helper.Cache;
using StabilityMatrix.Core.Helper.Factory;
using StabilityMatrix.Core.Models;
using StabilityMatrix.Core.Models.Api;
using StabilityMatrix.Core.Models.Packages;
using StabilityMatrix.Core.Python;
using StabilityMatrix.Core.Services;

namespace StabilityMatrix.Avalonia.DesignData;

public static class DesignData
{
    private static IServiceProvider Services { get; }

    static DesignData()
    {
        var services = new ServiceCollection();

        var activePackageId = new Guid();
        services.AddSingleton<ISettingsManager, MockSettingsManager>(_ => new MockSettingsManager
        {
            Settings =
            {
                InstalledPackages = new List<InstalledPackage>
                {
                    new()
                    {
                        Id = activePackageId,
                        DisplayName = "My Installed Package",
                        PackageName = "stable-diffusion-webui",
                        PackageVersion = "v1.0.0",
                        LibraryPath = "Packages\\example-webui",
                        LastUpdateCheck = DateTimeOffset.Now
                    }
                },
                ActiveInstalledPackage = activePackageId
            }
        });
        
        // General services
        services.AddLogging();
        services.AddSingleton<IPackageFactory, PackageFactory>()
            .AddSingleton<INotificationService, MockNotificationService>()
            .AddSingleton<ISharedFolders, SharedFolders>()
            .AddSingleton<IDownloadService, MockDownloadService>()
            .AddSingleton<ModelFinder>();
        
        // Placeholder services that nobody should need during design time
        services
            .AddSingleton<IPyRunner>(_ => null!)
            .AddSingleton<ILiteDbContext>(_ => null!)
            .AddSingleton<ICivitApi>(_ => null!)
            .AddSingleton<IGithubApiCache>(_ => null!)
            .AddSingleton<IPrerequisiteHelper>(_ => null!);
        
        // Using some default service implementations from App
        App.ConfigurePackages(services);
        App.ConfigurePageViewModels(services);
        App.ConfigureDialogViewModels(services);
        App.ConfigureViews(services);
        
        Services = services.BuildServiceProvider();

        var dialogFactory = Services.GetRequiredService<ServiceManager<ViewModelBase>>();
        var settingsManager = Services.GetRequiredService<ISettingsManager>();
        var downloadService = Services.GetRequiredService<IDownloadService>();
        var modelFinder = Services.GetRequiredService<ModelFinder>();
        var notificationService = Services.GetRequiredService<INotificationService>();
        
        // Main window
        MainWindowViewModel = new MainWindowViewModel(settingsManager, dialogFactory)
        {
            Pages = new List<PageViewModelBase>
            {
                LaunchPageViewModel,
                PackageManagerViewModel,
                CheckpointBrowserViewModel
            },
            FooterPages = new List<PageViewModelBase>
            {
                SettingsViewModel
            }
        };
        
        // Sample data
        var sampleCivitVersions = new List<CivitModelVersion>
        {
            new()
            {
                Name = "BB95 Furry Mix",
                Description = "v1.0.0",
            }
        };
        
        // Sample data for dialogs
        SelectModelVersionViewModel.Versions = sampleCivitVersions;
        SelectModelVersionViewModel.SelectedVersion = sampleCivitVersions[0];
        
        // Checkpoints page
        CheckpointsPageViewModel.CheckpointFolders = new ObservableCollection<CheckpointFolder>
        {
            new(settingsManager, downloadService, modelFinder)
            {
                Title = "Lora",
                DirectoryPath = "Packages/lora",
                CheckpointFiles = new ObservableCollection<CheckpointFile>
                {
                    new()
                    {
                        FilePath = "~/Models/Lora/electricity-light.safetensors",
                        Title = "Auroral Background",
                        ConnectedModel = new ConnectedModelInfo
                        {
                            VersionName = "Lightning Auroral",
                            BaseModel = "SD 1.5",
                            ModelName = "Auroral Background",
                            ModelType = CivitModelType.LORA,
                            FileMetadata = new CivitFileMetadata
                            {
                                Format = CivitModelFormat.SafeTensor,
                                Fp = CivitModelFpType.fp16,
                                Size = CivitModelSize.pruned,
                            }
                        }
                    },
                    new()
                    {
                        FilePath = "~/Models/Lora/model.safetensors",
                        Title = "Some model"
                    },
                }
            },
            new(settingsManager, downloadService, modelFinder)
            {
                Title = "VAE",
                DirectoryPath = "Packages/VAE",
                CheckpointFiles = new ObservableCollection<CheckpointFile>
                {
                    new()
                    {
                        FilePath = "~/Models/VAE/vae_v2.pt",
                        Title = "VAE v2",
                    }
                }
            }
        };

        CheckpointBrowserViewModel.ModelCards = new 
            ObservableCollection<CheckpointBrowserCardViewModel>
            {
                new(new CivitModel
                    {
                        Name = "BB95 Furry Mix",
                        Description = "A furry mix of BB95",
                    }, downloadService, settingsManager,
                    dialogFactory, notificationService)
            };
        
    }
    
    public static MainWindowViewModel MainWindowViewModel { get; }
    public static LaunchPageViewModel LaunchPageViewModel => Services.GetRequiredService<LaunchPageViewModel>();
    public static PackageManagerViewModel PackageManagerViewModel => Services.GetRequiredService<PackageManagerViewModel>();
    public static CheckpointsPageViewModel CheckpointsPageViewModel => Services.GetRequiredService<CheckpointsPageViewModel>();
    public static SettingsViewModel SettingsViewModel => Services.GetRequiredService<SettingsViewModel>();
    public static CheckpointBrowserViewModel CheckpointBrowserViewModel => Services.GetRequiredService<CheckpointBrowserViewModel>();
    public static SelectModelVersionViewModel SelectModelVersionViewModel => Services.GetRequiredService<SelectModelVersionViewModel>();
    public static OneClickInstallViewModel OneClickInstallViewModel => Services.GetRequiredService<OneClickInstallViewModel>();
    public static InstallerViewModel InstallerViewModel => Services.GetRequiredService<InstallerViewModel>();
}

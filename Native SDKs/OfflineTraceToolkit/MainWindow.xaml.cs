using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Esri.ArcGISRuntime;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Security;
using Esri.ArcGISRuntime.Tasks.Offline;
using Esri.ArcGISRuntime.Toolkit.UI.Controls;
using Esri.ArcGISRuntime.UtilityNetworks;

namespace OfflineTraceToolkit;

public partial class MainWindow : Window
{
    #region Private Members

    // A webmap with named traces and map area that is configured to trace utility network features offline.
    public const string WebmapURL =
        "https://sampleserver7.arcgisonline.com/portal/home/item.html?id=8eb86267776146d694792ce55a835afc";

    // Portal login credentials that can access the webmap.
    public const string PortalURL = "https://sampleserver7.arcgisonline.com/portal/sharing/rest";
    public const string Username = "editor01";
    public const string Password = "S7#i2LWmYH75";

    // For adding starting points.
    private ArcGISFeatureTable? _lineTable = null;
    private const string WhereClause =
        "ASSETID in ('Dstrbtn-Pp-16071', 'Dstrbtn-Pp-15862', 'Dstrbtn-Pp-15937')";

    #endregion Private Members

    public MainWindow()
    {
        InitializeComponent();

        UtilityNetworkTraceTool.UtilityNetworkTraceCompleted += OnTraceCompleted;
        UtilityNetworkTraceTool.UtilityNetworkChanged += OnUtilityNetworkChanged;

        // Loads an offline map
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            IsBusy.Visibility = Visibility.Visible;
            var downloadDirectoryPath = Path.Combine(Path.GetTempPath(), "offline_map");

            if (Directory.Exists(downloadDirectoryPath))
            {
                var mmpk = await MobileMapPackage.OpenAsync(downloadDirectoryPath);
                MyMapView.Map = mmpk.Maps.ElementAtOrDefault(0);
            }
            else
            {
                #region Authentication

                AuthenticationManager.Current.ChallengeHandler = new ChallengeHandler(
                    async (info) =>
                    {
                        var portalUri = new Uri(PortalURL);
                        if (
                            AuthenticationManager
                                .Current
                                .FindCredential(portalUri, AuthenticationType.Token)
                            is Credential credential
                        )
                        {
                            return credential;
                        }

                        credential = await AuthenticationManager
                            .Current
                            .GenerateCredentialAsync(portalUri, Username, Password);
                        AuthenticationManager.Current.AddCredential(credential);
                        return credential;
                    }
                );

                #endregion Authentication

                var task = await OfflineMapTask.CreateAsync(new Map(new Uri(WebmapURL)));

                #region Download Offline Map

                var mapAreas = await task.GetPreplannedMapAreasAsync();
                var mapArea = mapAreas.ElementAtOrDefault(0);
                ArgumentNullException.ThrowIfNull(mapArea);
                var parameters =
                    await task.CreateDefaultDownloadPreplannedOfflineMapParametersAsync(mapArea);
                var job = task.DownloadPreplannedOfflineMap(parameters, downloadDirectoryPath);
                var result = await job.GetResultAsync();

                #endregion Download Offline Map

                MyMapView.Map = result.OfflineMap;
            }
            Badge.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Loading map failed: {ex.Message}", ex.GetType().Name);
        }
        finally
        {
            IsBusy.Visibility = Visibility.Collapsed;
        }
    }

    private void OnTraceCompleted(object? sender, UtilityNetworkTraceCompletedEventArgs e)
    {
        Debug.WriteLine($"\nPARAMETERS:\n\tTraceType: {e.Parameters.TraceType}");

        if (e.Error is not null)
        {
            Debug.WriteLine($"\nERROR:\n\t{e.Error}");
        }
        else if (e.Results is not null)
        {
            foreach (var result in e.Results)
            {
                if (result.Warnings.Count > 0)
                {
                    Debug.WriteLine($"\nWARNINGS:\n\t{string.Join("\n", result.Warnings)}");
                }

                if (result is UtilityElementTraceResult elementResult)
                {
                    Debug.WriteLine(
                        $"\nELEMENT RESULT:\n\t{elementResult.Elements.Count} element(s) found."
                    );
                }
                else if (result is UtilityFunctionTraceResult functionResult)
                {
                    Debug.WriteLine(
                        $"\nFUNCTION RESULT:\n\t{functionResult.FunctionOutputs.Count} functions(s) reported."
                    );
                }
                else if (result is UtilityGeometryTraceResult geometryResult)
                {
                    Debug.WriteLine(
                        $"\nGEOMETRY RESULT: "
                            + $"\n\t{geometryResult.Multipoint?.Points.Count ?? 0} multipoint(s) found."
                            + $"\n\t{geometryResult.Polyline?.Parts?.Count ?? 0} polyline(s) found."
                            + $"\n\t{geometryResult.Polygon?.Parts?.Count ?? 0} polygon(s) found."
                    );
                }
            }
        }
    }

    private async void OnUtilityNetworkChanged(object? sender, UtilityNetworkChangedEventArgs e)
    {
        if (e.UtilityNetwork is null)
        {
            return;
        }

        if (e.UtilityNetwork.LoadStatus != LoadStatus.Loaded)
        {
            await e.UtilityNetwork.LoadAsync();
        }

        if (
            e.UtilityNetwork
                .Definition
                ?.NetworkSources
                .FirstOrDefault(ns => ns.SourceUsageType == UtilityNetworkSourceUsageType.Line)
            is not UtilityNetworkSource networkSource
        )
        {
            return;
        }

        _lineTable = networkSource.FeatureTable;
    }

    private async void OnAddStartingPoints(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lineTable is null || UtilityNetworkTraceTool is null)
            {
                return;
            }

            var features = await _lineTable.QueryFeaturesAsync(
                new QueryParameters() { WhereClause = WhereClause }
            );

            foreach (ArcGISFeature feature in features.Cast<ArcGISFeature>())
            {
                UtilityNetworkTraceTool.AddStartingPoint(feature, null);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, $"Add Starting Points failed: {ex.GetType().Name}");
        }
    }
}

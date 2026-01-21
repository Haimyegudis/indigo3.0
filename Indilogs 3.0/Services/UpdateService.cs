using System;
using System.Deployment.Application; // Ensure System.Deployment reference exists
using System.Threading.Tasks;
using System.Windows;

namespace IndiLogs_3._0.Services
{
    public class UpdateService
    {
        public async Task CheckForUpdatesSimpleAsync()
        {
            await Task.Run(() =>
            {
                System.Diagnostics.Debug.WriteLine("[UpdateService] Starting update check...");

                // Check if running as ClickOnce
                if (!ApplicationDeployment.IsNetworkDeployed)
                {
                    System.Diagnostics.Debug.WriteLine("[UpdateService] Not running as ClickOnce deployment - skipping update check");
                    return;
                }

                System.Diagnostics.Debug.WriteLine("[UpdateService] Running as ClickOnce deployment");

                try
                {
                    var deployment = ApplicationDeployment.CurrentDeployment;
                    System.Diagnostics.Debug.WriteLine($"[UpdateService] Current version: {deployment.CurrentVersion}");
                    System.Diagnostics.Debug.WriteLine($"[UpdateService] Update location: {deployment.UpdateLocation}");

                    // Check for update
                    System.Diagnostics.Debug.WriteLine("[UpdateService] Checking for updates...");
                    UpdateCheckInfo info = deployment.CheckForDetailedUpdate();
                    System.Diagnostics.Debug.WriteLine($"[UpdateService] Update available: {info.UpdateAvailable}");

                    if (info.UpdateAvailable)
                    {
                        System.Diagnostics.Debug.WriteLine($"[UpdateService] New version available: {info.AvailableVersion}");
                        System.Diagnostics.Debug.WriteLine($"[UpdateService] Is required: {info.IsUpdateRequired}");

                        // Back to UI thread
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var result = MessageBox.Show(
                                $"A new version is available: {info.AvailableVersion}\n" +
                                $"Current version: {deployment.CurrentVersion}\n\n" +
                                "Do you want to update now?",
                                "Update Available",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);

                            if (result == MessageBoxResult.Yes)
                            {
                                deployment.Update();
                                MessageBox.Show("The application has been updated and will now restart.");
                                System.Windows.Forms.Application.Restart();
                                Application.Current.Shutdown();
                            }
                        });
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[UpdateService] No update available - already at latest version");
                    }
                }
                catch (DeploymentDownloadException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateService] Download error: {ex.Message}");
                }
                catch (InvalidDeploymentException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateService] Invalid deployment: {ex.Message}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateService] Error checking for updates: {ex.GetType().Name}: {ex.Message}");
                }
            });
        }
    }
}
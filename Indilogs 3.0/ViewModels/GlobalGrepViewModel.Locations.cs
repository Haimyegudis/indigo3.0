using System.Threading.Tasks;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services.Interfaces;
using IndiLogs_3._0.Views;

namespace IndiLogs_3._0.ViewModels
{
    public partial class GlobalGrepViewModel
    {
        #region Location Management

        private void AddLocation()
        {
            var dialog = _viewFactory.Create<LocationDialog>("Add Search Location", "", "", "");
            if (dialog.ShowDialog() != true) return;

            var loc = new SearchLocation { Name = dialog.LocationName, Address = dialog.Address, BasePath = dialog.LocationPath };
            _locationService.Add(loc);
            Locations.Add(loc);
        }

        private void EditLocation()
        {
            if (SelectedLocation == null) return;
            var dialog = _viewFactory.Create<LocationDialog>("Edit Search Location", SelectedLocation.Name, SelectedLocation.Address, SelectedLocation.BasePath);
            if (dialog.ShowDialog() != true) return;

            SelectedLocation.Name = dialog.LocationName;
            SelectedLocation.Address = dialog.Address;
            SelectedLocation.BasePath = dialog.LocationPath;
            _locationService.Update(SelectedLocation);
        }

        private void RemoveLocation()
        {
            if (SelectedLocation == null) return;
            if (_dialogService.ShowConfirm($"Remove location '{SelectedLocation.Name}'?", "Confirm") == DialogResult.Yes)
            {
                _locationService.Remove(SelectedLocation.Id);
                Locations.Remove(SelectedLocation);
            }
        }

        private async Task TestLocationAsync()
        {
            if (SelectedLocation == null) return;
            StatusMessage = $"Testing connectivity to {SelectedLocation.Name}...";
            var status = await _locationService.TestConnectivityAsync(SelectedLocation);
            StatusMessage = $"{SelectedLocation.Name}: {status}";
        }

        private string? PromptInput(string title, string prompt, string defaultValue)
        {
            var dialog = _viewFactory.Create<InputDialog>(title, prompt, defaultValue);
            return dialog.ShowDialog() == true ? dialog.InputText : null;
        }

        #endregion
    }
}

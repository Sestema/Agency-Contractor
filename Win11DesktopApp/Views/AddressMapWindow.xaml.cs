using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Views
{
    public partial class AddressMapWindow : Window
    {
        private const string MapHtml = """
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8"/>
            <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css"/>
            <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
            <style>
              html, body, #map { height: 100%; margin: 0; }
              .leaflet-control-attribution { font-size: 11px; }
            </style>
            </head>
            <body>
            <div id="map"></div>
            <script>
              const map = L.map('map').setView([49.8, 15.5], 6);
              const streets = L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
                maxZoom: 19,
                attribution: '&copy; OpenStreetMap'
              }).addTo(map);
              const satelliteTileCache = new Map();
              function satelliteTileUrl(z, x, y) {
                return 'https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/' + z + '/' + y + '/' + x;
              }
              function loadSatelliteImage(z, x, y) {
                const key = z + '/' + x + '/' + y;
                if (!satelliteTileCache.has(key)) {
                  satelliteTileCache.set(key, new Promise((resolve, reject) => {
                    const img = new Image();
                    img.crossOrigin = 'anonymous';
                    img.onload = () => resolve(img);
                    img.onerror = () => reject(new Error('tile'));
                    img.src = satelliteTileUrl(z, x, y);
                  }));
                }
                return satelliteTileCache.get(key);
              }
              function isMissingSatelliteTile(img) {
                try {
                  const sample = document.createElement('canvas');
                  sample.width = img.width;
                  sample.height = img.height;
                  const ctx = sample.getContext('2d', { willReadFrequently: true });
                  ctx.drawImage(img, 0, 0);
                  const points = [[4, 4], [251, 4], [4, 251], [251, 251], [128, 6], [6, 128]];
                  let gray = 0;
                  for (const point of points) {
                    const pixel = ctx.getImageData(point[0], point[1], 1, 1).data;
                    const spread = Math.max(pixel[0], pixel[1], pixel[2]) - Math.min(pixel[0], pixel[1], pixel[2]);
                    if (spread < 8 && pixel[0] > 170 && pixel[0] < 220) gray++;
                  }
                  return gray >= 5;
                } catch (e) {
                  return false;
                }
              }
              async function resolveSatelliteTile(z, x, y, depth) {
                if (z < 1 || depth > 12) {
                  const img = await loadSatelliteImage(Math.max(z, 1), x, y);
                  return { img: img, sx: 0, sy: 0, sw: 256, sh: 256 };
                }
                let img = null;
                try { img = await loadSatelliteImage(z, x, y); } catch (e) { img = null; }
                if (img && !isMissingSatelliteTile(img)) {
                  return { img: img, sx: 0, sy: 0, sw: 256, sh: 256 };
                }
                const parentX = Math.floor(x / 2);
                const parentY = Math.floor(y / 2);
                const parent = await resolveSatelliteTile(z - 1, parentX, parentY, depth + 1);
                const sw = parent.sw / 2;
                const sh = parent.sh / 2;
                const quadX = ((x % 2) + 2) % 2;
                const quadY = ((y % 2) + 2) % 2;
                return {
                  img: parent.img,
                  sx: parent.sx + quadX * sw,
                  sy: parent.sy + quadY * sh,
                  sw: sw,
                  sh: sh
                };
              }
              const satellite = L.gridLayer({
                maxZoom: 19,
                attribution: 'Tiles &copy; Esri',
                tileSize: 256
              });
              satellite.createTile = function (coords) {
                const tile = document.createElement('canvas');
                const size = this.getTileSize();
                tile.width = size.x;
                tile.height = size.y;
                resolveSatelliteTile(coords.z, coords.x, coords.y, 0).then(source => {
                  const ctx = tile.getContext('2d');
                  ctx.imageSmoothingEnabled = true;
                  ctx.imageSmoothingQuality = 'high';
                  ctx.drawImage(source.img, source.sx, source.sy, source.sw, source.sh, 0, 0, size.x, size.y);
                }).catch(() => {});
                return tile;
              };
              function setBaseLayer(name) {
                const useSatellite = name === 'satellite';
                if (useSatellite) {
                  if (map.hasLayer(streets)) map.removeLayer(streets);
                  if (!map.hasLayer(satellite)) satellite.addTo(map);
                } else {
                  if (map.hasLayer(satellite)) map.removeLayer(satellite);
                  if (!map.hasLayer(streets)) streets.addTo(map);
                }
              }
              let marker = null;
              function setMarker(lat, lng, zoom) {
                const point = [lat, lng];
                if (!marker) marker = L.marker(point).addTo(map);
                else marker.setLatLng(point);
                map.setView(point, zoom || 17);
              }
              map.on('click', function (e) {
                if (window.chrome && window.chrome.webview)
                  window.chrome.webview.postMessage(JSON.stringify({ lat: e.latlng.lat, lng: e.latlng.lng }));
              });
            </script>
            </body>
            </html>
            """;

        private readonly List<MappedAddress> _results = new();
        private bool _mapReady;
        private bool _suppressSelection;
        private double? _pendingLat;
        private double? _pendingLng;
        private bool _satellite;

        public MappedAddress? SelectedAddress { get; private set; }

        public AddressMapWindow(string seedQuery)
        {
            InitializeComponent();
            SearchBox.Text = seedQuery ?? string.Empty;
            Loaded += async (_, _) => await StartAsync();
        }

        private async Task StartAsync()
        {
            try
            {
                var folder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AgencyContractor", "WebView2", "AddressMap");
                System.IO.Directory.CreateDirectory(folder);
                var env = await CoreWebView2Environment.CreateAsync(null, folder);
                await MapView.EnsureCoreWebView2Async(env);
                MapView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                MapView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                MapView.CoreWebView2.WebMessageReceived += MapView_WebMessageReceived;
                MapView.CoreWebView2.NavigationCompleted += async (_, args) =>
                {
                    _mapReady = args.IsSuccess;
                    MapStatus.Visibility = args.IsSuccess ? Visibility.Collapsed : Visibility.Visible;
                    if (!args.IsSuccess)
                        return;
                    await ApplyLayerAsync();
                    if (_pendingLat is double lat && _pendingLng is double lng)
                        await MoveMarkerAsync(lat, lng);
                };
                var pageFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AgencyContractor", "AddressMap");
                System.IO.Directory.CreateDirectory(pageFolder);
                System.IO.File.WriteAllText(System.IO.Path.Combine(pageFolder, "address-map.html"), MapHtml);
                MapView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "app-map.local",
                    pageFolder,
                    CoreWebView2HostResourceAccessKind.Allow);
                MapView.CoreWebView2.Navigate("https://app-map.local/address-map.html");
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AddressMapWindow.Start", ex.Message);
                MapView.Visibility = Visibility.Collapsed;
                MapStatus.Text = FindResource("MapOffline") as string ?? ex.Message;
                MapStatus.Visibility = Visibility.Visible;
            }

            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                await SearchAsync();
        }

        private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await SearchAsync();
            }
        }

        private async Task SearchAsync()
        {
            StatusText.Text = string.Empty;
            try
            {
                var found = await AddressMapLookup.SearchAsync(SearchBox.Text);
                ShowResults(found);
                if (found.Count == 0)
                    StatusText.Text = FindResource("MapNotFound") as string ?? string.Empty;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AddressMapWindow.Search", ex.Message);
                StatusText.Text = FindResource("MapOffline") as string ?? ex.Message;
            }
        }

        private void ShowResults(IReadOnlyList<MappedAddress> found)
        {
            _results.Clear();
            _results.AddRange(found);
            _suppressSelection = true;
            ResultsList.ItemsSource = null;
            ResultsList.ItemsSource = _results;
            _suppressSelection = false;
            if (_results.Count > 0)
                ResultsList.SelectedIndex = 0;
        }

        private async void ResultsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressSelection || ResultsList.SelectedItem is not MappedAddress address)
                return;
            ApplySelection(address);
            await MoveMarkerAsync(address.Latitude, address.Longitude);
        }

        private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultsList.SelectedItem is MappedAddress)
                Apply_Click(sender, e);
        }

        private async void MapView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
                var root = doc.RootElement;
                var lat = root.GetProperty("lat").GetDouble();
                var lng = root.GetProperty("lng").GetDouble();
                var address = await AddressMapLookup.ReverseAsync(lat, lng);
                if (address == null)
                {
                    StatusText.Text = FindResource("MapNotFound") as string ?? string.Empty;
                    return;
                }

                ShowResults(new[] { address });
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AddressMapWindow.Pin", ex.Message);
                StatusText.Text = FindResource("MapOffline") as string ?? ex.Message;
            }
        }

        private void ApplySelection(MappedAddress address)
        {
            SelectedAddress = address;
            ApplyButton.IsEnabled = !string.IsNullOrWhiteSpace(address.Street)
                || !string.IsNullOrWhiteSpace(address.City);
            StatusText.Text = address.Label;
        }

        private async void Streets_Click(object sender, RoutedEventArgs e)
        {
            _satellite = false;
            SetLayerButtons();
            await ApplyLayerAsync();
        }

        private async void Satellite_Click(object sender, RoutedEventArgs e)
        {
            _satellite = true;
            SetLayerButtons();
            await ApplyLayerAsync();
        }

        private void SetLayerButtons()
        {
            StreetsButton.Style = (Style)FindResource(_satellite ? "HeaderGhostButton" : "FilledActionButton");
            SatelliteButton.Style = (Style)FindResource(_satellite ? "FilledActionButton" : "HeaderGhostButton");
        }

        private async Task ApplyLayerAsync()
        {
            if (!_mapReady || MapView.CoreWebView2 == null)
                return;

            var name = _satellite ? "satellite" : "streets";
            await MapView.ExecuteScriptAsync($"setBaseLayer('{name}');");
        }

        private async Task MoveMarkerAsync(double latitude, double longitude)
        {
            _pendingLat = latitude;
            _pendingLng = longitude;
            if (!_mapReady || MapView.CoreWebView2 == null)
                return;

            var lat = latitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var lng = longitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await MapView.ExecuteScriptAsync($"setMarker({lat}, {lng}, 17);");
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedAddress == null)
                return;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}

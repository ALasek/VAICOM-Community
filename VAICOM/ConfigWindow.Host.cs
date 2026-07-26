using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VAICOM.Interfaces;

namespace VAICOM.UI
{
    public partial class ConfigWindow
    {
        private IStandaloneHostControl standaloneHostControl;
        private DispatcherTimer standaloneHostRefreshTimer;
        private bool updatingStandaloneHostUi;

        private void InitializeStandaloneHostTab()
        {
            standaloneHostControl = State.Proxy as IStandaloneHostControl;
            if (standaloneHostControl == null)
            {
                return;
            }

            StandaloneHostPage.Visibility = Visibility.Visible;
            RefreshStandaloneHostUi(true);

            standaloneHostRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            standaloneHostRefreshTimer.Tick += StandaloneHostRefreshTimer_Tick;
            standaloneHostRefreshTimer.Start();
            Closed += StandaloneHostWindow_Closed;
        }

        private void StandaloneHostWindow_Closed(object sender, EventArgs e)
        {
            if (standaloneHostRefreshTimer == null)
            {
                return;
            }

            standaloneHostRefreshTimer.Stop();
            standaloneHostRefreshTimer.Tick -= StandaloneHostRefreshTimer_Tick;
            standaloneHostRefreshTimer = null;
        }

        private void StandaloneHostRefreshTimer_Tick(object sender, EventArgs e)
        {
            RefreshStandaloneHostUi(false);
        }

        private void RefreshStandaloneHostUi(bool refreshMicrophones)
        {
            if (standaloneHostControl == null)
            {
                return;
            }

            try
            {
                var snapshot = standaloneHostControl.GetHostSnapshot();
                if (snapshot == null)
                {
                    ApplyStandaloneHostError("Host status is unavailable.");
                    return;
                }

                ApplyStandaloneHostSnapshot(snapshot);
                if (refreshMicrophones)
                {
                    RefreshStandaloneHostMicrophones(snapshot.MicrophoneDeviceNumber);
                    RefreshStandaloneSpeechBackends(snapshot.SpeechBackendId);
                }
            }
            catch (Exception ex)
            {
                ApplyStandaloneHostError(ex.Message);
            }
        }

        private void ApplyStandaloneHostSnapshot(StandaloneHostSnapshot snapshot)
        {
            HostStatusText.Text = ValueOrFallback(snapshot.Status, "Unavailable");
            HostDcsText.Text = snapshot.IsDcsConnected ? "Connected" : "Not connected";
            HostWhisperText.Text = ValueOrFallback(snapshot.WhisperRuntime, "Unavailable");
            HostTx1BindingText.Text = ValueOrFallback(snapshot.Tx1Binding, "Not assigned");
            HostTx2BindingText.Text = ValueOrFallback(snapshot.Tx2Binding, "Not assigned");
            HostTx1ConnectionText.Text = snapshot.Tx1Connected ? "Connected" : "Disconnected";
            HostTx2ConnectionText.Text = snapshot.Tx2Connected ? "Connected" : "Disconnected";
            HostErrorText.Text = snapshot.Error ?? string.Empty;
            HostCaptureText.Text = snapshot.CapturingTx > 0
                ? "Press a keyboard key or HOTAS button for TX" + snapshot.CapturingTx + "."
                : string.Empty;
            HostCancelCaptureButton.Visibility = snapshot.CapturingTx > 0 ? Visibility.Visible : Visibility.Collapsed;
            HostBindTx1Button.IsEnabled = snapshot.CapturingTx == 0;
            HostBindTx2Button.IsEnabled = snapshot.CapturingTx == 0;

            if (!updatingStandaloneHostUi && HostMicrophoneComboBox.SelectedValue is int selectedDevice &&
                selectedDevice != snapshot.MicrophoneDeviceNumber)
            {
                SelectStandaloneHostMicrophone(snapshot.MicrophoneDeviceNumber);
            }

            if (!updatingStandaloneHostUi && !Equals(HostSpeechBackendComboBox.SelectedValue, snapshot.SpeechBackendId))
            {
                SelectStandaloneSpeechBackend(snapshot.SpeechBackendId);
            }
        }

        private void RefreshStandaloneSpeechBackends(string selectedId)
        {
            try
            {
                updatingStandaloneHostUi = true;
                HostSpeechBackendComboBox.ItemsSource = standaloneHostControl.GetSpeechBackends()
                    ?? Enumerable.Empty<StandaloneSpeechBackendInfo>();
                SelectStandaloneSpeechBackend(selectedId);
            }
            finally
            {
                updatingStandaloneHostUi = false;
            }
        }

        private void SelectStandaloneSpeechBackend(string id)
        {
            HostSpeechBackendComboBox.SelectedItem = HostSpeechBackendComboBox.Items
                .OfType<StandaloneSpeechBackendInfo>()
                .FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private void HostSpeechBackendComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (updatingStandaloneHostUi || standaloneHostControl == null)
            {
                return;
            }

            var backend = HostSpeechBackendComboBox.SelectedItem as StandaloneSpeechBackendInfo;
            if (backend == null)
            {
                return;
            }

            try
            {
                standaloneHostControl.SelectSpeechBackend(backend.Id);
                RefreshStandaloneHostUi(false);
            }
            catch (Exception ex)
            {
                ApplyStandaloneHostError(ex.Message);
            }
        }

        private void RefreshStandaloneHostMicrophones(int selectedDeviceNumber)
        {
            try
            {
                IReadOnlyList<StandaloneMicrophoneInfo> microphones = standaloneHostControl.GetMicrophones();
                updatingStandaloneHostUi = true;
                HostMicrophoneComboBox.ItemsSource = microphones ?? Enumerable.Empty<StandaloneMicrophoneInfo>();
                SelectStandaloneHostMicrophone(selectedDeviceNumber);
            }
            catch (Exception ex)
            {
                ApplyStandaloneHostError(ex.Message);
            }
            finally
            {
                updatingStandaloneHostUi = false;
            }
        }

        private void SelectStandaloneHostMicrophone(int deviceNumber)
        {
            var microphone = HostMicrophoneComboBox.Items
                .OfType<StandaloneMicrophoneInfo>()
                .FirstOrDefault(item => item.DeviceNumber == deviceNumber);
            HostMicrophoneComboBox.SelectedItem = microphone;
            HostMicrophoneComboBox.ToolTip = microphone == null ? null : microphone.Name;
        }

        private void HostMicrophoneComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (updatingStandaloneHostUi || standaloneHostControl == null)
            {
                return;
            }

            var microphone = HostMicrophoneComboBox.SelectedItem as StandaloneMicrophoneInfo;
            if (microphone == null)
            {
                return;
            }

            try
            {
                standaloneHostControl.SelectMicrophone(microphone.DeviceNumber);
                RefreshStandaloneHostUi(false);
            }
            catch (Exception ex)
            {
                ApplyStandaloneHostError(ex.Message);
            }
        }

        private void RefreshStandaloneHostInputDevices(object sender, RoutedEventArgs e)
        {
            try
            {
                standaloneHostControl.RefreshInputDevices();
                RefreshStandaloneHostUi(true);
            }
            catch (Exception ex)
            {
                ApplyStandaloneHostError(ex.Message);
            }
        }

        private void BeginStandaloneHostPttBinding(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var tx = button == HostBindTx1Button ? 1 : 2;
            try
            {
                standaloneHostControl.BeginPttBindingCapture(tx);
                RefreshStandaloneHostUi(false);
            }
            catch (Exception ex)
            {
                ApplyStandaloneHostError(ex.Message);
            }
        }

        private void ClearStandaloneHostPttBinding(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var tx = button == HostClearTx1Button ? 1 : 2;
            try
            {
                standaloneHostControl.ClearPttBinding(tx);
                RefreshStandaloneHostUi(false);
            }
            catch (Exception ex)
            {
                ApplyStandaloneHostError(ex.Message);
            }
        }

        private void CancelStandaloneHostPttBinding(object sender, RoutedEventArgs e)
        {
            try
            {
                standaloneHostControl.CancelPttBindingCapture();
                RefreshStandaloneHostUi(false);
            }
            catch (Exception ex)
            {
                ApplyStandaloneHostError(ex.Message);
            }
        }

        private void ApplyStandaloneHostError(string error)
        {
            HostErrorText.Text = ValueOrFallback(error, "Host communication failed.");
        }

        private static string ValueOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }
}

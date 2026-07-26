using System;
using System.Threading;
using System.Windows.Forms;
using VAICOM.Client;
using VAICOM.Static;


namespace VAICOM
{

    namespace UI
    {

        public partial class Initialize
        {

            private sealed class ConfigWindowStartOptions
            {
                public ConfigWindowStartOptions(bool resetWindow, bool? useDarkMode)
                {
                    ResetWindow = resetWindow;
                    UseDarkMode = useDarkMode;
                }

                public bool ResetWindow { get; }
                public bool? UseDarkMode { get; }
            }

            public static void OpenConfiguration(dynamic vaProxy, bool resetwindow)
            {
                bool windowThreadAlive = State.configwindowthread != null && State.configwindowthread.IsAlive;
                if (!State.configwindowopen || (State.configurationwindow == null && !windowThreadAlive))
                {
                    try
                    {
                        State.configwindowopen = true;

                        vaProxy.WriteToLog("Opening Configuration window", Colors.Message);

                        bool? useDarkMode = null;
                        if (ConfigWindow.TryResolveVoiceAttackDarkMode(vaProxy, out bool resolvedDarkMode))
                        {
                            useDarkMode = resolvedDarkMode;
                        }

                        var startOptions = new ConfigWindowStartOptions(resetwindow, useDarkMode);

                        ParameterizedThreadStart newwindow = new ParameterizedThreadStart(StartConfigWindow);
                        State.configwindowthread = new Thread(newwindow);
                        State.configwindowthread.IsBackground = true;
                        State.configwindowthread.SetApartmentState(ApartmentState.STA);
                        State.configwindowthread.Start(startOptions);

                        UI.Playsound.Startup();

                        DcsClient.SendUpdateRequest();
                    }
                    catch (Exception a)
                    {
                        Log.Write("There was a problem opening the configuration window: " + a.Message, Colors.Text);
                    }
                }
                else
                {
                    if (State.configurationwindow != null)
                    {
                        State.configurationwindow.Dispatcher.BeginInvoke((MethodInvoker)delegate
                        {
                            if (resetwindow)
                            {
                                State.configurationwindow.Left = 20;
                                State.configurationwindow.Top = 20;
                            }
                            if (State.configurationwindow.WindowState == System.Windows.WindowState.Minimized)
                            {
                                State.configurationwindow.WindowState = System.Windows.WindowState.Normal;
                            }
                            State.configurationwindow.Show();
                            State.configurationwindow.Activate();
                            State.configurationwindow.Topmost = true;
                            State.configurationwindow.Topmost = false;
                            State.configurationwindow.Focus();
                        });
                    }
                }
            }


            public static void StartConfigWindow(Object resetwindow)
            {
                var startOptions = resetwindow as ConfigWindowStartOptions;
                bool shouldReset = startOptions?.ResetWindow ?? (resetwindow is bool reset && reset);
                bool? useDarkMode = startOptions?.UseDarkMode;

                State.configurationwindow = new ConfigWindow(useDarkMode);

                if (shouldReset)
                {
                    State.configurationwindow.Left = 20;
                    State.configurationwindow.Top = 20;
                }

                State.configurationwindow.ShowDialog();

            }

        }
    }
}

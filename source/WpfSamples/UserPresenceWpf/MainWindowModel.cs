//-----------------------------------------------------------------------
// Copyright 2014 Tobii Technology AB. All rights reserved.
//-----------------------------------------------------------------------

namespace UserPresenceWpf
{
    using System;
    using System.ComponentModel;
    using System.Windows;
    using EyeXFramework;
    using EyeXFramework.Wpf;
    using Tobii.EyeX.Framework;
    using System.Runtime.InteropServices;



    /// <summary>
    /// The MainWindowModel retrieves the UserPresence state from the WpfEyeXHost,
    /// and sets up a listener for changes to the state. It exposes a property
    /// ImageSource, which changes depending on the UserPresence state.
    /// </summary>
    public class MainWindowModel : INotifyPropertyChanged, IDisposable
    {
        private readonly WpfEyeXHost _eyeXHost;
        private string _imageSource;
        private bool _isUserPresent;
        private bool _isTrackingGaze;
        private bool _isTrackingGazeSupported;
        private bool _hasEyesClosed;
        private int _screenWidth;
        private int _screenHeight;

        private GazePointEventArgs _capturedOnClosed;
        private DateTime _timeClosed;
        private GazePointEventArgs _lastEvent;

        [DllImport("Magnification.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MagInitialize();

        [DllImport("Magnification.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MagUninitialize();

        [DllImport("Magnification.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MagGetFullscreenTransform(ref float magLevel, ref int xOffset, ref  int yOffset);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("USER32.DLL", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string lpClassName,
            string lpWindowName);

        // Activate an application window.
        [DllImport("USER32.DLL")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        public MainWindowModel()
        {
            IsUserPresent = false;
            IsTrackingGaze = false;
            IsTrackingGazeSupported = true;
            _hasEyesClosed = false;

            // Create and start the WpfEyeXHost. Starting the host means
            // that it will connect to the EyeX Engine and be ready to 
            // start receiving events and get the current values of
            // different engine states. In this sample we will be using
            // the UserPresence engine state.
            _eyeXHost = new WpfEyeXHost();

            // Register an status-changed event listener for UserPresence.
            // NOTE that the event listener must be unregistered too. This is taken care of in the Dispose(bool) method.
            _eyeXHost.UserPresenceChanged += EyeXHost_UserPresenceChanged; 
            _eyeXHost.GazeTrackingChanged += EyeXHost_GazeTrackingChanged;

            // 
            GazePointDataStream lightlyFilteredGazeDataStream = _eyeXHost.CreateGazePointDataStream(GazePointDataMode.LightlyFiltered);
            lightlyFilteredGazeDataStream.Next += (s, e) =>
            {
                //Console.WriteLine("Gaze point at ({0:0.0}, {1:0.0}) @{2:0}", e.X, e.Y, e.Timestamp);
                _lastEvent = e;
            };

            // TODO: make sure the app "magnifier" is running, or else the user has to restart pc if zoom breaks

            /*

Process p = Process.Start("notepad.exe");
p.WaitForInputIdle();
IntPtr h = p.MainWindowHandle;
SetForegroundWindow(h);
SendKeys.SendWait("k");
*/

            MagInitialize();

            const int SM_CXSCREEN = 0;
            const int SM_CYSCREEN = 1;

           _screenWidth = GetSystemMetrics(SM_CXSCREEN);
           _screenHeight = GetSystemMetrics(SM_CYSCREEN);
            Console.WriteLine("Screen {0}x{1}", _screenWidth, _screenHeight);
            // TODO maybe use virtual screen https://msdn.microsoft.com/en-us/library/windows/desktop/hh162714(v=vs.85).aspx

            // Start the EyeX host.
            _eyeXHost.Start();

            // Wait until we're connected.
            if (_eyeXHost.WaitUntilConnected(TimeSpan.FromSeconds(5)))
            {
                // Make sure the EyeX Engine version is equal to or greater than 1.4.
                var engineVersion = _eyeXHost.GetEngineVersion().Result;
                if (engineVersion.Major != 1 || engineVersion.Major == 1 && engineVersion.Minor < 4)
                {
                    IsTrackingGazeSupported = false;
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// A path to an image corresponding to the current UserPresence state.
        /// </summary>
        public string ImageSource
        {
            get { return _imageSource; }
        }

        /// <summary>
        /// Gets whether or not the user is present.
        /// </summary>
        public bool IsUserPresent
        {
            get { return _isUserPresent; }
            private set
            {
                _isUserPresent = value;
                _imageSource = _isUserPresent 
                    ? "/Images/present.png" 
                    : "/Images/not-present.png";

                // Notify of properties that have changed.
                OnPropertyChanged("IsUserPresent");
                OnPropertyChanged("ImageSource");
            }
        }

        /// <summary>
        /// Gets whether or not gaze is being tracked.
        /// </summary>
        public bool IsTrackingGaze
        {
            get { return _isTrackingGaze; }
            private set 
            {
                _isTrackingGaze = value;

                if (!_isTrackingGaze && !_hasEyesClosed)
                {
                    _timeClosed = DateTime.Now;
                    _hasEyesClosed = true;
                    Console.WriteLine("user closed eyes");
                    _capturedOnClosed = _lastEvent;                   
                }

                if (_isTrackingGaze && _hasEyesClosed)
                {
                    _hasEyesClosed = false;
                    DateTime openTime = DateTime.Now;
                    double delay = openTime.Subtract(_timeClosed).TotalMilliseconds;
                    if (delay > 400 && delay < 1500)
                    {
                        Console.WriteLine("move window to ");
                        Console.WriteLine("Gaze point at ({0:0.0}, {1:0.0}), {2:0}", _capturedOnClosed.X, _capturedOnClosed.Y, _capturedOnClosed.Timestamp);

                        // use Visual.PointToScreen if you want to convert to window coordinates

                        float magLevel = 0;
                        int xOffset = 0;
                        int yOffset = 0;

                        bool result = MagGetFullscreenTransform(ref magLevel, ref xOffset, ref yOffset);
                        if ( result && magLevel > 1)
                        {

                            // Calculate the rectangle which is magnified
                            Console.WriteLine("Current Transform ({0:0.0}, {1:0}, {2:0})", magLevel, xOffset, yOffset);
                            if (_capturedOnClosed.X < _screenWidth/2)
                            {
                                // move left ctrl alt left key
                                //https://msdn.microsoft.com/en-us/library/ms171548(v=vs.110).aspx
                                //SendKeys.SendWait("{ENTER}");
                                System.Windows.Forms.Sendkeys.Sendwait("<Message>");
                                Console.WriteLine("move left");
                            } else
                            {
                                Console.WriteLine("move right");
                                // move right

                            }
                           
                        }

                        



                    }

                    Console.WriteLine("user closed eyes for {0}", delay);
                }


                OnPropertyChanged("IsTrackingGaze");
            }
        }

        public bool IsTrackingGazeSupported
        {
            get { return _isTrackingGazeSupported; }
            set
            {

                _isTrackingGazeSupported = value;

                OnPropertyChanged("IsTrackingGazeSupported");
                OnPropertyChanged("IsTrackingGazeNotSupported");
            }
        }

        public bool IsTrackingGazeNotSupported
        {
            get { return !IsTrackingGazeSupported; }
        }

        /// <summary>
        /// Cleans up any resources being used.
        /// </summary>
        public void Dispose()
        {
            MagUninitialize();
            _eyeXHost.UserPresenceChanged -= EyeXHost_UserPresenceChanged;
            _eyeXHost.GazeTrackingChanged -= EyeXHost_GazeTrackingChanged;
            _eyeXHost.Dispose();
        }

        private void EyeXHost_UserPresenceChanged(object sender, EngineStateValue<UserPresence> value)
        {
            // State-changed events are received on a background thread.
            // But operations that affect the GUI must be executed on the main thread.
            RunOnMainThread(() =>
            {
                IsUserPresent = value.IsValid && value.Value == UserPresence.Present;
            });
        }

        private void EyeXHost_GazeTrackingChanged(object sender, EngineStateValue<GazeTracking> value)
        {
            // State-changed events are received on a background thread.
            // But operations that affect the GUI must be executed on the main thread.
            RunOnMainThread(() =>
            {
                IsTrackingGaze = value.IsValid && value.Value == GazeTracking.GazeTracked;
            });
        }

        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }

        /// <summary>
        /// Marshals the given operation to the UI thread.
        /// </summary>
        /// <param name="action">The operation to be performed.</param>
        private static void RunOnMainThread(Action action)
        {
            if (Application.Current != null)
            {
                Application.Current.Dispatcher.BeginInvoke(action);
            }
        }

    }
}

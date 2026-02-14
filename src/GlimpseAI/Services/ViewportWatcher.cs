using System;
using System.Threading;
using Rhino.Display;

namespace GlimpseAI.Services;

/// <summary>
/// Event args raised when the viewport camera changes.
/// </summary>
public class ViewportChangedEventArgs : EventArgs
{
    /// <summary>Name of the viewport that changed.</summary>
    public string ViewportName { get; set; }

    /// <summary>Current camera position.</summary>
    public Rhino.Geometry.Point3d CameraPosition { get; set; }

    /// <summary>Current camera target.</summary>
    public Rhino.Geometry.Point3d CameraTarget { get; set; }
}

/// <summary>
/// Watches for Rhino viewport camera changes with debouncing.
/// Fires <see cref="ViewportChanged"/> after the user stops moving the camera.
/// </summary>
public class ViewportWatcher : IDisposable
{
    /// <summary>
    /// Raised when the viewport camera has changed (after debounce).
    /// </summary>
    public event EventHandler<ViewportChangedEventArgs> ViewportChanged;

    private Timer _debounceTimer;
    private int _debounceMs;
    private bool _isEnabled;
    private bool _disposed;

    // Last known camera state for change detection
    private Rhino.Geometry.Point3d _lastCameraPosition;
    private Rhino.Geometry.Point3d _lastCameraTarget;

    // Threshold for considering a camera "moved" (in model units)
    private const double CameraMovementThreshold = 0.001;

    public ViewportWatcher(int debounceMs = 300)
    {
        _debounceMs = debounceMs;
        _lastCameraPosition = Rhino.Geometry.Point3d.Unset;
        _lastCameraTarget = Rhino.Geometry.Point3d.Unset;
    }

    /// <summary>
    /// Gets or sets whether the watcher is enabled.
    /// When disabled, viewport changes are ignored.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;

            if (_isEnabled)
                Start();
            else
                Stop();
        }
    }

    /// <summary>
    /// Gets or sets the debounce delay in milliseconds.
    /// </summary>
    public int DebounceMs
    {
        get => _debounceMs;
        set => _debounceMs = Math.Max(50, value);
    }

    /// <summary>
    /// Starts listening for viewport modifications.
    /// </summary>
    public void Start()
    {
        _isEnabled = true;
        RhinoView.Modified += OnViewModified;
    }

    /// <summary>
    /// Stops listening for viewport modifications.
    /// </summary>
    public void Stop()
    {
        Timer timerToDispose = null;
        
        lock (this)
        {
            _isEnabled = false;
            timerToDispose = _debounceTimer;
            _debounceTimer = null;
        }
        
        // Unregister event handler
        try
        {
            RhinoView.Modified -= OnViewModified;
        }
        catch (Exception ex)
        {
            Rhino.RhinoApp.WriteLine($"Glimpse AI: Error unregistering view modified handler: {ex.Message}");
        }
        
        // Dispose timer outside lock
        timerToDispose?.Dispose();
    }

    /// <summary>
    /// Resets the last known camera state so the next change always fires.
    /// Thread-safe operation.
    /// </summary>
    public void Reset()
    {
        lock (this)
        {
            _lastCameraPosition = Rhino.Geometry.Point3d.Unset;
            _lastCameraTarget = Rhino.Geometry.Point3d.Unset;
        }
    }

    private void OnViewModified(object sender, ViewEventArgs e)
    {
        if (!_isEnabled || _disposed) return;

        try
        {
            var viewport = e.View?.ActiveViewport;
            if (viewport == null) return;

            var cameraPos = viewport.CameraLocation;
            var cameraTarget = viewport.CameraTarget;

            // Check if the camera actually moved (thread-safe)
            bool hasChanged;
            lock (this)
            {
                hasChanged = CameraHasChanged(cameraPos, cameraTarget);
            }
            
            if (!hasChanged) return;

            // Store pending state for debounce callback
            var viewportName = viewport.Name ?? "Unknown";

            // Thread-safe timer reset
            Timer oldTimer = null;
            lock (this)
            {
                if (_disposed) return;
                
                oldTimer = _debounceTimer;
                _debounceTimer = new Timer(
                    _ => OnDebounceElapsed(viewportName, cameraPos, cameraTarget),
                    null,
                    _debounceMs,
                    Timeout.Infinite);
            }
            
            // Dispose old timer outside lock to prevent potential deadlock
            oldTimer?.Dispose();
        }
        catch (Exception ex)
        {
            Rhino.RhinoApp.WriteLine($"Glimpse AI: View modified event error: {ex.Message}");
        }
    }

    private void OnDebounceElapsed(string viewportName, Rhino.Geometry.Point3d cameraPos, Rhino.Geometry.Point3d cameraTarget)
    {
        if (!_isEnabled || _disposed) return;

        try
        {
            // Update last known state (thread-safe)
            lock (this)
            {
                _lastCameraPosition = cameraPos;
                _lastCameraTarget = cameraTarget;
            }

            // Marshal event invocation to UI thread to prevent cross-thread operations
            // ViewportChanged handlers may need to access UI controls
            Rhino.RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                try
                {
                    // Double-check state in case watcher was disposed while marshalling
                    if (!_isEnabled || _disposed) return;

                    ViewportChanged?.Invoke(this, new ViewportChangedEventArgs
                    {
                        ViewportName = viewportName,
                        CameraPosition = cameraPos,
                        CameraTarget = cameraTarget
                    });
                }
                catch (Exception ex)
                {
                    Rhino.RhinoApp.WriteLine($"Glimpse AI: Viewport changed event error: {ex.Message}");
                }
            }));
        }
        catch (Exception ex)
        {
            Rhino.RhinoApp.WriteLine($"Glimpse AI: Debounce timer error: {ex.Message}");
        }
    }

    private bool CameraHasChanged(Rhino.Geometry.Point3d newPos, Rhino.Geometry.Point3d newTarget)
    {
        if (_lastCameraPosition == Rhino.Geometry.Point3d.Unset)
            return true;

        var posDelta = newPos.DistanceTo(_lastCameraPosition);
        var targetDelta = newTarget.DistanceTo(_lastCameraTarget);

        return posDelta > CameraMovementThreshold || targetDelta > CameraMovementThreshold;
    }

    public void Dispose()
    {
        bool wasDisposed;
        lock (this)
        {
            wasDisposed = _disposed;
            _disposed = true;
        }
        
        if (!wasDisposed)
        {
            Stop();
        }
    }
}

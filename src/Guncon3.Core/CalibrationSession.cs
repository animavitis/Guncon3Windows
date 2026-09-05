// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;

namespace Guncon3.Core
{
    public enum CalibrationPhase
    {
        /// <summary>Shooting the five targets.</summary>
        Capturing,
        /// <summary>All five captured; aiming through the candidate to judge it.</summary>
        Checking
    }

    /// <summary>
    /// The calibration window's state, kept free of any UI so it can be tested: which
    /// screen is being calibrated, which target is next, the points shot so far, and
    /// once all five are in, the candidate calibration under review. Nothing is written
    /// to disk here; <see cref="Accept"/> hands the candidate back and the caller
    /// decides.
    /// </summary>
    public sealed class CalibrationSession
    {
        /// <summary>
        /// The targets in capture order, normalized to the screen: top-left, top-right,
        /// bottom-right, bottom-left, centre.
        /// </summary>
        public static IReadOnlyList<(double X, double Y)> Targets { get; } = new[]
        {
            (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0), (0.5, 0.5)
        };

        private readonly IReadOnlyList<ScreenPlacement> _screens;
        private readonly List<(double X, double Y)> _points = new();
        private readonly IReadOnlyList<(double X, double Y)> _pointsView;

        public CalibrationPhase Phase { get; private set; } = CalibrationPhase.Capturing;

        public int ScreenIndex { get; private set; }
        public int ScreenCount => _screens.Count;
        public ScreenPlacement Screen => _screens[ScreenIndex];

        /// <summary>Index into <see cref="Targets"/> of the next point to shoot.</summary>
        public int NextTarget => Math.Min(_points.Count, Targets.Count - 1);

        public IReadOnlyList<(double X, double Y)> CapturedPoints => _pointsView;

        /// <summary>Which mapping the check phase aims through. Toggle to compare.</summary>
        public CalibrationMode Mode { get; private set; }

        /// <summary>The calibration under review. Null until all five points are captured.</summary>
        public CalibrationFile? Candidate { get; private set; }

        /// <summary>
        /// True when the last five shots were thrown away because they did not span a
        /// rectangle. Cleared by the next shot.
        /// </summary>
        public bool LastCaptureRejected { get; private set; }

        public CalibrationSession(IReadOnlyList<ScreenPlacement> screens, int initialScreen, CalibrationMode mode)
        {
            if (screens == null || screens.Count == 0)
                throw new ArgumentException("At least one screen is needed.", nameof(screens));

            _screens = screens;
            ScreenIndex = initialScreen >= 0 && initialScreen < screens.Count ? initialScreen : 0;
            Mode = mode;
            _pointsView = _points.AsReadOnly();
        }

        /// <summary>
        /// Records one shot. False when not capturing or a coordinate is not finite.
        /// The fifth point builds the candidate and moves to
        /// <see cref="CalibrationPhase.Checking"/>.
        /// </summary>
        public bool Capture(double rawX, double rawY)
        {
            if (Phase != CalibrationPhase.Capturing)
                return false;

            if (!double.IsFinite(rawX) || !double.IsFinite(rawY))
                return false;

            LastCaptureRejected = false;
            _points.Add((rawX, rawY));

            if (_points.Count == Targets.Count)
            {
                var candidate = CalibrationFile.FromCapture(_points, Screen);

                // Zero-width or zero-height box: Load would refuse the file, so letting
                // it be saved would replace a good calibration with none.
                if (!candidate.Rect.IsValid())
                {
                    Restart();
                    LastCaptureRejected = true;
                    return true;
                }

                Candidate = candidate;
                Phase = CalibrationPhase.Checking;
            }

            return true;
        }

        /// <summary>Throws away everything shot so far and starts on the first target again.</summary>
        public void Restart()
        {
            _points.Clear();
            Candidate = null;
            LastCaptureRejected = false;
            Phase = CalibrationPhase.Capturing;
        }

        /// <summary>
        /// Moves to another monitor, wrapping around. A capture belongs to one screen,
        /// so changing screen restarts it.
        /// </summary>
        public void SelectScreen(int delta)
        {
            if (_screens.Count < 2)
                return;

            ScreenIndex = ((ScreenIndex + delta) % _screens.Count + _screens.Count) % _screens.Count;
            Restart();
        }

        public void ToggleMode()
        {
            Mode = Mode == CalibrationMode.Rect ? CalibrationMode.Homography : CalibrationMode.Rect;
        }

        /// <summary>The candidate to save, or null when there is none yet.</summary>
        public CalibrationFile? Accept() => Phase == CalibrationPhase.Checking ? Candidate : null;

        /// <summary>
        /// Applies one user action under the phase rules. Returns true when the action
        /// was valid now. <see cref="CalibrationAction.Shoot"/> captures
        /// (<paramref name="rawX"/>, <paramref name="rawY"/>); the other actions ignore
        /// them. <see cref="CalibrationAction.Accept"/> only reports validity — the
        /// caller then takes the candidate from <see cref="Accept"/> and saves it.
        /// <see cref="CalibrationAction.Cancel"/> is always valid and leaves the session
        /// unchanged; closing is the caller's job.
        /// </summary>
        public bool Apply(CalibrationAction action, double rawX = double.NaN, double rawY = double.NaN)
        {
            switch (action)
            {
                case CalibrationAction.Shoot:
                    return Capture(rawX, rawY);

                case CalibrationAction.Restart:
                    Restart();
                    return true;

                case CalibrationAction.ToggleMode:
                    if (Phase != CalibrationPhase.Checking) return false;
                    ToggleMode();
                    return true;

                case CalibrationAction.Accept:
                    return Phase == CalibrationPhase.Checking;

                case CalibrationAction.PreviousScreen:
                case CalibrationAction.NextScreen:
                    if (_screens.Count < 2) return false;
                    SelectScreen(action == CalibrationAction.NextScreen ? +1 : -1);
                    return true;

                case CalibrationAction.Cancel:
                    return true;

                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, "not a CalibrationAction");
            }
        }
    }
}

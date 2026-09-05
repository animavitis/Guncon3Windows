// SPDX-License-Identifier: GPL-2.0-only
namespace Guncon3.Core
{
    /// <summary>Everything a user can do in the calibration window. <see cref="CalibrationSession.Apply"/>
    /// decides what each is allowed to do in the current phase.</summary>
    public enum CalibrationAction
    {
        /// <summary>Capture the current target (capturing phase only).</summary>
        Shoot,
        /// <summary>Throw the shots away and start on the first target (either phase).</summary>
        Restart,
        /// <summary>Switch the check phase between the linear and projective mapping (checking only).</summary>
        ToggleMode,
        /// <summary>Keep the candidate (checking only). The caller performs the save.</summary>
        Accept,
        /// <summary>Move to the previous monitor; restarts the capture (either phase).</summary>
        PreviousScreen,
        /// <summary>Move to the next monitor; restarts the capture (either phase).</summary>
        NextScreen,
        /// <summary>Leave without saving. Always valid; changes nothing here.</summary>
        Cancel
    }
}

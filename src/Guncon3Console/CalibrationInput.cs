// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Windows.Forms;
using Guncon3.Core;

namespace Guncon3Console
{
    /// <summary>
    /// What each key and gun button means in the calibration window. The README's key
    /// table is written from these two tables; change them together. D (show raw
    /// coordinates) is a view toggle on the form, not a session action.
    /// </summary>
    internal static class CalibrationInput
    {
        public static readonly IReadOnlyDictionary<Keys, CalibrationAction> KeyActions = new Dictionary<Keys, CalibrationAction>
        {
            [Keys.Space] = CalibrationAction.Shoot,
            [Keys.Back] = CalibrationAction.Restart,
            [Keys.H] = CalibrationAction.ToggleMode,
            [Keys.Enter] = CalibrationAction.Accept,
            [Keys.Left] = CalibrationAction.PreviousScreen,
            [Keys.Right] = CalibrationAction.NextScreen,
            [Keys.Escape] = CalibrationAction.Cancel,
        };

        public static readonly IReadOnlyDictionary<GunButton, CalibrationAction> ButtonActions = new Dictionary<GunButton, CalibrationAction>
        {
            [GunButton.Trigger] = CalibrationAction.Shoot,
            [GunButton.A1] = CalibrationAction.Restart,
            [GunButton.A2] = CalibrationAction.ToggleMode,
            [GunButton.C2] = CalibrationAction.Accept,
            [GunButton.B1] = CalibrationAction.PreviousScreen,
            [GunButton.B2] = CalibrationAction.NextScreen,
        };
    }
}

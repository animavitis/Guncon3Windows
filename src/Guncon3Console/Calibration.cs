using System;
using System.IO;

namespace Guncon3Console
{
    /// <summary>
    /// Compatibility stub: the real calibration is now handled by CalibrationFile
    /// (RectCalib + HomographyCalib) + CalibrationWindow.
    /// The 'Calibration' signature is kept so old calls do not break.
    /// </summary>
    public static class Calibration
    {
        /// <summary>
        /// Compat flag: kept so existing readers do not break. Not used.
        /// </summary>
        public static bool k_coefs_seted = false;

        /// <summary>
        /// Default path of the rectangular calibration file.
        /// </summary>
        public static string DefaultPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "calibration_rect.txt");

        /// <summary>
        /// No-op. The method is kept for compatibility with old calls.
        /// </summary>
        public static void Do_Calibration(ref short x, ref short y)
        {
            // The effective calibration is now done with CalibrationFile.MapNormalized in
            // GunWorker. Left empty for compatibility.
        }
    }
}

namespace Guncon3.Core
{
    /// <summary>
    /// Logical buttons exposed by the Guncon for mapping to keyboard/mouse.
    /// Public so the reader, the feeders and the mapping parser can all name the
    /// same button.
    /// </summary>
    public enum GunButton
    {
        // Physical buttons
        Trigger,
        A1,
        A2,
        B1,
        B2,
        C1,
        C2,
        AClick,
        BClick,

        // "Digitalized" left stick axes
        LUp,
        LDown,
        LLeft,
        LRight,

        // "Digitalized" right stick axes
        RUp,
        RDown,
        RLeft,
        RRight
    }
}

namespace Texpix
{
    /// <summary>
    ///     A component that shows a Texpix text, whichever UI system draws it. Lets code that only sets the
    ///     string work with both <see cref="TexpixText" /> (uGUI) and the YAUI element of the Texpix.Yaui assembly.
    /// </summary>
    public interface ITexpixText
    {
        string Text { get; set; }
    }
}

namespace TiaSclStudio.Core.Model
{
    public enum PlcCpuFamily
    {
        Unknown = 0,
        S71200 = 1,
        S71500 = 2
    }

    public enum PlcSignalKind
    {
        DigitalInput,
        DigitalOutput,
        AnalogInput,
        AnalogOutput,
        Memory
    }

    /// <summary>
    /// The three address spaces the editor can reach. Digital and analog
    /// operands of the same direction share one space: %IW0 and %I1.3 are the
    /// same two bytes of the input process image, addressed differently.
    /// </summary>
    public enum PlcMemoryArea
    {
        Input,
        Output,
        Marker
    }
}

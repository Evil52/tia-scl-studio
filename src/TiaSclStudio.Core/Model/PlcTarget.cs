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
}

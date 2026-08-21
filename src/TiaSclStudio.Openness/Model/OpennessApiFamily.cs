namespace TiaSclStudio.Openness.Model
{
    /// <summary>
    /// Identifies the binary layout used by a TIA Portal Openness API.
    /// V21 introduced modular Siemens.Engineering.* assemblies and is not
    /// binary-compatible with the monolithic V17-V20 API.
    /// </summary>
    public enum OpennessApiFamily
    {
        LegacyV17ToV20 = 0,
        ModularV21Plus = 1
    }
}

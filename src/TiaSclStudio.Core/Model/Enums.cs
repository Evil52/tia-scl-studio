namespace TiaSclStudio.Core.Model
{
    public enum BlockKind
    {
        FunctionBlock = 0,
        Function = 1
    }

    public enum InterfaceSection
    {
        Input = 0,
        Output = 1,
        InOut = 2,
        Static = 3,
        Temp = 4,
        Constant = 5
    }

    public enum GeneratedSourceKind
    {
        DataTypes = 0,
        Block = 1,
        InstanceDataBlocks = 2,
        CallBlock = 3,
        CallBlockInstanceDataBlock = 4
    }

    public enum ValidationSeverity
    {
        Warning = 0,
        Error = 1
    }
}

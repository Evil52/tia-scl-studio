namespace TiaSclStudio.Diagram.Model
{
    public enum CallNodeKind
    {
        BlockCall,
        Tag,
        Constant,
        Logic,
        Note
    }

    public enum PinDirection
    {
        Input,
        Output
    }

    public enum PinRole
    {
        Parameter,
        FunctionReturn,
        Terminal,
        Logic
    }

    public enum TerminalDirection
    {
        Source,
        Sink
    }

    public enum LogicOperation
    {
        And,
        Or,
        Not,
        GreaterThan,
        LessThan,
        Equal
    }
}

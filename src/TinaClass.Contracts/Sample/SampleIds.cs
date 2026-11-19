namespace TinaClass.Contracts.Sample;

/// <summary>Fixed ids of the sample data (栖云科技), shared by the API's Development seeders and the app's sample services so both tell the same story.</summary>
public static class SampleIds
{
    private static Guid Make(int kind, int n) => new($"5a6f{kind:x4}-0000-4000-8000-{n:x12}");

    public static Guid User(int n) => Make(1, n);
    public static Guid Provider(int n) => Make(2, n);
    public static Guid Model(int n) => Make(3, n);
    public static Guid Folder(int n) => Make(4, n);
    public static Guid Conversation(int n) => Make(5, n);
    public static Guid Message(int n) => Make(6, n);
    public static Guid Base(int n) => Make(7, n);
    public static Guid Document(int n) => Make(8, n);
    public static Guid Chunk(int n) => Make(9, n);
    public static Guid Agent(int n) => Make(10, n);
    public static Guid ToolServer(int n) => Make(11, n);
    public static Guid Tool(int n) => Make(12, n);
    public static Guid Run(int n) => Make(13, n);
    public static Guid Prompt(int n) => Make(14, n);
    public static Guid Flow(int n) => Make(15, n);
    public static Guid Space(int n) => Make(16, n);
    public static Guid Approval(int n) => Make(17, n);

    public static Guid Owner => User(1);
    public static Guid Admin => User(2);
    public static Guid He => User(3);
    public static Guid Zhou => User(4);
    public static Guid Tang => User(5);
}

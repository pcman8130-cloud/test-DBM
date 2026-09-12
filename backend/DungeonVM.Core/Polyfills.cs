#if NETSTANDARD2_1
namespace System.Runtime.CompilerServices
{
    /// <summary>netstandard2.1에는 없는 C# 9 init 접근자 지원을 위한 컴파일러 전용 폴리필.</summary>
    internal static class IsExternalInit { }
}
#endif

// In netstandard2.0, record/init accessors cannot find this marker type that the
// compiler expects. In net5.0+, the BCL carries it. Source-generator projects use
// this standard solution: an empty type whose presence is all the compiler checks.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;

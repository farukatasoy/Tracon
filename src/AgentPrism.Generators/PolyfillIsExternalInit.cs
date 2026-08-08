// netstandard2.0'da record/init erisimcisi derleyicinin aradigi bu isaretci
// tipini bulamaz (net5.0+'ta BCL'nin kendisi tasir). Kaynak ureteci projeleri
// icin standart cozum: derleyicinin yalniz VARLIGINA baktigi bos bir tip.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;

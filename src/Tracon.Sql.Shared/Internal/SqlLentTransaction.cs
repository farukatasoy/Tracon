using System.Data.Common;

namespace Tracon;

/// <summary>An open transaction a store lent to the work it awaits (<see cref="SqlStoreContext.LendTransaction"/>).</summary>
/// <param name="Connection">The open connection the transaction runs on.</param>
/// <param name="Transaction">The transaction.</param>
internal sealed record SqlLentTransaction(DbConnection Connection, DbTransaction Transaction);

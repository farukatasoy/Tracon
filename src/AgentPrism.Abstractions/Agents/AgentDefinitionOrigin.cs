namespace AgentPrism;

/// <summary>Bir agent tanimin nereden geldigini bildirir.</summary>
public enum AgentDefinitionOrigin
{
    /// <summary>
    /// Tanim kodda yapilmistir. Derleme zamaninda dogrulanmistir, bu yuzden ad
    /// cakismasinda veritabani tanimina karsi oncelik kazanir.
    /// </summary>
    Code = 0,

    /// <summary>Tanim veritabaninda saklanir ve calisma aninda derlenir.</summary>
    Database = 1,
}

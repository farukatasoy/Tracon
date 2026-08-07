using System.Reflection;
using Microsoft.Agents.AI;

namespace AgentPrism.StoreContracts;

/// <summary>
/// Paylasilan depo katmanindaki <strong>her</strong> public metodun ya kiraci
/// yalitimi sozlesmesinde sinandigini ya da gerekcesiyle muaf tutuldugunu
/// dogrular.
/// </summary>
/// <remarks>
/// <para>
/// Faz 41'de eklendi. En kolay kacirilan sey, <em>yarin eklenecek</em> metodun
/// testsiz kalmasidir; bu denetim onu bir derleme sonrasi kirmizisina cevirir.
/// </para>
/// <para>
/// 🚨 Yansima yalniz TEST projesindedir. <c>AgentPrism.Sql.Shared</c> yansimaya
/// dokunmaz ve AOT durusu etkilenmez. <c>TenantAgnosticAttribute</c> de
/// <c>internal</c>'dir; public sozlesme buyumez.
/// </para>
/// <para>
/// Muafiyet <strong>sessiz olamaz</strong>: gerekce metodun yaninda durur ve
/// kod incelemesinde gorunur. Kapsam listesi ise sozlesmelerin gercekten
/// dokundugu metotlari sayar; listede olup artik var olmayan bir metot da
/// hatadir (bayat kayit).
/// </para>
/// </remarks>
public sealed class TenantCoverageTests
{
    /// <summary>
    /// Depo basina, kiraci yalitimi sozlesmelerinin GERCEKTEN cagirdigi
    /// metotlar. Yeni bir metot eklendiginde ya buraya (bir testle birlikte)
    /// ya da <c>[TenantAgnostic]</c> ile muafiyete girer.
    /// </summary>
    private static readonly Dictionary<string, string[]> Covered = new(StringComparer.Ordinal)
    {
        ["SqlAgentDefinitionStore"] =
            ["GetAsync", "GetVersionAsync", "ListAsync", "ListVersionsAsync", "SaveAsync", "DeleteAsync", "RollbackAsync"],
        ["SqlAgentFileStore"] =
            ["ReadAsync", "WriteAsync", "DeleteAsync", "FileExistsAsync", "ListChildrenAsync", "SearchAsync", "CreateDirectoryAsync"],
        ["SqlAgentSkillStore"] = ["ListAsync", "GetAsync", "SaveAsync", "DeleteAsync"],
        ["SqlToolApprovalRuleStore"] = ["ListAsync", "AddAsync", "DeleteAsync"],
        ["SqlMcpServerStore"] = ["ListAsync", "GetAsync", "SaveAsync", "DeleteAsync"],
        ["SqlAttachmentStore"] =
            ["SaveAsync", "GetAsync", "OpenReadAsync", "ListAsync", "DeleteAsync", "DeleteBySessionAsync"],
        ["SqlAuditLog"] = ["WriteAsync", "QueryAsync"],
        ["SqlEvalStore"] =
            ["ListSuitesAsync", "GetSuiteAsync", "SaveSuiteAsync", "DeleteSuiteAsync", "CreateRunAsync",
             "GetRunAsync", "GetRunByJobIdAsync", "ListCaseResultsAsync", "QueryRunsAsync"],
        ["SqlExperimentStore"] =
            ["ListAsync", "GetAsync", "SaveAsync", "DeleteAsync", "GetRunningAsync", "StartAsync", "StopAsync"],
        ["SqlJobScheduleStore"] = ["GetAsync", "ListAsync", "SaveAsync", "DeleteAsync"],
        ["SqlJobStore"] = ["EnqueueAsync", "GetAsync", "QueryAsync", "CancelAsync"],
        ["SqlQuotaStore"] = ["ListAsync", "GetAsync", "SaveAsync", "DeleteAsync", "GetUsageAsync", "AddUsageAsync"],
        ["SqlRetentionPolicyStore"] =
            ["ListPoliciesAsync", "GetPolicyAsync", "SavePolicyAsync", "DeletePolicyAsync", "CreateRunAsync", "ListRunsAsync"],
        ["SqlRetentionStore"] =
            ["CountOlderThanAsync", "ReadForArchiveAsync", "DeleteBatchAsync", "FindRowLimitCutoffAsync"],
        ["SqlRunScoreStore"] = ["UpsertAsync", "ListAsync", "DeleteAsync"],
        ["SqlRunStore"] =
            ["StartRunAsync", "GetRunAsync", "QueryRunsAsync", "ReadEventsAsync", "ListToolInvocationsAsync",
             "GetToolUsageAsync", "GetStatisticsAsync", "GetTimeSeriesAsync", "GetExperimentResultsAsync"],
        ["SqlSessionStore"] = ["SaveAsync", "GetAsync", "DeleteAsync", "QueryAsync"],
        ["SqlSkillScriptGrantStore"] = ["ListAsync", "FindActiveAsync", "GrantAsync", "RevokeAsync"],
        ["SqlTraceStore"] = ["WriteSpansAsync", "GetTraceByRunAsync"],
        ["SqlVoiceSessionStore"] = ["SaveAsync", "QueryAsync"],
        ["SqlWebhookStore"] =
            ["ListSubscriptionsAsync", "GetSubscriptionAsync", "FindForEventAsync", "SaveSubscriptionAsync",
             "DeleteSubscriptionAsync", "CreateDeliveryAsync", "QueryDeliveriesAsync"],
        ["SqlWorkflowCheckpointStore"] = ["CreateAsync", "ReadAsync", "ListAsync", "ListByRunAsync", "DeleteAsync"],
        ["SqlWorkflowDefinitionStore"] = ["GetAsync", "ListAsync", "SaveAsync", "DeleteAsync"],
        ["SqlTenantStore"] = [],
        ["SqlChatHistoryProvider"] = [],
        ["SqlSingletonLeaseStore"] = [],
    };

    [Fact]
    public void Her_public_depo_metodu_ya_sinaniyor_ya_gerekceli_muaf()
    {
        var eksik = new List<string>();

        foreach (var store in DiscoverStores())
        {
            var covered = Covered.TryGetValue(store.Name, out var listed)
                ? new HashSet<string>(listed, StringComparer.Ordinal)
                : null;

            if (covered is null)
            {
                eksik.Add($"{store.Name}: kapsam listesinde hic yok (yeni bir depo mu eklendi?)");

                continue;
            }

            foreach (var method in PublicMethods(store))
            {
                if (covered.Contains(method.Name))
                {
                    continue;
                }

                if (method.GetCustomAttribute<TenantAgnosticAttribute>() is not null)
                {
                    continue;
                }

                eksik.Add($"{store.Name}.{method.Name}");
            }
        }

        eksik.ShouldBeEmpty(
            "Bu metotlar ne kiraci yalitimi sozlesmesinde gorunuyor ne de "
            + "[TenantAgnostic(\"gerekce\")] ile muaf: " + string.Join(", ", eksik));
    }

    [Fact]
    public void Muafiyet_gerekcesi_yazilmadan_verilemez()
    {
        foreach (var store in DiscoverStores())
        {
            foreach (var method in PublicMethods(store))
            {
                if (method.GetCustomAttribute<TenantAgnosticAttribute>() is not { } exemption)
                {
                    continue;
                }

                // Gerekce, muafiyeti bir kacis kapisi olmaktan cikaran tek
                // seydir; bos veya bir kelimelik bir metin kabul edilmez.
                exemption.Reason.Length.ShouldBeGreaterThan(
                    40,
                    $"{store.Name}.{method.Name} muafiyeti gercek bir gerekce tasimiyor.");
            }
        }
    }

    [Fact]
    public void Kapsam_listesi_bayat_kayit_tasimaz()
    {
        var stores = DiscoverStores().ToDictionary(static type => type.Name, StringComparer.Ordinal);
        var bayat = new List<string>();

        foreach (var (storeName, methods) in Covered)
        {
            if (!stores.TryGetValue(storeName, out var store))
            {
                bayat.Add($"{storeName} (boyle bir depo yok)");

                continue;
            }

            var actual = PublicMethods(store).Select(static method => method.Name).ToHashSet(StringComparer.Ordinal);

            foreach (var method in methods.Where(method => !actual.Contains(method)))
            {
                bayat.Add($"{storeName}.{method}");
            }
        }

        bayat.ShouldBeEmpty("Kapsam listesinde artik var olmayan kayitlar: " + string.Join(", ", bayat));
    }

    [Fact]
    public void Denetim_gercekten_depo_buluyor()
    {
        // 🚨 Bos bir yansima sorgusu her seyi yesil gosterirdi. Bu test kapinin
        // kendisini korur.
        DiscoverStores().Count.ShouldBeGreaterThanOrEqualTo(20);
    }

    /// <summary>
    /// Saglayici derlemesindeki (baglantili kaynak, K-176) butun depo
    /// siniflarini bulur.
    /// </summary>
    /// <returns>Depo tipleri.</returns>
    private static IReadOnlyList<Type> DiscoverStores()
        => [.. typeof(SqlRunStore).Assembly
            .GetTypes()
            .Where(static type => type is { IsClass: true, IsAbstract: false })
            .Where(static type => string.Equals(type.Namespace, "AgentPrism", StringComparison.Ordinal))
            .Where(static type => type.Name.StartsWith("Sql", StringComparison.Ordinal))
            .Where(IsStoreLike)
            .OrderBy(static type => type.Name, StringComparer.Ordinal)];

    /// <summary>Tip bir depo mu (kalicilik yuzeyi mi).</summary>
    /// <param name="type">Aday tip.</param>
    /// <returns>Depo ise <see langword="true"/>.</returns>
    private static bool IsStoreLike(Type type)
    {
#pragma warning disable MAAI001 // AgentFileStore "evaluation purposes only"; gerekce urun kodundaki ile ayni.
        if (type.IsSubclassOf(typeof(AgentFileStore)) || type.IsSubclassOf(typeof(ChatHistoryProvider)))
#pragma warning restore MAAI001
        {
            return true;
        }

        return type.GetInterfaces().Any(static contract =>
            string.Equals(contract.Namespace, "AgentPrism", StringComparison.Ordinal)
            && (contract.Name.EndsWith("Store", StringComparison.Ordinal)
                || string.Equals(contract.Name, "IAuditLog", StringComparison.Ordinal)));
    }

    /// <summary>Bir deponun kendi tanimladigi public ornek metotlari.</summary>
    /// <param name="store">Depo tipi.</param>
    /// <returns>Metotlar.</returns>
    private static IEnumerable<MethodInfo> PublicMethods(Type store)
        => store
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => !method.IsSpecialName)
            .Where(static method => method.DeclaringType != typeof(object));
}

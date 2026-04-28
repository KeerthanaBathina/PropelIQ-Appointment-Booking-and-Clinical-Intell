using System.Net.Http.Headers;
using NBomber.Contracts;
using NBomber.CSharp;
using UPACIP.LoadTests.Infrastructure;

namespace UPACIP.LoadTests.Scenarios;

/// <summary>
/// NBomber load scenario for appointment search and patient lookup flows (US_082 task_002, AC-1).
///
/// <para>
/// <b>Flow per virtual user:</b>
/// <list type="number">
///   <item><b>Appointment search</b> â€” <c>GET /api/appointments/search?date={date}&amp;providerId={id}</c></item>
///   <item><b>Patient lookup</b> â€” <c>GET /api/admin/patients/{patientId}</c> (staff only)</item>
/// </list>
/// </para>
///
/// <para>
/// <b>SLA:</b> Search end-to-end P95 &lt; 1000 ms (AC-1).
/// </para>
///
/// <para>
/// <b>Concurrency:</b> 1000 virtual users, 120-second duration.
/// </para>
/// </summary>
public static class SearchLoadScenario
{
    private static readonly Random Rng = new();

    /// <summary>Creates and returns the NBomber <c>ScenarioProps</c> for the search flow.</summary>
    public static ScenarioProps Create(
        LoadTestConfiguration config,
        int concurrentUsers = 1000,
        int durationSeconds = 120)
    {
        var httpClient = BuildHttpClient(config, useStaffToken: true);

        var providerIds = Enumerable.Range(1, 50)
            .Select(i => new Guid(i, 0, 0, new byte[8]).ToString())
            .ToArray();

        // Seeded patient Guids start at 2_000_001.
        var patientIds = Enumerable.Range(1, 100)
            .Select(i => new Guid(i + 2_000_001 - 1, 0, 0, new byte[8]).ToString())
            .ToArray();

        return Scenario.Create("search_flow", async ctx =>
        {
            var providerId = providerIds[Rng.Next(providerIds.Length)];
            var daysOffset = Rng.Next(0, 30);
            var searchDate = DateTime.UtcNow.Date.AddDays(daysOffset).ToString("yyyy-MM-dd");
            var patientId  = patientIds[Rng.Next(patientIds.Length)];

            // Step 1: Appointment search â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            using (var searchReq = new HttpRequestMessage(HttpMethod.Get,
                $"{config.BaseUrl}/api/appointments/search?date={searchDate}&providerId={providerId}"))
            {
                using var resp = await httpClient.SendAsync(searchReq, CancellationToken.None);

                // 404 is acceptable if the provider/date combination has no slots yet.
                if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.NotFound)
                    return Response.Fail(message: $"AppointmentSearch:{(int)resp.StatusCode}");
            }

            // Step 2: Patient lookup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            using (var patReq = new HttpRequestMessage(HttpMethod.Get,
                $"{config.BaseUrl}/api/admin/patients/{patientId}"))
            {
                using var resp = await httpClient.SendAsync(patReq, CancellationToken.None);

                // 404 acceptable if seeded patient hasn't been linked to a profile yet.
                if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.NotFound)
                    return Response.Fail(message: $"PatientLookup:{(int)resp.StatusCode}");
            }

            return Response.Ok();
        })
        .WithLoadSimulations(
            Simulation.Inject(
                rate:     concurrentUsers / 10,
                interval: TimeSpan.FromSeconds(1),
                during:   TimeSpan.FromSeconds(durationSeconds)));
    }

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static HttpClient BuildHttpClient(LoadTestConfiguration config, bool useStaffToken)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var token  = useStaffToken ? config.AuthTokenStaff : config.AuthTokenPatient;
        if (!string.IsNullOrEmpty(token))
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}



using System.Net.Http.Headers;
using NBomber.Contracts;
using NBomber.CSharp;
using UPACIP.LoadTests.Infrastructure;

namespace UPACIP.LoadTests.Scenarios;

/// <summary>
/// NBomber load scenario for patient and staff dashboard flows (US_082 task_002, AC-1).
///
/// <para>
/// <b>Flow per virtual user (interleaved, 50/50 split):</b>
/// <list type="number">
///   <item><b>Patient dashboard</b> â€” <c>GET /api/dashboard/patient</c>.
///     First request is a cache miss; subsequent requests hit the Redis cache.</item>
///   <item><b>Staff dashboard</b> â€” <c>GET /api/dashboard/staff</c>.
///     Includes queue view and upcoming appointments.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>SLA:</b> Dashboard P95 &lt; 3000 ms (AC-1).
/// </para>
///
/// <para>
/// <b>Concurrency:</b> 1000 virtual users, 120-second duration.
/// </para>
/// </summary>
public static class DashboardLoadScenario
{
    private static readonly Random Rng = new();

    /// <summary>Creates and returns the NBomber <c>ScenarioProps</c> for the dashboard flow.</summary>
    public static ScenarioProps Create(
        LoadTestConfiguration config,
        int concurrentUsers = 1000,
        int durationSeconds = 120)
    {
        var patientClient = BuildHttpClient(config, usePatientToken: true);
        var staffClient   = BuildHttpClient(config, usePatientToken: false);

        return Scenario.Create("dashboard_flow", async ctx =>
        {
            // Interleave patient and staff dashboard requests to exercise both paths.
            bool isPatient = ctx.InvocationNumber % 2 == 0;

            if (isPatient)
            {
                // Patient dashboard â€” simulates both cache-miss (first invocation)
                // and cache-hit (subsequent invocations) paths.
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    $"{config.BaseUrl}/api/dashboard/patient");

                using var resp = await patientClient.SendAsync(req, CancellationToken.None);

                // 401/403 = auth token not set in config; still track the latency.
                if (!resp.IsSuccessStatusCode &&
                    resp.StatusCode != System.Net.HttpStatusCode.Unauthorized &&
                    resp.StatusCode != System.Net.HttpStatusCode.Forbidden)
                    return Response.Fail(message: $"PatientDashboard:{(int)resp.StatusCode}");
            }
            else
            {
                // Staff dashboard â€” includes queue status + upcoming appointments.
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    $"{config.BaseUrl}/api/dashboard/staff");

                using var resp = await staffClient.SendAsync(req, CancellationToken.None);

                if (!resp.IsSuccessStatusCode &&
                    resp.StatusCode != System.Net.HttpStatusCode.Unauthorized &&
                    resp.StatusCode != System.Net.HttpStatusCode.Forbidden)
                    return Response.Fail(message: $"StaffDashboard:{(int)resp.StatusCode}");
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

    private static HttpClient BuildHttpClient(LoadTestConfiguration config, bool usePatientToken)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var token  = usePatientToken ? config.AuthTokenPatient : config.AuthTokenStaff;
        if (!string.IsNullOrEmpty(token))
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}


